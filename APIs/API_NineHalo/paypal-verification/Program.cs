using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using System.Net.Http.Json;

var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "API_NineHalo");
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"APICompras/appsettings.json"))
    .AddJsonFile(Path.Combine(root,"APICompras/appsettings.Development.json"),true).AddEnvironmentVariables().Build();
var csb = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NINEHALO_TEST_CONNECTION") ?? config.GetConnectionString("DefaultConnection")) { ConnectionTimeout=5 };
var migration = File.ReadAllText(Path.Combine(root,"APICompras/Migrations/20261118_paypal.sql"))+"\n"+File.ReadAllText(Path.Combine(root,"APICompras/Migrations/20261119_facturas.sql"));
if(args.Contains("--migrate")) {
    using var destination=new MySqlConnection(csb.ConnectionString);await destination.OpenAsync();await destination.ExecuteAsync(migration);
    var maxReference=await destination.ExecuteScalarAsync<int>("SELECT CHARACTER_MAXIMUM_LENGTH FROM information_schema.columns WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='compra' AND COLUMN_NAME='referencia_pago'");
    Console.WriteLine($"PayPal: tabla creada o verificada; capacidad de referencia_pago: {maxReference} caracteres. No se modificaron compras existentes.");return;
}
var database="ninehalo_paypal_verify_"+Guid.NewGuid().ToString("N");
csb.Database="";
using var admin=new MySqlConnection(csb.ConnectionString);await admin.OpenAsync();
await admin.ExecuteAsync($"CREATE DATABASE `{database}`");
try {
    csb.Database=database;
    using var db=new MySqlConnection(csb.ConnectionString);await db.OpenAsync();
    await db.ExecuteAsync("""
        CREATE TABLE usuario(id_usuario INT PRIMARY KEY,estado VARCHAR(20),correo_verificado TINYINT);
        INSERT INTO usuario VALUES(1,'ACTIVO',1),(2,'ACTIVO',1);
        CREATE TABLE compra(id_compra BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,id_usuario INT,id_licencia INT,
          precio_pagado DECIMAL(15,2),estado VARCHAR(20),duracion_meses_compra INT,fecha_vencimiento DATETIME,referencia_pago VARCHAR(160));
        CREATE TABLE persona(id_usuario INT PRIMARY KEY,nombre VARCHAR(150),apellidos VARCHAR(150));
        INSERT INTO persona VALUES(1,'Ana','Rodríguez'),(2,'Otro','Usuario');
        CREATE TABLE licencia(id_licencia INT PRIMARY KEY,nombre VARCHAR(100));
        INSERT INTO licencia VALUES(1,'Licencia de prueba');
        CREATE TABLE pais(id_pais INT PRIMARY KEY,descripcion VARCHAR(100));INSERT INTO pais VALUES(1,'Costa Rica');
        CREATE TABLE provincias(id_provincia INT PRIMARY KEY,id_pais INT,descripcion VARCHAR(100));INSERT INTO provincias VALUES(1,1,'San José');
        CREATE TABLE cantones(id_canton INT PRIMARY KEY,id_provincia INT,descripcion VARCHAR(100));INSERT INTO cantones VALUES(1,1,'Central');
        CREATE TABLE distritos(id_distrito INT PRIMARY KEY,id_canton INT,descripcion VARCHAR(100));INSERT INTO distritos VALUES(1,1,'Carmen');
        CREATE TABLE bitacora(actor INT,accion VARCHAR(50));
        """);
    await db.ExecuteAsync("CREATE PROCEDURE sp_registrar_bitacora(IN p_actor INT,IN p_accion VARCHAR(50),IN p_entidad VARCHAR(50)) INSERT INTO bitacora VALUES(p_actor,p_accion)");
    await db.ExecuteAsync(migration);
    var localConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=csb.ConnectionString }).Build();
    var factory=new DbConnectionFactory(localConfig);
    var options=new PayPalOptions{ClientId="test-client",ClientSecret="test-secret"};
    using var http=new HttpClient{BaseAddress=new Uri("http://localhost:19400/paypal/"),Timeout=TimeSpan.FromSeconds(10)};
    var cart=new CarritoRepository(factory,NullLogger<CarritoRepository>.Instance);
    var service=new PayPalCheckoutService(new PayPalClient(http,options),new PayPalRepository(factory,options),cart,options);
    var invoices=new FacturaRepository(factory,new BccrService(new MockBccrFactory(),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Bccr:Token"]="test-bccr-token"}).Build(),TimeProvider.System));
    int checks=0;
    void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
    async Task<Guid> Seed(){var key=Guid.NewGuid();for(int i=0;i<2;i++)await db.ExecuteAsync("INSERT INTO compra(id_usuario,id_licencia,precio_pagado,estado,duracion_meses_compra,fecha_vencimiento,referencia_pago) VALUES(1,1,5,'PENDIENTE',12,DATE_SUB(NOW(),INTERVAL 1 DAY),@reference)",new{reference=$"cart:1:{key:N}:{i}"});return key;}
    async Task Mode(string mode){using var response=await http.PostAsJsonAsync("__scenario",new{mode});response.EnsureSuccessStatusCode();}
    async Task<int> Captures(){var data=await http.GetFromJsonAsync<System.Text.Json.JsonElement>("__state");return data.GetProperty("captures").GetInt32();}
    async Task Reject(Func<Task> action,string name){try{await action();}catch(CartPurchaseException){Check(true,name);return;}throw new Exception("Should reject: "+name);}
    await Mode("success");var key=await Seed();
    var order=await service.CreateAsync(1,key);var again=await service.CreateAsync(1,key);
    Check(order.OrderId==again.OrderId,"reuses persisted order on creation retry");
    await Reject(()=>service.CaptureAsync(2,key),"another user cannot capture or activate this order");
    int before=await Captures();var result=await service.CaptureAsync(1,key);
    Check(result.Compras.Count==2&&result.Compras.All(row=>row.Estado=="ACTIVA"),"completed capture activates every purchased unit");
    var expiry=await db.QuerySingleAsync<DateTime>("SELECT fecha_vencimiento FROM compra WHERE referencia_pago LIKE @reference LIMIT 1",new{reference=$"cart:1:{key:N}:%"});
    await service.CaptureAsync(1,key);
    Check(await Captures()==before+1,"activation retry never captures again");
    Check(expiry==await db.QuerySingleAsync<DateTime>("SELECT fecha_vencimiento FROM compra WHERE referencia_pago LIKE @reference LIMIT 1",new{reference=$"cart:1:{key:N}:%"}),"retry never extends license validity");
    foreach(var mode in new[]{"wrong-amount","wrong-currency","foreign-order","pending","declined"}) {
        await Mode("success");var rejectedKey=await Seed();await service.CreateAsync(1,rejectedKey);await Mode(mode);
        await Reject(()=>service.CaptureAsync(1,rejectedKey),mode+" cannot activate licenses");
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra WHERE referencia_pago LIKE @reference AND estado='ACTIVA'",new{reference=$"cart:1:{rejectedKey:N}:%"})==0,mode+" leaves database pending");
    }
    await Mode("success");var lostKey=await Seed();await service.CreateAsync(1,lostKey);await Mode("lost-response");
    before=await Captures();await Reject(()=>service.CaptureAsync(1,lostKey),"lost capture response preserves recoverable checkout");
    await Mode("success");await service.CaptureAsync(1,lostKey);
    Check(await Captures()==before+1,"recovers captured payment after response loss without another charge");
    var paypalInvoice=await invoices.GetAsync(1,key);
    Check(paypalInvoice?.detalle.metodo=="paypal"&&paypalInvoice.proveedor=="paypal","PayPal creates persisted invoice on activation");
    Check(await invoices.GetAsync(2,key)==null,"invoice lookup is scoped to owning user");
    var cardKey=await Seed();
    var invalid=new FacturacionRequest{Metodo="tarjeta",Nombre="Ana Rodríguez",Direccion="Calle de prueba 123",Id_Pais=1,Id_Provincia=1,Id_Canton=1,Id_Distrito=999};
    await Reject(()=>invoices.PrepareAsync(1,cardKey,invalid),"invalid billing location is rejected before payment");
    invalid.Id_Distrito=1; invalid.Direccion="Calle de prueba\nCasa 123";
    var snapshot=await invoices.PrepareAsync(1,cardKey,invalid);
    Check(!snapshot.cambio_provisional&&snapshot.fuente_tipo_cambio=="BCCR"&&!string.IsNullOrEmpty(snapshot.fecha_tipo_cambio),"invoice snapshots preserve BCCR source and publication date");
    Check(snapshot.items.Count==1&&snapshot.items[0].cantidad==2&&snapshot.total_crc==5000&&snapshot.direccion=="Calle de prueba Casa 123","billing snapshot groups licenses and converts the saved total");
    Check(await invoices.GetAsync(1,cardKey)==null,"preparing billing never issues a paid invoice");
    await cart.ConfirmAsync(1,cardKey,10,"5001","bank","tarjeta");
    var cardInvoice=await invoices.GetAsync(1,cardKey);
    Check(cardInvoice?.detalle.nombre=="Ana Rodríguez"&&cardInvoice.detalle.ubicacion.Contains("Carmen"),"card invoice preserves billing name and location");
    await cart.ConfirmAsync(1,cardKey,10,"5001","bank","tarjeta");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM factura WHERE clave_operacion=@key",new{key=cardKey.ToString()})==1,"confirmation retries produce one invoice");
    await db.ExecuteAsync("UPDATE licencia SET nombre='Nuevo nombre' WHERE id_licencia=1; UPDATE persona SET nombre='Nuevo nombre' WHERE id_usuario=1");
    invalid.Nombre="Otro nombre";
    var unchanged=await invoices.PrepareAsync(1,cardKey,invalid);
    Check(unchanged.nombre=="Ana Rodríguez"&&unchanged.items[0].nombre=="Licencia de prueba","issued invoice remains immutable after catalog or account changes");
    var duplicateKey=await Seed();await Reject(()=>cart.ConfirmAsync(1,duplicateKey,10,"5001","bank","tarjeta"),"one transaction cannot invoice another order");
    var sinpeKey=await Seed();await invoices.PrepareAsync(1,sinpeKey,new(){Metodo="sinpe"});
    await cart.ConfirmAsync(1,sinpeKey,10,"5002","bank","sinpe");
    Check((await invoices.GetAsync(1,sinpeKey))?.detalle.metodo=="sinpe","SINPE invoice is stored without card-only billing fields");
    var failureKey=await Seed();await invoices.PrepareAsync(1,failureKey,new(){Metodo="sinpe"});
    await db.ExecuteAsync("CREATE TRIGGER test_invoice_failure BEFORE INSERT ON factura FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Simulated invoice storage failure'");
    try{await cart.ConfirmAsync(1,failureKey,10,"5003","bank","sinpe");throw new Exception("Expected storage failure");}catch(MySqlException){}
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra WHERE referencia_pago LIKE @reference AND estado='ACTIVA'",new{reference=$"cart:1:{failureKey:N}:%"})==0,"invoice storage failure rolls back license activation");
    Check(await invoices.GetAsync(1,failureKey)==null,"storage failure leaves no partial invoice");
    await db.ExecuteAsync("DROP TRIGGER test_invoice_failure");
    await cart.ConfirmAsync(1,failureKey,10,"5003","bank","sinpe");
    Check(await invoices.GetAsync(1,failureKey)!=null,"storage recovery emits invoice on the same payment retry");
    Check(!(await invoices.ListAsync(2)).Any(),"invoice history never leaks another user's receipts");
    Console.WriteLine($"{checks} checks passed against real repositories, isolated MySQL and the local PayPal simulation.");
}
finally { await admin.ExecuteAsync($"DROP DATABASE `{database}`"); }

sealed class MockBccrFactory : IHttpClientFactory { public HttpClient CreateClient(string name)=>new HttpClient{BaseAddress=new Uri("http://localhost:19400/bccr/"),Timeout=TimeSpan.FromSeconds(5)}; }
