# Generador de documentos (agente de la Console + Render)

Subes la documentación (PDF de las unidades, apuntes, enunciados) y un `instrucciones.md`.
Tu agente de Claude Managed Agents la procesa en la infraestructura de Anthropic y te devuelve los ficheros generados en un zip.

```
Navegador ──► Render (esta API .NET) ──► Anthropic: Files API + sesión del agente
                     ▲                                │
                     └──────── resultado.zip ◄────────┘  (lo que el agente deja en /mnt/session/outputs/)
```

Render no ejecuta Claude: solo recibe los ficheros, lanza la sesión, espera y monta el zip. Por eso basta con el plan Free.

## Endpoints

```
POST /trabajos                  -> instrucciones + documentación; devuelve un id (202)
GET  /trabajos/{id}             -> estado: EnCola, Procesando, Terminado o Error (+ id de la sesión y coste)
GET  /trabajos/{id}/resultado   -> descarga el zip
GET  /health                    -> comprueba la configuración (sin clave)
/                               -> página web para usarlo desde el navegador
```

Todas las rutas `/trabajos` exigen la cabecera `X-Api-Key` con el valor de `API_ACCESS_KEY`.

## Antes de desplegar: el agente

En la Console (platform.claude.com) necesitas:

1. **El agente**, con el prompt de sistema que trabaja con `/mnt/session/uploads/` y `/mnt/session/outputs/`.
   - Desactiva `web_search` y `web_fetch`.
   - Pon la política de permisos de las herramientas en **always allow**. Si no, la sesión se para a pedir permiso y el trabajo acaba con un aviso.
2. **El entorno**: tipo cloud y red **Limited**. Para que lea y genere PDF:
   - activa `allow_package_managers` (obligatorio si añades paquetes);
   - paquetes `pip`: `markdown-pdf` y `pymupdf`;
   - paquetes `apt`: `poppler-utils`.
3. Copia el **id del agente** (`agent_...`) y el **id del entorno** (`env_...`).

## Desplegar en Render (plan Free)

1. Sube esta carpeta a un repositorio de GitHub (puede ser privado).
2. En Render: **New → Blueprint** → elige el repositorio. Render lee `render.yaml`.
3. Rellena las variables que te pide:
   - `ANTHROPIC_API_KEY`: la API key de la Console. El uso se cobra de su saldo.
   - `AGENT_ID` y `ENVIRONMENT_ID`.
4. Cuando termine el despliegue, copia en **Environment** el valor generado de `API_ACCESS_KEY`.
5. Abre `https://<tu-servicio>.onrender.com/health`. Todos los campos deben salir `true`.
6. Abre la página principal y haz lo siguiente:
   1. Pega la clave.
   2. Sube `instrucciones.md` en el campo de fichero de instrucciones.
   3. Sube los PDF y demás documentos en «Documentación», sueltos o en un zip.
   4. Pulsa **Enviar trabajo**.

Para Lenguajes de Marcas tienes un ejemplo preparado en `ejemplo/instrucciones_LMSGI.md`: renómbralo a `instrucciones.md` y cambia solo la sección de parámetros.

## Qué recibe el agente

- Cada documento se monta en `/mnt/session/uploads/<nombre>`.
- El primer mensaje de la sesión es la lista de documentos seguida del contenido de `instrucciones.md`.
- Al terminar se descarga todo lo que haya en `/mnt/session/outputs/`. Se añade `_resumen.md` con la respuesta final del agente.
- La web muestra el **id de la sesión**. Búscalo en platform.claude.com/sessions para ver paso a paso qué hizo, los tokens y el coste.

## Variables de entorno

| Variable | Por defecto | Qué hace |
|---|---|---|
| `API_ACCESS_KEY` | — | Clave para usar la web y la API (obligatoria) |
| `ANTHROPIC_API_KEY` | — | API key de la Console (obligatoria) |
| `AGENT_ID` | — | Id del agente (obligatoria) |
| `ENVIRONMENT_ID` | — | Id del entorno (obligatoria) |
| `MAX_COST_USD` | 2 | Presupuesto máximo por trabajo; al alcanzarlo el agente para (0 = sin límite) |
| `TIMEOUT_MINUTES` | 20 | Tiempo máximo de espera por trabajo |
| `MAX_UPLOAD_MB` | 30 | Tamaño máximo de la subida (descomprimido: el triple) |
| `MAX_FILES` | 50 | Ficheros máximos por trabajo |
| `MAX_QUEUE` | 5 | Trabajos máximos en cola |
| `RESULT_TTL_MINUTES` | 120 | Tiempo que se guardan los resultados |
| `ANTHROPIC_VERSION` | 2023-06-01 | Cabecera `anthropic-version` de la API |

## Limitaciones del plan Free

- **Se duerme** tras 15 minutos sin peticiones, y la primera petición tarda hasta un minuto en despertarlo.
  - Mientras hay un trabajo en marcha, la página consulta el estado cada 4 segundos, y eso lo mantiene despierto. No cierres la pestaña hasta que termine.
- **El disco es efímero**: un reinicio borra los trabajos y los zips. Descarga el resultado en cuanto termine.
- Si se reinicia a mitad de un trabajo, la sesión del agente sigue en Anthropic. Puedes ver lo que hizo en la Console aunque la web lo haya perdido.

Para un uso continuado, cambia `plan: free` por `plan: starter` en `render.yaml`.

## Ejecutar en local (opcional)

Necesitas el SDK de .NET 10.

```powershell
$env:API_ACCESS_KEY="prueba"; $env:ANTHROPIC_API_KEY="sk-ant-..."; $env:AGENT_ID="agent_..."; $env:ENVIRONMENT_ID="env_..."
dotnet run
# abre http://localhost:8080
```
