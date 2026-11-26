
using System.Text.Json.Serialization;

namespace APINineHalo.Entities
{
    public class SendLoginCodeRequest
    {
        [JsonPropertyName("pre_auth_token")]
        public string PreAuthToken { get; set; } = string.Empty;
    }
}
