namespace APINineHalo.Entities;
public class ConversacionDto
{
    public ulong Id_Conversacion { get; set; }
    public int Id_Usuario { get; set; }
    public int? Id_Agente_Asignado { get; set; }
    public string Asunto { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Prioridad { get; set; } = string.Empty;
    public DateTime Fecha_Creacion { get; set; }
    public DateTime? Fecha_Cierre { get; set; }
}
public class CrearConversacionRequest
{
    public string Asunto { get; set; } = string.Empty;
    public ulong? Id_Compra { get; set; }
}
public class MensajeRequest { public string Contenido { get; set; } = string.Empty; }
public class AgenteRequest { public int Id_Agente { get; set; } }
public class EstadoRequest { public string Estado { get; set; } = string.Empty; }
