using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

/// <summary>
/// Authentisierung gegen ein Verzeichnis: Benutzer suchen, Passwort per Anmeldung als dieser Benutzer prüfen,
/// Rolle aus den Gruppen ableiten. Fehler im Verzeichnis führen immer zu "abgelehnt", nie zu "angemeldet".
/// </summary>
public sealed class LdapAuthenticationProvider(
    ILdapDirectory directory, IOptions<LdapOptions> options, ILogger<LdapAuthenticationProvider> logger)
    : IAuthenticationProvider
{
    public string Name => "ldap";

    public async Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken ct)
    {
        // Ein leeres Passwort würde bei vielen Servern als anonyme Anmeldung "gelingen".
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        try
        {
            var user = await directory.FindUserAsync(userName, ct);
            if (user is null || !await directory.VerifyPasswordAsync(user.Dn, password, ct))
            {
                return null;
            }

            if (LdapRoleMapper.Map(user.Groups, options.Value) is not { } role)
            {
                logger.LogInformation("LDAP user '{UserName}' is not in any ODM group; sign-in rejected.", userName);
                return null;
            }

            var normalized = User.Normalize(userName);
            var display = string.IsNullOrWhiteSpace(user.DisplayName) ? userName.Trim() : user.DisplayName.Trim();
            return new AuthenticatedUser(ExternalUserIds.Create(Name, normalized), normalized, display, role);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LDAP sign-in for '{UserName}' failed (directory unreachable or misconfigured).", userName);
            return null;
        }
    }
}
