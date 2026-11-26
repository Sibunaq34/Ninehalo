using APINineHalo.Entities;

namespace APINineHalo.Services
{
    public interface ILicenciasService
    {
        Task<(bool Success, string Message, List<LicenciaDto>? Data)>
            GetLicenciasConDetallesAsync();

        Task<(bool Success, string Message, LicenciaActualDto? Data)>
            GetLicenciaActualAsync(int idUsuario);
    }
}
