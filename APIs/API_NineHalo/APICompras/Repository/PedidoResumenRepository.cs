using APINineHalo.Entities;
using Dapper;
using System.Data;
using System.Text.Json;
namespace APINineHalo.Repository;

public sealed record PedidoLinea(int id_licencia, int cantidad, decimal precio_unitario, decimal precio_pagado, decimal subtotal, decimal descuento, decimal total);
public sealed record PedidoResumen(string? cupon, decimal subtotal, decimal descuento, decimal total, List<PedidoLinea> items);

public static class PedidoResumenRepository
{
    public static async Task<PedidoResumen?> ReadAsync(IDbConnection db, IDbTransaction? tx, int actor, Guid key)
    {
        var json = await db.QuerySingleOrDefaultAsync<string>("SELECT contenido FROM pedido_resumen WHERE id_usuario=@actor AND clave_operacion=@key", new { actor, key=key.ToString() }, tx);
        return json is null ? null : JsonSerializer.Deserialize<PedidoResumen>(json);
    }
    public static async Task SaveAsync(IDbConnection db, IDbTransaction tx, int actor, Guid key, string? code, List<CartPurchaseItem> items, List<CartPurchaseDto> purchases)
    {
        var lines = items.OrderBy(item=>item.Id_Licencia).Select(item => {
            var paid = purchases.Where(row=>row.Id_Licencia==item.Id_Licencia).ToArray();
            var subtotal = item.Precio_Unitario!.Value * item.Cantidad;
            var total = paid.Sum(row=>row.Precio_Pagado);
            return new PedidoLinea(item.Id_Licencia,item.Cantidad,item.Precio_Unitario.Value,paid[0].Precio_Pagado,subtotal,subtotal-total,total);
        }).ToList();
        var summary = new PedidoResumen(code,lines.Sum(row=>row.subtotal),lines.Sum(row=>row.descuento),lines.Sum(row=>row.total),lines);
        await db.ExecuteAsync("INSERT INTO pedido_resumen(id_usuario,clave_operacion,contenido) VALUES(@actor,@key,@json)",new{actor,key=key.ToString(),json=JsonSerializer.Serialize(summary)},tx);
    }
    public static bool Matches(PedidoResumen summary, string? code, List<CartPurchaseItem> items) =>
        summary.cupon==code && summary.items.Count==items.Count && items.All(item=>summary.items.Any(row=>row.id_licencia==item.Id_Licencia && row.cantidad==item.Cantidad && row.precio_unitario==item.Precio_Unitario));
}
