using APINineHalo.Entities;
using Dapper;
using System.Data;
using System.Text.Json;
namespace APINineHalo.Repository;

public class CarritoRepository(IDbConnectionF dbFactory, ILogger<CarritoRepository> logger, IConfiguration configuration)
{
    public async Task<List<CartPurchaseDto>> RegisterAsync(int actor, Guid key, List<CartPurchaseItem> items, string? coupon = null)
    {
        var code = string.IsNullOrWhiteSpace(coupon) ? null : coupon.Trim().ToUpperInvariant();
        var promotionsEnabled = configuration.GetValue<bool>("Partners:PromoHub:Enabled");
        if (code is not null && (code.Length > 32 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new CartPurchaseException("El código del cupón no es válido.");


        using var connection=dbFactory.CreateConnection();
        connection.Open();
        // Every retry of one user's operation shares a database-level lock.
        // Unit references survive process restarts without a schema migration.
        string lockName=$"nhcart:{actor}:{key:N}", prefix=$"cart:{actor}:{key:N}:";
        var acquired=await connection.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName,10)",new{lockName});
        if(acquired!=1)throw new CartPurchaseException("El pedido se está procesando. Vuelve a consultar este mismo pedido.",409,false);
        try
        {
            using var transaction=connection.BeginTransaction();
            var user=await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@actor AND estado='ACTIVO' AND correo_verificado=1",new{actor},transaction);
            if(user!=1)throw new CartPurchaseException("Tu cuenta debe estar activa y el correo verificado para registrar un pedido.",403);
            var existing=(await connection.QueryAsync<CartPurchaseDto>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference ORDER BY id_compra",new{actor,reference=prefix+"%"},transaction)).ToList();
            if(existing.Count>0)
            {
                await PedidoLock.LegacyAsync(connection,actor,key,transaction);
                if((await PedidoLock.ReadAsync(connection,actor,key,transaction))?.Estado=="CANCELADO")throw new CartPurchaseException("El pedido fue cancelado. Regresa al carrito para crear uno nuevo.",409,false);
                var frozen = await PedidoResumenRepository.ReadAsync(connection,transaction,actor,key);
                if(frozen is not null){
                    if(!PedidoResumenRepository.Matches(frozen,code,items))throw new CartPurchaseException("Esta operación ya corresponde a otro pedido. Conserva su cupón y clave al reintentar.",409,false);
                    transaction.Commit();return existing;
                }
                var promotion = (promotionsEnabled || code is not null) ? await connection.QuerySingleOrDefaultAsync<Promotion>(
                     $"SELECT codigo AS Code, porcentaje AS Percent FROM {PromotionSchema}.cupon_pedido WHERE id_usuario=@actor AND clave_operacion=@operation",
                    new { actor, operation = key.ToString() }, transaction) : null;
                var matches=code==promotion?.Code && existing.Count==items.Sum(item=>item.Cantidad)
                    && items.All(item=>existing.Count(row=>row.Id_Licencia==item.Id_Licencia &&
                        row.Precio_Pagado==ApplyDiscount(item.Precio_Unitario!.Value, promotion?.Percent ?? 0))==item.Cantidad);
                if(!matches)throw new CartPurchaseException("Esta operación ya corresponde a otro pedido. Consulta su estado antes de continuar.",409,false);
                await PedidoResumenRepository.SaveAsync(connection,transaction,actor,key,code,items,existing);
                transaction.Commit();
                return existing;
            }
            if(code is not null && !promotionsEnabled)throw new CartPurchaseException("La validación de cupones no está disponible.",503);
            if(code is not null && items.Any(item=>item.Id_Licencia==1))throw new CartPurchaseException("El cupón no se puede aplicar si el carrito contiene Básica. Puedes continuar sin cupón.",422);
            if(code=="NINE10" && items.Any(item=>item.Id_Licencia is not (2 or 3)))throw new CartPurchaseException("NINE10 solo aplica a Intermedia y Professional.",422);
            var ids=items.Select(item=>item.Id_Licencia).Order().ToArray();
            var licenses=(await connection.QueryAsync<LicenseSnapshot>("SELECT id_licencia,precio,duracion_meses,max_juegos FROM licencia WHERE id_licencia IN @ids AND activo=1 ORDER BY id_licencia FOR SHARE",new{ids},transaction)).ToDictionary(row=>row.Id_Licencia);
            if(licenses.Count!=items.Count)throw new CartPurchaseException("Una licencia ya no está disponible. Revisa tu carrito.",409);
            if(items.Any(item=>licenses[item.Id_Licencia].Precio!=item.Precio_Unitario))throw new CartPurchaseException("El precio de una licencia cambió. Revisa los precios actuales en tu carrito.",409);
            decimal discount = 0;
            if (code is not null)
            {
                discount = await connection.QueryFirstOrDefaultAsync<decimal>(
                     $"CALL {PromotionSchema}.sp_promohub_reservar(@code,@licenses,@actor,@operation)",
                    new { code, licenses = JsonSerializer.Serialize(ids), actor, operation = key.ToString() }, transaction);
                if (discount <= 0) throw new CartPurchaseException(await CouponErrorAsync(connection,transaction,code),422);
                if(code=="NINE10" && discount!=10)throw new CartPurchaseException("No se pudo confirmar el descuento de NINE10. Puedes continuar sin cupón.",503);
            }
            var result=new List<CartPurchaseDto>();
            int index=0;
            foreach(var item in items.OrderBy(item=>item.Id_Licencia))
            {
                var license=licenses[item.Id_Licencia];
                var price = ApplyDiscount(license.Precio, discount);
                for(int unit=0;unit<item.Cantidad;unit++)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO compra(id_usuario,id_licencia,precio_pagado,duracion_meses_compra,max_juegos_compra,fecha_vencimiento,estado,referencia_pago)
                        VALUES(@actor,@id,@price,@months,@games,DATE_ADD(NOW(),INTERVAL @months MONTH),'PENDIENTE',@reference)
                        """,new{actor,id=item.Id_Licencia,price,months=license.Duracion_Meses,games=license.Max_Juegos,reference=prefix+index++},transaction);
                    var id=await connection.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()",transaction:transaction);
                    await connection.ExecuteAsync("sp_registrar_bitacora",new{p_actor=actor,p_accion="COMPRA_CREADA",p_entidad="compra"},transaction,commandType:CommandType.StoredProcedure);
                    result.Add(new(){Id_Compra=id,Id_Licencia=item.Id_Licencia,Precio_Pagado=price,Estado="PENDIENTE"});
                }
            }
            await PedidoResumenRepository.SaveAsync(connection,transaction,actor,key,code,items,result);
            await connection.ExecuteAsync("INSERT INTO pedido_pago_control(id_usuario,clave_operacion,estado) VALUES(@actor,@operation,'LIBRE')",new{actor,operation=key.ToString()},transaction);
            transaction.Commit();
            return result;
        }
        finally
        {
            try { await connection.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@lockName)",new{lockName}); }
            catch(Exception ex) { logger.LogWarning(ex,"No se pudo liberar el bloqueo del pedido; se descarta la conexión.");
                // A named lock must never survive in a pooled connection.
                if(connection is MySql.Data.MySqlClient.MySqlConnection mysql) MySql.Data.MySqlClient.MySqlConnection.ClearPool(mysql);
            }
        }
    }

    public async Task<List<CartPurchaseDto>> ConfirmAsync(int actor, Guid key, decimal amount, string bankTransaction, string provider = "bank", string? method = null)
    {
        using var connection=dbFactory.CreateConnection();
        connection.Open();
        if(provider is not ("bank" or "paypal") || bankTransaction.Length is <1 or >32 || bankTransaction.Any(c=>!char.IsAsciiLetterOrDigit(c)))
            throw new CartPurchaseException("La referencia de pago no es válida.",400);
        method ??= provider=="paypal"?"paypal":"tarjeta";
        if(method is not ("tarjeta" or "sinpe" or "paypal") || (provider=="paypal")!=(method=="paypal"))throw new CartPurchaseException("El método de pago no es válido.");
        await PedidoLock.TakeAsync(connection,actor,key);
        var lockName=$"nhpay:{provider}:{bankTransaction}";
        if(await connection.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName,10)",new{lockName})!=1){await PedidoLock.ReleaseAsync(connection,actor,key);throw new CartPurchaseException("La confirmación se está procesando. Reintenta sin volver a pagar.",409,false);}
        try
        {
            using var transaction=connection.BeginTransaction();
            var prefix=$"cart:{actor}:{key:N}:";
            var suffix=$"|{provider}:{bankTransaction}";
            var rows=(await connection.QueryAsync<CartPurchaseDto>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference ORDER BY id_compra FOR UPDATE",new{actor,reference=prefix+"%"},transaction)).ToList();
            if(rows.Count==0)throw new CartPurchaseException("El pedido no existe para este usuario.",404);
            if(rows.Sum(row=>row.Precio_Pagado)!=amount)throw new CartPurchaseException("El monto del pago no coincide con el total del pedido.",409);
            var used=await connection.QueryAsync<ulong>("SELECT id_compra FROM compra WHERE referencia_pago LIKE @reference",new{reference="%"+suffix},transaction);
            if(used.Any(id=>!rows.Any(row=>row.Id_Compra==id)))throw new CartPurchaseException("Esta transacción ya se aplicó a otro pedido.",409);
            if(rows.All(row=>row.Estado=="ACTIVA"))
            {
                if(used.Count()!=rows.Count)throw new CartPurchaseException("El pedido ya tiene otro pago confirmado.",409);
                await FacturaRepository.EmitAsync(connection,transaction,actor,key,amount,provider,bankTransaction,method,configuration);
                await connection.ExecuteAsync("UPDATE pedido_pago_control SET estado='CONFIRMADO' WHERE id_usuario=@actor AND clave_operacion=@operation",new{actor,operation=key.ToString()},transaction);
                transaction.Commit();return rows;
            }
            if(rows.Any(row=>row.Estado!="PENDIENTE"))throw new CartPurchaseException("El estado del pedido no permite activar sus licencias.",409);
            var active=await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@actor AND estado='ACTIVO' AND correo_verificado=1",new{actor},transaction);
            if(active!=1)throw new CartPurchaseException("La cuenta debe estar activa y verificada.",403);
            await connection.ExecuteAsync("UPDATE compra SET estado='ACTIVA',fecha_vencimiento=DATE_ADD(NOW(),INTERVAL duracion_meses_compra MONTH),referencia_pago=CONCAT(referencia_pago,@suffix) WHERE id_compra IN @ids",new{suffix,ids=rows.Select(row=>row.Id_Compra).ToArray()},transaction);
            await connection.ExecuteAsync("sp_registrar_bitacora",new{p_actor=actor,p_accion="PAGO_CONFIRMADO",p_entidad="compra"},transaction,commandType:CommandType.StoredProcedure);
            await FacturaRepository.EmitAsync(connection,transaction,actor,key,amount,provider,bankTransaction,method,configuration);
            if (configuration.GetValue<bool>("Partners:PromoHub:Enabled"))
                await connection.ExecuteAsync( $"CALL {PromotionSchema}.sp_promohub_confirmar(@actor,@operation)",
                    new { actor, operation = key.ToString() }, transaction);
            await connection.ExecuteAsync("UPDATE pedido_pago_control SET estado='CONFIRMADO' WHERE id_usuario=@actor AND clave_operacion=@operation",new{actor,operation=key.ToString()},transaction);
            transaction.Commit();
            foreach(var row in rows)row.Estado="ACTIVA";
            return rows;
        }
        finally
        {
            await PedidoLock.ReleaseAsync(connection,actor,key);
            try{await connection.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@lockName)",new{lockName});}
            catch{if(connection is MySql.Data.MySqlClient.MySqlConnection mysql)MySql.Data.MySqlClient.MySqlConnection.ClearPool(mysql);}
        }
    }
    public async Task<PedidoResumen?> GetSummaryAsync(int actor,Guid key){using var connection=dbFactory.CreateConnection();return await PedidoResumenRepository.ReadAsync(connection,null,actor,key);}
    private string PromotionSchema {
        get { var name=configuration["Partners:PromoHub:Database"]??"promohub_db";
            if(name.Length is <1 or >64 || name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new InvalidOperationException("Nombre de base PromoHub inválido.");
            return "`"+name+"`"; }
    }
    private async Task<string> CouponErrorAsync(IDbConnection db,IDbTransaction tx,string code){
        var state=await db.QuerySingleOrDefaultAsync<CouponState>($"SELECT c.activo AS Active,c.vence_utc<=UTC_TIMESTAMP() AS Expired,(SELECT COUNT(*) FROM {PromotionSchema}.cupon_pedido p WHERE p.codigo=c.codigo)>=c.limite_usos AS Exhausted FROM {PromotionSchema}.cupon c WHERE c.codigo=@code",new{code},tx);
        return state is null||!state.Active?"El código de descuento no es válido.":state.Expired?"El cupón está vencido.":state.Exhausted?"El cupón agotó sus usos disponibles.":"El cupón no aplica a las licencias del carrito.";
    }
    private sealed class CouponState {public bool Active{get;set;}public bool Expired{get;set;}public bool Exhausted{get;set;}}
    private static decimal ApplyDiscount(decimal price, decimal percent) =>
        decimal.Round(price * (1 - percent / 100), 2, MidpointRounding.AwayFromZero);

    private sealed record Promotion(string Code, decimal Percent);

    private class LicenseSnapshot
    {
        public int Id_Licencia {get;set;}
        public decimal Precio {get;set;}
        public int Duracion_Meses {get;set;}
        public int Max_Juegos {get;set;}
    }
}
