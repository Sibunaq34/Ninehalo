using APINineHalo.Entities;
using Dapper;
using System.Data;
namespace APINineHalo.Repository;
public class CompraRepository(IDbConnectionF dbFactory)
{
    public async Task<PurchaseDto?> RegisterPurchaseAsync(int idUsuario,int idLicencia)
    {
        using var connection=dbFactory.CreateConnection();
        var id=await connection.QuerySingleAsync<ulong>("sp_registrar_compra",new{p_usuario=idUsuario,p_licencia=idLicencia,p_referencia=(string?)null},commandType:CommandType.StoredProcedure);
        return await connection.QuerySingleAsync<PurchaseDto>("SELECT id_compra,id_licencia,fecha_compra,fecha_vencimiento,estado FROM compra WHERE id_compra=@id AND id_usuario=@idUsuario",new{id,idUsuario});
    }
}
