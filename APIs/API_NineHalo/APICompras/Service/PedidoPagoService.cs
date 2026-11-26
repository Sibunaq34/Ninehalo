using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Dapper;
using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;

public sealed record BankResult(int Status,JsonElement Body);
public sealed class PedidoPagoService(IDbConnectionF factory,IHttpClientFactory clients,IConfiguration config,IWebHostEnvironment environment){
 string Secret=>config["PAYMENT_SIGNING_KEY"]??File.ReadAllText(Path.Combine(environment.ContentRootPath,".payment-signing-key")).Trim();
 string Ticket(int actor,Guid key,Guid attempt,string method,decimal amount,string action){
  static string B64(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
  var payload=B64(JsonSerializer.SerializeToUtf8Bytes(new{usuario=actor,pedido=key,intento=attempt,metodo=method,centavos=(amount*100).ToString("0",System.Globalization.CultureInfo.InvariantCulture),accion=action,exp=DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()}));
  return payload+"."+B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret),Encoding.UTF8.GetBytes(payload)));
 }
 async Task<List<CartPurchaseDto>> Rows(IDbConnection db,int actor,Guid key,IDbTransaction? tx=null){var rows=(await db.QueryAsync<CartPurchaseDto>("SELECT id_compra,id_licencia,precio_pagado,estado FROM compra WHERE id_usuario=@actor AND referencia_pago LIKE @reference ORDER BY id_compra FOR UPDATE",new{actor,reference=$"cart:{actor}:{key:N}:%"},tx)).ToList();if(rows.Count==0)throw new CartPurchaseException("El pedido no existe para este usuario.",404);return rows;}
 public async Task<BankResult> PayAsync(int actor,Guid key,string method,JsonElement data){
  if(method is not ("sinpe" or "tarjeta"))throw new CartPurchaseException("Selecciona un método bancario válido.");
  using var db=factory.CreateConnection();db.Open();await PedidoLock.TakeAsync(db,actor,key);
  try{
   if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@actor AND estado='ACTIVO' AND correo_verificado=1",new{actor})!=1)throw new CartPurchaseException("La cuenta debe estar activa y verificada.",403);
   var rows=await Rows(db,actor,key);if(rows.Any(r=>r.Estado!="PENDIENTE"))throw new CartPurchaseException("Este pedido ya no permite pagos.",409,false);
   var state=await PedidoLock.ReadAsync(db,actor,key);if(state?.Estado is not ("LIBRE" or "RECHAZADO"))throw new CartPurchaseException("Hay un pago pendiente de verificar. No se realizará otro cobro.",409,false);
   if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM checkout_paypal WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()})>0)throw new CartPurchaseException("Revisa el pago PayPal antes de cambiar de método.",409,false);
   var amount=rows.Sum(r=>r.Precio_Pagado);var attempt=Guid.NewGuid();
   await db.ExecuteAsync("UPDATE pedido_pago_control SET estado='EN_CURSO',metodo=@method,intento=@attempt,codigo=NULL,respuesta=NULL WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString(),method,attempt=attempt.ToString()});
   var body=new Dictionary<string,object?>{{"monto",amount},{"pedido",new{id_usuario=actor,clave_operacion=key}}};
   foreach(var field in method=="sinpe"?new[]{"telefono"}:new[]{"numeroTarjeta","cvv","fechaVencimiento"})if(data.TryGetProperty(field,out var value)&&value.ValueKind==JsonValueKind.String)body[field]=value.GetString();
   try{
    using var request=new HttpRequestMessage(HttpMethod.Post,Destination($"/api/pagos/{method}"));request.Headers.Add("X-NineHalo-Intent",Ticket(actor,key,attempt,method,amount,"pagar"));request.Content=JsonContent.Create(body);
    using var client=clients.CreateClient("Banco");using var response=await client.SendAsync(request);var payload=await response.Content.ReadFromJsonAsync<JsonElement>();
    return await StoreAsync(db,actor,key,method,amount,new((int)response.StatusCode,payload));
   }catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException){await db.ExecuteAsync("UPDATE pedido_pago_control SET estado='INCIERTO' WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()});return Uncertain();}
  }finally{await PedidoLock.ReleaseAsync(db,actor,key);}
 }
 Uri Destination(string path){var root=config["Banco:GatewayUrl"]??"http://localhost:5253";if(!Uri.TryCreate(root,UriKind.Absolute,out var uri)||(!uri.IsLoopback&&uri.Scheme!="https"))throw new InvalidOperationException("Configura la URL del gateway bancario.");return new Uri(uri,path);}
 static BankResult Uncertain()=>new(409,JsonSerializer.SerializeToElement(new{ok=false,codigo="PAGO_INCIERTO",mensaje="No se pudo verificar el pago. No vuelvas a pagar ni canceles hasta consultar su resultado con el banco o soporte.",definitivo=false}));
 async Task<BankResult> StoreAsync(IDbConnection db,int actor,Guid key,string method,decimal amount,BankResult result){
  var p=result.Body;string state="INCIERTO",code="PAGO_INCIERTO";
  if(result.Status==200&&p.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True){
   var proof=p.GetProperty("data").GetProperty("constancia").GetString();var receipt=PaymentProof.Validate(proof,Secret,actor,key);if(receipt.Monto!=amount||receipt.Metodo!=method)throw new InvalidOperationException("El comprobante no coincide.");state="CONFIRMADO";code="PAGO_CONFIRMADO";
  }else if(result.Status is 400 or 404 or 422 && p.TryGetProperty("ok",out var rejected)&&rejected.ValueKind==JsonValueKind.False &&p.TryGetProperty("definitivo",out var definite)&&definite.ValueKind==JsonValueKind.True&&p.TryGetProperty("codigo",out var errorCode)&&errorCode.ValueKind==JsonValueKind.String){state="RECHAZADO";code=errorCode.GetString()!;}
  await db.ExecuteAsync("UPDATE pedido_pago_control SET estado=@state,codigo=@code,respuesta=@json WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString(),state,code,json=p.GetRawText()});
  return state=="INCIERTO"?Uncertain():result;
 }
 public async Task<object> StatusAsync(int actor,Guid key){
  using var db=factory.CreateConnection();db.Open();await PedidoLock.TakeAsync(db,actor,key);
  try{var rows=await Rows(db,actor,key);await PedidoLock.LegacyAsync(db,actor,key);var state=(await PedidoLock.ReadAsync(db,actor,key))!;
   if(rows.All(r=>r.Estado=="ACTIVA"))return new{estado="ACTIVA",metodo=state.Metodo,bloqueado=true,cancelable=false,intento=state.Intento,codigo="PAGO_CONFIRMADO",respuesta=state.Respuesta is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(state.Respuesta)};
   if(state.Estado is "EN_CURSO" or "INCIERTO" && state.Metodo is "sinpe" or "tarjeta" &&state.Intento is Guid attempt){
    try{using var request=new HttpRequestMessage(HttpMethod.Get,Destination($"/api/pagos/intentos/{attempt}"));request.Headers.Add("X-NineHalo-Intent",Ticket(actor,key,attempt,state.Metodo,rows.Sum(r=>r.Precio_Pagado),"consultar"));using var client=clients.CreateClient("Banco");using var response=await client.SendAsync(request);var result=await response.Content.ReadFromJsonAsync<JsonElement>();
     if(response.IsSuccessStatusCode&&result.GetProperty("encontrado").GetBoolean()&&result.GetProperty("http_status").ValueKind==JsonValueKind.Number){await StoreAsync(db,actor,key,state.Metodo,rows.Sum(r=>r.Precio_Pagado),new(result.GetProperty("http_status").GetInt32(),result.GetProperty("respuesta")));state=(await PedidoLock.ReadAsync(db,actor,key))!;}
    }catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException){}
   }
   var paypal=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM checkout_paypal WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()});
   var free=(state.Estado is "LIBRE" or "RECHAZADO") && paypal==0;
   if(paypal>0)state.Metodo="paypal";
   return new{estado=state.Estado,metodo=state.Metodo,bloqueado=!free,cancelable=free,intento=state.Intento,codigo=state.Codigo,respuesta=state.Respuesta is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(state.Respuesta)};
  }finally{await PedidoLock.ReleaseAsync(db,actor,key);}
 }
 public async Task<object> CancelAsync(int actor,Guid key){
  using var db=factory.CreateConnection();db.Open();await PedidoLock.TakeAsync(db,actor,key);
  try{using var tx=db.BeginTransaction();var rows=await Rows(db,actor,key,tx);var state=await PedidoLock.ReadAsync(db,actor,key,tx);
   if(state?.Estado=="CANCELADO"){tx.Commit();return new{clave_operacion=key,estado="CANCELADO"};}
   if(rows.Any(r=>r.Estado!="PENDIENTE")||state?.Estado is not ("LIBRE" or "RECHAZADO"))throw new CartPurchaseException("No se puede cancelar: el pedido tiene un pago activo, incierto o confirmado. Revisa su resultado con el proveedor o soporte.",409,false);
   if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM checkout_paypal WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()},tx)>0)throw new CartPurchaseException("Revisa el pago PayPal antes de cancelar.",409,false);
   var schema=config["Partners:PromoHub:Database"]??"promohub_db";if(schema.Length is <1 or >64||schema.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new InvalidOperationException();
   if(config.GetValue<bool>("Partners:PromoHub:Enabled"))await db.ExecuteAsync($"DELETE FROM `{schema}`.cupon_pedido WHERE id_usuario=@actor AND clave_operacion=@key AND confirmado_utc IS NULL",new{actor,key=key.ToString()},tx);
   await db.ExecuteAsync("UPDATE compra SET estado='CANCELADA' WHERE id_compra IN @ids",new{ids=rows.Select(r=>r.Id_Compra).ToArray()},tx);
   await db.ExecuteAsync("UPDATE pedido_pago_control SET estado='CANCELADO' WHERE id_usuario=@actor AND clave_operacion=@key",new{actor,key=key.ToString()},tx);tx.Commit();return new{clave_operacion=key,estado="CANCELADO"};
  }finally{await PedidoLock.ReleaseAsync(db,actor,key);}
 }
}
