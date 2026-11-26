using System.Text.Json.Serialization;
namespace APINineHalo.Entities;

public class AuthenticatorPasswordRequest
{
    public string Password { get; set; } = "";
}
public class AuthenticatorConfirmationRequest
{
    public string Id_Vinculacion { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Password { get; set; } = "";
}
public class VerifyAuthenticatorRequest
{
    public string Pre_Auth_Token { get; set; } = "";
    public string Codigo { get; set; } = "";
}
public record AuthenticatorStatus(
    [property: JsonPropertyName("habilitado")] bool Habilitado,
    [property: JsonPropertyName("id_vinculacion")] string? IdVinculacion);
public record AuthenticatorSetup(
    [property: JsonPropertyName("id_vinculacion")] string IdVinculacion,
    [property: JsonPropertyName("secreto")] string Secreto,
    [property: JsonPropertyName("qr_png")] string QrPng,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
public record TotpCheck(bool Success, string Message, int? UserId = null, int Status = 400);
public class AuthenticatorException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
