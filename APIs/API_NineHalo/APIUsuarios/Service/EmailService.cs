
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace APINineHalo.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendVerificationCodeAsync(
            string destinatario,
            string codigo,
            string proposito,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinatario))
                throw new ArgumentException(
                    "El correo del destinatario es obligatorio.");

            if (codigo == null ||
                codigo.Length != 6 ||
                !codigo.All(c => c >= '0' && c <= '9'))
            {
                throw new ArgumentException(
                    "El código debe contener exactamente seis dígitos.");
            }

            if (proposito != "REGISTRO" &&
                proposito != "LOGIN_2FA")
            {
                throw new ArgumentException(
                    "Propósito de verificación inválido.");
            }

            string host = _configuration["Email:SmtpHost"]
                ?? throw new InvalidOperationException(
                    "Falta configurar Email:SmtpHost.");

            int port = int.Parse(
                _configuration["Email:SmtpPort"]
                ?? throw new InvalidOperationException(
                    "Falta configurar Email:SmtpPort."));

            string usuario = _configuration["Email:Username"]
                ?? throw new InvalidOperationException(
                    "Falta configurar Email:Username.");

            string password = _configuration["Email:Password"]
                ?? throw new InvalidOperationException(
                    "Falta configurar Email:Password.");

            string remitente = _configuration["Email:From"]
                ?? usuario;

            string asunto;
            string mensaje;

            if (proposito == "REGISTRO")
            {
                asunto = "Nine Halo - Verificación de correo";

                mensaje =
                    "Bienvenido a Nine Halo.\n\n" +
                    "Tu código para verificar el correo electrónico es:\n\n" +
                    codigo + "\n\n" +
                    "Este código vence en 5 minutos.\n\n" +
                    "Si no solicitaste este registro, ignora el mensaje.";
            }
            else
            {
                asunto = "Nine Halo - Código de inicio de sesión";

                mensaje =
                    "Se ha solicitado iniciar sesión en Nine Halo.\n\n" +
                    "Tu código de autenticación es:\n\n" +
                    codigo + "\n\n" +
                    "Este código vence en 5 minutos.\n\n" +
                    "Si no intentaste iniciar sesión, no compartas " +
                    "este código con nadie.";
            }

            var email = new MimeMessage();

            email.From.Add(
                new MailboxAddress("Nine Halo", remitente));

            email.To.Add(
                MailboxAddress.Parse(destinatario));

            email.Subject = asunto;

            email.Body = new TextPart("plain")
            {
                Text = mensaje
            };

            using var smtp = new SmtpClient();

            smtp.Timeout = 15000;

            await smtp.ConnectAsync(
                host,
                port,
                SecureSocketOptions.StartTls,
                cancellationToken
            );

            try
            {
                await smtp.AuthenticateAsync(
                    usuario,
                    password,
                    cancellationToken
                );

                await smtp.SendAsync(
                    email,
                    cancellationToken
                );
            }
            finally
            {
                if (smtp.IsConnected)
                {
                    await smtp.DisconnectAsync(
                        true,
                        CancellationToken.None
                    );
                }
            }
        }
    }
}
