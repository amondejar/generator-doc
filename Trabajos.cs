using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace GeneradorDocs;

public enum EstadoTrabajo { EnCola, Procesando, Terminado, Error }

public sealed class Trabajo
{
    public required string Id { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoTrabajo>))]
    public EstadoTrabajo Estado { get; set; } = EstadoTrabajo.EnCola;
    public DateTimeOffset Creado { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Iniciado { get; set; }
    public DateTimeOffset? Terminado { get; set; }
    public List<string> FicherosEntrada { get; init; } = new();
    public List<string> FicherosResultado { get; set; } = new();
    public string? Resumen { get; set; }
    public string? Mensaje { get; set; }
    public decimal? CosteUsd { get; set; }
    /// <summary>Id de la sesión del agente: búscalo en platform.claude.com/sessions para ver qué hizo.</summary>
    public string? SesionId { get; set; }

    // Sin "required": System.Text.Json no admite propiedades required que además lleven [JsonIgnore].
    [JsonIgnore] public string Carpeta { get; init; } = "";
    [JsonIgnore] public string CarpetaEntrada => Path.Combine(Carpeta, "entrada");
    [JsonIgnore] public string CarpetaSalida => Path.Combine(Carpeta, "salida");
    [JsonIgnore] public string RutaInstrucciones => Path.Combine(Carpeta, "instrucciones.md");
    [JsonIgnore] public string RutaZip => Path.Combine(Carpeta, "resultado.zip");
}

/// <summary>Trabajos en memoria. En el plan Free de Render todo se pierde al reiniciar, así que no tiene sentido persistirlos.</summary>
public sealed class AlmacenTrabajos
{
    private readonly ConcurrentDictionary<string, Trabajo> _trabajos = new();
    public void Agregar(Trabajo t) => _trabajos[t.Id] = t;
    public Trabajo? Obtener(string id) => _trabajos.TryGetValue(id, out var t) ? t : null;
    public IEnumerable<Trabajo> Todos() => _trabajos.Values;
    public void Quitar(string id) => _trabajos.TryRemove(id, out _);
}

public sealed class ColaTrabajos(Opciones opciones)
{
    private readonly Channel<string> _canal = Channel.CreateBounded<string>(new BoundedChannelOptions(opciones.MaxEnCola)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    /// <summary>Devuelve false si la cola está llena.</summary>
    public bool Encolar(string id) => _canal.Writer.TryWrite(id);
    public IAsyncEnumerable<string> Leer(CancellationToken ct) => _canal.Reader.ReadAllAsync(ct);
}

/// <summary>Procesa los trabajos de uno en uno (el plan Free tiene 512 MB y 0,1 CPU).</summary>
public sealed class ProcesadorTrabajos(
    ColaTrabajos cola, AlmacenTrabajos almacen, AgenteClaude agente, ILogger<ProcesadorTrabajos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var id in cola.Leer(ct))
        {
            var t = almacen.Obtener(id);
            if (t is null) continue;

            t.Estado = EstadoTrabajo.Procesando;
            t.Iniciado = DateTimeOffset.UtcNow;
            log.LogInformation("Procesando trabajo {Id}", id);

            try
            {
                Directory.CreateDirectory(t.CarpetaSalida);
                var r = await agente.EjecutarAsync(t, ct);
                t.CosteUsd = r.CosteUsd;
                t.Resumen = r.Texto;

                var generados = Directory.EnumerateFiles(t.CarpetaSalida, "*", SearchOption.AllDirectories).ToList();
                if (generados.Count == 0)
                {
                    Fallar(t, r.Error ?? "El agente terminó pero no dejó ningún fichero en /mnt/session/outputs/. Revisa las instrucciones o la sesión en la Console.");
                }
                else
                {
                    // Si hubo un aviso (presupuesto, tiempo…) pero hay ficheros, se entregan igualmente con el aviso.
                    t.Mensaje = r.Ok ? null : r.Error;
                    if (!string.IsNullOrWhiteSpace(r.Texto))
                        await File.WriteAllTextAsync(Path.Combine(t.CarpetaSalida, "_resumen.md"), r.Texto, ct);

                    ZipFile.CreateFromDirectory(t.CarpetaSalida, t.RutaZip, CompressionLevel.Optimal, includeBaseDirectory: false);
                    t.FicherosResultado = Directory.EnumerateFiles(t.CarpetaSalida, "*", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(t.CarpetaSalida, f).Replace('\\', '/'))
                        .OrderBy(f => f).ToList();
                    t.Estado = EstadoTrabajo.Terminado;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Fallar(t, "El servicio se detuvo mientras se procesaba el trabajo.");
                break;
            }
            catch (ErrorApi ex)
            {
                log.LogWarning("Trabajo {Id}: {Error}", id, ex.Message);
                Fallar(t, ex.Message);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error en el trabajo {Id}", id);
                Fallar(t, "Error interno: " + ex.Message);
            }
            finally
            {
                t.Terminado = DateTimeOffset.UtcNow;
                // La documentación de entrada ya no hace falta: liberamos disco.
                TryBorrar(t.CarpetaEntrada);
            }
        }
    }

    private static void Fallar(Trabajo t, string mensaje)
    {
        t.Estado = EstadoTrabajo.Error;
        t.Mensaje = mensaje;
    }

    internal static void TryBorrar(string carpeta)
    {
        try { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true); } catch { /* se reintenta en la limpieza */ }
    }
}

/// <summary>Borra los trabajos caducados y sus ficheros.</summary>
public sealed class LimpiezaTrabajos(AlmacenTrabajos almacen, Opciones opciones) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            var limite = DateTimeOffset.UtcNow - opciones.Caducidad;
            foreach (var t in almacen.Todos().Where(t => t.Estado is EstadoTrabajo.Terminado or EstadoTrabajo.Error && t.Creado < limite).ToList())
            {
                ProcesadorTrabajos.TryBorrar(t.Carpeta);
                almacen.Quitar(t.Id);
            }
        } while (await reloj.WaitForNextTickAsync(ct));
    }
}
