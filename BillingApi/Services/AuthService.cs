using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BillingApi.Services
{
    public class AuthService
    {
        private readonly string _key;
        private readonly string _issuer;

        public AuthService(IConfiguration config)
        {
            _key = config["Jwt:Key"]
                ?? throw new InvalidOperationException("Missing Jwt__Key");
            _issuer = config["Jwt:Issuer"] ?? "billing-api";
        }

        public string GenerateToken(int tenandId, string email)
        {
            var claims = new[]
            {
                new Claim("tenantId", tenandId.ToString()),
                new Claim("email", email),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);


            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _issuer,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();

            try
            {
                // ValidateToken provjerava: je li potpis ispravan, je li istekao, je li issuer tačan.
                // Ako bilo šta ne valja — baci exception, mi vratimo null.
                return handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _issuer,
                    ValidateAudience = true,
                    ValidAudience = _issuer,
                    ValidateLifetime = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key))
                }, out _);
            }
            catch
            {
                return null;
            }
        }
    }
}
