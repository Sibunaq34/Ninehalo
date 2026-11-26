
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace APINineHalo.Services
{
    public class JwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(
            int idUsuario,
            IEnumerable<string> roles)
        {
            if (idUsuario <= 0)
            {
                throw new ArgumentException(
                    "El identificador del usuario es inválido.");
            }

            // Obtener la clave desde User Secrets
            string secret = _configuration["Jwt:SecretKey"]
                ?? throw new InvalidOperationException(
                    "No se configuró Jwt:SecretKey.");

            byte[] keyBytes;

            try
            {
                keyBytes = Convert.FromBase64String(secret);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException(
                    "La clave JWT debe estar codificada en Base64.");
            }

            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "La clave JWT debe contener al menos 32 bytes.");
            }

            string issuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "No se configuró Jwt:Issuer.");

            string audience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "No se configuró Jwt:Audience.");

            int expirationMinutes = _configuration
                .GetValue<int>("Jwt:ExpirationMinutes");

            if (expirationMinutes <= 0)
            {
                throw new InvalidOperationException(
                    "El tiempo de expiración JWT debe ser positivo.");
            }

            var securityKey = new SymmetricSecurityKey(keyBytes);

            var credentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256
            );

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    idUsuario.ToString()
                ),

                new Claim(
                    ClaimTypes.NameIdentifier,
                    idUsuario.ToString()
                ),

                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString()
                )
            };

            // Agregar roles obtenidos de la base de datos
            foreach (var rol in roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, rol));
            }

            var ahora = DateTime.UtcNow;

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: ahora,
                expires: ahora.AddMinutes(expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }
    }
}
