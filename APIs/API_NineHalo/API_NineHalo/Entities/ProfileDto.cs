using System.Text.Json.Serialization;
namespace APINineHalo.Entities;
public class ProfileDto
{
    [JsonPropertyName("id_usuario")] public int Id_Usuario { get; set; }
    [JsonPropertyName("id")] public int Id => Id_Usuario;
    [JsonPropertyName("identificacion")] public string? Identificacion { get; set; }
    [JsonPropertyName("nombre")] public string? Nombre { get; set; }
    [JsonPropertyName("apellidos")] public string? Apellidos { get; set; }
    [JsonPropertyName("telefono")] public string? Telefono { get; set; }
    [JsonPropertyName("correo")] public string? Correo { get; set; }
    [JsonPropertyName("correo_verificado")] public bool Correo_Verificado { get; set; }
    [JsonPropertyName("estado")] public string? Estado { get; set; }
    [JsonPropertyName("tipo_licencia")] public string? Tipo_Licencia { get; set; }
    [JsonPropertyName("fecha_caducidad")] public DateTime? Fecha_Caducidad { get; set; }
    [JsonPropertyName("id_pais")] public int? Id_Pais { get; set; }
    [JsonPropertyName("id_provincia")] public int? Id_Provincia { get; set; }
    [JsonPropertyName("id_canton")] public int? Id_Canton { get; set; }
    [JsonPropertyName("id_distrito")] public int? Id_Distrito { get; set; }
    [JsonPropertyName("pais")] public string? Pais { get; set; }
    [JsonPropertyName("provincia")] public string? Provincia { get; set; }
    [JsonPropertyName("canton")] public string? Canton { get; set; }
    [JsonPropertyName("distrito")] public string? Distrito { get; set; }
}
