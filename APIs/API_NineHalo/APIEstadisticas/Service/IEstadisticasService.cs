using APINineHalo.Entities;
namespace APINineHalo.Services;
public interface IEstadisticasService
{
    Task<IEnumerable<EstadisticaDto>> GetStatisticsAsync(int actor, int idUsuario);
}
