
using System.Text.Json.Serialization;

namespace APINineHalo.Entities
{
    public class LoginResponseDto
    {
        [JsonPropertyName("requires_2fa")]
        public bool Requires2FA { get; set; }

        [JsonPropertyName("id_verificacion")]
        public long IdVerificacion { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
