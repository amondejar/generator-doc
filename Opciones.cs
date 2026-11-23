using System.Globalization;

namespace GeneradorDocs;

/// <summary>Configuración leída de variables de entorno (en Render se definen en el panel o en render.yaml).</summary>
public sealed class Opciones
{
    /// <summary>Clave que deben enviar los clientes en la cabecera X-Api-Key.</summary>
    public string ClaveAcceso { get; init; } = "";

    // --- Agente de la Console (Claude Managed Agents) ---
    public string ClaveAnthropic { get; init; } = "";
    public string AgentId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    /// <summary>Almacén de memoria (memstore_...). Vacío = sin memoria entre trabajos.</summary>
    public string MemoryStoreId { get; init; } = "";
    public string VersionApi { get; init; } = "2023-06-01";
    /// <summary>Presupuesto máximo por trabajo, en céntimos de dólar. 0 = sin límite.</summary>
    public long MaxCosteCentimos { get; init; } = 200;

    // --- Límites del servicio ---
    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromMinutes(20);
    public long MaxSubidaBytes { get; init; } = 30L * 1024 * 1024;
    public long MaxDescomprimidoBytes { get; init; } = 90L * 1024 * 1024;
    public int MaxFicheros { get; init; } = 50;
    public int MaxEnCola { get; init; } = 5;
    public TimeSpan Caducidad { get; init; } = TimeSpan.FromMinutes(120);
    public string CarpetaTrabajos { get; init; } = Path.Combine(Path.GetTempPath(), "trabajos");

    public static Opciones DesdeEntorno(IConfiguration c)
    {
        int Entero(string clave, int defecto) => int.TryParse(c[clave], out var v) && v > 0 ? v : defecto;
        string Texto(string clave, string defecto = "") => string.IsNullOrWhiteSpace(c[clave]) ? defecto : c[clave]!.Trim();

        var subidaMb = Entero("MAX_UPLOAD_MB", 30);
        var costeUsd = decimal.TryParse(c["MAX_COST_USD"], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : 2m;

        return new Opciones
        {
            ClaveAcceso = Texto("API_ACCESS_KEY"),
            ClaveAnthropic = Texto("ANTHROPIC_API_KEY"),
            AgentId = Texto("AGENT_ID"),
            EnvironmentId = Texto("ENVIRONMENT_ID"),
            MemoryStoreId = Texto("MEMORY_STORE_ID"),
            VersionApi = Texto("ANTHROPIC_VERSION", "2023-06-01"),
            MaxCosteCentimos = (long)Math.Round(costeUsd * 100m),
            TiempoMaximo = TimeSpan.FromMinutes(Entero("TIMEOUT_MINUTES", 20)),
            MaxSubidaBytes = subidaMb * 1024L * 1024,
            MaxDescomprimidoBytes = subidaMb * 3L * 1024 * 1024,
            MaxFicheros = Entero("MAX_FILES", 50),
            MaxEnCola = Entero("MAX_QUEUE", 5),
            Caducidad = TimeSpan.FromMinutes(Entero("RESULT_TTL_MINUTES", 120)),
            CarpetaTrabajos = Texto("WORK_DIR", Path.Combine(Path.GetTempPath(), "trabajos")),
        };
    }
}
