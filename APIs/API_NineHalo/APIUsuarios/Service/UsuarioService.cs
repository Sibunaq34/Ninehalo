
using APINineHalo.Entities;
using APINineHalo.Repository;

namespace APINineHalo.Services
{
    public class UsuarioService : IUsuarioService
    {
        private readonly UsuarioRepository _repository;

        public UsuarioService(UsuarioRepository repository)
        {
            _repository = repository;
        }

        // ==========================================
        // REGISTRAR USUARIO
        // ==========================================

        public async Task<(bool Success, string Message, int? IdUsuario)>
            RegisterAsync(RegisterRequest request)
        {
            if (request == null)
            {
                return (false, "Datos de registro inválidos.", null);
            }

            if (string.IsNullOrWhiteSpace(request.Id) ||
                string.IsNullOrWhiteSpace(request.Nombre) ||
                string.IsNullOrWhiteSpace(request.Apellidos) ||
                string.IsNullOrWhiteSpace(request.Correo) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return (false, "Todos los campos son obligatorios.", null);
            }

            if (request.Id.Length > 20)
            {
                return (false,
                    "La identificación supera el máximo de 20 caracteres.",
                    null);
            }

            if (request.Nombre.Length > 100 ||
                request.Apellidos.Length > 100)
            {
                return (false,
                    "Nombre o apellidos exceden la longitud máxima de 100 caracteres.",
                    null);
            }

            if (request.Telefono != null &&
                request.Telefono.Length > 20)
            {
                return (false,
                    "El teléfono excede la longitud máxima de 20 caracteres.",
                    null);
            }

            if (request.Correo.Length > 150)
            {
                return (false,
                    "El correo excede la longitud máxima de 150 caracteres.",
                    null);
            }

            // Validar formato de correo electrónico
            try
            {
                var correo = request.Correo.Trim();
                var addr = new System.Net.Mail.MailAddress(correo);

                if (addr.Address != correo)
                {
                    return (false,
                        "Correo electrónico inválido.",
                        null);
                }
            }
            catch
            {
                return (false,
                    "Correo electrónico inválido.",
                    null);
            }

            if (request.IdPais <= 0 || request.IdProvincia <= 0 ||
                request.IdCanton <= 0 || request.IdDistrito <= 0)
            {
                return (false, "País, provincia, cantón y distrito son obligatorios y deben tener identificadores válidos.", null);
            }

            // Generar hash BCrypt
            string passwordHash;

            try
            {
                passwordHash = BCrypt.Net.BCrypt.HashPassword(
                    request.Password);
            }
            catch
            {
                return (false,
                    "Error al procesar la contraseña.",
                    null);
            }

            // Registrar utilizando el procedimiento almacenado
            var result = await _repository.RegisterUserAsync(
                request.Id.Trim(),
                request.Nombre.Trim(),
                request.Apellidos.Trim(),
                request.Telefono?.Trim() ?? string.Empty,
                request.Correo.Trim(),
                passwordHash,
                request.IdPais, request.IdProvincia, request.IdCanton, request.IdDistrito
            );

            if (result == null)
            {
                return (false,
                    "Error interno al registrar usuario.",
                    null);
            }

            if (result is string msg)
            {
                if (msg.Contains("Duplicate entry",
                        StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("duplicate",
                        StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("ya está registrado",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return (false,
                        "Identificación o correo ya registrados.",
                        null);
                }

                return (false, msg, null);
            }

            try
            {
                dynamic dyn = result;

                object? idVal =
                    dyn.id_usuario ?? dyn.idUsuario ?? dyn.Id;

                if (idVal == null)
                {
                    return (false,
                        "No se recibió el identificador del usuario.",
                        null);
                }

                if (!int.TryParse(idVal.ToString(), out int idUsuario))
                {
                    return (false,
                        "El identificador devuelto no es válido.",
                        null);
                }

                return (true,
                    "Usuario registrado correctamente.",
                    idUsuario);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());

                return (false,
                    "Error al procesar la respuesta del servidor.",
                    null);
            }
        }

        // ==========================================
        // CAMBIAR CONTRASEÑA
        // ==========================================

        public async Task<(bool Success, string Message)>
            ChangePasswordAsync(
                int idUsuario,
                ChangePasswordRequest request)
        {
            if (idUsuario <= 0)
            {
                return (false, "Id de usuario inválido.");
            }

            if (request == null ||
                string.IsNullOrWhiteSpace(request.Password_Actual) ||
                string.IsNullOrWhiteSpace(request.Password_Nueva))
            {
                return (false,
                    "Contraseña actual y nueva son obligatorias.");
            }

            // Consultar el usuario por ID
            var user = await _repository.GetUserByIdAsync(idUsuario);

            if (user == null)
            {
                return (false, "Usuario no encontrado.");
            }

            if (!string.Equals(
                user.Estado,
                "ACTIVO",
                StringComparison.OrdinalIgnoreCase))
            {
                return (false, "La cuenta no está activa.");
            }

            // Verificar contraseña actual mediante BCrypt
            bool passwordValida;

            try
            {
                passwordValida = BCrypt.Net.BCrypt.Verify(
                    request.Password_Actual,
                    user.Password_Hash
                );
            }
            catch
            {
                passwordValida = false;
            }

            if (!passwordValida)
            {
                return (false, "Contraseña actual incorrecta.");
            }

            // Evitar reutilizar la contraseña actual
            if (request.Password_Actual == request.Password_Nueva)
            {
                return (false,
                    "La nueva contraseña debe ser diferente a la actual.");
            }

            // Generar hash de la nueva contraseña
            string nuevoHash;

            try
            {
                nuevoHash = BCrypt.Net.BCrypt.HashPassword(
                    request.Password_Nueva);
            }
            catch
            {
                return (false,
                    "Error al procesar la nueva contraseña.");
            }

            // Actualizar contraseña mediante procedimiento almacenado
            var result = await _repository.ChangePasswordAsync(
                idUsuario,
                nuevoHash
            );

            if (result == null)
            {
                return (false,
                    "Error interno al cambiar la contraseña.");
            }

            if (result is string mensaje)
            {
                return (false, mensaje);
            }

            return (true, "Contraseña cambiada correctamente.");
        }
    }
}
