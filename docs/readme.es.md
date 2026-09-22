# JEV-MCP

Servidor HTTP del [Model Context Protocol (MCP)](https://modelcontextprotocol.io) que expone el modelo **Jev** (System One, TypeSafe) como herramientas de juicio tipadas, con interfaz administrativa Blazor y auditoría de llamadas en SQLite.

Port de [`jkudish/jev-mcp`](https://github.com/jkudish/jev-mcp) (MIT, v0.5.0), que es un servidor Node **stdio**. Este proyecto usa transporte **Streamable HTTP**, implementa las herramientas en C# (.NET 10) y añade interfaz administrativa e historial persistente.

Jev no genera texto libre: recibe `state` + `questions` tipados y devuelve probabilidades calibradas en decenas a cientos de milisegundos. Las herramientas **no** son passthrough directo de la API. Cada una descompone el problema en preguntas atómicas, valida la respuesta del modelo y aplica política en código (`auto` / `review` / `block` / `escalate`). La salida malformada del modelo se trata como `invalid_response` (fail-closed).

## Comparación con el original

| | jev-mcp | JEV-MCP |
| --- | --- | --- |
| Transporte | stdio (`npx -y @jkudish/jev-mcp`) | Streamable HTTP en `/mcp` |
| Runtime | Node 20 | .NET 10, imagen Docker de runtime (sin SDK en la imagen final) |
| UI administrativa | ninguna | Blazor Server + MudBlazor (panel, auditoría, playground, ajustes) |
| Historial | ninguno | SQLite en volumen, retención configurable |
| Autenticación | entorno del proceso (stdio) | Bearer token en `/mcp` y `/api/jev`, cookie en admin |

Los diez nombres de herramientas, esquemas de entrada y payloads JSON coinciden con el original. Un cliente que ya llama `jev_verify` sigue llamando `jev_verify` sin cambios.

## Características

### Servidor MCP

- **Streamable HTTP** en `POST /mcp` con peticiones **stateless** (sin sesión MCP en el servidor), de modo que varias réplicas pueden ir detrás de un balanceador sin sticky sessions.
- **Diez herramientas de solo lectura** (véase [Herramientas MCP](#herramientas-mcp)) con el mismo contrato que jev-mcp.
- **Autenticación Bearer** en `/mcp` y `/api/jev` por defecto; acceso anónimo opcional solo para experimentos locales.
- **Filtro de auditoría de llamadas** registra cada invocación de herramienta (latencia, tokens, proveedor, resultado) en SQLite.
- **Endpoint de salud** en `GET /health` informando la configuración del proveedor (`ok` vs `degraded`).

### API REST

Las diez herramientas también están en `POST /api/jev/{name}` con el mismo JSON snake_case que MCP. `GET /api/jev/tools` las lista. Documentación interactiva: **Scalar** en `/scalar` (OpenAPI en `/openapi/v1.json`). Mismo Bearer token que `/mcp`.

### Backends del proveedor Jev

Resolución automática cuando `JEV__PROVIDER` está ausente o es `auto`, en este orden:

1. **TypeSafe** (API directa, recomendado)
2. **OpenRouter**
3. **Cloudflare Workers AI**
4. **Vercel AI Gateway**
5. **Compatible** (URL POST personalizada)

Establezca `JEV__PROVIDER` para forzar un único backend. El alias de modelo predeterminado es `jev-latest` (`JEV__MCP__MODEL` / `JEV:MCP:MODEL`). Las claves API del proveedor nunca se almacenan en SQLite, se envían al navegador ni se escriben en logs.

Política de **reintento** configurable para llamadas upstream (`JEV:RETRY` en la configuración).

### Interfaz web administrativa

UI Blazor Interactive Server (autenticación por cookie):

| Ruta | Propósito |
| --- | --- |
| `/` | **Panel** — resumen de proveedor/modelo, volumen de llamadas, latencia, uso de tokens, coste estimado, series temporales ECharts (24h / 7d / 30d), filtros de drill-down en auditoría |
| `/audit` | **Registro de auditoría** — cuadrícula buscable de llamadas MCP (herramienta, proveedor, acción, estado, rango temporal), detalle por fila |
| `/playground` | **Playground** — ejecutar cualquiera de las diez herramientas desde la UI con formularios de ejemplo (usa credenciales live del proveedor) |
| `/readme` | **Documentación** — documentación localizada del proyecto en la UI admin (`pt-BR`, `en`, `es`; incrustada en el build desde `docs/readme.*.md` y `README.md` para inglés; recompile tras editar) |
| `/settings` | **Ajustes** — vista general de presencia de credenciales (enmascaradas), precios de tokens por proveedor (USD por millón de tokens), días de retención, captura opcional de payload, toggle de MCP anónimo, **token de acceso MCP** crear / revocar / reactivar / eliminar |
| `/scalar` | **Referencia de la API REST** (Scalar) — OpenAPI interactivo de los diez endpoints de juicio |
| `/login` | Inicio de sesión admin usuario/contraseña |
| `/setup` | Primer token MCP en **loopback** cuando no hay token ni login admin configurados |
| `/tokens` | Redirige a `/settings` |

**Idiomas de la UI:** portugués (Brasil) por defecto, inglés, español (selector de idioma en el layout). La página **Documentación** (`/readme`) sigue la misma cultura.

**Tema:** alternancia entre modo claro/oscuro.

### Control de acceso

- **Admin:** usuario + contraseña de `appsettings.Development.json` en local o `ACCESSCONTROL__*` en Docker/producción. Alias heredado: `JEVMCP_ADMIN_*`. El JSON versionado deja contraseñas vacías.
- **Clientes MCP** y **clientes REST:** **tokens Bearer** de larga duración emitidos en Ajustes (o `POST /api/tokens` autenticado como admin); almacenados como hashes SHA-256; secreto completo mostrado solo una vez al crear.
- **Seguridad al arranque:** enlazar a una dirección **no loopback** sin token MCP activo, sin credenciales admin y sin MCP anónimo explícito hace que el arranque **falle** (Docker espera login admin o token antes de exponer el puerto).
- En **loopback**, la app puede iniciar sin credenciales; cree el primer token vía `/setup` o configure credenciales admin y emita tokens en Ajustes.

### Auditoría de llamadas (SQLite)

- Ruta predeterminada de la base de datos: `data/jevmcp.db` en local; Docker Compose: `/app/data/jevmcp.db` en el contenedor → `./data/jevmcp.db` en el host.
- **Retención** (`CALLAUDIT__RETENTIONDAYS`, predeterminado 30); `0` desactiva la purga.
- **Captura opcional de payload** (`CALLAUDIT__CAPTUREPAYLOADS`, predeterminado desactivado) con límite de tamaño (`CALLAUDIT__PAYLOADMAXCHARS`, predeterminado 4096).
- **Cuadrícula de precios de tokens** en Ajustes: añadir, actualizar y eliminar proveedores. Las tarifas persisten en SQLite (semilla inicial `0.042`; proveedores desconocidos usan ese valor).

### Despliegue

- **Docker Compose** con health check, volumen con nombre para datos, usuario de runtime no root tras el entrypoint.
- **Dockerfile multi-stage** (build .NET 10 SDK, runtime aspnet).

## Estructura de la solución

| Proyecto | Rol |
| --- | --- |
| `JevMcp.App` | Host ASP.NET Core, UI Blazor, endpoint HTTP MCP, middleware de auth |
| `JevMcp.Tools` | Tipos de herramientas MCP y servicios de juicio |
| `JevMcp.Providers` | Clientes upstream Jev (TypeSafe, OpenRouter, Cloudflare, Vercel, compatible) |
| `JevMcp.Core` | Tipos compartidos, JSON, validación |
| `JevMcp.Data` | Esquema SQLite, tokens de acceso, log de llamadas, ajustes operativos |
| `JevMcp.Tests` | Pruebas unitarias y de integración |

Archivo de solución: `JevMcp.slnx`. Versión del SDK fijada en `global.json` (.NET 10).

## Herramientas MCP

Los umbrales (`auto_accept`, `block_at`, `review_at`, cortes de existencia) son **puntos de partida** de los [cookbooks TypeSafe](https://docs.typesafe.ai/cookbooks). Ajústelos con sus datos antes de usarlos como compuertas de producción. Véase [cómo TypeSafe informa la confianza](https://docs.typesafe.ai/confidence.md).

| Herramienta | Resumen | Patrón cookbook |
| --- | --- | --- |
| `jev_verify` | Comprobar cada afirmación contra evidencia; veredicto por afirmación (`verified` \| `contradicted` \| `unsupported`), distribución, confianza, auto vs review | [Citation check](https://docs.typesafe.ai/cookbooks/citation_check) |
| `jev_screen` | Filtrar texto externo antes del contexto del agente: probabilidad de inyección, sustancia, relevancia opcional; recomendación `pass` \| `review` \| `block` \| `skip` | [LLM guardrails](https://docs.typesafe.ai/cookbooks/llm_guardrails) |
| `jev_find` | Selección semántica del(los) mejor(es) candidato(s) para una consulta (sin embeddings); incluye si algún candidato responde realmente a la consulta | [Semantic find](https://docs.typesafe.ai/cookbooks/semantic_find) |
| `jev_rerank` | Puntuar y ordenar todos los candidatos por relevancia en una petición | [Rerank](https://docs.typesafe.ai/cookbooks/rerank_typesafe) |
| `jev_classify` | Asignar en lote ítems a un catálogo de etiquetas compartido con auto vs review (margen + probabilidad top) | — |
| `jev_decide` | Decisión acotada entre 2–6 alternativas con evidencia, prioridades, requisitos opcionales, escape hatches | — |
| `jev_compare` | Relación entre dos pasajes (`same_fact`, `contradicts`, `different_facts`); juicios opcionales por aspecto | — |
| `jev_extract` | Regex suministrada por el llamador encuentra candidatos; Jev elige la coincidencia verdadera; valores devueltos **literalmente** del documento | — |
| `jev_review` | Puntuar parche/diff propuesto frente a la petición (rúbrica, `safe_to_apply`, `auto` \| `review` \| `escalate`) | — |
| `jev_gate` | `jev_review` más verificación de afirmaciones de finalización contra evidencia en una llamada | — |

### Límites (seleccionados)

- `jev_find` / `jev_rerank`: hasta **250** candidatos; texto truncado a **2000** caracteres por candidato.
- `jev_classify`: hasta **64** ítems por llamada.
- `jev_decide`: **2–6** candidatos; hasta **3** requisitos.
- `jev_compare`: pasajes hasta **20.000** caracteres cada uno.
- `jev_extract`: documento hasta **50.000** caracteres; hasta **32** campos.
- `jev_review` / `jev_gate`: diff/pruebas hasta **50.000** caracteres; evidencia del gate limitada a **16** ítems y **200.000** caracteres en total.

Los nombres de parámetros en los esquemas de herramientas usan **snake_case** (p. ej. `auto_accept`, `top_k`) para coincidir con el contrato original jev-mcp.

## Ejecutar con Docker

La imagen usa un **Dockerfile** multi-stage (build con SDK → runtime `aspnet`, puerto **10022**). [Docker Compose v2](https://docs.docker.com/compose/) construye esa imagen y ejecuta la app con volumen SQLite y health check.

### Requisitos previos

- **Docker Engine** con **Compose v2** (`docker compose version`).
- Raíz del repositorio como directorio de trabajo (donde están `Dockerfile` y `docker-compose.yml`).

### 1. Crear `.env`

Compose carga `.env` para interpolación y pasa claves de proveedor/admin al contenedor (archivo en `.gitignore`; no lo suba al git).

**Linux / macOS / Git Bash:**

```bash
cp .env.example .env
```

**Windows PowerShell:**

```powershell
Copy-Item .env.example .env
```

Edite `.env` y defina al menos:

| Variable | Propósito |
| --- | --- |
| `ACCESSCONTROL__USERNAME` | Login de la UI admin |
| `ACCESSCONTROL__PASSWORD` | Contraseña de la UI admin |
| `TYPESAFE__API__KEY` (u otro bloque de proveedor) | Al menos una credencial del backend Jev |

Opcional: `JEVMCP_HOST_PORT` (predeterminado **10022** en el host). Las claves usan `SECTION__KEY` como en [`.env.example`](.env.example) y en `appsettings.Development.json` — puede copiar valores del JSON de Development al `.env` para Docker.

### 2. Build y arranque

```bash
docker compose up --build -d
```

Esto construye la imagen `jevmcp:dev`, publica `http://localhost:10022` (o su `JEVMCP_HOST_PORT`) y monta la carpeta del host **`./data`** en `/app/data` (SQLite: `./data/jevmcp.db`).

Solo build de imagen (sin contenedor):

```bash
docker build -t jevmcp:dev .
```

### 3. Verificar

```bash
docker compose ps
curl -fsS http://localhost:10022/health
docker compose logs -f jevmcp
```

Espere a que el servicio esté **healthy** (`GET /health` devuelve 200). El proceso escucha en `0.0.0.0:10022` dentro del contenedor y rechaza bind público sin login admin, token MCP activo o MCP anónimo habilitado.

**UI admin:** `http://localhost:10022` — inicie sesión con credenciales del `.env`. Emita tokens MCP en **Ajustes** y úselos en su cliente MCP.

### Persistencia y ciclo de vida

- **SQLite en disco:** en Compose, `CALLAUDIT__DATABASEPATH` es `/app/data/jevmcp.db` en el contenedor, reflejado en **`./data/jevmcp.db` en el host** (bind mount). Reconstruir la imagen (`docker compose build`, `up --build`) **no** borra esa carpeta; solo borrar `./data` en el host (o cambiar el mount en `docker-compose.yml`) pierde el historial.
- Opcional: volumen Docker con nombre fijo en lugar de `./data` — vea el comentario al final de `docker-compose.yml` (`jevmcp_sqlite_data`).
- Tras cambios de código: `docker compose up --build -d`.

```bash
docker compose down          # detiene contenedores; ./data/jevmcp.db permanece en el host
docker compose down -v       # solo elimina volúmenes nombrados (no aplica al bind ./data por defecto)
```

Puerto en el host predeterminado **10022** (`JEVMCP_HOST_PORT` en `.env`; puerto del contenedor siempre **10022**).

## Ejecutar sin Docker

Instale el **.NET 10 SDK**. `dotnet run` usa **Development** y carga `appsettings.Development.json` (mismas claves en mayúsculas que `.env.example`, secretos vacíos en git). Rellene `USERNAME`, `PASSWORD` y claves de proveedor allí, o use user secrets. `dotnet run` **no** lee `.env`.

```bash
# Edite src/JevMcp.App/appsettings.Development.json, luego:
dotnet run --project src/JevMcp.App/JevMcp.App.csproj --launch-profile https
```

En loopback sin credenciales admin, cree el primer token MCP en `/setup`.

El perfil **`https`** escucha en `https://localhost:7201` (y `http://localhost:5246`). El perfil **`http`** es solo HTTP en `http://localhost:5246`. Ruta SQLite predeterminada: `data/jevmcp.db` relativa a la content root.

## Configuración del cliente MCP

Sustituya el servidor stdio original por esta URL. `Authorization` es obligatorio salvo MCP anónimo habilitado.

**Cursor** (`.cursor/mcp.json` o `~/.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "JEV-MCP": {
      "url": "http://localhost:10022/mcp",
      "headers": {
        "Authorization": "Bearer jevmcp_your_token_here"
      }
    }
  }
}
```

Use `https://localhost:7201/mcp` en local con `--launch-profile https`, o `http://localhost:5246/mcp` con `--launch-profile http`.

Cualquier cliente Streamable HTTP:

```http
POST /mcp HTTP/1.1
Host: localhost:10022
Accept: application/json, text/event-stream
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here
```

(En dev local con HTTPS, use `Host: localhost:7201` y TLS.)

Las diez herramientas deben aparecer en `tools/list` con los mismos payloads que jev-mcp.

Los mismos payloads también están disponibles como HTTP JSON:

```http
POST /api/jev/verify HTTP/1.1
Host: localhost:10022
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here

{"claims":["The sky is blue."],"evidence":"The sky appears blue during the day."}
```

Referencia interactiva: `http://localhost:10022/scalar` con Docker, o `https://localhost:7201/scalar` en local con perfil `https` (OpenAPI en `/openapi/v1.json`).

No haga commit de tokens completos en git. En Cursor puede usar interpolación de entorno, p. ej. `Bearer ${env:JEVMCP_TOKEN}`.

## API HTTP

| Método | Ruta | Auth | Propósito |
| --- | --- | --- | --- |
| `GET` | `/health` | ninguna | Liveness y estado del proveedor |
| `GET` | `/scalar` | ninguna | Referencia OpenAPI Scalar |
| `GET` | `/openapi/v1.json` | ninguna | Documento OpenAPI |
| `GET` | `/api/jev/tools` | Bearer (salvo anónimo) | Catálogo de las diez herramientas |
| `POST` | `/api/jev/verify` | Bearer (salvo anónimo) | `jev_verify` |
| `POST` | `/api/jev/screen` | Bearer (salvo anónimo) | `jev_screen` |
| `POST` | `/api/jev/find` | Bearer (salvo anónimo) | `jev_find` |
| `POST` | `/api/jev/classify` | Bearer (salvo anónimo) | `jev_classify` |
| `POST` | `/api/jev/decide` | Bearer (salvo anónimo) | `jev_decide` |
| `POST` | `/api/jev/rerank` | Bearer (salvo anónimo) | `jev_rerank` |
| `POST` | `/api/jev/compare` | Bearer (salvo anónimo) | `jev_compare` |
| `POST` | `/api/jev/extract` | Bearer (salvo anónimo) | `jev_extract` |
| `POST` | `/api/jev/review` | Bearer (salvo anónimo) | `jev_review` |
| `POST` | `/api/jev/gate` | Bearer (salvo anónimo) | `jev_gate` |
| `POST` | `/mcp` | Bearer (salvo anónimo) | MCP Streamable HTTP |
| `POST` | `/api/session` | anónimo | Login admin (formulario) |
| `POST` | `/api/session/logout` | cookie | Logout admin |
| `POST` | `/api/setup` | anónimo (setup loopback) | Crear el primer token MCP |
| `POST` | `/api/tokens` | cookie admin | Emitir token MCP (secreto devuelto una vez) |
| `POST` | `/api/culture` | anónimo | Establecer cookie de cultura de la UI |

MCP, REST, `/health`, `/scalar` y `/openapi` devuelven códigos de estado adecuados y `WWW-Authenticate` en 401; no se reescriben a páginas de error HTML.

## Variables de entorno

Precedencia para auto-detección de proveedor (`JEV__PROVIDER` no definido o `auto`): **TypeSafe → OpenRouter → Cloudflare → Vercel → compatible**. `JEV__PROVIDER` fuerza un proveedor. Modelo predeterminado: `jev-latest` (`JEV__MCP__MODEL`).

| Variable | Predeterminado | Propósito |
| --- | --- | --- |
| `ACCESSCONTROL__USERNAME` | (vacío) | Usuario admin. Igual que `ACCESSCONTROL:USERNAME` (JSON Development o env). |
| `ACCESSCONTROL__PASSWORD` | (vacío) | Contraseña admin. Nunca haga commit de valores reales. |
| `JEVMCP_ADMIN_USERNAME` | (vacío) | Alias heredado cuando `ACCESSCONTROL__USERNAME` no está definido. |
| `JEVMCP_ADMIN_PASSWORD` | (vacío) | Alias heredado cuando `ACCESSCONTROL__PASSWORD` no está definido. |
| `ACCESSCONTROL__ALLOWANONYMOUSMCP` | `false` | Permitir `/mcp` y `/api/jev` sin Bearer. No para producción. |
| `JEV__PROVIDER` | `auto` | `typesafe`, `openrouter`, `cloudflare`, `vercel`, `compatible` o `auto`. Igual que `JEV:PROVIDER`. Heredado: `JEV_PROVIDER`. |
| `JEV__MCP__MODEL` | `jev-latest` | Id del modelo Jev (OpenRouter no tiene alias `latest`). Igual que `JEV:MCP:MODEL`. Heredado: `JEV_MCP_MODEL`. |
| `TYPESAFE__API__KEY` | | Clave API directa TypeSafe (`TYPESAFE:API:KEY`). Heredado: `TYPESAFE_API_KEY`, `JEV_TYPESAFE_API_KEY`. |
| `TYPESAFE__BASEURL` | `https://api.typesafe.ai` | Solo origen; se añade `/v1/systemone`. Heredado: `TYPESAFE_BASE_URL`. |
| `OPENROUTER__API__KEY` | | Clave OpenRouter (debe empezar con `sk-or-`). Heredado: `OPENROUTER_API_KEY`, `JEV_OPENROUTER_API_KEY`. |
| `CLOUDFLARE__API__TOKEN` | | Token API Cloudflare. Heredado: `CLOUDFLARE_API_TOKEN`, `JEV_CLOUDFLARE_API_TOKEN`. |
| `CLOUDFLARE__ACCOUNT__ID` | | Id de cuenta Cloudflare. Heredado: `CLOUDFLARE_ACCOUNT_ID`, `JEV_CLOUDFLARE_ACCOUNT_ID`. |
| `AIGATEWAY__API__KEY` | | Vercel AI Gateway. Heredado: `AI_GATEWAY_API_KEY`, `JEV_AI_GATEWAY_API_KEY`. |
| `AIGATEWAY__BASEURL` | | Origen opcional del gateway. Heredado: `AI_GATEWAY_BASE_URL`. |
| `COMPATIBLE__API__KEY` | | Clave API del endpoint compatible. Heredado: `JEV_API_KEY`. |
| `COMPATIBLE__BASEURL` | | URL POST completa para modo compatible (usada literalmente). Heredado: `JEV_API_BASE_URL`. |
| `CALLAUDIT__DATABASEPATH` | `data/jevmcp.db` | Ruta SQLite (`/app/data/jevmcp.db` en Compose). |
| `CALLAUDIT__CAPTUREPAYLOADS` | `false` | Persistir cuerpos de request/response en auditoría. |
| `CALLAUDIT__PAYLOADMAXCHARS` | `4096` | Máximo de texto de payload capturado. |
| `CALLAUDIT__RETENTIONDAYS` | `30` | Eliminar filas más antiguas; `0` desactiva. |
| `JEVMCP_HOST_PORT` | `10022` | Mapeo de puerto en el host (solo Compose; mapea al **10022** del contenedor). |
| `ASPNETCORE_URLS` | `http://+:10022` en la imagen | Direcciones de bind. |

Lista de referencia comentada: [`.env.example`](.env.example).

**Dev local:** `appsettings.Development.json` contiene el árbol de claves en mayúsculas (`ACCESSCONTROL`, `JEV`, `TYPESAFE`, …) con secretos vacíos en git. **Docker/producción:** `appsettings.json` solo lleva valores predeterminados compartidos (p. ej. `CALLAUDIT`); proveedor y admin vienen de variables de entorno `SECTION__KEY` (véase `.env.example`) y nombres heredados jev-mcp tras el bind. Los precios de tokens se editan en Ajustes y se almacenan en SQLite.

## Desarrollo

Gate verde del repositorio (build, pruebas, imagen Docker, smoke HTTP incluyendo Compose healthy, `tools/list` autenticado y persistencia tras reinicio):

```powershell
./scripts/green.ps1
```

Requiere **.NET 10 SDK** en PATH (o `JEVMCP_DOTNET`). En Windows, ejecute en **Windows PowerShell 5.1** (no `pwsh`, según convención del proyecto).

`-SkipDocker` omite build de imagen y smoke de contenedor para iteración local más rápida; no sustituye el gate completo para release/fases OpenSpec.

El smoke también comprueba REST autenticado (`POST /api/jev/extract`), OpenAPI y Scalar.

Solo pruebas:

```bash
dotnet test JevMcp.slnx
```

## Notas de seguridad

- Tokens de acceso MCP: **hash en reposo**; texto plano mostrado solo al emitir.
- Secretos de proveedor: solo entorno/configuración; nunca en la BD de auditoría ni tablas admin.
- Habilite `AllowAnonymousMcp` solo en redes locales de confianza; abre `/mcp` y `/api/jev`.
- `/setup` inicial es **solo loopback** cuando no hay token ni contraseña admin configurados.

## Licencia y atribución

[MIT](LICENSE). Este repositorio es un port de [jev-mcp](https://github.com/jkudish/jev-mcp), copyright [Joey Kudish](https://github.com/jkudish), también MIT. Este port .NET es mantenido por [Marcelo Toledo](mailto:marcelo.toledo@theronsystems.com.br) en TheronSystems. La política y el diseño de herramientas siguen [cookbooks TypeSafe](https://docs.typesafe.ai/cookbooks). El panel usa [Apache ECharts 5.6.0](https://github.com/apache/echarts) (Apache-2.0); véase NOTICE junto al asset en `wwwroot/lib/echarts/`.
