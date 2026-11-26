using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;
public class LicenciasService(LicenciasRepository repository) : ILicenciasService
{
    public async Task<(bool Success, string Message, List<LicenciaDto>? Data)> GetLicenciasConDetallesAsync() =>
        (true, "Licencias obtenidas correctamente.", (await repository.GetLicenciasAsync()).ToList());
    public async Task<(bool Success, string Message, LicenciaActualDto? Data)> GetLicenciaActualAsync(int idUsuario)
    {
        var license = await repository.GetLicenciaActualAsync(idUsuario);
        return (true, license is null ? "El usuario no tiene una licencia activa." : "Licencia activa obtenida correctamente.", license);
    }
}
