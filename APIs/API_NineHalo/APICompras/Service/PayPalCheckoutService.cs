using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;

public sealed record PayPalCreateResult(string OrderId);
public sealed record PayPalCheckoutResult(List<CartPurchaseDto> Compras, string CaptureId);
public sealed class PayPalCheckoutService(PayPalClient client, PayPalRepository repository, CarritoRepository cart, PayPalOptions options)
{
    public Task<PayPalCreateResult> CreateAsync(int actor, Guid key)
    {
        options.RequireEnabled();
        return repository.WithOrderAsync(actor, key, async (connection, order, rows) =>
        {
            if (rows.Any(row => row.Estado != "PENDIENTE"))
                throw new CartPurchaseException("Este pedido ya está activo. Revisa el resultado del pago.", 409, false);
            if (order.Paypal_Order_Id is not null) return new PayPalCreateResult(order.Paypal_Order_Id);
            // Do not silently create another order after an ambiguous creation outlives the retry window.
            if (DateTime.UtcNow - DateTime.SpecifyKind(order.Creado_Utc, DateTimeKind.Utc) > TimeSpan.FromHours(5))
                throw new CartPurchaseException("La creación anterior no pudo confirmarse. Revisa el pedido con soporte antes de iniciar otro pago.", 409, false);
            var id = await client.CreateAsync(actor, key, order.Monto);
            await repository.SaveOrderAsync(connection, actor, key, id);
            return new PayPalCreateResult(id);
        });
    }
    public async Task<PayPalCheckoutResult> CaptureAsync(int actor, Guid key)
    {
        options.RequireEnabled();
        var captured = await repository.WithOrderAsync(actor, key, async (connection, order, rows) =>
        {
            if (order.Paypal_Order_Id is null) throw new CartPurchaseException("Primero inicia el pago con el botón PayPal.", 409, false);
            // Read authoritative provider state first: retries after a lost response must not charge again.
            var remote = await client.GetAsync(order.Paypal_Order_Id);
            var status = PayPalClient.ValidateOrder(remote, order.Paypal_Order_Id, actor, key, order.Monto, order.Moneda);
            if (status == "APPROVED")
            {
                if (rows.Any(row => row.Estado != "PENDIENTE")) throw new CartPurchaseException("El pedido ya fue pagado. No se realizará otro cobro.", 409, false);
                await client.CaptureAsync(actor, key, order.Paypal_Order_Id);
                // GET includes all purchase-unit amounts and the capture status even if POST was minimal.
                remote = await client.GetAsync(order.Paypal_Order_Id);
                status = PayPalClient.ValidateOrder(remote, order.Paypal_Order_Id, actor, key, order.Monto, order.Moneda);
            }
            if (status != "COMPLETED")
                throw new CartPurchaseException("El pago PayPal no está completado. Aprueba el pago o vuelve a revisar su estado.", 409, false);
            var captureId = PayPalClient.CompletedCapture(remote, order.Monto, order.Moneda);
            if (order.Capture_Id is not null && order.Capture_Id != captureId)
                throw new CartPurchaseException("La captura de PayPal no coincide con la registrada.", 409, false);
            await repository.SaveCaptureAsync(connection, actor, key, captureId);
            return (Amount:order.Monto,Capture:captureId);
        });
        var purchases=await cart.ConfirmAsync(actor,key,captured.Amount,captured.Capture,"paypal");
        return new PayPalCheckoutResult(purchases,captured.Capture);
    }
}
