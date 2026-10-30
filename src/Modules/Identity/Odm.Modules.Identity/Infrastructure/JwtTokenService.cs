using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Odm.Modules.Identity.Application;

namespace Odm.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "open-device-manager";
    public string Audience { get; set; } = "open-device-manager";
    public string Secret { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public (string Token, DateTimeOffset ExpiresAt) Create(AuthenticatedUser user)
    {
        var o = options.Value;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(o.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.UserName),
                new Claim("display_name", user.DisplayName),
                new Claim("role", user.Role.ToString())
            }),
            Expires = expiresAt.UtcDateTime,
            Issuer = o.Issuer,
            Audience = o.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
