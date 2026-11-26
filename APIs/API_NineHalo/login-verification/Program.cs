using System.IdentityModel.Tokens.Jwt;
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
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;

// Service integration checks: real repositories and MySQL procedures, with email
// captured exclusively in memory. No messages are sent and no real users change.
var root=Path.GetFullPath(args.ElementAtOrDefault(0)??"API_NineHalo");
var dump=args.ElementAtOrDefault(1)??throw new ArgumentException("Supply backup.sql path.");
using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"APIUsuarios","appsettings.json")));
var connectionString=Environment.GetEnvironmentVariable("NINEHALO_TEST_CONNECTION")
    ??settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
var builder=new MySqlConnectionStringBuilder(connectionString){Database="",ConnectionTimeout=5};
using var admin=new MySqlConnection(builder.ConnectionString);
await admin.OpenAsync();
var database="ninehalo_login_verify_"+Guid.NewGuid().ToString("N");
int checks=0;
void Check(bool condition,string description){if(!condition)throw new Exception("FAILED: "+description);checks++;}
try
{
    await admin.ExecuteAsync($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
    builder.Database=database;
    using var db=new MySqlConnection(builder.ConnectionString);
    await db.OpenAsync();
    var sql=File.ReadAllText(dump);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");
    foreach(Match match in Regex.Matches(sql,@"(?ms)^CREATE TABLE `\w+`.*?\) ENGINE=InnoDB[^;]*;"))await db.ExecuteAsync(match.Value);
    await db.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");
    foreach(Match match in Regex.Matches(sql,@"(?ms)^CREATE DEFINER=[^\r\n]* PROCEDURE `\w+`.*?END ;;"))
        await db.ExecuteAsync(Regex.Replace(match.Value,@"^CREATE DEFINER=.*? PROCEDURE","CREATE PROCEDURE")[..^3]);
    var hash=BCrypt.Net.BCrypt.HashPassword("Test password only",workFactor:4);
    await db.ExecuteAsync("""
        INSERT INTO usuario(id_usuario,correo,password_hash,correo_verificado,estado) VALUES
          (1,'login@test.invalid',@hash,1,'ACTIVO'),(2,'registration@test.invalid',@hash,0,'ACTIVO'),
          (3,'mail-failure@test.invalid',@hash,0,'ACTIVO'),(4,'no-roles@test.invalid',@hash,1,'ACTIVO'),
          (5,'expired@test.invalid',@hash,1,'ACTIVO'),(6,'attempts@test.invalid',@hash,1,'ACTIVO'),
          (7,'blocked@test.invalid',@hash,1,'BLOQUEADO');
        INSERT INTO rol(id_rol,codigo,nombre) VALUES(1,'CLIENTE','Cliente');
        INSERT INTO usuario_rol(id_usuario,id_rol) VALUES(1,1),(2,1),(5,1),(6,1);
        """,new{hash});
    var codeSecret=RandomNumberGenerator.GetBytes(32);
    var jwtSecret=RandomNumberGenerator.GetBytes(32);
    var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["ConnectionStrings:DefaultConnection"]=builder.ConnectionString,
        ["VerificationCode:SecretKey"]=Convert.ToBase64String(codeSecret),
        ["Jwt:SecretKey"]=Convert.ToBase64String(jwtSecret),["Jwt:Issuer"]="login-test",["Jwt:Audience"]="login-test",["Jwt:ExpirationMinutes"]="15"
    }).Build();
    var protection=new EphemeralDataProtectionProvider();
    var mail=new CapturedEmail();
    var services=new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton<IDataProtectionProvider>(protection);
    services.AddSingleton<IDbConnectionF,DbConnectionFactory>();
    services.AddScoped<LoginRepository>();services.AddScoped<UsuarioRepository>();
    services.AddSingleton<IEmailService>(mail);services.AddSingleton<JwtTokenService>();
    services.AddScoped<TotpRepository>();services.AddScoped<AuthenticatorService>();services.AddSingleton(TimeProvider.System);
    services.AddScoped<ILoginService,LoginService>();
    // Optional registrations let these same checks run before and after extraction.
    var assembly=typeof(LoginService).Assembly;
    var verification=assembly.GetType("APINineHalo.Services.IVerificationCodeService");
    if(verification is not null)services.AddScoped(verification,assembly.GetType("APINineHalo.Services.VerificationCodeService")!);
    var preAuthentication=assembly.GetType("APINineHalo.Services.PreAuthenticationService");
    if(preAuthentication is not null)services.AddSingleton(preAuthentication);
    using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});
    using var scope=provider.CreateScope();
    var login=scope.ServiceProvider.GetRequiredService<ILoginService>();
    var invalid=await login.LoginAsync("","");Check(!invalid.Success&&invalid.Data is null,"Invalid credentials issue no challenge");
    var wrong=await login.LoginAsync("login@test.invalid","wrong");Check(!wrong.Success&&wrong.Message=="Correo o contraseña incorrectos.","Password failure message unchanged");
    Check(await db.ExecuteScalarAsync<int>("SELECT intentos_fallidos FROM usuario WHERE id_usuario=1")==1,"Password failure recorded");
    var unverified=await login.LoginAsync("registration@test.invalid","Test password only");Check(!unverified.Success&&unverified.Message=="Debes verificar tu correo electrónico.","Unverified account denied");
    var blocked=await login.LoginAsync("blocked@test.invalid","Test password only");Check(!blocked.Success&&blocked.Message=="La cuenta no está activa.","Blocked account denied");
    var prepared=await login.LoginAsync("  login@test.invalid  ","Test password only");
    Check(prepared.Success&&prepared.Data?.Requires2FA==true&&prepared.Data.ExpiresIn==300,"Credentials only create five-minute pre-authentication");
    Check(mail.Messages.Count==0&&await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM codigo_verificacion")==0,"Password step sends no email or code");
    var oldProtector=protection.CreateProtector("NineHalo.Login.PreAuthentication.v1").ToTimeLimitedDataProtector();
    Check(oldProtector.Unprotect(prepared.Data!.PreAuthToken)=="1","Pre-authentication purpose and payload remain compatible");
    var empty=await login.SendLoginCodeAsync("");Check(!empty.Success&&empty.Data is null,"Empty authorization rejected");
    var expiredToken=oldProtector.Protect("1",TimeSpan.FromSeconds(-1));
    var expiredAuthorization=await login.SendLoginCodeAsync(expiredToken);Check(!expiredAuthorization.Success&&expiredAuthorization.Message.Contains("expiró"),"Expired pre-authentication rejected");
    var invalidAuthorization=await login.SendLoginCodeAsync("invalid");Check(!invalidAuthorization.Success,"Tampered authorization rejected");
    var sent=await login.SendLoginCodeAsync(prepared.Data.PreAuthToken);
    Check(sent.Success&&sent.Data?.Requires2FA==true&&sent.Data.ExpiresIn==300,"Login code delivery result unchanged");
    var firstMail=mail.Messages.Single();
    Check(Regex.IsMatch(firstMail.Code,"^[0-9]{6}$")&&firstMail.Purpose=="LOGIN_2FA","Six-digit login code and purpose preserved");
    var stored=await db.QuerySingleAsync<byte[]>("SELECT codigo_hash FROM codigo_verificacion WHERE id_codigo=@id",new{id=sent.Data!.IdVerificacion});
    Check(stored.SequenceEqual(HMACSHA256.HashData(codeSecret,Encoding.UTF8.GetBytes($"1:LOGIN_2FA:{firstMail.Code}"))),"Stored HMAC binding to account and purpose preserved");
    var resend=await login.SendLoginCodeAsync(prepared.Data.PreAuthToken);Check(!resend.Success&&mail.Messages.Count==1,"Resend cooldown preserved");
    var malformed=await login.VerifyCodeAsync(new(){IdVerificacion=sent.Data.IdVerificacion,Codigo="１２３４５６"});Check(!malformed.Success&&malformed.AccessToken is null,"Non-ASCII code rejected without JWT");
    var missing=await login.VerifyCodeAsync(new(){IdVerificacion=long.MaxValue,Codigo="000000"});Check(!missing.Success&&missing.AccessToken is null,"Unknown challenge rejected without JWT");
    string WrongCode(string code)=>code=="000000"?"000001":"000000";
    var wrongCode=await login.VerifyCodeAsync(new(){IdVerificacion=sent.Data.IdVerificacion,Codigo=WrongCode(firstMail.Code)});
    Check(!wrongCode.Success&&wrongCode.AccessToken is null,"Incorrect code issues no JWT");
    Check(await db.ExecuteScalarAsync<int>("SELECT intentos FROM codigo_verificacion WHERE id_codigo=@id",new{id=sent.Data.IdVerificacion})==1,"Incorrect-code attempt recorded");
    var verified=await login.VerifyCodeAsync(new(){IdVerificacion=sent.Data.IdVerificacion,Codigo=firstMail.Code});
    Check(verified.Success&&!string.IsNullOrWhiteSpace(verified.AccessToken),"JWT issued only after valid code");
    var handler=new JwtSecurityTokenHandler();
    var principal=handler.ValidateToken(verified.AccessToken,new TokenValidationParameters{
        ValidIssuer="login-test",ValidAudience="login-test",IssuerSigningKey=new SymmetricSecurityKey(jwtSecret),ClockSkew=TimeSpan.Zero
    },out _);
    Check(principal.Claims.Any(claim=>claim.Value=="CLIENTE"),"Roles loaded from database into signed JWT");
    Check(await db.ExecuteScalarAsync<int>("SELECT intentos_fallidos FROM usuario WHERE id_usuario=1")==0,"Successful login resets failed attempts");
    var reused=await login.VerifyCodeAsync(new(){IdVerificacion=sent.Data.IdVerificacion,Codigo=firstMail.Code});Check(!reused.Success&&reused.AccessToken is null,"Consumed code cannot issue another JWT");
    Check(!(await login.SendRegistrationCodeAsync(0)).Success,"Invalid registration account rejected");
    Check(!(await login.SendRegistrationCodeAsync(999)).Success,"Missing registration account rejected");
    Check(!(await login.SendRegistrationCodeAsync(1)).Success,"Verified email cannot request registration code");
    var registration=await login.SendRegistrationCodeAsync(2);
    Check(registration.Success&&registration.Data?.Requires2FA==false&&registration.Data.ExpiresIn==300,"Registration code contract unchanged");
    var registrationMail=mail.Messages.Last();Check(registrationMail.Purpose=="REGISTRO","Registration purpose preserved");
    Check(!(await login.VerifyCodeAsync(new(){IdVerificacion=registration.Data!.IdVerificacion,Codigo=registrationMail.Code})).Success,"Registration code cannot authorize login");
    Check(!(await login.VerifyRegistrationCodeAsync(new(){IdVerificacion=sent.Data.IdVerificacion,Codigo=firstMail.Code})).Success,"Login code cannot verify registration");
    Check(!(await login.VerifyRegistrationCodeAsync(new(){IdVerificacion=registration.Data.IdVerificacion,Codigo=WrongCode(registrationMail.Code)})).Success,"Incorrect registration code rejected");
    Check((await login.VerifyRegistrationCodeAsync(new(){IdVerificacion=registration.Data.IdVerificacion,Codigo=registrationMail.Code})).Success,"Correct registration code verifies email");
    Check(await db.ExecuteScalarAsync<bool>("SELECT correo_verificado FROM usuario WHERE id_usuario=2"),"Email verification persisted");
    Check(!(await login.VerifyRegistrationCodeAsync(new(){IdVerificacion=registration.Data.IdVerificacion,Codigo=registrationMail.Code})).Success,"Registration code is single-use");
    mail.FailNext=true;
    var mailFailure=await login.SendRegistrationCodeAsync(3);Check(!mailFailure.Success&&mailFailure.Data is null&&mailFailure.Message.Contains("no se pudo enviar"),"Email delivery failure preserves existing response");
    var noRoles=await login.SendLoginCodeAsync(oldProtector.Protect("4",TimeSpan.FromMinutes(5)));
    var noRolesResult=await login.VerifyCodeAsync(new(){IdVerificacion=noRoles.Data!.IdVerificacion,Codigo=mail.Messages.Last().Code});
    Check(!noRolesResult.Success&&noRolesResult.AccessToken is null&&noRolesResult.Message.Contains("roles activos"),"No JWT without active database roles");
    var expiredCode=await login.SendLoginCodeAsync(oldProtector.Protect("5",TimeSpan.FromMinutes(5)));
    var expiredMail=mail.Messages.Last();
    await db.ExecuteAsync("UPDATE codigo_verificacion SET creado_en=DATE_SUB(NOW(),INTERVAL 10 MINUTE),expira_en=DATE_SUB(NOW(),INTERVAL 1 SECOND) WHERE id_codigo=@id",new{id=expiredCode.Data!.IdVerificacion});
    var expiredResult=await login.VerifyCodeAsync(new(){IdVerificacion=expiredCode.Data.IdVerificacion,Codigo=expiredMail.Code});Check(!expiredResult.Success&&expiredResult.AccessToken is null,"Expired code issues no JWT");
    var attempts=await login.SendLoginCodeAsync(oldProtector.Protect("6",TimeSpan.FromMinutes(5)));
    var attemptsMail=mail.Messages.Last();
    for(int i=0;i<5;i++)Check(!(await login.VerifyCodeAsync(new(){IdVerificacion=attempts.Data!.IdVerificacion,Codigo=WrongCode(attemptsMail.Code)})).Success,"Incorrect attempt denied");
    var exhausted=await login.VerifyCodeAsync(new(){IdVerificacion=attempts.Data!.IdVerificacion,Codigo=attemptsMail.Code});Check(!exhausted.Success&&exhausted.AccessToken is null,"Attempt limit enforced even with correct code");
    await db.ExecuteAsync("UPDATE usuario SET estado='BLOQUEADO' WHERE id_usuario=1");
    var changedState=await login.SendLoginCodeAsync(prepared.Data.PreAuthToken);Check(!changedState.Success&&changedState.Message.Contains("no está habilitada"),"Account rechecked after password step");
    Console.WriteLine($"PASS: {checks} login/verification checks against isolated MySQL; all email captured in memory.");
}
finally
{
    if(!Regex.IsMatch(database,@"^ninehalo_login_verify_[a-f0-9]{32}$"))throw new InvalidOperationException("Invalid cleanup database.");
    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{database}`");
}

sealed class CapturedEmail : IEmailService
{
    public List<(string Code,string Purpose)> Messages {get;}=[];
    public bool FailNext {get;set;}
    public Task SendVerificationCodeAsync(string destinatario,string codigo,string proposito,CancellationToken cancellationToken=default)
    {
        if(FailNext){FailNext=false;throw new IOException("Simulated email delivery failure.");}
        Messages.Add((codigo,proposito));return Task.CompletedTask;
    }
}
