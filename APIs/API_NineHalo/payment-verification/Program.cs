using APINineHalo.Services;
using APINineHalo.Entities;
var key="payment-verification-key-for-local-tests-only";
var proof=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"../../..","receipt.txt"));
var order=Guid.Parse("1a111111-1111-4111-8111-111111111111");
var receipt=PaymentProof.Validate(proof,key,7,order);
if(receipt.Monto!=12.34m||receipt.Transaccion!="42")throw new Exception("Receipt mismatch");
int rejected=0;
void Reject(Action action){try{action();}catch(CartPurchaseException){rejected++;return;}throw new Exception("Accepted invalid receipt");}
Reject(()=>PaymentProof.Validate(proof,key,8,order));
Reject(()=>PaymentProof.Validate(proof,key,7,Guid.NewGuid()));
Reject(()=>PaymentProof.Validate(proof,"wrong-payment-verification-key-32-chars",7,order));
Reject(()=>PaymentProof.Validate("invalid",key,7,order));
Reject(()=>PaymentProof.Validate(proof.Split('.')[0]+".AAAA",key,7,order));
Console.WriteLine($"PASS: Node signature verified by APICompras; {rejected} invalid confirmations rejected.");
