namespace Odm.Modules.Identity.Application;

public sealed class AuthService(IEnumerable<IAuthenticationProvider> providers, ITokenService tokens)
{
    public async Task<LoginResponse?> LoginAsync(string userName, string password, CancellationToken ct)
    {
        foreach (var provider in providers)
        {
            var user = await provider.AuthenticateAsync(userName.Trim(), password, ct);
            if (user is null)
            {
                continue;
            }

            var (token, expiresAt) = tokens.Create(user);
            return new LoginResponse(token, expiresAt, user.ToDto());
        }

        return null;
    }
}
