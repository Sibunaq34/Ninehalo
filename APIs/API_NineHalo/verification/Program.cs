using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;

// Executes the real APIs against schema/procedures from the supplied dump in a
// unique disposable database. Never imports INSERTs, USE, DROP or DEFINER clauses.
var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "API_NineHalo");
var dump = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Supply backup.sql path.");
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"APILicencias","appsettings.json")));
var cs = Environment.GetEnvironmentVariable("NINEHALO_TEST_CONNECTION")
    ?? config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
var csb = new MySqlConnectionStringBuilder(cs) { Database="", ConnectionTimeout=5 };
using var admin = new MySqlConnection(csb.ConnectionString);
await admin.OpenAsync();
var dbName = "ninehalo_verify_" + Guid.NewGuid().ToString("N");
var processes = new List<Process>();
var secret = RandomNumberGenerator.GetBytes(32);
var checks = 0;
try
{
    await admin.ExecuteAsync($"CREATE DATABASE `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
    csb.Database=dbName;
    using var db = new MySqlConnection(csb.ConnectionString);
    await db.OpenAsync();
    var sql = File.ReadAllText(dump);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");
    foreach (Match match in Regex.Matches(sql, @"(?ms)^CREATE TABLE `\w+`.*?\) ENGINE=InnoDB[^;]*;"))
        await db.ExecuteAsync(match.Value);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");
    foreach (Match match in Regex.Matches(sql, @"(?ms)^CREATE DEFINER=[^\r\n]* PROCEDURE `\w+`.*?END ;;"))
    {
        var procedure = Regex.Replace(match.Value, @"^CREATE DEFINER=.*? PROCEDURE", "CREATE PROCEDURE");
        await db.ExecuteAsync(procedure[..^3]);
    }
    await db.ExecuteAsync("""
        INSERT INTO usuario(id_usuario,correo,password_hash) VALUES
          (1,'customer@test.invalid','test'),(2,'agent@test.invalid','test'),(3,'other@test.invalid','test');
        INSERT INTO persona(id_usuario,identificacion,nombre,apellidos,telefono) VALUES
          (1,'001-TEST','Cuenta','Prueba','8888-0000'),(3,'003-TEST','Otra','Cuenta',NULL);
        INSERT INTO rol(id_rol,codigo,nombre) VALUES(1,'ADMIN','Admin');
        INSERT INTO usuario_rol(id_usuario,id_rol) VALUES(2,1);
        INSERT INTO permiso(id_permiso,codigo,descripcion) VALUES
          (1,'licencia.gestionar','test'),(2,'estadistica.gestionar','test'),
          (3,'chat.asignar','test'),(4,'chat.gestionar','test');
        INSERT INTO rol_permiso(id_rol,id_permiso) VALUES(1,1),(1,2),(1,3),(1,4);
        INSERT INTO licencia(id_licencia,nombre,descripcion,precio,max_juegos) VALUES(1,'Prueba','Descripción',25,2),(2,'Oculta','',0,1);
        UPDATE licencia SET activo=0 WHERE id_licencia=2;
        INSERT INTO detalle_licencia(id_licencia,descripcion,orden) VALUES(1,'Segundo',2),(1,'Primero',1);
        INSERT INTO plataforma(id_plataforma,nombre) VALUES(1,'PC'),(2,'Consola');
        INSERT INTO licencia_plataforma(id_licencia,id_plataforma) VALUES(1,1);
        INSERT INTO juego(id_juego,nombre) VALUES(1,'Juego de prueba');
        INSERT INTO juego_plataforma(id_juego,id_plataforma) VALUES(1,1);
        INSERT INTO compra(id_compra,id_usuario,id_licencia,precio_pagado,duracion_meses_compra,max_juegos_compra,fecha_compra,fecha_vencimiento,estado)
          VALUES(1,1,1,20,12,2,NOW(),DATE_ADD(NOW(),INTERVAL 1 YEAR),'ACTIVA'),
          (2,3,1,20,12,2,DATE_SUB(NOW(),INTERVAL 1 YEAR),DATE_SUB(NOW(),INTERVAL 1 DAY),'ACTIVA');
        INSERT INTO compra_juego(id_compra_juego,id_compra,id_juego) VALUES(1,1,1);
        INSERT INTO estadistica(id_compra_juego,id_plataforma,jugadores_baneados) VALUES(1,1,5000000000);
        """);

    var clients = new Dictionary<string,HttpClient>();
    var services = args.Contains("--cart-only") ? new[] { ("APICompras",18572) } : args.Contains("--profile-only")
        ? new[] { ("API_NineHalo",18590) }
        : new[] { ("APILicencias",18533),("APIEstadisticas",18528),("APISoporte",18506) };
    foreach (var (name,port) in services)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory=Path.Combine(root,name), UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        var assembly = name == "API_NineHalo" ? "API_Persona" : name;
        start.ArgumentList.Add(Path.Combine(root,name,"bin","Debug","net10.0",assembly+".dll"));
        start.Environment["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";
        start.Environment["ConnectionStrings__DefaultConnection"]=csb.ConnectionString;
        start.Environment["Jwt__SecretKey"]=Convert.ToBase64String(secret);
        start.Environment["Jwt__Issuer"]="verification";
        start.Environment["Jwt__Audience"]="verification";
        var process = Process.Start(start)!;
        process.OutputDataReceived += (_,_) => {};
        process.ErrorDataReceived += (_,_) => {};
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        processes.Add(process);
        var client = new HttpClient { BaseAddress=new Uri($"http://127.0.0.1:{port}"), Timeout=TimeSpan.FromSeconds(10) };
        clients[name]=client;
        bool ready=false;
        for (int attempt=0;attempt<80;attempt++)
        {
            if (process.HasExited) throw new Exception($"{name} exited before startup.");
            try { using var response=await client.GetAsync("/health-unused"); ready=true; break; } catch(HttpRequestException) { await Task.Delay(100); }
        }
        if (!ready) throw new Exception($"{name} did not start.");
    }
    string Token(int actor, bool expired=false, bool wrongKey=false) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer:"verification", audience:"verification", claims:new[] {new Claim("sub",actor.ToString())},
        notBefore:DateTime.UtcNow.AddMinutes(-10), expires:DateTime.UtcNow.AddMinutes(expired ? -5 : 10),
        signingCredentials:new SigningCredentials(new SymmetricSecurityKey(wrongKey ? RandomNumberGenerator.GetBytes(32) : secret), SecurityAlgorithms.HmacSha256)));
    void Assert(bool condition,string label) { if(!condition) throw new Exception("FAILED: "+label); checks++; }
    async Task<JsonElement> Request(string service,string method,string path,int expected,int? actor=null,object? body=null,string? token=null)
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),path);
        if(actor is not null || token is not null) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token ?? Token(actor!.Value));
        if(body is not null) request.Content=JsonContent.Create(body);
        using var response=await clients[service].SendAsync(request);
        var text=await response.Content.ReadAsStringAsync();
        Assert((int)response.StatusCode==expected,$"{method} {path}: expected {expected}, got {(int)response.StatusCode}; {text}");
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
    if (args.Contains("--cart-only"))
    {
        const string cart="APICompras";
        await db.ExecuteAsync("UPDATE usuario SET correo_verificado=1 WHERE id_usuario=1; UPDATE licencia SET activo=1,precio=15 WHERE id_licencia=2;");
        object Order(string key,int quantity=2) => new {clave_operacion=key,items=new[]{new{id_licencia=1,cantidad=quantity,precio_unitario=25},new{id_licencia=2,cantidad=3,precio_unitario=15}}};
        var key=Guid.NewGuid().ToString();
        var cartBefore=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra");
        await Request(cart,"POST","/api/compras/carrito",401,body:Order(key));
        await Request(cart,"POST","/api/compras/carrito",403,3,Order(key));
        await Request(cart,"POST","/api/compras/carrito",400,1,new{clave_operacion="invalid",items=Array.Empty<object>()});
        await Request(cart,"POST","/api/compras/carrito",400,1,new{clave_operacion=key,items=new[]{new{id_licencia=1,cantidad=21,precio_unitario=25}}});
        await Request(cart,"POST","/api/compras/carrito",400,1,new{clave_operacion=key,items=new[]{new{id_licencia=1,cantidad=1,precio_unitario=25},new{id_licencia=1,cantidad=1,precio_unitario=25}}});
        await Request(cart,"POST","/api/compras/carrito",409,1,new{clave_operacion=key,items=new[]{new{id_licencia=1,cantidad=1,precio_unitario=24}}});
        await Request(cart,"POST","/api/compras/carrito",409,1,new{clave_operacion=key,items=new[]{new{id_licencia=999,cantidad=1,precio_unitario=25}}});
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra")==cartBefore,"Rejected orders create no purchases");
        var result=await Request(cart,"POST","/api/compras/carrito",201,1,Order(key));
        var purchases=result.GetProperty("data").GetProperty("compras");
        Assert(purchases.GetArrayLength()==5,"Quantities expand into five licenses");
        Assert(result.GetProperty("data").GetProperty("total").GetDecimal()==95,"Server calculates full order total");
        Assert(purchases.EnumerateArray().All(item=>item.GetProperty("estado").GetString()=="PENDIENTE"),"Orders are pending, never silently activated");
        var ids=purchases.EnumerateArray().Select(item=>item.GetProperty("id_compra").GetUInt64()).ToArray();
        await db.ExecuteAsync("UPDATE licencia SET precio=99 WHERE id_licencia=1");
        var replay=await Request(cart,"POST","/api/compras/carrito",201,1,Order(key));
        Assert(replay.GetProperty("data").GetProperty("compras").EnumerateArray().Select(item=>item.GetProperty("id_compra").GetUInt64()).SequenceEqual(ids),"Retry returns original IDs despite catalog price change");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra")==cartBefore+5,"Retry creates no duplicates");
        var conflict=await Request(cart,"POST","/api/compras/carrito",409,1,Order(key,1));
        Assert(!conflict.GetProperty("rechazado").GetBoolean(),"Changed operation requires reconciliation");
        await db.ExecuteAsync("UPDATE licencia SET precio=25 WHERE id_licencia=1");
        var concurrentKey=Guid.NewGuid().ToString();
        var concurrent=await Task.WhenAll(Request(cart,"POST","/api/compras/carrito",201,1,Order(concurrentKey)),Request(cart,"POST","/api/compras/carrito",201,1,Order(concurrentKey)));
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra")==cartBefore+10,"Concurrent confirmations do not duplicate purchases");
        var auditBefore=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bitacora");
        await db.ExecuteAsync("CREATE TRIGGER fail_purchase BEFORE INSERT ON compra FOR EACH ROW BEGIN IF NEW.id_licencia=2 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Fallo de prueba'; END IF; END");
        var rollbackKey=Guid.NewGuid().ToString();
        await Request(cart,"POST","/api/compras/carrito",400,1,Order(rollbackKey));
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra")==cartBefore+10,"Mid-order failure rolls back every unit");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bitacora")==auditBefore,"Mid-order failure rolls back audit entries");
        await db.ExecuteAsync("DROP TRIGGER fail_purchase");
        await Request(cart,"POST","/api/compras/carrito",201,1,Order(rollbackKey));
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra")==cartBefore+15,"Same operation safely recovers after rollback");
        await Request(cart,"POST","/api/compras",401,body:new{id_usuario=1,id_licencia=1});
        await Request(cart,"POST","/api/compras",403,1,new{id_usuario=3,id_licencia=1});
        await Request(cart,"POST","/api/compras",201,1,new{id_usuario=1,id_licencia=1});
        Console.WriteLine($"PASS: {checks} cart assertions against real HTTP API and isolated MySQL schema.");
        return;
    }
    if (args.Contains("--profile-only"))
    {
        const string persona = "API_NineHalo";
        await Request(persona,"GET","/api/perfil/1",401);
        await Request(persona,"GET","/api/perfil/1",401,token:Token(1,expired:true));
        await Request(persona,"GET","/api/perfil/1",403,3);
        await Request(persona,"GET","/api/perfil/0",400,1);
        await Request(persona,"GET","/api/perfil/2",404,2);
        var profile=await Request(persona,"GET","/api/perfil/1",200,1);
        var data=profile.GetProperty("data");
        Assert(data.GetProperty("id_usuario").GetInt32()==1,"Profile internal account ID");
        Assert(data.GetProperty("identificacion").GetString()=="001-TEST","Personal identification preserved separately");
        Assert(data.GetProperty("nombre").GetString()=="Cuenta","Profile name loaded");
        Assert(data.GetProperty("correo").GetString()=="customer@test.invalid","Profile email loaded");
        Assert(data.GetProperty("telefono").GetString()=="8888-0000","Profile phone loaded");
        Assert(data.GetProperty("tipo_licencia").GetString()=="Prueba","Active license loaded");
        Assert(data.GetProperty("fecha_caducidad").ValueKind==JsonValueKind.String,"License expiration loaded");
        var noLicense=await Request(persona,"GET","/api/perfil/3",200,3);
        Assert(noLicense.GetProperty("data").GetProperty("tipo_licencia").ValueKind==JsonValueKind.Null,"Expired license excluded from profile");
        var edit=new {nombre="  Actualizado  ",apellidos="Perfil",correo="updated@test.invalid"};
        await Request(persona,"PUT","/api/perfil/1",401,body:edit);
        await Request(persona,"PUT","/api/perfil/1",403,3,edit);
        await Request(persona,"PUT","/api/perfil/1",400,1,new {nombre="",apellidos="Perfil",correo="invalid"});
        var updated=await Request(persona,"PUT","/api/perfil/1",200,1,edit);
        Assert(updated.GetProperty("data").GetProperty("nombre").GetString()=="Actualizado","Updated profile returned after resultless stored procedure");
        Assert(updated.GetProperty("data").GetProperty("telefono").GetString()=="8888-0000","Editing does not clear phone");
        Assert(updated.GetProperty("data").GetProperty("identificacion").GetString()=="001-TEST","Editing retains identification");
        Assert(updated.GetProperty("data").GetProperty("tipo_licencia").GetString()=="Prueba","Editing retains license");
        await Request(persona,"PUT","/api/perfil/1",409,1,new {nombre="No persistir",apellidos="Perfil",correo="other@test.invalid"});
        profile=await Request(persona,"GET","/api/perfil/1",200,1);
        Assert(profile.GetProperty("data").GetProperty("nombre").GetString()=="Actualizado","Duplicate email rolls back edits");
        Assert(profile.GetProperty("data").GetProperty("correo").GetString()=="updated@test.invalid","Updated email persisted");
        Console.WriteLine($"PASS: {checks} profile assertions against real HTTP API and isolated MySQL schema.");
        return;
    }
    const string lic="APILicencias", stats="APIEstadisticas", support="APISoporte";
    var licenses=await Request(lic,"GET","/api/licencias",200);
    Assert(licenses.GetProperty("data").GetArrayLength()==1,"Only active licenses");
    Assert(licenses.GetProperty("data")[0].GetProperty("detalles")[0].GetString()=="Primero","Ordered detail descriptions");
    await Request(lic,"GET","/api/licencias/usuario/1",401);
    await Request(lic,"GET","/api/licencias/usuario/1",401,token:Token(1,expired:true));
    await Request(lic,"GET","/api/licencias/usuario/1",401,token:Token(1,wrongKey:true));
    await Request(lic,"GET","/api/licencias/usuario/1",403,3);
    var active=await Request(lic,"GET","/api/licencias/usuario/1",200,1);
    Assert(active.GetProperty("data").GetProperty("precio").GetDecimal()==20,"Purchase snapshot price");
    var expired=await Request(lic,"GET","/api/licencias/usuario/3",200,3);
    Assert(expired.GetProperty("data").ValueKind==JsonValueKind.Null,"Expired purchase excluded");
    var update=new { nombre="Actualizada", descripcion="Cambio", precio=30, duracion_meses=6, max_juegos=3, activo=true };
    await Request(lic,"PUT","/api/licencias/1",403,1,update);
    await Request(lic,"PUT","/api/licencias/1",400,2,new { nombre="Inválida" });
    await Request(lic,"PUT","/api/licencias/1",200,2,update);
    active=await Request(lic,"GET","/api/licencias/usuario/1",200,1);
    Assert(active.GetProperty("data").GetProperty("max_juegos").GetInt32()==2,"Purchase limit snapshot preserved");
    await Request(stats,"GET","/api/estadisticas/1",401);
    await Request(stats,"GET","/api/estadisticas/1",403,3);
    var rows=await Request(stats,"GET","/api/estadisticas/1",200,1);
    Assert(rows.GetProperty("data")[0].GetProperty("jugadores_baneados").GetUInt64()==5000000000,"BIGINT statistic preserved");
    Assert(rows.GetProperty("data")[0].GetProperty("juego").GetString()=="Juego de prueba","Game association returned");
    await Request(stats,"GET","/api/estadisticas/1",200,2);
    var empty=await Request(stats,"GET","/api/estadisticas/3",200,3);
    Assert(empty.GetProperty("data").GetArrayLength()==0,"No active statistics returns empty array");
    await Request(stats,"PUT","/api/estadisticas",403,1,new { id_compra_juego=1,id_plataforma=1,jugadores_baneados=10 });
    await Request(stats,"PUT","/api/estadisticas",400,2,new { id_compra_juego=1,id_plataforma=2,jugadores_baneados=10 });
    await Request(stats,"PUT","/api/estadisticas",400,2,new { id_compra_juego=1,id_plataforma=1,jugadores_baneados=-1 });
    await Request(stats,"PUT","/api/estadisticas",200,2,new { id_compra_juego=1,id_plataforma=1,jugadores_baneados=12 });
    rows=await Request(stats,"GET","/api/estadisticas/1",200,1);
    Assert(rows.GetProperty("data")[0].GetProperty("jugadores_baneados").GetInt32()==12,"Statistic persisted");
    await Request(support,"POST","/api/tickets",403,3,new { id_usuario=1,titulo="X",detalle="Y" });
    var ticket=await Request(support,"POST","/api/tickets",201,1,new { id_usuario=1,titulo="Ayuda",detalle="Mensaje inicial",id_compra=1 });
    var id=ticket.GetProperty("data").GetProperty("id_conversacion").GetUInt64();
    var history=await Request(support,"GET","/api/tickets/1",200,1);
    Assert(history.GetProperty("data").GetProperty("total_tickets").GetInt32()==1,"Legacy history totals");
    Assert(history.GetProperty("data").GetProperty("tickets")[0].GetProperty("titulo").GetString()=="Ayuda","Legacy title mapping");
    await Request(support,"GET","/api/tickets/1",403,3);
    await Request(support,"GET","/api/conversaciones/soporte?tamano=101",400,2);
    await Request(support,"GET","/api/conversaciones/soporte",403,1);
    await Request(support,"GET","/api/conversaciones/soporte",200,2);
    await Request(support,"GET",$"/api/conversaciones/{id}/mensajes",403,3);
    var messages=await Request(support,"GET",$"/api/conversaciones/{id}/mensajes",200,1);
    Assert(messages.GetProperty("data")[0].GetProperty("contenido").GetString()=="Mensaje inicial","Legacy initial message persisted");
    await Request(support,"PUT",$"/api/conversaciones/{id}/agente",403,1,new {id_agente=2});
    await Request(support,"PUT",$"/api/conversaciones/{id}/agente",200,2,new {id_agente=2});
    await Request(support,"POST",$"/api/conversaciones/{id}/mensajes",201,2,new {contenido="Respuesta del agente"});
    await Request(support,"POST",$"/api/conversaciones/{id}/mensajes",400,1,new {contenido=new string('x',10001)});
    await Request(support,"PUT",$"/api/conversaciones/{id}/estado",200,2,new {estado="CERRADA"});
    await Request(support,"POST",$"/api/conversaciones/{id}/mensajes",403,1,new {contenido="Mensaje rechazado"});
    await Request(support,"POST","/api/conversaciones",403,1,new {asunto="Compra ajena",id_compra=2});
    await Request(support,"POST","/api/conversaciones",201,3,new {asunto="Sin licencia vigente"});
    var before=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM conversacion");
    await db.ExecuteAsync("CREATE TRIGGER fail_message BEFORE INSERT ON mensaje FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Fallo de prueba'");
    await Request(support,"POST","/api/tickets",400,1,new {id_usuario=1,titulo="Rollback",detalle="No persistir"});
    Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM conversacion")==before,"Initial message failure rolls back conversation");
    await db.ExecuteAsync("DROP TRIGGER fail_message");
    Console.WriteLine($"PASS: {checks} assertions against real HTTP APIs and isolated MySQL schema.");
}
finally
{
    foreach(var process in processes) { if(!process.HasExited) { process.Kill(entireProcessTree:true); process.WaitForExit(); } process.Dispose(); }
    if (!Regex.IsMatch(dbName,@"^ninehalo_verify_[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid cleanup schema.");
    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{dbName}`");
}

