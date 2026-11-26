
using System.Text.Json.Serialization;

namespace APINineHalo.Entities
{
    public class LoginPreparationDto
    {
        [JsonPropertyName("requires_2fa")]
        public bool Requires2FA { get; set; } = true;

        [JsonPropertyName("pre_auth_token")]
        public string PreAuthToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
