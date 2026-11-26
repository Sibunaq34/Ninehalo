using System.Security.Claims;
using System.Text.Json;
using APINineHalo.Services;
using APINineHalo.Entities;
namespace APINineHalo.Endpoints;
public static class PedidoPagoEndpoints {
 public static void MapPedidoPagoEndpoints(this WebApplication app){
  var group=app.MapGroup("/api/compras/carrito/{key:guid}").RequireAuthorization();
  group.MapPost("/pagar/{method}",async(Guid key,string method,JsonElement request,ClaimsPrincipal user,PedidoPagoService service)=>{
   try{var result=await service.PayAsync(user.Actor(),key,method,request);return Results.Json(result.Body,statusCode:result.Status);}catch(CartPurchaseException ex){return Results.Json(new{ok=false,codigo="PEDIDO_BLOQUEADO",mensaje=ex.Message,definitivo=false},statusCode:ex.Status);}
  });
  group.MapGet("/estado-pago",async(Guid key,ClaimsPrincipal user,PedidoPagoService service)=>{
   try{return Results.Ok(new{success=true,data=await service.StatusAsync(user.Actor(),key)});}catch(CartPurchaseException ex){return Results.Json(new{success=false,message=ex.Message},statusCode:ex.Status);}
  });
  group.MapPost("/cancelar",async(Guid key,ClaimsPrincipal user,PedidoPagoService service)=>{
   try{return Results.Ok(new{success=true,data=await service.CancelAsync(user.Actor(),key)});}catch(CartPurchaseException ex){return Results.Json(new{success=false,message=ex.Message},statusCode:ex.Status);}
  });
 }
}
