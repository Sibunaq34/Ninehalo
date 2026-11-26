using System;
using System.Text.Json.Serialization;

namespace APINineHalo.Entities
{
    public class LicenciaActualDto
    {
        [JsonPropertyName("id_compra")]
        public ulong Id_Compra { get; set; }

        [JsonPropertyName("id_licencia")]
        public int Id_Licencia { get; set; }

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonPropertyName("precio")]
        public decimal Precio { get; set; }

        [JsonPropertyName("fecha_compra")]
        public DateTime Fecha_Compra { get; set; }

        [JsonPropertyName("fecha_vencimiento")]
        public DateTime Fecha_Vencimiento { get; set; }
        [JsonPropertyName("max_juegos")] public int Max_Juegos { get; set; }
        [JsonPropertyName("duracion_meses")] public int Duracion_Meses { get; set; }
    }
}

