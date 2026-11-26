using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;
public class EstadisticasService(EstadisticasRepository repository) : IEstadisticasService
{
    public Task<IEnumerable<EstadisticaDto>> GetStatisticsAsync(int actor, int idUsuario) => repository.GetStatisticsAsync(actor, idUsuario);
}
