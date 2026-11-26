using APINineHalo.Entities;
using Dapper;
using System.Data;
namespace APINineHalo.Repository;
public class SoporteRepository(IDbConnectionF dbFactory, IConfiguration configuration)
{
    public async Task<List<ConversacionDto>> GetConversationsAsync(int actor)
    {
        using var connection = dbFactory.CreateConnection();
        return (await connection.QueryAsync<ConversacionDto>("sp_consultar_conversaciones", new { p_actor=actor }, commandType: CommandType.StoredProcedure)).ToList();
    }
    public async Task<IEnumerable<ConversacionDto>> GetInboxAsync(int actor, int page, int size)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QueryAsync<ConversacionDto>("sp_consultar_chats_soporte", new { p_actor=actor, p_pagina=page, p_tamano=size }, commandType: CommandType.StoredProcedure);
    }
    public async Task<ulong> CreateAsync(int actor, CrearConversacionRequest request)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QuerySingleAsync<ulong>("sp_crear_conversacion", new {
            p_usuario=actor, p_compra=request.Id_Compra, p_asunto=request.Asunto.Trim()
        }, commandType: CommandType.StoredProcedure);
    }
    public async Task<IEnumerable<dynamic>> GetMessagesAsync(int actor, ulong id, int page, int size)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QueryAsync("sp_consultar_mensajes", new {
            p_actor=actor, p_conversacion=id, p_pagina=page, p_tamano=size
        }, commandType: CommandType.StoredProcedure);
    }
    public async Task<ulong> SendAsync(int actor, ulong id, string content)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QuerySingleAsync<ulong>("sp_enviar_mensaje", new {
            p_actor=actor, p_conversacion=id, p_contenido=content.Trim()
        }, commandType: CommandType.StoredProcedure);
    }
    public async Task AssignAsync(int actor, ulong id, int agent)
    {
        using var connection = dbFactory.CreateConnection();
        await connection.ExecuteAsync("sp_asignar_agente", new { p_actor=actor, p_conversacion=id, p_agente=agent }, commandType: CommandType.StoredProcedure);
    }
    public async Task ChangeStateAsync(int actor, ulong id, string state)
    {
        using var connection = dbFactory.CreateConnection();
        await connection.ExecuteAsync("sp_cambiar_estado_conversacion", new { p_actor=actor, p_conversacion=id, p_estado=state }, commandType: CommandType.StoredProcedure);
    }
    public async Task<IResult> AcceptSalesforceReplyAsync(ulong id, int agent, string content, string state, string reference)
    {
        using var connection = dbFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var conversation = await connection.QuerySingleOrDefaultAsync<PartnerConversation>(
            "SELECT id_usuario AS Actor, estado AS State FROM conversacion WHERE id_conversacion=@id FOR UPDATE",
            new { id }, transaction);
        if (conversation is null) return ApiSecurity.Error("La conversación no existe.", 404);

        var queued = await connection.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM socio_envio WHERE socio='Salesforce' AND referencia=@ticket)",
            new { ticket = $"ticket:{id}" }, transaction);
        if (queued == 0) return ApiSecurity.Error("Conversación sin caso asociado en Salesforce.", 404);

        var previous = await connection.QuerySingleOrDefaultAsync<PartnerReply>(
            "SELECT contenido AS Content, estado AS State FROM soporte_respuesta_socio WHERE id_conversacion=@id AND referencia=@reference",
            new { id, reference }, transaction);
        if (previous is not null)
            return previous.Content == content && previous.State == state
                ? ApiSecurity.Ok(new { id_conversacion = id, estado = conversation.State == "CERRADA" ? "RESUELTA" : conversation.State })
                : ApiSecurity.Error("La referencia corresponde a otra respuesta.", 409);

        if (conversation.State == "CERRADA" || conversation.State == "RESUELTA" && state == "EN_PROCESO")
            return ApiSecurity.Error("La conversación ya fue resuelta o cerrada.", 409);
        var validAgent = await connection.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM usuario WHERE id_usuario=@agent AND estado='ACTIVO')",
            new { agent }, transaction);
        if (validAgent == 0 || agent == conversation.Actor)
            return ApiSecurity.Error("El agente de Salesforce no está habilitado.", 503);

        await connection.ExecuteAsync("""
            INSERT INTO conversacion_participante(id_conversacion,id_usuario) VALUES(@id,@agent)
            ON DUPLICATE KEY UPDATE id_usuario=conversacion_participante.id_usuario;
            INSERT INTO mensaje(id_conversacion,id_remitente,contenido) VALUES(@id,@agent,@content);
            UPDATE conversacion SET estado=@state WHERE id_conversacion=@id;
            INSERT INTO soporte_respuesta_socio(id_conversacion,referencia,contenido,estado)
            VALUES(@id,@reference,@content,@state);
            """, new { id, agent, content, state, reference }, transaction);
        await connection.ExecuteAsync("sp_registrar_bitacora",
            new { p_actor = agent, p_accion = "MENSAJE_ENVIADO", p_entidad = "mensaje" },
            transaction, commandType: CommandType.StoredProcedure);
        await PartnerOutbox.NotifyAsync(connection, transaction, configuration, conversation.Actor,
            $"respuesta:{id}:{reference}", $"El ticket #{id} recibió una respuesta de soporte.");
        transaction.Commit();
        return ApiSecurity.Ok(new { id_conversacion = id, estado = state });
    }

    private sealed class PartnerConversation
    {
        public int Actor { get; set; }
        public string State { get; set; } = "";
    }
    private sealed class PartnerReply
    {
        public string Content { get; set; } = "";
        public string State { get; set; } = "";
    }

    // The procedures commit independently. The legacy request needs an atomic
    // conversation + first message, so reproduce their inserts in one transaction.
    public async Task<ulong> CreateTicketAsync(int actor, CreateTicketRequest request)
    {
        using var connection = dbFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var active = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@actor AND estado='ACTIVO'", new { actor }, transaction);
        if (active == 0) throw new UnauthorizedAccessException();
        if (request.Id_Compra is not null)
        {
            var owned = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM compra WHERE id_compra=@id AND id_usuario=@actor", new { id=request.Id_Compra, actor }, transaction);
            if (owned == 0) throw new UnauthorizedAccessException();
        }
        await connection.ExecuteAsync("INSERT INTO conversacion(id_usuario,id_compra,asunto) VALUES(@actor,@compra,@asunto)",
            new { actor, compra=request.Id_Compra, asunto=request.Titulo.Trim() }, transaction);
        var id = await connection.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()", transaction: transaction);
        await connection.ExecuteAsync("INSERT INTO conversacion_participante(id_conversacion,id_usuario) VALUES(@id,@actor)", new { id, actor }, transaction);
        await connection.ExecuteAsync("INSERT INTO mensaje(id_conversacion,id_remitente,contenido) VALUES(@id,@actor,@contenido)", new { id, actor, contenido=request.Detalle.Trim() }, transaction);
        await connection.ExecuteAsync("sp_registrar_bitacora", new { p_actor=actor, p_accion="CHAT_CREADO", p_entidad="conversacion" }, transaction, commandType: CommandType.StoredProcedure);
        await connection.ExecuteAsync("sp_registrar_bitacora", new { p_actor=actor, p_accion="MENSAJE_ENVIADO", p_entidad="mensaje" }, transaction, commandType: CommandType.StoredProcedure);
        await PartnerOutbox.EnqueueAsync(connection, transaction, configuration, "Salesforce", $"ticket:{id}", new
        {
            id_conversacion = id.ToString(),
            id_compra = request.Id_Compra?.ToString() ?? "0",
            asunto = request.Titulo.Trim(),
            detalle = request.Detalle.Trim()
        });
        await PartnerOutbox.NotifyAsync(connection, transaction, configuration, actor, $"ticket:{id}",
            $"NineHalo recibió tu ticket #{id}.");
        transaction.Commit();
        return id;
    }
}
