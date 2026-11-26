using System.Text.Json.Serialization;
namespace APINineHalo.Entities;
public class CartPurchaseRequest
{
    public string Clave_Operacion { get; set; } = "";
    public List<CartPurchaseItem>? Items { get; set; }
    public string? Cupon { get; set; }
}
public class CartPurchaseItem
{
    public int Id_Licencia { get; set; }
    public int Cantidad { get; set; }
    public decimal? Precio_Unitario { get; set; }
}
public class CartPurchaseDto
{
    [JsonPropertyName("id_compra")] public ulong Id_Compra { get; set; }
    [JsonPropertyName("id_licencia")] public int Id_Licencia { get; set; }
    [JsonPropertyName("precio_pagado")] public decimal Precio_Pagado { get; set; }
    [JsonPropertyName("estado")] public string Estado { get; set; } = "";
}
public class CartPurchaseException(string message, int status=400, bool rejected=true) : Exception(message)
{
    public int Status { get; } = status;
    public bool Rejected { get; } = rejected;
}
