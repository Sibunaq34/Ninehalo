
namespace APINineHalo.Entities
{
    public class RegisterRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_pais")]
        public int IdPais { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("id_provincia")]
        public int IdProvincia { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("id_canton")]
        public int IdCanton { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("id_distrito")]
        public int IdDistrito { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
