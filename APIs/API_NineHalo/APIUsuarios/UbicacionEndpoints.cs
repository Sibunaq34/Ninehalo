using APINineHalo.Repository;
using Dapper;
using System.Data;
using MySql.Data.MySqlClient;

namespace APINineHalo.Endpoints;

public static class UbicacionEndpoints
{
    public static void MapUbicacionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ubicaciones").AllowAnonymous();
        group.MapGet("/paises", async (IDbConnectionF factory) =>
        {
            using var db = factory.CreateConnection();
            var rows = await Consultar(db, null);
            var data = rows.Select(row => new
            {
                id = row.Id, descripcion = row.Descripcion,
                etiquetas = row.Descripcion switch
                {
                    "Panamá" => new { provincia = "Provincia / comarca", canton = "Distrito", distrito = "Corregimiento" },
                    "Nicaragua" => new { provincia = "Departamento / región", canton = "Municipio", distrito = "Barrio / comunidad" },
                    _ => new { provincia = "Provincia", canton = "Cantón", distrito = "Distrito" }
                }
            });
            return Results.Ok(new { success = true, data });
        }).WithName("ListarPaises");

        group.MapGet("/paises/{id:int}/provincias", (int id, IDbConnectionF factory) =>
            Children(factory, id, "PA"))
            .WithName("ListarProvincias");
        group.MapGet("/provincias/{id:int}/cantones", (int id, IDbConnectionF factory) =>
            Children(factory, id, "PR"))
            .WithName("ListarCantones");
        group.MapGet("/cantones/{id:int}/distritos", (int id, IDbConnectionF factory) =>
            Children(factory, id, "CA"))
            .WithName("ListarDistritos");
    }

    private static Task<IEnumerable<Ubicacion>> Consultar(IDbConnection db, string? idPadre) =>
        db.QueryAsync<Ubicacion>("sp_consultar_ubicaciones", new { p_id_padre = idPadre },
            commandType: CommandType.StoredProcedure);

    private static async Task<IResult> Children(IDbConnectionF factory, int id, string prefix)
    {
        if (id <= 0) return Results.BadRequest(new { success = false, message = "Identificador de ubicación inválido." });
        using var db = factory.CreateConnection();
        try
        {
            var data = (await Consultar(db, $"{prefix}:{id}")).Select(row => new { id = row.Id, descripcion = row.Descripcion });
            return Results.Ok(new { success = true, data });
        }
        catch (MySqlException ex) when (ex.Number == 30002)
        {
            return Results.NotFound(new { success = false, message = "La ubicación seleccionada no existe." });
        }
        catch (MySqlException ex) when (ex.Number == 30001)
        {
            return Results.BadRequest(new { success = false, message = "Identificador de ubicación inválido." });
        }
    }

    private sealed class Ubicacion
    {
        public int Id { get; set; }
        public string Descripcion { get; set; } = "";
    }
}
