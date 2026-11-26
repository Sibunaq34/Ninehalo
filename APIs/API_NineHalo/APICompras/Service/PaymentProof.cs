using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace APINineHalo.Services;
public record PaymentReceipt(Guid Pedido, int Usuario, decimal Monto, string Transaccion, string Metodo);
public static class PaymentProof
{
    public static PaymentReceipt Validate(string? proof, string key, int actor, Guid order)
    {
        if(key.Length<32)throw new InvalidOperationException("Configure la clave de firma bancaria.");
        try
        {
            if(proof is null || proof.Length>4096)throw new FormatException();
            var parts=proof.Split('.');
            if(parts.Length!=2)throw new FormatException();
            static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-','+').Replace('_','/').PadRight((value.Length+3)/4*4,'='));
            var expected=HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes(parts[0]));
            if(!CryptographicOperations.FixedTimeEquals(expected,Decode(parts[1])))throw new FormatException();
            using var json=JsonDocument.Parse(Decode(parts[0]));
            var p=json.RootElement;
            if(p.GetProperty("usuario").GetInt32()!=actor || p.GetProperty("pedido").GetGuid()!=order
                || p.GetProperty("metodo").GetString() is not ("tarjeta" or "sinpe"))throw new FormatException();
            var transaction=p.GetProperty("transaccion").GetString()!;
            if(transaction.Length is <1 or >20 || transaction.Any(c=>c<'0'||c>'9') || transaction.All(c=>c=='0'))throw new FormatException();
            var cents=p.GetProperty("centavos").GetString()!;
            if(cents.Length is <1 or >16 || cents.Any(c=>c<'0'||c>'9') || !decimal.TryParse(cents,out var amount)||amount<=0)throw new FormatException();
            return new(order,actor,amount/100m,transaction,p.GetProperty("metodo").GetString()!);
        }
        catch(Exception ex) when(ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {throw new APINineHalo.Entities.CartPurchaseException("La constancia bancaria no es válida para este usuario y pedido.",400);}
    }
}
