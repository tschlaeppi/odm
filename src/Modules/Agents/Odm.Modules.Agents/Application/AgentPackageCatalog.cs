using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Odm.Modules.Agents.Application;

/// <summary>A prebuilt, self-contained agent package for one platform.</summary>
public sealed record AgentPackage(string Platform, string Version, string FileName, string FullPath, long Size, string Sha256);

/// <summary>
/// Finds the agent packages offered by the server (<c>odm-agent-{version}-{platform}.zip|tar.gz</c>) and picks the
/// highest version per platform. File names come exclusively from the directory, never from user input.
/// </summary>
public sealed partial class AgentPackageCatalog(IOptions<AgentsOptions> options, IHostEnvironment environment)
{
    public static readonly string[] Platforms = ["win-x64", "win-arm64", "linux-x64", "linux-arm64"];

    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, string Hash)> _hashes = new();

    [GeneratedRegex(@"^odm-agent-(?<v>\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?)-(?<p>win-x64|win-arm64|linux-x64|linux-arm64)\.(?<e>zip|tar\.gz)$")]
    private static partial Regex FileNamePattern();

    public string Directory
    {
        get
        {
            var path = options.Value.PackagesPath;
            return Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
        }
    }

    /// <summary>Parses a file name; <c>null</c> if it does not match the scheme or the extension does not fit the platform.</summary>
    public static (string Version, string Platform)? ParseFileName(string fileName)
    {
        var m = FileNamePattern().Match(fileName);
        if (!m.Success)
        {
            return null;
        }

        var platform = m.Groups["p"].Value;
        var expected = platform.StartsWith("win", StringComparison.Ordinal) ? "zip" : "tar.gz";
        return m.Groups["e"].Value == expected ? (m.Groups["v"].Value, platform) : null;
    }

    /// <summary>SemVer comparison (prerelease versions are lower than the release version).</summary>
    public static int CompareVersions(string a, string b)
    {
        static (Version Core, bool Pre) Split(string v)
        {
            var dash = v.IndexOf('-');
            return (Version.Parse(dash < 0 ? v : v[..dash]), dash >= 0);
        }

        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        var core = ca.CompareTo(cb);
        if (core != 0)
        {
            return core;
        }

        return pa == pb ? string.CompareOrdinal(a, b) : (pa ? -1 : 1);
    }

    /// <summary>All validly named packages (including older versions), per platform in descending version order.</summary>
    public IReadOnlyList<AgentPackage> ListAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var all = new List<AgentPackage>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (ParseFileName(Path.GetFileName(file)) is { } p)
            {
                var info = new FileInfo(file);
                all.Add(new AgentPackage(p.Platform, p.Version, info.Name, info.FullName, info.Length, Hash(info)));
            }
        }

        return all
            .OrderBy(a => Array.IndexOf(Platforms, a.Platform))
            .ThenByDescending(a => a.Version, Comparer<string>.Create(CompareVersions))
            .ToList();
    }

    public AgentPackage? Find(string platform, string version) =>
        ListAll().FirstOrDefault(p => p.Platform == platform && p.Version == version);

    public IReadOnlyList<AgentPackage> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var best = new Dictionary<string, (string Version, string Path)>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            var parsed = ParseFileName(Path.GetFileName(file));
            if (parsed is not { } p)
            {
                continue;
            }

            if (!best.TryGetValue(p.Platform, out var current) || CompareVersions(p.Version, current.Version) > 0)
            {
                best[p.Platform] = (p.Version, file);
            }
        }

        return Platforms
            .Where(best.ContainsKey)
            .Select(platform =>
            {
                var (version, path) = best[platform];
                var info = new FileInfo(path);
                return new AgentPackage(platform, version, info.Name, info.FullName, info.Length, Hash(info));
            })
            .ToList();
    }

    public AgentPackage? Find(string platform) => List().FirstOrDefault(p => p.Platform == platform);

    private string Hash(FileInfo file)
    {
        var written = file.LastWriteTimeUtc;
        if (_hashes.TryGetValue(file.FullName, out var cached) && cached.Length == file.Length && cached.Written == written)
        {
            return cached.Hash;
        }

        using var stream = file.OpenRead();
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        _hashes[file.FullName] = (file.Length, written, hash);
        return hash;
    }
}
