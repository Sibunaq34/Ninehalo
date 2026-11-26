using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

var root=Path.GetFullPath(args.ElementAtOrDefault(0)??"API_NineHalo");
using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"APIUsuarios","appsettings.json")));
var csb=new MySqlConnectionStringBuilder(settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!){ConnectionTimeout=5};
string migration=File.ReadAllText(Path.Combine(root,"APIUsuarios","Migrations","20261027_password_recovery.sql"));
if(args.Contains("--migrate"))
{
    if(csb.Database!="nine_halo")throw new Exception("Expected nine_halo");
    using var target=new MySqlConnection(csb.ConnectionString);await target.OpenAsync();await target.ExecuteAsync(migration);
    Console.WriteLine("Applied additive recovery table migration. No account passwords changed.");return;
}
csb.Database="";using var admin=new MySqlConnection(csb.ConnectionString);await admin.OpenAsync();
string database="ninehalo_recovery_verify_"+Guid.NewGuid().ToString("N");int checks=0;Process? host=null;
void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);checks++;}
async Task Reject(Func<Task> action,int status=400){try{await action();throw new Exception("Expected rejection");}catch(AuthenticatorException ex){Check(ex.Status==status,"Rejected with expected status");}}
try
{
    await admin.ExecuteAsync($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");csb.Database=database;
    using var db=new MySqlConnection(csb.ConnectionString);await db.OpenAsync();
    string sql=File.ReadAllText(args[1]);await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");
    foreach(Match m in Regex.Matches(sql,@"(?ms)^CREATE TABLE `\w+`.*?\) ENGINE=InnoDB[^;]*;"))await db.ExecuteAsync(m.Value);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");
    foreach(Match m in Regex.Matches(sql,@"(?ms)^CREATE DEFINER=[^\r\n]* PROCEDURE `\w+`.*?END ;;"))await db.ExecuteAsync(Regex.Replace(m.Value,@"^CREATE DEFINER=.*? PROCEDURE","CREATE PROCEDURE")[..^3]);
    await db.ExecuteAsync(migration);await db.ExecuteAsync(migration);
    await db.ExecuteAsync(File.ReadAllText(Path.Combine(root,"APIUsuarios","Migrations","20261027_usuario_totp.sql")));
    string oldPassword="Original password 123",newPassword="Replacement password 456",oldHash=BCrypt.Net.BCrypt.HashPassword(oldPassword,4);
    for(int id=1;id<=8;id++)
    {
        await db.ExecuteAsync("INSERT INTO usuario(id_usuario,correo,password_hash,correo_verificado,estado) VALUES(@id,@email,@oldHash,1,'ACTIVO')",new{id,email=$"abc{id}@test.invalid",oldHash});
        await db.ExecuteAsync("INSERT INTO persona(id_usuario,identificacion,nombre,apellidos,telefono) VALUES(@id,@ident,'Test','Recovery','8888-7777')",new{id,ident=$"ID-{id}99"});
    }
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:DefaultConnection"]=csb.ConnectionString}).Build();
    var factory=new DbConnectionFactory(config);var protection=new EphemeralDataProtectionProvider();var clock=new TestClock();
    var auth=new AuthenticatorService(new UsuarioRepository(factory),new TotpRepository(factory),new PreAuthenticationService(protection),protection,clock);
    var recovery=new PasswordRecoveryService(factory,auth,protection,clock);
    RecoveryQuestionsRequest Answers(int id)=>new($"abc{id}@test.invalid",$"id {id}99","88887777");
    var account=new RecoveryAccount{Correo="abc@test.invalid",Identificacion="ID-199",Telefono="8888-7777"};
    Check(PasswordRecoveryService.CorrectAnswers(account,Answers(1))==2,"Case and display separators normalized");
    Check(PasswordRecoveryService.CorrectAnswers(account,Answers(1) with{Telefono="wrong"})==1,"Identification alone insufficient");
    Check(PasswordRecoveryService.CorrectAnswers(account,Answers(1) with{Identificacion="wrong"})==1,"Phone alone insufficient");
    Check(PasswordRecoveryService.CorrectAnswers(new(){Correo="abc@test.invalid"},new("abc@test.invalid","",""))==0,"Missing personal fields never count as correct");
    await Reject(()=>recovery.QuestionsAsync(Answers(1) with{Telefono="wrong",Identificacion="wrong"}));
    await Reject(()=>recovery.QuestionsAsync(Answers(1) with{Telefono="wrong"}));
    await Reject(()=>recovery.QuestionsAsync(Answers(1) with{Identificacion="wrong"}));
    var grant=await recovery.QuestionsAsync(Answers(1));Check(grant.expires_in==600,"Recovery grant ten minutes");
    Check(!(await db.ExecuteScalarAsync<string>("SELECT nonce_hash FROM usuario_recuperacion WHERE id_usuario=1"))!.Contains(grant.token),"Raw token never stored in DB");
    await Reject(()=>recovery.ResetAsync(new(grant.token,"short","short")));
    await Reject(()=>recovery.ResetAsync(new(grant.token,newPassword,"mismatch")));
    await Reject(()=>recovery.ResetAsync(new(grant.token,oldPassword,oldPassword)));
    await Reject(()=>recovery.ResetAsync(new("invalid",newPassword,newPassword)),401);
    await recovery.ResetAsync(new(grant.token,newPassword,newPassword));
    var hash=await db.ExecuteScalarAsync<string>("SELECT password_hash FROM usuario WHERE id_usuario=1");
    Check(BCrypt.Net.BCrypt.Verify(newPassword,hash)&&!BCrypt.Net.BCrypt.Verify(oldPassword,hash),"Only replacement password verifies");
    Check(await db.ExecuteScalarAsync<string>("SELECT nonce_hash FROM usuario_recuperacion WHERE id_usuario=1")==null,"Reset consumes grant");
    await Reject(()=>recovery.ResetAsync(new(grant.token,"Another password 789","Another password 789")),401);
    var first=await recovery.QuestionsAsync(Answers(2));var second=await recovery.QuestionsAsync(Answers(2));
    await Reject(()=>recovery.ResetAsync(new(first.token,newPassword,newPassword)),401);
    var results=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{try{await recovery.ResetAsync(new(second.token,newPassword,newPassword));return true;}catch(AuthenticatorException){return false;}}));
    Check(results.Count(x=>x)==1,"Concurrent reset succeeds only once");
    for(int attempt=0;attempt<5;attempt++)await Reject(()=>recovery.QuestionsAsync(Answers(3) with{Identificacion="bad",Telefono="bad"}));
    Check(await db.ExecuteScalarAsync<int>("SELECT fallos FROM usuario_recuperacion WHERE id_usuario=3")==5,"Failed attempts persist");
    await Reject(()=>recovery.QuestionsAsync(Answers(3)));clock.Advance(TimeSpan.FromMinutes(16));
    Check(!string.IsNullOrEmpty((await recovery.QuestionsAsync(Answers(3))).token),"Question lockout expires");
    await db.ExecuteAsync("UPDATE usuario SET estado='BLOQUEADO' WHERE id_usuario=4");await Reject(()=>recovery.QuestionsAsync(Answers(4)));
    await db.ExecuteAsync("UPDATE usuario SET correo_verificado=0 WHERE id_usuario=5");await Reject(()=>recovery.QuestionsAsync(Answers(5)));
    await Reject(()=>recovery.QuestionsAsync(new("unknown@test.invalid","unknown","123")));
    await Reject(()=>recovery.AuthenticatorAsync(new("abc6@test.invalid","123456")));
    var setup=await auth.BeginAsync(6,oldPassword);string Code()=>TotpAlgorithm.GenerateCode(Decode(setup.Secreto),clock.GetUtcNow().ToUnixTimeSeconds()/30);
    await auth.ConfirmAsync(6,new(){Id_Vinculacion=setup.IdVinculacion,Codigo=Code()});
    await Reject(()=>recovery.AuthenticatorAsync(new("abc6@test.invalid",Code())));clock.Advance(TimeSpan.FromSeconds(30));
    var appGrant=await recovery.AuthenticatorAsync(new("abc6@test.invalid",Code()));
    await Reject(()=>recovery.AuthenticatorAsync(new("abc6@test.invalid",Code())));
    await recovery.ResetAsync(new(appGrant.token,newPassword,newPassword));Check(BCrypt.Net.BCrypt.Verify(newPassword,await db.ExecuteScalarAsync<string>("SELECT password_hash FROM usuario WHERE id_usuario=6")),"Authenticator recovers without old password");
    var expired=protection.CreateProtector("NineHalo.PasswordRecovery.v1").ToTimeLimitedDataProtector().Protect(JsonSerializer.Serialize(new RecoveryProof(7,oldHash,"nonce")),TimeSpan.FromSeconds(-1));
    await Reject(()=>recovery.ResetAsync(new(expired,newPassword,newPassword)),401);
    var changed=await recovery.QuestionsAsync(Answers(7));await db.ExecuteAsync("UPDATE usuario SET password_hash=@hash WHERE id_usuario=7",new{hash=BCrypt.Net.BCrypt.HashPassword("Changed separately 123",4)});
    await Reject(()=>recovery.ResetAsync(new(changed.token,newPassword,newPassword)),401);
    var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"APIUsuarios"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(Path.Combine(root,"APIUsuarios/bin/Debug/net10.0/APIUsuarios.dll"));
    foreach(var pair in new Dictionary<string,string>{["ASPNETCORE_URLS"]="http://127.0.0.1:18646",["ASPNETCORE_ENVIRONMENT"]="Production",["ConnectionStrings__DefaultConnection"]=csb.ConnectionString,["Jwt__SecretKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),["Jwt__Issuer"]="test",["Jwt__Audience"]="test",["VerificationCode__SecretKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))})start.Environment[pair.Key]=pair.Value;
    host=Process.Start(start)!;host.OutputDataReceived+=(_,_)=>{};host.ErrorDataReceived+=(_,_)=>{};host.BeginOutputReadLine();host.BeginErrorReadLine();
    using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:18646")};bool ready=false;
    for(int n=0;n<80;n++){try{await http.GetAsync("/health-test");ready=true;break;}catch(HttpRequestException){await Task.Delay(100);}}
    Check(ready,"HTTP host running");
    using var response=await http.PostAsJsonAsync("/api/login/recuperacion/preguntas",Answers(8));Check(response.IsSuccessStatusCode,"Anonymous question endpoint accepts proof");Check(response.Headers.CacheControl?.NoStore==true,"Recovery authorization not cached");
    var payload=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;string token=payload.GetProperty("data").GetProperty("token").GetString()!;
    using var reset=await http.PostAsJsonAsync("/api/login/recuperacion/restablecer",new RecoveryPasswordRequest(token,newPassword,newPassword));Check(reset.IsSuccessStatusCode,"HTTP reset accepts proof");
    using var replay=await http.PostAsJsonAsync("/api/login/recuperacion/restablecer",new RecoveryPasswordRequest(token,"Another password 123","Another password 123"));Check((int)replay.StatusCode==401,"HTTP replay denied");
    bool limited=false;for(int i=0;i<12;i++){using var r=await http.PostAsJsonAsync("/api/login/recuperacion/preguntas",new RecoveryQuestionsRequest("absent@test.invalid","123","456"));if((int)r.StatusCode==429){limited=true;break;}}
    Check(limited,"HTTP rate limit enforced");
    Console.WriteLine($"PASS: {checks} password recovery checks; isolated MySQL and HTTP, no real accounts changed or emails sent.");
}
finally
{
    if(host is not null){if(!host.HasExited){host.Kill(true);host.WaitForExit();}host.Dispose();}
    if(!Regex.IsMatch(database,@"^ninehalo_recovery_verify_[a-f0-9]{32}$"))throw new Exception("Unsafe cleanup target");
    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{database}`");
}
static byte[] Decode(string value){const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();int buffer=0,bits=0;foreach(char c in value){buffer=(buffer<<5)|alphabet.IndexOf(c);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}return bytes.ToArray();}
sealed class TestClock:TimeProvider
{
    private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan value)=>now+=value;
}
