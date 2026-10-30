using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Odm.Modules.Agents.Application;

/// <summary>Entscheidet, ob und welches Update ein Agent beim Heartbeat angeboten bekommt.</summary>
public static partial class AgentUpdatePlanner
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?$")]
    private static partial Regex VersionPattern();

    /// <summary>Stabiler Wert 0–99 je Gerät für die gestaffelte Verteilung.</summary>
    public static int Bucket(Guid deviceId)
    {
        var hash = SHA256.HashData(deviceId.ToByteArray());
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }

    public static AgentUpdateOfferDto? Plan(
        AgentUpdateOptions options,
        AgentPackageCatalog catalog,
        string? platform,
        string? agentVersion,
        Guid deviceId)
    {
        if (options.Mode == AgentUpdateMode.Off ||
            platform is null || !AgentPackageCatalog.Platforms.Contains(platform) ||
            agentVersion is null || !VersionPattern().IsMatch(agentVersion))
        {
            return null;
        }

        if (Bucket(deviceId) >= Math.Clamp(options.RolloutPercent, 0, 100))
        {
            return null;
        }

        AgentPackage? target;
        if (options.Mode == AgentUpdateMode.Pinned)
        {
            if (!VersionPattern().IsMatch(options.TargetVersion))
            {
                return null;
            }

            target = catalog.Find(platform, options.TargetVersion);
            if (target is null || target.Version == agentVersion)
            {
                return null;
            }
        }
        else
        {
            target = catalog.List().FirstOrDefault(p => p.Platform == platform);
            if (target is null || AgentPackageCatalog.CompareVersions(target.Version, agentVersion) <= 0)
            {
                return null;
            }
        }

        return new AgentUpdateOfferDto(
            target.Version, target.Sha256, target.Size,
            $"api/agent/download/{platform}?version={target.Version}");
    }
}
