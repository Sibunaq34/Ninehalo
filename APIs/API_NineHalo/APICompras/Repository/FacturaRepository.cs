using APINineHalo.Services;
using System.Data;
using System.Text.Json;
using APINineHalo.Entities;
using Dapper;

namespace APINineHalo.Repository;

public sealed class FacturaRepository(IDbConnectionF factory, BccrService bccr)
{
    public async Task<DatosFactura> PrepareAsync(int actor, Guid key, FacturacionRequest request)
    {
        if (request.Metodo is not ("tarjeta" or "sinpe" or "paypal")) throw new CartPurchaseException("Selecciona un método de pago válido.");
        // Fetch before acquiring database row locks; never hold a transaction while contacting BCCR.
        var alreadyIssued = await GetAsync(actor,key);
        if(alreadyIssued is not null)return alreadyIssued.detalle;
        var stored = await GetContextAsync(actor,key);
        var quote = stored is { cambio_provisional: false, fuente_tipo_cambio: "BCCR", fecha_tipo_cambio: not null, tipo_cambio_id: not null } && stored.metodo==request.Metodo
            ? new BccrQuote(stored.tipo_cambio,stored.fecha_tipo_cambio,"BCCR",318,false,DateTimeOffset.UtcNow,stored.tipo_cambio_id)
            : await bccr.GetAsync();
        if(request.Tipo_Cambio_Id is not null && request.Tipo_Cambio_Id!=quote.id)
            throw new CartPurchaseException("El tipo de cambio cambió. Vuelve al formulario para revisar el total actualizado antes de pagar.",409,false);
        using var connection = factory.CreateConnection(); connection.Open();
        using var transaction = connection.BeginTransaction();
        var rows = (await connection.QueryAsync<InvoiceRow>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference ORDER BY id_compra FOR UPDATE", new { actor, reference = $"cart:{actor}:{key:N}:%" }, transaction)).ToList();
        if (rows.Count == 0) throw new CartPurchaseException("El pedido no existe para este usuario.",404);
        var issued = await GetAsync(connection, transaction, actor, key);
        if (issued is not null) return issued.detalle;
        if (rows.Any(row => row.Estado != "PENDIENTE")) throw new CartPurchaseException("No se puede cambiar la facturación de este pedido.",409);
        var person = await connection.QuerySingleOrDefaultAsync<PersonSnapshot>("SELECT p.nombre,p.apellidos FROM usuario u JOIN persona p ON p.id_usuario=u.id_usuario WHERE u.id_usuario=@actor AND u.estado='ACTIVO' AND u.correo_verificado=1",new { actor },transaction);
        if (person is null) throw new CartPurchaseException("La cuenta debe estar activa y verificada.",403);
        string name = string.Join(" ", new[]{ person.Nombre, person.Apellidos }.Where(s=>!string.IsNullOrWhiteSpace(s))), location = "", address = "";
        if(request.Metodo == "tarjeta")
        {
            name = request.Nombre?.Trim() ?? ""; address = (request.Direccion?.Trim() ?? "").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");
            if(name.Length is <2 or >150 || name.Any(char.IsControl) || address.Length is <5 or >250 || address.Any(char.IsControl))
                throw new CartPurchaseException("Completa el nombre y la dirección de facturación.");
            location = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT CONCAT_WS(', ',pa.descripcion,pr.descripcion,c.descripcion,d.descripcion)
                FROM distritos d JOIN cantones c ON c.id_canton=d.id_canton
                JOIN provincias pr ON pr.id_provincia=c.id_provincia JOIN pais pa ON pa.id_pais=pr.id_pais
                WHERE pa.id_pais=@country AND pr.id_provincia=@province AND c.id_canton=@canton AND d.id_distrito=@district
                """,new{country=request.Id_Pais,province=request.Id_Provincia,canton=request.Id_Canton,district=request.Id_Distrito},transaction) ?? "";
            if(location.Length == 0) throw new CartPurchaseException("Selecciona una ubicación de facturación válida.");
        }
        var snapshot = await SnapshotAsync(connection,transaction,rows,request.Metodo,name,location,address,quote.venta,
            request.Metodo=="tarjeta"?request:null,quote);
        snapshot=await WithOrderSummaryAsync(connection,transaction,actor,key,snapshot);
        await connection.ExecuteAsync("INSERT INTO compra_facturacion(id_usuario,clave_operacion,contenido) VALUES(@actor,@key,@json) ON DUPLICATE KEY UPDATE contenido=@json",
            new{actor,key=key.ToString(),json=JsonSerializer.Serialize(snapshot)},transaction);
        transaction.Commit(); return snapshot;
    }

    public async Task<DatosFactura?> GetContextAsync(int actor, Guid key)
    {
        using var connection=factory.CreateConnection();
        var json=await connection.QuerySingleOrDefaultAsync<string>("SELECT contenido FROM compra_facturacion WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()});
        return json is null?null:JsonSerializer.Deserialize<DatosFactura>(json);
    }
    public async Task<FacturaDocumento?> GetAsync(int actor, Guid key)
    { using var connection=factory.CreateConnection();return await GetAsync(connection,null,actor,key); }
    private static async Task<FacturaDocumento?> GetAsync(IDbConnection connection, IDbTransaction? transaction, int actor, Guid key)
    {
        var json=await connection.QuerySingleOrDefaultAsync<string>("SELECT contenido FROM factura WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()},transaction);
        return json is null?null:JsonSerializer.Deserialize<FacturaDocumento>(json);
    }
    public async Task<IEnumerable<object>> ListAsync(int actor)
    {
        using var connection=factory.CreateConnection();
        return await connection.QueryAsync("SELECT numero,clave_operacion,DATE_FORMAT(fecha_utc,'%Y-%m-%dT%H:%i:%sZ') AS fecha_utc,total_usd,total_crc,metodo FROM factura WHERE id_usuario=@actor ORDER BY id_factura DESC LIMIT 100",new{actor});
    }
    // Called inside the SAME transaction that activates the purchased licenses.
    public static async Task EmitAsync(IDbConnection connection, IDbTransaction transaction, int actor, Guid key, decimal total, string provider, string reference, string method, IConfiguration configuration)
    {
        var existing=await GetAsync(connection,transaction,actor,key);
        if(existing is not null)
        {
            if(existing.proveedor!=provider || existing.transaccion!=reference)throw new CartPurchaseException("La factura ya corresponde a otro pago.",409);
            await EnqueuePartnersAsync(connection, transaction, configuration, actor, existing);
            return;
        }
        var context=await connection.QuerySingleOrDefaultAsync<string>("SELECT contenido FROM compra_facturacion WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()},transaction);
        var details=context is null?null:JsonSerializer.Deserialize<DatosFactura>(context);
        if(details is null)
        {
            // Recover payments created before billing details were introduced.
            var rows=(await connection.QueryAsync<InvoiceRow>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference",new{actor,reference=$"cart:{actor}:{key:N}:%"},transaction)).ToList();
            var name=await connection.QuerySingleOrDefaultAsync<string>("SELECT CONCAT_WS(' ',nombre,apellidos) FROM persona WHERE id_usuario=@actor",new{actor},transaction)??"Cliente Nine Halo";
            details=await SnapshotAsync(connection,transaction,rows,method,name,"","",500m,null);
        }
        details=await WithOrderSummaryAsync(connection,transaction,actor,key,details);
        if(details.total_usd!=total || details.metodo!=method)throw new CartPurchaseException("La facturación no coincide con el pago confirmado.",409);
        var invoice=new FacturaDocumento("NH-"+Guid.NewGuid().ToString("N").ToUpperInvariant(),key,DateTime.UtcNow,provider,reference,details);
        await connection.ExecuteAsync("""
            INSERT INTO factura(numero,id_usuario,clave_operacion,proveedor,transaccion,metodo,fecha_utc,total_usd,tipo_cambio,total_crc,contenido)
            VALUES(@number,@actor,@key,@provider,@reference,@method,UTC_TIMESTAMP(),@total,@rate,@crc,@json)
            """,new{number=invoice.numero,actor,key=key.ToString(),provider,reference,method,total,rate=details.tipo_cambio,crc=details.total_crc,json=JsonSerializer.Serialize(invoice)},transaction);
        await EnqueuePartnersAsync(connection, transaction, configuration, actor, invoice);
    }
    private static async Task<DatosFactura> WithOrderSummaryAsync(IDbConnection db,IDbTransaction tx,int actor,Guid key,DatosFactura details){
        var summary=await PedidoResumenRepository.ReadAsync(db,tx,actor,key);
        return summary is null?details:details with {subtotal_usd=summary.subtotal,descuento_usd=summary.descuento,cupon=summary.cupon};
    }
    private static async Task EnqueuePartnersAsync(IDbConnection connection, IDbTransaction transaction,
        IConfiguration configuration, int actor, FacturaDocumento invoice)
    {
        await PartnerOutbox.EnqueueAsync(connection, transaction, configuration, "ContaCloud", invoice.numero, new
        {
            factura = invoice.numero,
            fecha = invoice.fecha_utc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            total_usd = invoice.detalle.total_usd,
            total_crc = invoice.detalle.total_crc
        });
        await PartnerOutbox.NotifyAsync(connection, transaction, configuration, actor, $"factura:{invoice.numero}",
            $"NineHalo confirmó tu compra. Factura {invoice.numero}.");
    }

    private static async Task<DatosFactura> SnapshotAsync(IDbConnection connection,IDbTransaction transaction,List<InvoiceRow> rows,string method,string name,string location,string address,decimal rate,FacturacionRequest? request,BccrQuote? quote=null)
    {
        var ids=rows.Select(row=>row.Id_Licencia).Distinct().ToArray();
        var names=(await connection.QueryAsync<LicenseName>("SELECT id_licencia,nombre FROM licencia WHERE id_licencia IN @ids",new{ids},transaction)).ToDictionary(row=>row.Id_Licencia,row=>row.Nombre);
        decimal Convert(decimal usd)=>decimal.Round(usd*rate,2,MidpointRounding.AwayFromZero);
        var items=rows.GroupBy(row=>new{row.Id_Licencia,row.Precio_Pagado}).Select(group=>new FacturaItem(group.Key.Id_Licencia,names.GetValueOrDefault(group.Key.Id_Licencia,$"Licencia #{group.Key.Id_Licencia}"),group.Count(),group.Key.Precio_Pagado,group.Sum(row=>row.Precio_Pagado),Convert(group.Sum(row=>row.Precio_Pagado)),group.Select(row=>row.Id_Compra).ToArray())).ToList();
        return new(method,name,location,address,request?.Id_Pais,request?.Id_Provincia,request?.Id_Canton,request?.Id_Distrito,rate,quote is null,items.Sum(row=>row.subtotal_usd),items.Sum(row=>row.subtotal_crc),items,quote?.fuente,quote?.fecha,quote?.id);
    }
    private sealed class InvoiceRow {public ulong Id_Compra{get;set;} public int Id_Licencia{get;set;} public decimal Precio_Pagado{get;set;} public string Estado{get;set;}="";}
    private sealed class LicenseName {public int Id_Licencia{get;set;} public string Nombre{get;set;}="";}
    private sealed class PersonSnapshot {public string Nombre{get;set;}="";public string Apellidos{get;set;}="";}
}
