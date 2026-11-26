using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

var root=Path.GetFullPath(args.ElementAtOrDefault(0)??"API_NineHalo");
var migration=File.ReadAllText(Path.Combine(root,"APIUsuarios","Migrations","20261027_usuario_totp.sql"));
using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"APIUsuarios","appsettings.json")));
var cs=Environment.GetEnvironmentVariable("NINEHALO_TEST_CONNECTION")??settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
var csb=new MySqlConnectionStringBuilder(cs){ConnectionTimeout=5};
if(args.Contains("--migrate"))
{
    if(csb.Database!="nine_halo")throw new InvalidOperationException("Expected the configured nine_halo database.");
    using var target=new MySqlConnection(csb.ConnectionString);await target.OpenAsync();
    await target.ExecuteAsync(migration);
    await target.QueryAsync("SELECT id_usuario,id_vinculacion,secreto_protegido,habilitado,pendiente_expira_en,ultimo_paso,fallos,bloqueado_hasta FROM usuario_totp LIMIT 0");
    Console.WriteLine("Applied additive usuario_totp migration; existing accounts and data were not modified.");return;
}
var dump=args.ElementAtOrDefault(1)??throw new ArgumentException("Supply backup.sql path.");
csb.Database="";
using var admin=new MySqlConnection(csb.ConnectionString);await admin.OpenAsync();
string database="ninehalo_totp_verify_"+Guid.NewGuid().ToString("N");
int checks=0;
void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);checks++;}
async Task Reject(Func<Task> action,int status){try{await action();throw new Exception("Expected rejection");}catch(AuthenticatorException ex){Check(ex.Status==status,"Expected authenticator rejection status");}}
Process? server=null;
try
{
    byte[] vectorSecret=Encoding.ASCII.GetBytes("12345678901234567890");
    foreach(var vector in new[]{(59L,"94287082"),(1111111109L,"07081804"),(1111111111L,"14050471"),(1234567890L,"89005924"),(2000000000L,"69279037"),(20000000000L,"65353130")})
        Check(TotpAlgorithm.GenerateCode(vectorSecret,vector.Item1/30,8)==vector.Item2,"RFC 6238 SHA1 vector");
    Check(TotpAlgorithm.Base32(vectorSecret)=="GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ","Base32 standard vector");
    Check(TotpAlgorithm.Match(vectorSecret,"287082",DateTimeOffset.FromUnixTimeSeconds(59),null)==1,"Six digit TOTP");
    Check(TotpAlgorithm.Match(vectorSecret,"287082",DateTimeOffset.FromUnixTimeSeconds(59),1)==null,"Replay rejected by time step");
    Check(TotpAlgorithm.Match(vectorSecret,"１２３４５６",DateTimeOffset.UtcNow,null)==null,"Non-ASCII code rejected");
    await admin.ExecuteAsync($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");csb.Database=database;
    using var db=new MySqlConnection(csb.ConnectionString);await db.OpenAsync();
    var sql=File.ReadAllText(dump);await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");
    foreach(Match match in Regex.Matches(sql,@"(?ms)^CREATE TABLE `\w+`.*?\) ENGINE=InnoDB[^;]*;"))await db.ExecuteAsync(match.Value);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");
    foreach(Match match in Regex.Matches(sql,@"(?ms)^CREATE DEFINER=[^\r\n]* PROCEDURE `\w+`.*?END ;;"))await db.ExecuteAsync(Regex.Replace(match.Value,@"^CREATE DEFINER=.*? PROCEDURE","CREATE PROCEDURE")[..^3]);
    await db.ExecuteAsync(migration);await db.ExecuteAsync(migration);
    string password="Temporary test password";
    var hash=BCrypt.Net.BCrypt.HashPassword(password,workFactor:4);
    for(int user=1;user<=7;user++)await db.ExecuteAsync("INSERT INTO usuario(id_usuario,correo,password_hash,correo_verificado,estado) VALUES(@user,@email,@hash,1,'ACTIVO')",new{user,email=$"user{user}@test.invalid",hash});
    await db.ExecuteAsync("INSERT INTO rol(id_rol,codigo,nombre) VALUES(1,'CLIENTE','Cliente'); INSERT INTO usuario_rol(id_usuario,id_rol) VALUES(1,1),(2,1),(3,1),(4,1),(5,1),(7,1);");
    string jwtSecret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),codeSecret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["ConnectionStrings:DefaultConnection"]=csb.ConnectionString,["VerificationCode:SecretKey"]=codeSecret,
        ["Jwt:SecretKey"]=jwtSecret,["Jwt:Issuer"]="totp-test",["Jwt:Audience"]="totp-test",["Jwt:ExpirationMinutes"]="15"
    }).Build();
    var clock=new TestClock();var protection=new EphemeralDataProtectionProvider();
    var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IConfiguration>(config);services.AddSingleton<IDataProtectionProvider>(protection);services.AddSingleton<TimeProvider>(clock);
    services.AddSingleton<IDbConnectionF,DbConnectionFactory>();services.AddScoped<UsuarioRepository>();services.AddScoped<LoginRepository>();services.AddScoped<TotpRepository>();
    services.AddSingleton<PreAuthenticationService>();services.AddScoped<AuthenticatorService>();services.AddScoped<IVerificationCodeService,VerificationCodeService>();
    services.AddSingleton<IEmailService,NoEmail>();services.AddSingleton<JwtTokenService>();services.AddScoped<ILoginService,LoginService>();
    using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});using var scope=provider.CreateScope();
    var auth=scope.ServiceProvider.GetRequiredService<AuthenticatorService>();var login=scope.ServiceProvider.GetRequiredService<ILoginService>();var pre=scope.ServiceProvider.GetRequiredService<PreAuthenticationService>();
    string Code(AuthenticatorSetup setup)=>TotpAlgorithm.GenerateCode(Decode(setup.Secreto),clock.GetUtcNow().ToUnixTimeSeconds()/30);
    string Wrong(AuthenticatorSetup setup){var secret=Decode(setup.Secreto);var now=clock.GetUtcNow().ToUnixTimeSeconds()/30;var valid=new[]{-1,0,1}.Select(offset=>TotpAlgorithm.GenerateCode(secret,now+offset)).ToHashSet();return Enumerable.Range(0,10).Select(i=>i.ToString("D6")).First(code=>!valid.Contains(code));}
    Check(!(await auth.StatusAsync(1)).Habilitado,"Account starts unlinked");
    await Reject(()=>auth.BeginAsync(1,"wrong"),400);Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario_totp")==0,"Wrong password stores no secret");
    var setup=await auth.BeginAsync(1,password);
    Check(setup.Secreto.Length==32&&setup.ExpiresIn==600,"Random 160-bit key and ten-minute enrollment");
    Check(Convert.FromBase64String(setup.QrPng).Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"Locally generated QR is PNG");
    var encrypted=await db.ExecuteScalarAsync<string>("SELECT secreto_protegido FROM usuario_totp WHERE id_usuario=1") ?? throw new Exception("Missing encrypted key");
    Check(encrypted!=setup.Secreto&&!encrypted.Contains(Convert.ToBase64String(Decode(setup.Secreto))),"Stored secret encrypted");
    Check(!(await auth.StatusAsync(1)).Habilitado,"Displaying QR alone never activates TOTP");
    await Reject(()=>auth.ConfirmAsync(2,new(){Id_Vinculacion=setup.IdVinculacion,Codigo=Code(setup)}),409);
    Check(!(await auth.ConfirmAsync(1,new(){Id_Vinculacion=setup.IdVinculacion,Codigo=Wrong(setup)})).Success,"Wrong activation code rejected");
    Check((await auth.ConfirmAsync(1,new(){Id_Vinculacion=setup.IdVinculacion,Codigo=Code(setup)})).Success,"Possession proof activates TOTP");
    Check((await auth.LoginStatusAsync(pre.CreateToken(1))).Habilitado,"Method discovery bound to password authorization");
    var statusJson=JsonSerializer.Serialize(await auth.StatusAsync(1));Check(!statusJson.Contains(setup.Secreto)&&!statusJson.Contains(encrypted),"Status never exposes key");
    await Reject(()=>auth.BeginAsync(1,password),409);
    var oldCode=await login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)});Check(!oldCode.Success&&oldCode.AccessToken is null,"Enrollment code cannot be reused for login");
    clock.Advance(TimeSpan.FromSeconds(30));
    var verified=await login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)});
    Check(verified.Success&&!string.IsNullOrEmpty(verified.AccessToken),"TOTP proof grants JWT via login coordinator");
    Check(new JwtSecurityTokenHandler().ReadJwtToken(verified.AccessToken).Claims.Any(c=>c.Type=="sub"&&c.Value=="1"),"JWT identity taken from protected authorization");
    var replay=await login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)});Check(!replay.Success&&replay.AccessToken is null,"Fresh pre-auth cannot replay accepted TOTP");
    clock.Advance(TimeSpan.FromSeconds(30));
    var concurrent=await Task.WhenAll(login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)}),login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)}));
    Check(concurrent.Count(result=>result.Success)==1,"Concurrent requests consume a step only once");
    var attemptSetup=await auth.BeginAsync(3,password);await auth.ConfirmAsync(3,new(){Id_Vinculacion=attemptSetup.IdVinculacion,Codigo=Code(attemptSetup)});clock.Advance(TimeSpan.FromSeconds(30));
    for(int attempt=0;attempt<5;attempt++)Check(!(await auth.VerifyLoginAsync(new(){Pre_Auth_Token=pre.CreateToken(3),Codigo=Wrong(attemptSetup)})).Success,"Bad TOTP denied");
    Check((await auth.VerifyLoginAsync(new(){Pre_Auth_Token=pre.CreateToken(3),Codigo=Code(attemptSetup)})).Status==429,"Persistent five-attempt lockout enforced");
    clock.Advance(TimeSpan.FromMinutes(6));Check((await auth.VerifyLoginAsync(new(){Pre_Auth_Token=pre.CreateToken(3),Codigo=Code(attemptSetup)})).Success,"Lockout expires after five minutes");
    var pending=await auth.BeginAsync(4,password);clock.Advance(TimeSpan.FromMinutes(11));await Reject(()=>auth.ConfirmAsync(4,new(){Id_Vinculacion=pending.IdVinculacion,Codigo=Code(pending)}),409);
    var replacement=await auth.BeginAsync(4,password);Check(replacement.IdVinculacion!=pending.IdVinculacion&&replacement.Secreto!=pending.Secreto,"Renewed enrollment replaces pending key");
    await Reject(()=>auth.ConfirmAsync(4,new(){Id_Vinculacion=pending.IdVinculacion,Codigo=Code(replacement)}),409);
    await Reject(()=>auth.LoginStatusAsync("invalid"),401);
    var expired=protection.CreateProtector("NineHalo.Login.PreAuthentication.v1").ToTimeLimitedDataProtector().Protect("1",TimeSpan.FromSeconds(-1));await Reject(()=>auth.LoginStatusAsync(expired),401);
    await db.ExecuteAsync("UPDATE usuario SET estado='BLOQUEADO' WHERE id_usuario=1");await Reject(()=>auth.VerifyLoginAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)}),403);await db.ExecuteAsync("UPDATE usuario SET estado='ACTIVO' WHERE id_usuario=1");
    await Reject(()=>auth.DisableAsync(1,new(){Id_Vinculacion=setup.IdVinculacion,Password="wrong",Codigo=Code(setup)}),400);
    Check((await auth.StatusAsync(1)).Habilitado,"Wrong password cannot remove authenticator");
    Check((await auth.DisableAsync(1,new(){Id_Vinculacion=setup.IdVinculacion,Password=password,Codigo=Code(setup)})).Success,"Password and fresh code remove authenticator");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario_totp WHERE id_usuario=1")==0,"Removal erases encrypted key");
    await Reject(()=>auth.VerifyLoginAsync(new(){Pre_Auth_Token=pre.CreateToken(1),Codigo=Code(setup)}),400);
    var noRole=await auth.BeginAsync(6,password);await auth.ConfirmAsync(6,new(){Id_Vinculacion=noRole.IdVinculacion,Codigo=Code(noRole)});clock.Advance(TimeSpan.FromSeconds(30));
    Check(!(await login.VerifyAuthenticatorAsync(new(){Pre_Auth_Token=pre.CreateToken(6),Codigo=Code(noRole)})).Success,"No JWT for accounts without active roles");

    // HTTP checks use another isolated account and the actual production host.
    var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"APIUsuarios"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(Path.Combine(root,"APIUsuarios","bin","Debug","net10.0","APIUsuarios.dll"));
    foreach(var pair in new Dictionary<string,string>{["ASPNETCORE_URLS"]="http://127.0.0.1:18645",["ASPNETCORE_ENVIRONMENT"]="Production",["ConnectionStrings__DefaultConnection"]=csb.ConnectionString,["Jwt__SecretKey"]=jwtSecret,["Jwt__Issuer"]="totp-test",["Jwt__Audience"]="totp-test",["Jwt__ExpirationMinutes"]="15",["VerificationCode__SecretKey"]=codeSecret})start.Environment[pair.Key]=pair.Value;
    server=Process.Start(start)!;server.OutputDataReceived+=(_,_)=>{};server.ErrorDataReceived+=(_,_)=>{};server.BeginOutputReadLine();server.BeginErrorReadLine();
    using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:18645"),Timeout=TimeSpan.FromSeconds(10)};
    bool ready=false;for(int attempt=0;attempt<80;attempt++){if(server.HasExited)throw new Exception("Test host exited");try{await http.GetAsync("/unused-health");ready=true;break;}catch(HttpRequestException){await Task.Delay(100);}}
    Check(ready,"HTTP host started");
    string jwt=scope.ServiceProvider.GetRequiredService<JwtTokenService>().GenerateToken(7,new[]{"CLIENTE"});
    async Task<JsonElement> Request(string method,string path,int expected,object? body=null,bool authorized=false)
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),path);if(body is not null)request.Content=JsonContent.Create(body);if(authorized)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",jwt);
        using var response=await http.SendAsync(request);string text=await response.Content.ReadAsStringAsync();Check((int)response.StatusCode==expected,$"HTTP {method} {path}: {(int)response.StatusCode}, expected {expected}");
        if(authorized&&method=="GET"&&expected==200)Check(response.Headers.CacheControl?.NoStore==true,"Management response not cached");
        if(path.StartsWith("/api/login/autenticador")&&expected==200)Check(response.Headers.CacheControl?.NoStore==true,"Login verification response not cached");
        return text.Length==0?default:JsonDocument.Parse(text).RootElement.Clone();
    }
    await Request("GET","/api/perfil/autenticador",401);
    var initialStatus=await Request("GET","/api/perfil/autenticador",200,authorized:true);Check(!initialStatus.GetProperty("data").GetProperty("habilitado").GetBoolean(),"HTTP status initially disabled");
    await Request("POST","/api/perfil/autenticador/vincular",400,new{password="wrong"},true);
    var enroll=await Request("POST","/api/perfil/autenticador/vincular",200,new{password},true);
    var httpSecret=Decode(enroll.GetProperty("data").GetProperty("secreto").GetString()!);string enrollment=enroll.GetProperty("data").GetProperty("id_vinculacion").GetString()!;
    await Request("POST","/api/perfil/autenticador/confirmar",200,new{id_vinculacion=enrollment,codigo=TotpAlgorithm.GenerateCode(httpSecret,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30)},true);
    var prepared=await Request("POST","/api/login",200,new{correo="user7@test.invalid",password});string preToken=prepared.GetProperty("data").GetProperty("pre_auth_token").GetString()!;
    Check(!prepared.GetProperty("data").TryGetProperty("access_token",out _),"Password HTTP step grants no JWT");
    var discovery=await Request("POST","/api/login/autenticador/estado",200,new{pre_auth_token=preToken});Check(discovery.GetProperty("data").GetProperty("habilitado").GetBoolean(),"App available after password verification");
    string nextCode=TotpAlgorithm.GenerateCode(httpSecret,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30+1);
    var session=await Request("POST","/api/login/autenticador/verificar",200,new{pre_auth_token=preToken,codigo=nextCode});Check(session.GetProperty("data").GetProperty("access_token").GetString()!.Length>0,"HTTP TOTP flow grants session");
    await Request("POST","/api/login/autenticador/verificar",400,new{pre_auth_token=preToken,codigo=nextCode});
    bool limited=false;for(int attempt=0;attempt<12;attempt++){using var response=await http.PostAsJsonAsync("/api/login/autenticador/estado",new{pre_auth_token=preToken});if((int)response.StatusCode==429){limited=true;break;}}
    Check(limited,"HTTP authenticator rate limit enforced");
    Console.WriteLine($"PASS: {checks} TOTP checks including RFC vectors, isolated MySQL and real HTTP endpoints; no email sent.");
}
finally
{
    if(server is not null){if(!server.HasExited){server.Kill(entireProcessTree:true);server.WaitForExit();}server.Dispose();}
    if(!Regex.IsMatch(database,@"^ninehalo_totp_verify_[a-f0-9]{32}$"))throw new InvalidOperationException("Invalid cleanup schema");
    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{database}`");
}
static byte[] Decode(string value)
{
    const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();int buffer=0,bits=0;
    foreach(char c in value){int next=alphabet.IndexOf(c);if(next<0)throw new Exception("Invalid Base32");buffer=(buffer<<5)|next;bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
    return bytes.ToArray();
}
sealed class TestClock:TimeProvider
{
    private DateTimeOffset now=DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow()=>now;
    public void Advance(TimeSpan duration)=>now+=duration;
}
sealed class NoEmail:IEmailService
{
    public Task SendVerificationCodeAsync(string recipient,string code,string purpose,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("TOTP must not send email");
}
