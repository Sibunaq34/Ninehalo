
namespace APINineHalo.Entities
{
    public class ChangePasswordRequest
    {
        public string Password_Actual { get; set; } = string.Empty;
        public string Password_Nueva { get; set; } = string.Empty;
    }
}
