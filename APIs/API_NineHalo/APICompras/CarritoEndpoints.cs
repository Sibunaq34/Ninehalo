using APINineHalo.Services;
using APINineHalo.Entities;
using APINineHalo.Repository;
using System.Security.Claims;
namespace APINineHalo.Endpoints;
public static class CarritoEndpoints
{
    public static void MapCarritoEndpoints(this WebApplication app)
    {
        app.MapPost("/api/compras/carrito/{key:guid}/confirmar-pago",async(Guid key,PaymentConfirmation request,ClaimsPrincipal user,CarritoRepository repository,FacturaRepository invoices,IConfiguration configuration,IWebHostEnvironment environment)=>
        {
            try
            {
                var secret=configuration["PAYMENT_SIGNING_KEY"] ?? File.ReadAllText(Path.Combine(environment.ContentRootPath,".payment-signing-key")).Trim();
                var receipt=PaymentProof.Validate(request.Constancia,secret,user.Actor(),key);
                var purchases=await repository.ConfirmAsync(user.Actor(),key,receipt.Monto,receipt.Transaccion,"bank",receipt.Metodo);
                return Results.Ok(new{success=true,message="Pago confirmado y licencias activadas.",data=new{clave_operacion=key,estado="ACTIVA",total=purchases.Sum(p=>p.Precio_Pagado),compras=purchases,factura=await invoices.GetAsync(user.Actor(),key)}});
            }
            catch(CartPurchaseException ex){return Results.Json(new{success=false,message=ex.Message},statusCode:ex.Status);}
        }).RequireAuthorization().WithName("ConfirmarPagoCarrito");
        app.MapPost("/api/compras/carrito",async(CartPurchaseRequest request,ClaimsPrincipal user,CarritoRepository repository,FacturaRepository invoices)=>
        {
            if(!Guid.TryParse(request.Clave_Operacion,out var key)||key==Guid.Empty||request.Items is null||request.Items.Count is <1 or >10
                ||request.Items.Any(item=>item is null||item.Id_Licencia<=0||item.Cantidad is <1 or >20||item.Precio_Unitario is null or <0 or >99999999.99m||decimal.Round(item.Precio_Unitario.Value,2)!=item.Precio_Unitario)
                ||request.Items.Select(item=>item.Id_Licencia).Distinct().Count()!=request.Items.Count||request.Items.Sum(item=>item.Cantidad)>50)
                return Results.Json(new{success=false,message="El carrito, las cantidades o los precios no son válidos.",rechazado=true,data=(object?)null},statusCode:400);
            try
            {
                var purchases=await repository.RegisterAsync(user.Actor(),key,request.Items,request.Cupon);
                var summary=await repository.GetSummaryAsync(user.Actor(),key)??throw new InvalidOperationException("Falta el resumen del pedido.");
                return Results.Json(new{success=true,message="Pedido registrado, pendiente de pago.",data=new{
                    clave_operacion=key,estado=purchases.All(p=>p.Estado=="ACTIVA")?"ACTIVA":"PENDIENTE",total=summary.total,subtotal=summary.subtotal,descuento=summary.descuento,cupon=summary.cupon,items=summary.items,compras=purchases,factura=purchases.All(p=>p.Estado=="ACTIVA")?await invoices.GetAsync(user.Actor(),key):null
                }},statusCode:201);
            }
            catch(CartPurchaseException ex)
            {return Results.Json(new{success=false,message=ex.Message,rechazado=ex.Rejected,cupon_rechazado=!string.IsNullOrWhiteSpace(request.Cupon)&&ex.Status is 400 or 422,data=(object?)null},statusCode:ex.Status);}
        }).RequireAuthorization().WithName("RegistrarCarrito");
    }
}

public record PaymentConfirmation(string? Constancia);
