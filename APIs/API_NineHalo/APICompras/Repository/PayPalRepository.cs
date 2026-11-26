using System.Data;
using Dapper;
using APINineHalo.Entities;
using APINineHalo.Services;

namespace APINineHalo.Repository;

public sealed class PayPalOrder
{
    public decimal Monto { get; set; }
    public string Moneda { get; set; } = "";
    public string Entorno { get; set; } = "";
    public string? Paypal_Order_Id { get; set; }
    public string? Capture_Id { get; set; }
    public DateTime Creado_Utc { get; set; }
}

public sealed class PayPalRepository(IDbConnectionF factory, PayPalOptions options)
{
    // The operation lock also serializes registration; it is released even on HTTP errors.
    public async Task<T> WithOrderAsync<T>(int actor, Guid key, Func<IDbConnection, PayPalOrder, List<CartPurchaseDto>, Task<T>> action)
    {
        using var connection = factory.CreateConnection();
        connection.Open();
        var lockName = $"nhcart:{actor}:{key:N}";
        if (await connection.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName,10)", new { lockName }) != 1)
            throw new CartPurchaseException("Este pedido se está procesando. Espera y vuelve a revisar el pago.", 409, false);
        try
        {
            var active = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@actor AND estado='ACTIVO' AND correo_verificado=1", new { actor });
            if (active != 1) throw new CartPurchaseException("La cuenta debe estar activa y verificada.", 403);
            var rows = (await connection.QueryAsync<CartPurchaseDto>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference ORDER BY id_compra", new { actor, reference = $"cart:{actor}:{key:N}:%" })).ToList();
            if (rows.Count == 0) throw new CartPurchaseException("El pedido no existe para este usuario.", 404);
            if (rows.Any(row => row.Estado is not ("PENDIENTE" or "ACTIVA"))) throw new CartPurchaseException("El pedido no permite pagos.", 409);
            var amount = rows.Sum(row => row.Precio_Pagado);
            if (amount <= 0) throw new CartPurchaseException("El pedido debe tener un importe mayor a cero.", 400);
            var parameters = new { actor, key = key.ToString(), amount, currency = options.Currency, environment = options.Environment };
            var order = await connection.QuerySingleOrDefaultAsync<PayPalOrder>("SELECT * FROM checkout_paypal WHERE id_usuario=@actor AND clave_operacion=@key", parameters);
            if (order is null)
            {
                if (rows.Any(row => row.Estado != "PENDIENTE")) throw new CartPurchaseException("Este pedido ya fue pagado con otro medio.", 409);
                var control=await PedidoLock.ReadAsync(connection,actor,key);
                if(control?.Estado is not ("LIBRE" or "RECHAZADO"))throw new CartPurchaseException("Hay un pago pendiente de verificar. No se iniciará PayPal.",409,false);
                await connection.ExecuteAsync("UPDATE pedido_pago_control SET estado='EN_CURSO',metodo='paypal',intento=NULL,codigo=NULL,respuesta=NULL WHERE id_usuario=@actor AND clave_operacion=@key",parameters);
                await connection.ExecuteAsync("INSERT INTO checkout_paypal(id_usuario,clave_operacion,monto,moneda,entorno,creado_utc) VALUES(@actor,@key,@amount,@currency,@environment,UTC_TIMESTAMP())", parameters);
                order = (await connection.QuerySingleAsync<PayPalOrder>("SELECT * FROM checkout_paypal WHERE id_usuario=@actor AND clave_operacion=@key", parameters));
            }
            if (order.Monto != amount || order.Moneda != options.Currency || order.Entorno != options.Environment)
                throw new CartPurchaseException("La configuración o el importe del pedido cambió. Revisa el pago antes de continuar.", 409, false);
            return await action(connection, order, rows);
        }
        finally
        {
            try { await connection.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@lockName)", new { lockName }); }
            catch { if (connection is MySql.Data.MySqlClient.MySqlConnection mysql) MySql.Data.MySqlClient.MySqlConnection.ClearPool(mysql); }
        }
    }
    public Task SaveOrderAsync(IDbConnection connection, int actor, Guid key, string orderId) =>
        connection.ExecuteAsync("UPDATE checkout_paypal SET paypal_order_id=@orderId WHERE id_usuario=@actor AND clave_operacion=@key", new { actor, key = key.ToString(), orderId });
    public Task SaveCaptureAsync(IDbConnection connection, int actor, Guid key, string captureId) =>
        connection.ExecuteAsync("UPDATE checkout_paypal SET capture_id=@captureId WHERE id_usuario=@actor AND clave_operacion=@key", new { actor, key = key.ToString(), captureId });
}
