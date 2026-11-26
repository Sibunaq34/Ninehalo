
using APINineHalo.Entities;
using Dapper;
using MySql.Data.MySqlClient;
using System.Data;

namespace APINineHalo.Repository
{
    public class UsuarioRepository
    {
        private readonly IDbConnectionF _dbFactory;

        public UsuarioRepository(IDbConnectionF dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<object?> RegisterUserAsync(
            string id,
            string nombre,
            string apellidos,
            string telefono,
            string correo,
            string passwordHash,
            int idPais, int idProvincia, int idCanton, int idDistrito)
        {
            try
            {
                using var connection = _dbFactory.CreateConnection();

                var parameters = new DynamicParameters();

                parameters.Add("p_identificacion", id, DbType.String);
                parameters.Add("p_nombre", nombre, DbType.String);
                parameters.Add("p_apellidos", apellidos, DbType.String);
                parameters.Add("p_telefono", telefono, DbType.String);
                parameters.Add("p_correo", correo.Trim().ToLowerInvariant(), DbType.String);
                parameters.Add("p_password_hash", passwordHash, DbType.String);
                parameters.Add("p_id_pais", idPais, DbType.Int32);
                parameters.Add("p_id_provincia", idProvincia, DbType.Int32);
                parameters.Add("p_id_canton", idCanton, DbType.Int32);
                parameters.Add("p_id_distrito", idDistrito, DbType.Int32);

                return await connection.QueryFirstOrDefaultAsync(
                    "sp_registrar_usuario_ubicacion",
                    parameters,
                    commandType: CommandType.StoredProcedure
                );
            }
            catch (MySqlException ex)
            {
                if (ex.Number == 1062)
                    return ex.Message;
                if (ex.Number == 1644 && ex.Message == "Ubicación geográfica inválida")
                    return ex.Message;

                throw;
            }
        }

        public async Task<UserAuthEntity?> GetUserByIdAsync(
            int idUsuario)
        {
            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("p_id_usuario", idUsuario, DbType.Int32);

            return await connection.QueryFirstOrDefaultAsync<UserAuthEntity>(
                "sp_consultar_usuario_id",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<object?> ChangePasswordAsync(
            int idUsuario,
            string passwordHash)
        {
            try
            {
                using var connection = _dbFactory.CreateConnection();

                var parameters = new DynamicParameters();

                parameters.Add("p_actor", idUsuario, DbType.Int32);
                parameters.Add("p_usuario", idUsuario, DbType.Int32);
                parameters.Add("p_nuevo_hash", passwordHash, DbType.String);

                await connection.ExecuteAsync(
                    "sp_cambiar_password",
                    parameters,
                    commandType: CommandType.StoredProcedure
                );

                return new { filas_afectadas = 1 };
            }
            catch (MySqlException ex)
            {
                return ex.Message;
            }
        }
    }
}
