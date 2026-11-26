
namespace APINineHalo.Entities
{
    public class CreateTicketRequest
    {
        public int Id_Usuario { get; set; }
        public ulong? Id_Compra { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
    }
}

