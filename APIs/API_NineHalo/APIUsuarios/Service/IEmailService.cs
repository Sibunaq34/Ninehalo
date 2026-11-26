
namespace APINineHalo.Services
{
    public interface IEmailService
    {
        Task SendVerificationCodeAsync(
            string destinatario,
            string codigo,
            string proposito,
            CancellationToken cancellationToken = default);
    }
}
