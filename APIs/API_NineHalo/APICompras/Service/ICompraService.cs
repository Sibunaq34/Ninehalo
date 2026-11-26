
using APINineHalo.Entities;

namespace APINineHalo.Services
{
    public interface ICompraService
    {
        Task<(bool Success, string Message, PurchaseDto? Purchase)>
            RegisterPurchaseAsync(int idUsuario, PurchaseRequest request);
    }
}
