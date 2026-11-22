using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GeneradorDocs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var opciones = Opciones.DesdeEntorno(builder.Configuration);
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
    claveAcceso = !string.IsNullOrEmpty(opciones.ClaveAcceso),
    presupuestoUsd = opciones.MaxCosteCentimos / 100m,
}));

var api = app.MapGroup("/trabajos").AddEndpointFilter(async (ctx, next) =>
{
    var enviada = ctx.HttpContext.Request.Headers["X-Api-Key"].ToString();
    if (!ClaveValida(enviada, opciones.ClaveAcceso))
        return Results.Problem("Falta la cabecera X-Api-Key o no es correcta.", statusCode: 401);
    return await next(ctx);
});

// POST /trabajos  (multipart/form-data)
//   instrucciones      -> fichero .md con las instrucciones (o bien el campo de texto "instruccionesTexto")
//   archivos           -> uno o varios ficheros de documentación; un .zip se descomprime
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
        // 1. Instrucciones
        var ficheroInstr = form.Files.GetFile("instrucciones");
        var textoInstr = form["instruccionesTexto"].ToString();
        if (ficheroInstr is { Length: > 0 })
        {
            await using var fs = File.Create(trabajo.RutaInstrucciones);
            await ficheroInstr.CopyToAsync(fs);
        }
        else if (!string.IsNullOrWhiteSpace(textoInstr))
        {
            await File.WriteAllTextAsync(trabajo.RutaInstrucciones, textoInstr, Encoding.UTF8);
        }

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
            throw new ErrorSubida("Faltan las instrucciones: envía el fichero 'instrucciones' o el campo 'instruccionesTexto'.");

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
