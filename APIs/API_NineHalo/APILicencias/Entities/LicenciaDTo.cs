using System.Text.Json.Serialization;
namespace APINineHalo.Entities;
public class LicenciaDto
{
    [JsonPropertyName("id_licencia")] public int Id_licencia { get; set; }
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    [JsonPropertyName("descripcion")] public string? Descripcion { get; set; }
    [JsonPropertyName("precio")] public decimal Precio { get; set; }
    [JsonPropertyName("duracion_meses")] public int Duracion_Meses { get; set; }
    [JsonPropertyName("max_juegos")] public int Max_Juegos { get; set; }
    [JsonPropertyName("detalles")] public List<string> Detalles { get; set; } = new();
}
public class ActualizarLicenciaRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal? Precio { get; set; }
    public int? Duracion_Meses { get; set; }
    public int? Max_Juegos { get; set; }
    public bool? Activo { get; set; }
}
