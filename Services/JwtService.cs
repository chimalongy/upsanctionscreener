using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Utils
{
    public static class JwtService
    {
        public static string GenerateToken(SanctionScanUser user, IConfiguration config)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expiry = DateTime.UtcNow.AddMinutes(
                              double.Parse(config["Jwt:ExpiryMinutes"] ?? "480"));

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.NameIdentifier,     user.Id.ToString()),
                new Claim(ClaimTypes.Name,               $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.Email,              user.Email),
                new Claim(ClaimTypes.Role,               user.Role ?? "Regular User"),
                new Claim("department",                  user.Department ?? ""),
                new Claim("firstName",                   user.FirstName),
                new Claim("lastName",                    user.LastName),
                new Claim("profileStatus",               user.ProfileStatus ?? "enabled"),
                new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: expiry,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}