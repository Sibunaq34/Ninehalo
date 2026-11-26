using APINineHalo.Entities;
using APINineHalo.Repository;
using System.Security.Claims;
namespace APINineHalo.Endpoints;

public static class FacturaEndpoints
{
    public static void MapFacturaEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/api/compras").RequireAuthorization();
        group.MapPut("/carrito/{key:guid}/facturacion",async(Guid key,FacturacionRequest request,ClaimsPrincipal user,FacturaRepository invoices)=>
        {
            try {return Results.Ok(new{success=true,data=await invoices.PrepareAsync(user.Actor(),key,request)});}
            catch(CartPurchaseException ex){return Results.Json(new{success=false,message=ex.Message,rechazado=ex.Rejected},statusCode:ex.Status);}
        });
        group.MapGet("/carrito/{key:guid}/facturacion",async(Guid key,ClaimsPrincipal user,FacturaRepository invoices)=>
        {
            var data=await invoices.GetContextAsync(user.Actor(),key);
            return data is null?Results.NotFound(new{success=false,message="No hay datos de facturación para este pedido."}):Results.Ok(new{success=true,data});
        });
        group.MapGet("/carrito/{key:guid}/factura",async(Guid key,ClaimsPrincipal user,FacturaRepository invoices)=>
        {
            var data=await invoices.GetAsync(user.Actor(),key);
            return data is null?Results.NotFound(new{success=false,message="Todavía no hay una factura confirmada para este pedido."}):Results.Ok(new{success=true,data});
        });
        group.MapGet("/facturas",async(ClaimsPrincipal user,FacturaRepository invoices)=>Results.Ok(new{success=true,data=await invoices.ListAsync(user.Actor())}));
    }
}
