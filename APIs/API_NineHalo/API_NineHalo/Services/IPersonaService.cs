using APINineHalo.Entities;
namespace APINineHalo.Services;
public interface IPersonaService
{
    Task<ProfileDto?> GetProfileAsync(int actor, int idUsuario);
    Task<ProfileDto?> UpdateProfileAsync(int actor, int idUsuario, ProfileUpdateRequest request);
}
