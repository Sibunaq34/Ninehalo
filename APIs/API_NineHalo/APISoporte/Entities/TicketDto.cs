
namespace APINineHalo.Entities
{
    public class TicketDto
    {
        public ulong Id_Soporte { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public DateTime Fecha_Creacion { get; set; }
        public string Estado { get; set; } = string.Empty;
    }
}

