
namespace APINineHalo.Entities
{
    public class PurchaseDto
    {
        public ulong Id_Compra { get; set; }
        public int Id_Licencia { get; set; }
        public DateTime Fecha_Compra { get; set; }
        public DateTime Fecha_Vencimiento { get; set; }
        public string Estado { get; set; } = string.Empty;
    }
}

