using APINineHalo.Entities;
namespace APINineHalo.Services;
public interface ISoporteService
{
    Task<PagedTicketsDto> GetTicketsAsync(int actor, int pagina);
    Task<ulong> CreateTicketAsync(int actor, CreateTicketRequest request);
}
