namespace Odm.Modules.Identity.Application;

public sealed class AuthService(IEnumerable<IAuthenticationProvider> providers, ITokenService tokens)
{
    public async Task<LoginResponse?> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var name = userName.Trim();
        var list = providers.ToList();

        // Gehört der Name einem Verfahren (z. B. einem lokalen Konto), entscheidet nur dieses.
        foreach (var provider in list)
        {
            if (await provider.OwnsUserAsync(name, ct))
            {
                return await TryAsync(provider, name, password, ct);
            }
        }

        foreach (var provider in list)
        {
            if (await TryAsync(provider, name, password, ct) is { } response)
            {
                return response;
            }
        }

        return null;
    }

    private async Task<LoginResponse?> TryAsync(
        IAuthenticationProvider provider, string name, string password, CancellationToken ct)
    {
        var user = await provider.AuthenticateAsync(name, password, ct);
        if (user is null)
        {
            return null;
        }

        var (token, expiresAt) = tokens.Create(user);
        return new LoginResponse(token, expiresAt, user.ToDto());
    }
}
