using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;
public class SoporteService(SoporteRepository repository) : ISoporteService
{
    public async Task<PagedTicketsDto> GetTicketsAsync(int actor, int pagina)
    {
        var conversations = (await repository.GetConversationsAsync(actor)).Where(c => c.Id_Usuario == actor).ToList();
        return new PagedTicketsDto {
            Tickets=conversations.Skip((pagina-1)*10).Take(10).Select(c => new TicketDto {
                Id_Soporte=c.Id_Conversacion, Titulo=c.Asunto, Fecha_Creacion=c.Fecha_Creacion, Estado=c.Estado
            }), Pagina=pagina, Tamano_Pagina=10, Total_Tickets=conversations.Count,
            Total_Paginas=(int)Math.Ceiling(conversations.Count/10.0)
        };
    }
    public Task<ulong> CreateTicketAsync(int actor, CreateTicketRequest request) => repository.CreateTicketAsync(actor, request);
}
