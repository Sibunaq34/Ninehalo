
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace APINineHalo.Entities
{
    public class VerifyLoginCodeRequest
    {
        [JsonPropertyName("id_verificacion")]
        [Range(1, long.MaxValue)]
        public long IdVerificacion { get; set; }

        [JsonPropertyName("codigo")]
        [Required]
        [RegularExpression(@"^[0-9]{6}$",
            ErrorMessage = "El código debe contener exactamente 6 dígitos.")]
        public string Codigo { get; set; } = string.Empty;
    }
}
