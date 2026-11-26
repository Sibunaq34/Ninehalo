using APINineHalo.Services;
using APINineHalo.Entities;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

// --live reads the private token locally and prints only the public BCCR quote.
if(args.Contains("--live"))
{
    var root=Path.GetFullPath(args.First(a=>a!="--live"));
    var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"appsettings.Bccr.local.json")).AddEnvironmentVariables().Build();
    var real=new BccrService(new ClientFactory("https://apim.bccr.fi.cr/SDDE/api/Bccr.Ge.SDDE.Publico.Indicadores.API/"),config,TimeProvider.System);
    try { Console.WriteLine(JsonSerializer.Serialize(await real.GetAsync())); }
    catch(CartPurchaseException ex){Console.WriteLine(ex.Message);Environment.ExitCode=1;}
    return;
}
var configTest=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Bccr:Token"]="test-bccr-token"}).Build();
var clock=new TestClock(DateTimeOffset.UtcNow);
var factory=new ClientFactory("http://localhost:19400/bccr/");
using var controls=factory.CreateClient("Bccr");
var service=new BccrService(factory,configTest,clock);
var checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
async Task Reject(Func<Task> action,string name){try{await action();}catch(CartPurchaseException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
async Task Mode(string mode){using var response=await controls.PostAsJsonAsync("__scenario",new{mode});response.EnsureSuccessStatusCode();}
await controls.GetAsync("__reset");
var noToken=new BccrService(factory,new ConfigurationBuilder().Build(),clock);
await Reject(()=>noToken.GetAsync(),"missing token fails without inventing a rate");
var quote=await service.GetAsync();
Check(quote is { venta:500m,fuente:"BCCR",indicador:318,provisional:false },"reads SDDE sale quote with official source");
Check(quote.fecha==DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd"),"uses Costa Rica publication date");
await Task.WhenAll(Enumerable.Range(0,10).Select(_=>service.GetAsync()));
var state=await controls.GetFromJsonAsync<JsonElement>("__state");Check(state.GetProperty("requests").GetInt32()==1,"reuses cached quote instead of repeating provider calls");
clock.Advance(TimeSpan.FromMinutes(16));await Mode("outage");
await Reject(()=>service.GetAsync(),"expired cache and provider outage never fall back to 500");
await Mode("unauthorized");await Reject(()=>service.GetAsync(),"invalid token produces a controlled error");
await Mode("malformed");await Reject(()=>service.GetAsync(),"malformed provider response is rejected");
await Mode("success");Check((await service.GetAsync()).venta==500,"provider recovery restores valid quotes");
var today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date;
JsonElement Payload(params (DateTime Date,object? Value)[] rows)=>JsonSerializer.SerializeToElement(new{estado=true,datos=new[]{new{codigoIndicador="318",series=rows.Select(row=>new{fecha=row.Date.ToString("yyyy-MM-dd"),valorDatoPorPeriodo=row.Value})}}});
var ordered=BccrService.Parse(Payload((today.AddDays(-2),510m),(today,512.34m),(today.AddDays(1),999m)),clock.GetUtcNow());
Check(ordered.venta==512.34m,"selects latest published value and ignores future values");
Check(BccrService.Parse(Payload((today,null),(today.AddDays(-1),511m)),clock.GetUtcNow()).venta==511,"uses latest non-null published observation");
foreach(var value in new object?[]{0m,-1m,"invalid"})await Reject(()=>Task.FromResult(BccrService.Parse(Payload((today,value)),clock.GetUtcNow())),"rejects invalid value "+value);
await Reject(()=>Task.FromResult(BccrService.Parse(Payload((today.AddDays(-10),500m)),clock.GetUtcNow())),"rejects obsolete observations");
await Reject(()=>Task.FromResult(BccrService.Parse(Payload((today,500m),(today,501m)),clock.GetUtcNow())),"rejects conflicting rates for the same date");
Console.WriteLine($"{checks} BCCR checks passed against the contract-based HTTP simulation; live provider validation is separate.");

sealed class ClientFactory(string url):IHttpClientFactory {public HttpClient CreateClient(string name)=>new(){BaseAddress=new Uri(url),Timeout=TimeSpan.FromSeconds(15)};}
sealed class TestClock(DateTimeOffset now):TimeProvider {private DateTimeOffset current=now;public override DateTimeOffset GetUtcNow()=>current;public void Advance(TimeSpan duration)=>current+=duration;}
