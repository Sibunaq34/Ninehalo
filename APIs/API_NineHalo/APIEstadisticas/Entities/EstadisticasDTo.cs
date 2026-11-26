using System.Text.Json.Serialization;
namespace APINineHalo.Entities;
public class EstadisticaDto
{
    [JsonPropertyName("id_compra")] public ulong Id_Compra { get; set; }
    [JsonPropertyName("id_compra_juego")] public ulong Id_Compra_Juego { get; set; }
    [JsonPropertyName("licencia")] public string Licencia { get; set; } = string.Empty;
    [JsonPropertyName("id_juego")] public int Id_Juego { get; set; }
    [JsonPropertyName("juego")] public string Juego { get; set; } = string.Empty;
    [JsonPropertyName("plataforma")] public string Plataforma { get; set; } = string.Empty;
    [JsonPropertyName("jugadores_baneados")] public ulong Jugadores_Baneados { get; set; }
}
public class ActualizarEstadisticaRequest
{
    public ulong Id_Compra_Juego { get; set; }
    public int Id_Plataforma { get; set; }
    public ulong? Jugadores_Baneados { get; set; }
}
