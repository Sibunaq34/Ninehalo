
namespace APINineHalo.Entities
{
    public class PagedTicketsDto
    {
        public IEnumerable<TicketDto> Tickets { get; set; }
            = new List<TicketDto>();

        public int Pagina { get; set; }
        public int Tamano_Pagina { get; set; }
        public int Total_Tickets { get; set; }
        public int Total_Paginas { get; set; }
    }
}
