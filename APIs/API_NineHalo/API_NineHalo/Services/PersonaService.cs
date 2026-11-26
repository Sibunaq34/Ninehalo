using APINineHalo.Entities;
using APINineHalo.Repository;
namespace APINineHalo.Services;
public class PersonaService(PersonaRepository repository) : IPersonaService
{
    public Task<ProfileDto?> GetProfileAsync(int actor, int idUsuario) => repository.GetProfileAsync(actor, idUsuario);
    public async Task<ProfileDto?> UpdateProfileAsync(int actor, int idUsuario, ProfileUpdateRequest request)
    {
        var current = await repository.GetProfileAsync(actor, idUsuario);
        if (current is null) return null;
        // The existing editor does not edit the phone. Preserve it rather than
        // clearing it when invoking the new stored procedure signature.
        await repository.EditProfileAsync(actor, idUsuario, request, current.Telefono);
        return await repository.GetProfileAsync(actor, idUsuario);
    }
}
