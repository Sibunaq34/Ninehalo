
using APINineHalo.Entities;

namespace APINineHalo.Services
{
    public interface IUsuarioService
    {
        Task<(bool Success, string Message, int? IdUsuario)>
            RegisterAsync(RegisterRequest request);

        Task<(bool Success, string Message)>
            ChangePasswordAsync(
                int idUsuario,
                ChangePasswordRequest request);
    }
}
