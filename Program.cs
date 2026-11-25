using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GeneradorDocs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var opciones = Opciones.DesdeEntorno(builder.Configuration);
const string CookieAcceso = "gd_acceso";
// La cookie guarda una huella de la clave, no la clave. Si cambias API_ACCESS_KEY, todas las sesiones caducan.
var huellaClave = string.IsNullOrEmpty(opciones.ClaveAcceso)
    ? ""
    : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("generator-doc:" + opciones.ClaveAcceso)));
var carpetaPlantillas = Path.Combine(AppContext.BaseDirectory, "plantillas");
builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton<AlmacenTrabajos>();
builder.Services.AddSingleton<ColaTrabajos>();
builder.Services.AddHttpClient(AgenteClaude.NombreCliente, c => AgenteClaude.Configurar(c, opciones));
builder.Services.AddSingleton<AgenteClaude>();
builder.Services.AddHostedService<ProcesadorTrabajos>();
builder.Services.AddHostedService<LimpiezaTrabajos>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = opciones.MaxSubidaBytes);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = opciones.MaxSubidaBytes + 1024 * 1024);

// Render indica el puerto en la variable PORT.
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var app = builder.Build();

// El disco de Render es efímero: empezamos siempre con la carpeta de trabajos vacía.
ProcesadorTrabajos.TryBorrar(opciones.CarpetaTrabajos);
Directory.CreateDirectory(opciones.CarpetaTrabajos);

if (string.IsNullOrEmpty(opciones.ClaveAcceso))
    app.Logger.LogWarning("API_ACCESS_KEY no está definida: la API rechazará todas las peticiones.");
if (!app.Services.GetRequiredService<AgenteClaude>().Configurado)
    app.Logger.LogWarning("Faltan ANTHROPIC_API_KEY, AGENT_ID o ENVIRONMENT_ID: los trabajos fallarán.");

// Cualquier error inesperado se devuelve con su motivo, para que la web no muestre solo "Error 500".
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(ex, "Error no controlado en {Ruta}", ctx.Request.Path);
    var (codigo, motivo) = ex switch
    {
        BadHttpRequestException b when b.StatusCode == StatusCodes.Status413PayloadTooLarge =>
            (413, $"La subida supera el máximo de {opciones.MaxSubidaBytes / 1024 / 1024} MB."),
        InvalidDataException => (413, $"La subida supera el máximo de {opciones.MaxSubidaBytes / 1024 / 1024} MB o el formulario no es válido."),
        _ => (500, "Error interno: " + ex?.Message),
    };
    await Results.Problem(motivo, statusCode: codigo).ExecuteAsync(ctx);
}));

app.UseDefaultFiles();
app.UseStaticFiles();

// Sin autenticación: Render lo usa para saber si el servicio está vivo.
app.MapGet("/health", () => Results.Ok(new
{
    estado = "ok",
    anthropicKey = !string.IsNullOrEmpty(opciones.ClaveAnthropic),
    agentId = !string.IsNullOrEmpty(opciones.AgentId),
    environmentId = !string.IsNullOrEmpty(opciones.EnvironmentId),
    memoria = !string.IsNullOrEmpty(opciones.MemoryStoreId),
    claveAcceso = !string.IsNullOrEmpty(opciones.ClaveAcceso),
    presupuestoUsd = opciones.MaxCosteCentimos / 100m,
}));

// --- Acceso: la clave se escribe una vez y queda una cookie segura durante 90 días ---
app.MapGet("/acceso", (HttpContext ctx) => Results.Ok(new { conectado = Autorizado(ctx) }));

app.MapPost("/acceso", async (HttpContext ctx, DatosAcceso datos) =>
{
    if (huellaClave.Length == 0 || !ClaveValida(datos.Clave?.Trim() ?? "", opciones.ClaveAcceso))
    {
        await Task.Delay(1000); // frena los intentos de adivinar la clave
        return Results.Problem("La clave no es correcta.", statusCode: 401);
    }
    ctx.Response.Cookies.Append(CookieAcceso, huellaClave, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromDays(90),
        Path = "/",
    });
    return Results.NoContent();
}).DisableAntiforgery();

app.MapDelete("/acceso", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete(CookieAcceso, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });
    return Results.NoContent();
});

// --- Plantillas de instrucciones (carpeta plantillas/ del repositorio) ---
app.MapGet("/plantillas", () => Results.Ok(ListarPlantillas())).AddEndpointFilter(FiltroAcceso);

var api = app.MapGroup("/trabajos").AddEndpointFilter(FiltroAcceso);

// POST /trabajos  (multipart/form-data). Hace falta al menos uno de los tres primeros:
//   plantilla          -> id de una plantilla de plantillas/ (por ejemplo "lmsgi-supuestos")
//   instrucciones      -> fichero .md con instrucciones
//   instruccionesTexto -> lo que hay que hacer, escrito a mano (por ejemplo, un enunciado en papel)
//   archivos           -> documentación opcional: ficheros, fotos o un .zip (se descomprime)
api.MapPost("", async (HttpRequest req, AlmacenTrabajos almacen, ColaTrabajos cola) =>
{
    if (!req.HasFormContentType)
        return Results.Problem("Envía los datos como multipart/form-data.", statusCode: 400);

    var form = await req.ReadFormAsync();
    var id = Guid.NewGuid().ToString("N");
    var trabajo = new Trabajo { Id = id, Carpeta = Path.Combine(opciones.CarpetaTrabajos, id) };
    Directory.CreateDirectory(trabajo.CarpetaEntrada);

    try
    {
        // 1. Instrucciones = plantilla + fichero + texto escrito en la página
        var ficheroInstr = form.Files.GetFile("instrucciones");
        var textoInstr = form["instruccionesTexto"].ToString().Trim();
        var idPlantilla = form["plantilla"].ToString().Trim();
        var partes = new List<string>();
        if (idPlantilla.Length > 0)
            partes.Add(LeerPlantilla(idPlantilla) ?? throw new ErrorSubida($"No existe la plantilla '{idPlantilla}'."));
        if (ficheroInstr is { Length: > 0 })
        {
            using var lector = new StreamReader(ficheroInstr.OpenReadStream(), Encoding.UTF8);
            partes.Add(await lector.ReadToEndAsync());
        }
        if (textoInstr.Length > 0)
        {
            partes.Add(partes.Count == 0
                ? textoInstr
                : "## Indicaciones de este trabajo\n\nLo que se indica aquí tiene prioridad sobre los parámetros y valores por defecto anteriores.\n\n" + textoInstr);
        }
        if (partes.Count > 0)
            await File.WriteAllTextAsync(trabajo.RutaInstrucciones, string.Join("\n\n---\n\n", partes), Encoding.UTF8);

        // 2. Documentación
        var archivos = form.Files.GetFiles("archivos");
        if (archivos.Count > opciones.MaxFicheros)
            throw new ErrorSubida($"Como máximo {opciones.MaxFicheros} ficheros por trabajo.");

        long descomprimido = 0;
        foreach (var f in archivos.Where(f => f.Length > 0))
        {
            var nombre = NombreSeguro(f.FileName);
            if (nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await using var s = f.OpenReadStream();
                descomprimido = ExtraerZip(s, trabajo.CarpetaEntrada, descomprimido);
            }
            else
            {
                await using var fs = File.Create(Path.Combine(trabajo.CarpetaEntrada, nombre));
                await f.CopyToAsync(fs);
            }
        }

        // Si las instrucciones venían dentro del zip, las sacamos a su sitio.
        var instrEnZip = Path.Combine(trabajo.CarpetaEntrada, "instrucciones.md");
        if (!File.Exists(trabajo.RutaInstrucciones) && File.Exists(instrEnZip))
            File.Move(instrEnZip, trabajo.RutaInstrucciones);

        if (!File.Exists(trabajo.RutaInstrucciones))
            throw new ErrorSubida("Faltan las instrucciones: escribe qué hay que hacer, elige una plantilla o sube un fichero de instrucciones.");

        trabajo.FicherosEntrada.AddRange(Directory.EnumerateFiles(trabajo.CarpetaEntrada, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(trabajo.CarpetaEntrada, f).Replace('\\', '/')).OrderBy(f => f));
        if (trabajo.FicherosEntrada.Count > opciones.MaxFicheros)
            throw new ErrorSubida($"Como máximo {opciones.MaxFicheros} ficheros por trabajo (contando los del zip).");
    }
    catch (Exception ex) when (ex is ErrorSubida or InvalidDataException)
    {
        ProcesadorTrabajos.TryBorrar(trabajo.Carpeta);
        return Results.Problem(ex is InvalidDataException ? "El zip no es válido." : ex.Message, statusCode: 400);
    }

    almacen.Agregar(trabajo);
    if (!cola.Encolar(id))
    {
        almacen.Quitar(id);
        ProcesadorTrabajos.TryBorrar(trabajo.Carpeta);
        return Results.Problem("Hay demasiados trabajos en cola. Inténtalo en unos minutos.", statusCode: 429);
    }

    return Results.Accepted($"/trabajos/{id}", trabajo);
}).DisableAntiforgery();

api.MapGet("/{id}", (string id, AlmacenTrabajos almacen) =>
    almacen.Obtener(id) is { } t ? Results.Ok(t) : Results.NotFound());

api.MapGet("/{id}/resultado", (string id, AlmacenTrabajos almacen) =>
{
    var t = almacen.Obtener(id);
    if (t is null) return Results.NotFound();
    if (t.Estado != EstadoTrabajo.Terminado || !File.Exists(t.RutaZip))
        return Results.Problem($"El trabajo está en estado {t.Estado}.", statusCode: 409);
    return Results.File(t.RutaZip, "application/zip", $"resultado-{id[..8]}.zip");
});

app.Run();

bool Autorizado(HttpContext ctx)
{
    if (huellaClave.Length == 0) return false;
    var cabecera = ctx.Request.Headers["X-Api-Key"].ToString();
    if (cabecera.Length > 0) return ClaveValida(cabecera, opciones.ClaveAcceso);
    var cookie = ctx.Request.Cookies[CookieAcceso] ?? "";
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(huellaClave));
}

async ValueTask<object?> FiltroAcceso(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next) =>
    Autorizado(ctx.HttpContext)
        ? await next(ctx)
        : Results.Problem("Falta la clave de acceso o no es correcta. Vuelve a entrar con la clave.", statusCode: 401);

List<PlantillaInfo> ListarPlantillas() =>
    !Directory.Exists(carpetaPlantillas)
        ? new List<PlantillaInfo>()
        : Directory.EnumerateFiles(carpetaPlantillas, "*.md").OrderBy(f => f)
            .Select(f => new PlantillaInfo(
                Path.GetFileNameWithoutExtension(f),
                File.ReadLines(f).Where(l => l.StartsWith("# ")).Select(l => l[2..].Trim()).FirstOrDefault()
                    ?? Path.GetFileNameWithoutExtension(f)))
            .ToList();

string? LeerPlantilla(string id)
{
    if (!Regex.IsMatch(id, "^[a-z0-9-]{1,60}$")) return null;
    var ruta = Path.Combine(carpetaPlantillas, id + ".md");
    return File.Exists(ruta) ? File.ReadAllText(ruta, Encoding.UTF8) : null;
}

static bool ClaveValida(string enviada, string esperada)
{
    if (string.IsNullOrEmpty(esperada) || string.IsNullOrEmpty(enviada)) return false;
    return CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(enviada)),
        SHA256.HashData(Encoding.UTF8.GetBytes(esperada)));
}

static string NombreSeguro(string nombre)
{
    var n = Path.GetFileName(nombre.Replace('\\', '/'));
    foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
    if (string.IsNullOrWhiteSpace(n) || n is "." or "..") throw new ErrorSubida($"Nombre de fichero no válido: '{nombre}'.");
    return n;
}

long ExtraerZip(Stream zip, string destino, long acumulado)
{
    var raiz = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
    using var archivo = new ZipArchive(zip, ZipArchiveMode.Read);
    foreach (var e in archivo.Entries)
    {
        if (string.IsNullOrEmpty(e.Name)) continue; // carpeta
        acumulado += e.Length;
        if (acumulado > opciones.MaxDescomprimidoBytes)
            throw new ErrorSubida("El contenido descomprimido es demasiado grande.");

        var ruta = Path.GetFullPath(Path.Combine(destino, e.FullName.Replace('\\', '/')));
        if (!ruta.StartsWith(raiz, StringComparison.Ordinal))
            throw new ErrorSubida($"Ruta no permitida dentro del zip: '{e.FullName}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        e.ExtractToFile(ruta, overwrite: true);
    }
    return acumulado;
}

sealed class ErrorSubida(string mensaje) : Exception(mensaje);
sealed record DatosAcceso(string? Clave);
sealed record PlantillaInfo(string Id, string Titulo);
