using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Odm.Agent;

// ---- Protocol (/api/agent/v1), counterpart of the server contracts ----

public sealed record RegisterRequest(
    string Token, string Hostname, string OsFamily, string OsName, string OsVersion, string AgentVersion, string? Csr = null);

public sealed record RegisterResponse(
    Guid AgentId, string Secret, Guid DeviceId, int HeartbeatSeconds,
    string? Certificate = null, string? CaCertificate = null);

public sealed record CertificateRequestBody(string Csr);

public sealed record CertificateResponse(string Certificate, string CaCertificate);

public sealed record JobResult(Guid JobId, bool Success, string? Output);

public sealed record HeartbeatRequest(
    string OsFamily, string OsName, string OsVersion, string AgentVersion,
    IReadOnlyList<JobResult> Results, IReadOnlyList<Guid> RunningJobs, string? Platform = null, string? Hostname = null);

public sealed record ResolveSecretsRequest(Guid JobId);

public sealed record ResolveSecretsResponse(Dictionary<string, string> Secrets);

public sealed record ServerJob(Guid Id, string Type, string? Payload);

public sealed record UpdateOffer(string Version, string Sha256, long Size, string Url);

public sealed record HeartbeatResponse(
    IReadOnlyList<ServerJob> Jobs, int NextHeartbeatSeconds, UpdateOffer? Update = null, bool CertificateRequested = false);

public sealed class ServerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/agent/v1/register", request, Json, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>(Json, ct))!;
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(AgentState state, HeartbeatRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/heartbeat")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        Authorize(message, state);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HeartbeatResponse>(Json, ct))!;
    }

    /// <summary>Requests a (new) client certificate. The response contains the certificate and the CA certificate.</summary>
    public async Task<CertificateResponse> RequestCertificateAsync(AgentState state, string csrPem, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/certificate")
        {
            Content = JsonContent.Create(new CertificateRequestBody(csrPem), options: Json)
        };
        Authorize(message, state);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CertificateResponse>(Json, ct))!;
    }

    /// <summary>Downloads a package to a file. The content is streamed, not held in memory.</summary>
    public async Task DownloadPackageAsync(AgentState state, Guid versionId, string destinationPath, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/agent/v1/packages/{versionId}/download");
        Authorize(message, state);

        // ResponseHeadersRead: the HttpClient timeout only applies up to the headers, not to the whole download.
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destinationPath);
        await source.CopyToAsync(target, ct);
    }

    /// <summary>Fetches the values of the secrets the running job refers to (only from the server, never in the job itself).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveSecretsAsync(AgentState state, Guid jobId, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/secrets/resolve")
        {
            Content = JsonContent.Create(new ResolveSecretsRequest(jobId), options: Json)
        };
        Authorize(message, state);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ResolveSecretsResponse>(Json, ct);
        return body?.Secrets ?? new Dictionary<string, string>();
    }

    /// <summary>Sends hardware and installed programs; the server replaces the previous snapshot of this device.</summary>
    public async Task SendInventoryAsync(AgentState state, Inventory.InventoryRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/inventory")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        Authorize(message, state);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
    }

    private static void Authorize(HttpRequestMessage message, AgentState state) =>
        message.Headers.Authorization = new AuthenticationHeaderValue("Agent", $"{state.AgentId}.{state.Secret}");
}
