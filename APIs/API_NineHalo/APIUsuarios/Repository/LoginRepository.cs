
using APINineHalo.Entities;
using Dapper;
using System.Data;

namespace APINineHalo.Repository
{
    public class LoginRepository
    {
        private readonly IDbConnectionF _dbFactory;

        public LoginRepository(IDbConnectionF dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // =====================================
        // CONSULTAR USUARIO POR CORREO
        // =====================================

        public async Task<UserAuthEntity?> GetUserByEmailAsync(
            string correo)
        {
            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("p_correo", correo.Trim(), DbType.String);

            return await connection.QueryFirstOrDefaultAsync<UserAuthEntity>(
                "sp_login",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // REGISTRAR INTENTO DE LOGIN
        // =====================================

        public async Task RegisterLoginAttemptAsync(
            int idUsuario,
            bool exitoso)
        {
            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("p_id_usuario", idUsuario, DbType.Int32);
            parameters.Add("p_exitoso", exitoso, DbType.Boolean);

            await connection.ExecuteAsync(
                "sp_registrar_intento_login",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // CREAR CODIGO DE VERIFICACION
        // =====================================

        public async Task<long> CreateVerificationCodeAsync(
            int idUsuario,
            string proposito,
            byte[] codigoHash)
        {
            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();

            parameters.Add("p_id_usuario", idUsuario, DbType.Int32);
            parameters.Add("p_proposito", proposito, DbType.String);
            parameters.Add(
                "p_codigo_hash",
                codigoHash,
                DbType.Binary,
                size: 32
            );

            return await connection.QuerySingleAsync<long>(
                "sp_crear_codigo_verificacion",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // VALIDAR CODIGO DE VERIFICACION
        // =====================================

        public async Task<bool> ValidateVerificationCodeAsync(
            int idUsuario,
            long idCodigo,
            string proposito,
            byte[] codigoHash)
        {
            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();

            parameters.Add("p_id_usuario", idUsuario, DbType.Int32);
            parameters.Add("p_id_codigo", idCodigo, DbType.Int64);
            parameters.Add("p_proposito", proposito, DbType.String);
            parameters.Add(
                "p_codigo_hash",
                codigoHash,
                DbType.Binary,
                size: 32
            );

            return await connection.QuerySingleAsync<bool>(
                "sp_validar_codigo_verificacion",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // CONSULTAR USUARIO DEL CODIGO DE LOGIN
        // =====================================

        public async Task<int?> GetUserIdByVerificationCodeAsync(
            long idVerificacion)
        {
            if (idVerificacion <= 0)
                return null;

            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();

            parameters.Add(
                "p_id_codigo",
                idVerificacion,
                DbType.Int64
            );

            return await connection.QueryFirstOrDefaultAsync<int?>(
                "sp_consultar_usuario_codigo_login",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // CONSULTAR USUARIO DEL CODIGO DE REGISTRO
        // =====================================

        public async Task<int?> GetUserIdByRegistrationCodeAsync(
            long idVerificacion)
        {
            if (idVerificacion <= 0)
                return null;

            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();

            parameters.Add(
                "p_id_codigo",
                idVerificacion,
                DbType.Int64
            );

            return await connection.QueryFirstOrDefaultAsync<int?>(
                "sp_consultar_usuario_codigo_registro",
                parameters,
                commandType: CommandType.StoredProcedure
            );
        }

        // =====================================
        // CONSULTAR ROLES DEL USUARIO
        // =====================================

        public async Task<IEnumerable<string>> GetUserRolesAsync(
            int idUsuario)
        {
            if (idUsuario <= 0)
                return Enumerable.Empty<string>();

            using var connection = _dbFactory.CreateConnection();

            var parameters = new DynamicParameters();

            parameters.Add(
                "p_id_usuario",
                idUsuario,
                DbType.Int32
            );

            var roles = await connection.QueryAsync<UsuarioRolDto>(
                "sp_consultar_roles_usuario",
                parameters,
                commandType: CommandType.StoredProcedure
            );

            return roles
                .Select(r => r.Codigo)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
