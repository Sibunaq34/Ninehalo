using System.Data;
using Dapper;
using APINineHalo.Entities;
namespace APINineHalo.Repository;

public sealed class PagoControl {
 public string Estado {get;set;}="LEGADO";
 public string? Metodo {get;set;}
 public Guid? Intento {get;set;}
 public string? Codigo {get;set;}
 public string? Respuesta {get;set;}
}
public static class PedidoLock {
 public static async Task TakeAsync(IDbConnection db,int actor,Guid key){if(await db.ExecuteScalarAsync<int>("SELECT GET_LOCK(@name,10)",new{name=$"nhcart:{actor}:{key:N}"})!=1)throw new CartPurchaseException("El pedido se está procesando. Vuelve a consultar su estado.",409,false);}
 public static async Task ReleaseAsync(IDbConnection db,int actor,Guid key){try{await db.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@name)",new{name=$"nhcart:{actor}:{key:N}"});}catch{if(db is MySql.Data.MySqlClient.MySqlConnection mysql)MySql.Data.MySqlClient.MySqlConnection.ClearPool(mysql);}}
 public static Task<PagoControl?> ReadAsync(IDbConnection db,int actor,Guid key,IDbTransaction? tx=null)=>db.QuerySingleOrDefaultAsync<PagoControl>("SELECT estado,metodo,intento,codigo,respuesta FROM pedido_pago_control WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()},tx);
 public static Task LegacyAsync(IDbConnection db,int actor,Guid key,IDbTransaction? tx=null)=>db.ExecuteAsync("INSERT IGNORE INTO pedido_pago_control(id_usuario,clave_operacion,estado) VALUES(@actor,@key,'LEGADO')",new{actor,key=key.ToString()},tx);
}
