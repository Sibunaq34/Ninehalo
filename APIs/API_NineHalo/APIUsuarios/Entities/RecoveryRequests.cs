namespace APINineHalo.Entities;

public record RecoveryQuestionsRequest(string Correo, string Identificacion, string Telefono);
public record RecoveryAuthenticatorRequest(string Correo, string Codigo);
public record RecoveryPasswordRequest(string Token, string Password_Nueva, string Confirmacion);
