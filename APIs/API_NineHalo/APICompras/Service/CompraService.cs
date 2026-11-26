
using APINineHalo.Entities;
using APINineHalo.Repository;
using MySql.Data.MySqlClient;

namespace APINineHalo.Services
{
    public class CompraService : ICompraService
    {
        private readonly CompraRepository _repository;

        public CompraService(CompraRepository repository)
        {
            _repository = repository;
        }

        public async Task<(bool Success, string Message, PurchaseDto? Purchase)>
            RegisterPurchaseAsync(int idUsuario, PurchaseRequest request)
        {
            if (request == null)
                return (false, "Datos de compra inválidos.", null);

            if (idUsuario <= 0 || request.id_usuario <= 0 || request.id_licencia <= 0)
                return (false, "Identificadores inválidos.", null);

            // Verificar que el usuario coincida con el de la solicitud.
            if (idUsuario != request.id_usuario)
                return (false, "No autorizado para realizar la compra para otro usuario.", null);

            try
            {
                var result = await _repository.RegisterPurchaseAsync(
                    idUsuario,
                    request.id_licencia
                );

                if (result == null)
                    return (false, "Usuario o licencia no encontrados.", null);

                return (true, "Compra registrada correctamente.", result);
            }
            catch (MySqlException ex)
            {
                var msg = ex.Message;

                if (msg.Contains("igual o superior", StringComparison.OrdinalIgnoreCase))
                    return (false, "Ya tienes una licencia igual o superior.", null);

                if (msg.Contains("no existe", StringComparison.OrdinalIgnoreCase))
                    return (false, "Usuario o licencia no encontrados.", null);

                Console.WriteLine(ex.ToString());

                return (false, "Error interno al registrar la compra.", null);
            }
            catch (Exception)
            {
                return (false, "Error interno al registrar la compra.", null);
            }
        }
    }
}
