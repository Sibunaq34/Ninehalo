
namespace APINineHalo.Entities
{
    public class UserAuthEntity
    {
        public int Id_Usuario { get; set; }

        public string Correo { get; set; } = string.Empty;

        public bool Correo_Verificado { get; set; }

        public string Password_Hash { get; set; } = string.Empty;

        public string Estado { get; set; } = string.Empty;

        public int Intentos_Fallidos { get; set; }
    }
}
