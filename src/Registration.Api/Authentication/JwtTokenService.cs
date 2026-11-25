using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Registration.Api.Data.Entities;

namespace Registration.Api.Authentication;

public sealed class JwtTokenService(IConfiguration configuration)
{
    private readonly string _issuer = configuration["Jwt:Issuer"]
        ?? throw new InvalidOperationException("Jwt:Issuer is required.");
    private readonly string _audience = configuration["Jwt:Audience"]
        ?? throw new InvalidOperationException("Jwt:Audience is required.");
    private readonly SymmetricSecurityKey _key = CreateKey(configuration["Jwt:SigningKey"]);
    private readonly int _lifetimeMinutes = int.TryParse(configuration["Jwt:LifetimeMinutes"], out var minutes)
        ? Math.Clamp(minutes, 5, 120)
        : 30;

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(AppUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_lifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.Student is not null)
        {
            claims.Add(new Claim("student_id", user.Student.Id.ToString()));
            claims.Add(new Claim("display_name", user.Student.FullName));
        }
        else
        {
            claims.Add(new Claim("display_name", user.Username));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(handler.CreateToken(descriptor)), expiresAt);
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _issuer,
        ValidateAudience = true,
        ValidAudience = _audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };

    private static SymmetricSecurityKey CreateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
        }
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value));
    }
}
