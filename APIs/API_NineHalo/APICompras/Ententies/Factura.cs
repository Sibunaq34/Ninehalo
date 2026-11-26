namespace APINineHalo.Entities;

public sealed class FacturacionRequest
{
    public string Metodo { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Tipo_Cambio_Id { get; set; }
    public string? Direccion { get; set; }
    public int? Id_Pais { get; set; }
    public int? Id_Provincia { get; set; }
    public int? Id_Canton { get; set; }
    public int? Id_Distrito { get; set; }
}
public sealed record FacturaItem(int id_licencia, string nombre, int cantidad, decimal precio_usd, decimal subtotal_usd, decimal subtotal_crc, ulong[] compras);
public sealed record DatosFactura(string metodo, string nombre, string ubicacion, string direccion, int? id_pais, int? id_provincia, int? id_canton, int? id_distrito,
    decimal tipo_cambio, bool cambio_provisional, decimal total_usd, decimal total_crc, List<FacturaItem> items, string? fuente_tipo_cambio = null, string? fecha_tipo_cambio = null, string? tipo_cambio_id = null, decimal? subtotal_usd = null, decimal descuento_usd = 0, string? cupon = null);
public sealed record FacturaDocumento(string numero, Guid clave_operacion, DateTime fecha_utc, string proveedor, string transaccion, DatosFactura detalle);
