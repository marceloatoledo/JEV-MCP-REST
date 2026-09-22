# JEV-MCP

HTTP [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes the **Jev** model (System One, TypeSafe) as typed judgment tools, with a Blazor admin UI and SQLite call audit.

A port of [`jkudish/jev-mcp`](https://github.com/jkudish/jev-mcp) (MIT, v0.5.0), which is a **stdio** Node server. This project uses **Streamable HTTP** transport, implements tools in C# (.NET 10), and adds an administrative interface plus persistent history.

Jev does not generate free-form text: it takes typed `state` + `questions` and returns calibrated probabilities in tens to hundreds of milliseconds. The tools are **not** raw API passthrough. Each one decomposes the problem into atomic questions, validates the model response, and applies policy in code (`auto` / `review` / `block` / `escalate`). Malformed model output is treated as `invalid_response` (fail-closed).

## Comparison with the original

| | jev-mcp | JEV-MCP |
| --- | --- | --- |
| Transport | stdio (`npx -y @jkudish/jev-mcp`) | Streamable HTTP at `/mcp` |
| Runtime | Node 20 | .NET 10, Docker runtime image (no SDK in final image) |
| Admin UI | none | Blazor Server + MudBlazor (dashboard, audit, playground, settings) |
| History | none | SQLite on a volume, configurable retention |
| Authentication | process environment (stdio) | Bearer token on `/mcp` and `/api/jev`, cookie on admin |

The ten tool names, input schemas, and JSON payloads match the original. A client that already calls `jev_verify` keeps calling `jev_verify` unchanged.

## Admin UI (screenshots)

After [Docker](#run-with-docker) or [local](#run-without-docker) startup, open the admin site in a browser (`http://localhost:10022` with Compose, or `https://localhost:7201` with the `https` launch profile). Captures below use the **dark** theme and **English** UI; the same pages are available in Portuguese (Brazil) and Spanish via the language switcher.

### Sign in (`/login`)

Use the username and password from `ACCESSCONTROL__*` (Docker / `.env`) or `AccessControl` in `appsettings.Development.json` when running locally. Until admin credentials or an MCP token exist, loopback-only flows such as `/setup` can bootstrap the first token.

![Sign in — username and password form for the admin UI](docs/images/login.png)

### Dashboard (`/`)

Summary cards show the effective provider, model, call count, latency, token usage, and estimated USD cost for the selected window (24 h, 7 days, or 30 days). ECharts series cover consumption, latency, tool mix, action outcomes, and status; chart interactions can narrow the audit log.

![Dashboard — metrics and charts for provider usage over time](docs/images/dashboard.png)

### Audit (`/audit`)

Server-side filters and paging over the SQLite call history: tool, provider, action, status, time range, per-call latency and cost, and token identity. Optional payload capture is controlled in Settings.

![Audit — searchable table of MCP and REST tool invocations](docs/images/audit.png)

### Playground (`/playground`)

Run any of the ten judgment tools with the same service layer as MCP, without configuring a client transport. JSON fields mirror the tool schemas (`state`, `questions`, thresholds, and tool-specific inputs).

![Playground — tool selector and sample JSON for jev_extract](docs/images/playground.png)

### Documentation (`/readme`)

The full project README for the active culture, rendered inside the app (rebuild after editing `README.md` or `docs/readme.*.md`).

![Documentation — embedded README and comparison table in the admin UI](docs/images/documentation.png)

### REST API reference (`/scalar`)

Interactive OpenAPI for `GET /health` and `POST /api/jev/{tool}`. Authorize with the same Bearer token issued in Settings as for `POST /mcp`.

![Scalar — OpenAPI browser for the REST judgment endpoints](docs/images/api-scalar.png)

### Settings (`/settings`)

Masked view of which provider and access-control environment variables are set, per-provider token pricing for cost estimates, retention and payload options, anonymous MCP toggle, and MCP access token lifecycle (create, revoke, remove).

![Settings — credentials overview, pricing, operations, and access tokens](docs/images/settings.png)

## Features

### MCP server

- **Streamable HTTP** at `POST /mcp` with **stateless** requests (no server-side MCP session), so multiple replicas can sit behind a load balancer without sticky sessions.
- **Ten read-only tools** (see [MCP tools](#mcp-tools)) with the same contract as jev-mcp.
- **Bearer authentication** on `/mcp` and `/api/jev` by default; optional anonymous access for local experiments only.
- **Call audit filter** records each tool invocation (latency, tokens, provider, outcome) into SQLite.
- **Health endpoint** at `GET /health` reporting provider configuration (`ok` vs `degraded`).

### REST API

The ten tools are also `POST /api/jev/{name}` with the same snake_case JSON as MCP. `GET /api/jev/tools` lists them. Interactive docs: **Scalar** at `/scalar` (OpenAPI at `/openapi/v1.json`). Same Bearer token as `/mcp`.

### Jev provider backends

Automatic resolution when `JEV__PROVIDER` is absent or `auto`, in this order:

1. **TypeSafe** (direct API, recommended)
2. **OpenRouter**
3. **Cloudflare Workers AI**
4. **Vercel AI Gateway**
5. **Compatible** (custom POST URL)

Set `JEV__PROVIDER` to force a single backend. Default model alias is `jev-latest` (`JEV__MCP__MODEL` / `JEV:MCP:MODEL`). Provider API keys are never stored in SQLite, sent to the browser, or written to logs.

Configurable **retry** policy for upstream calls (`JEV:RETRY` in configuration).

### Admin web UI

Blazor Interactive Server UI (cookie authentication):

| Route | Purpose |
| --- | --- |
| `/` | **Dashboard** — provider/model summary, call volume, latency, token usage, estimated cost, ECharts time series (24h / 7d / 30d), drill-down filters into audit |
| `/audit` | **Audit log** — searchable grid of MCP calls (tool, provider, action, status, time range), row detail |
| `/playground` | **Playground** — run any of the ten tools from the UI with sample forms (uses live provider credentials) |
| `/readme` | **Documentation** — localized project docs in the admin UI (`pt-BR`, `en`, `es`; embedded at build from `docs/readme.*.md` and `README.md` for English; rebuild after edits) |
| `/settings` | **Settings** — credential presence overview (masked), per-provider token pricing (USD per million tokens), retention days, optional payload capture, anonymous MCP toggle, **MCP access token** create / revoke / reactivate / delete |
| `/scalar` | **REST API reference** (Scalar) — interactive OpenAPI of the ten judgment endpoints |
| `/login` | Admin username/password login |
| `/setup` | First-time MCP token on **loopback** when no token and no admin login are configured |
| `/tokens` | Redirects to `/settings` |

**UI languages:** Portuguese (Brazil) default, English, Spanish (language switcher in the layout). The **Documentation** page (`/readme`) follows the same culture.

**Theme:** light/dark mode toggle.

### Access control

- **Admin:** username + password from `appsettings.Development.json` locally or `ACCESSCONTROL__*` in Docker/production. Legacy alias: `JEVMCP_ADMIN_*`. Committed JSON leaves passwords empty.
- **MCP clients** and **REST clients:** long-lived **Bearer tokens** issued in Settings (or `POST /api/tokens` while signed in as admin); stored as SHA-256 hashes; full secret shown only once at creation.
- **Startup safety:** binding to a **non-loopback** address without an active MCP token, without admin credentials, and without explicit anonymous MCP causes startup to **fail** (Docker expects admin login or a token before exposing the port).
- On **loopback**, the app can start without credentials; create the first token via `/setup` or set admin credentials and issue tokens in Settings.

### Call audit (SQLite)

- Default database path: `data/jevmcp.db` locally; Docker Compose: `/app/data/jevmcp.db` in the container → `./data/jevmcp.db` on the host.
- **Retention** (`CALLAUDIT__RETENTIONDAYS`, default 30); `0` disables purge.
- Optional **payload capture** (`CALLAUDIT__CAPTUREPAYLOADS`, default off) with size cap (`CALLAUDIT__PAYLOADMAXCHARS`, default 4096).
- **Token pricing** grid in Settings: add, update, and delete providers. Rates persist in SQLite (first-run seed `0.042`; unknown providers fall back to that).

### Deployment

- **Docker Compose** with health check, named volume for data, non-root runtime user after entrypoint.
- **Multi-stage Dockerfile** (.NET 10 SDK build, aspnet runtime).

## Solution layout

| Project | Role |
| --- | --- |
| `JevMcp.App` | ASP.NET Core host, Blazor UI, MCP HTTP endpoint, auth middleware |
| `JevMcp.Tools` | MCP tool types and judgment services |
| `JevMcp.Providers` | Jev upstream clients (TypeSafe, OpenRouter, Cloudflare, Vercel, compatible) |
| `JevMcp.Core` | Shared types, JSON, validation |
| `JevMcp.Data` | SQLite schema, access tokens, call log, operational settings |
| `JevMcp.Tests` | Unit and integration tests |

Solution file: `JevMcp.slnx`. SDK version pinned in `global.json` (.NET 10).

## MCP tools

Thresholds (`auto_accept`, `block_at`, `review_at`, existence cutoffs) are **starting points** from [TypeSafe cookbooks](https://docs.typesafe.ai/cookbooks). Tune them on your data before treating them as production gates. See [how TypeSafe reports confidence](https://docs.typesafe.ai/confidence.md).

| Tool | Summary | Cookbook pattern |
| --- | --- | --- |
| `jev_verify` | Check each claim against evidence; per-claim verdict (`verified` \| `contradicted` \| `unsupported`), distribution, confidence, auto vs review | [Citation check](https://docs.typesafe.ai/cookbooks/citation_check) |
| `jev_screen` | Screen external text before agent context: injection probability, substance, optional relevance; recommendation `pass` \| `review` \| `block` \| `skip` | [LLM guardrails](https://docs.typesafe.ai/cookbooks/llm_guardrails) |
| `jev_find` | Semantic pick of best candidate(s) for a query (no embeddings); includes whether any candidate truly addresses the query | [Semantic find](https://docs.typesafe.ai/cookbooks/semantic_find) |
| `jev_rerank` | Score and sort all candidates by relevance in one request | [Rerank](https://docs.typesafe.ai/cookbooks/rerank_typesafe) |
| `jev_classify` | Batch-assign items to a shared label catalog with auto vs review (margin + top probability) | — |
| `jev_decide` | Bounded decision among 2–6 alternatives with evidence, priorities, optional requirements, escape hatches | — |
| `jev_compare` | Relationship between two passages (`same_fact`, `contradicts`, `different_facts`); optional per-aspect judgments | — |
| `jev_extract` | Caller-supplied regex finds candidates; Jev picks the true match; values returned **verbatim** from the document | — |
| `jev_review` | Score a proposed patch/diff against the request (rubric, `safe_to_apply`, `auto` \| `review` \| `escalate`) | — |
| `jev_gate` | `jev_review` plus verification of completion claims against evidence in one call | — |

### Limits (selected)

- `jev_find` / `jev_rerank`: up to **250** candidates; text truncated at **2000** chars per candidate.
- `jev_classify`: up to **64** items per call.
- `jev_decide`: **2–6** candidates; up to **3** requirements.
- `jev_compare`: passages up to **20,000** chars each.
- `jev_extract`: document up to **50,000** chars; up to **32** fields.
- `jev_review` / `jev_gate`: diff/tests up to **50,000** chars; gate evidence capped at **16** items and **200,000** chars total.

Parameter names in tool schemas use **snake_case** (e.g. `auto_accept`, `top_k`) to match the original jev-mcp contract.

## Run with Docker

The image is a multi-stage **Dockerfile** (SDK build → `aspnet` runtime, port **10022**). [Docker Compose v2](https://docs.docker.com/compose/) builds that image and runs the app with a SQLite volume and health check.

### Prerequisites

- **Docker Engine** with **Compose v2** (`docker compose version`).
- Repository root as working directory (where `Dockerfile` and `docker-compose.yml` live).

### 1. Create `.env`

Compose loads `.env` for variable substitution and passes provider/admin keys into the container (the file is gitignored; never commit it).

**Linux / macOS / Git Bash:**

```bash
cp .env.example .env
```

**Windows PowerShell:**

```powershell
Copy-Item .env.example .env
```

Edit `.env` and set at minimum:

| Variable | Purpose |
| --- | --- |
| `ACCESSCONTROL__USERNAME` | Admin UI login |
| `ACCESSCONTROL__PASSWORD` | Admin UI password |
| `TYPESAFE__API__KEY` (or another provider block) | At least one Jev backend credential |

Optional: `JEVMCP_HOST_PORT` (default **10022** on the host). Keys use the same `SECTION__KEY` names as [`.env.example`](.env.example) and uppercase keys in `appsettings.Development.json` — you can copy values from Development JSON into `.env` for Docker.

### 2. Build and start

```bash
docker compose up --build -d
```

This builds image `jevmcp:dev`, publishes `http://localhost:10022` (or your `JEVMCP_HOST_PORT`), and bind-mounts host folder **`./data`** to `/app/data` (SQLite file: `./data/jevmcp.db`).

To build the image only (no container):

```bash
docker build -t jevmcp:dev .
```

### 3. Verify

```bash
docker compose ps
curl -fsS http://localhost:10022/health
docker compose logs -f jevmcp
```

Wait until the service is **healthy** (`GET /health` returns 200). The process listens on `0.0.0.0:10022` inside the container and refuses a public bind without admin login, an active MCP token, or explicitly enabled anonymous MCP.

**Admin UI:** `http://localhost:10022` — sign in with credentials from `.env`. Issue MCP tokens under **Settings** and use them in your MCP client.

### Persistence and lifecycle

- **SQLite on disk:** `CALLAUDIT__DATABASEPATH` in Compose is `/app/data/jevmcp.db` inside the container, backed by **`./data/jevmcp.db` on the host** (bind mount). Rebuilding the image (`docker compose build`, `up --build`) does **not** delete that folder; only removing `./data` on the host (or switching the mount in `docker-compose.yml`) loses history.
- Optional: use a fixed Docker named volume instead of `./data` — see the comment block at the bottom of `docker-compose.yml` (`jevmcp_sqlite_data`).
- Rebuild after code changes: `docker compose up --build -d`.

```bash
docker compose down          # stops containers; ./data/jevmcp.db remains on the host
docker compose down -v       # only removes named volumes (not used with the default ./data bind)
```

Host port defaults to **10022** (`JEVMCP_HOST_PORT` in `.env`; container port is always **10022**).

## Run without Docker

Install **.NET 10 SDK**. `dotnet run` uses **Development** and loads `appsettings.Development.json` (same uppercase keys as `.env.example`, secrets left empty in git). Fill `USERNAME`, `PASSWORD`, and provider keys there, or use user secrets. `dotnet run` does **not** read `.env`.

```bash
# Edit src/JevMcp.App/appsettings.Development.json, then:
dotnet run --project src/JevMcp.App/JevMcp.App.csproj --launch-profile https
```

On loopback without admin credentials, create the first MCP token at `/setup`.

The **`https`** profile listens on `https://localhost:7201` (and `http://localhost:5246`). The **`http`** profile is HTTP-only on `http://localhost:5246`. Default SQLite path: `data/jevmcp.db` relative to the content root.

## MCP client configuration

Replace the original stdio server with this URL. `Authorization` is required unless anonymous MCP is enabled.

**Cursor** (`.cursor/mcp.json` or `~/.cursor/mcp.json`):

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

Use `https://localhost:7201/mcp` when running locally with `--launch-profile https`, or `http://localhost:5246/mcp` with `--launch-profile http`.

Any Streamable HTTP client:

```http
POST /mcp HTTP/1.1
Host: localhost:10022
Accept: application/json, text/event-stream
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here
```

(With local HTTPS dev, use `Host: localhost:7201` and TLS.)

All ten tools should appear in `tools/list` with the same payloads as jev-mcp.

The same payloads are also available as JSON HTTP:

```http
POST /api/jev/verify HTTP/1.1
Host: localhost:10022
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here

{"claims":["The sky is blue."],"evidence":"The sky appears blue during the day."}
```

Interactive reference: `http://localhost:10022/scalar` with Docker, or `https://localhost:7201/scalar` locally with the `https` profile (OpenAPI at `/openapi/v1.json`).

Do not commit full tokens to git. In Cursor you can use environment interpolation, e.g. `Bearer ${env:JEVMCP_TOKEN}`.

## HTTP API

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| `GET` | `/health` | none | Liveness and provider status |
| `GET` | `/scalar` | none | Scalar OpenAPI reference |
| `GET` | `/openapi/v1.json` | none | OpenAPI document |
| `GET` | `/api/jev/tools` | Bearer (unless anonymous) | Catalog of the ten tools |
| `POST` | `/api/jev/verify` | Bearer (unless anonymous) | `jev_verify` |
| `POST` | `/api/jev/screen` | Bearer (unless anonymous) | `jev_screen` |
| `POST` | `/api/jev/find` | Bearer (unless anonymous) | `jev_find` |
| `POST` | `/api/jev/classify` | Bearer (unless anonymous) | `jev_classify` |
| `POST` | `/api/jev/decide` | Bearer (unless anonymous) | `jev_decide` |
| `POST` | `/api/jev/rerank` | Bearer (unless anonymous) | `jev_rerank` |
| `POST` | `/api/jev/compare` | Bearer (unless anonymous) | `jev_compare` |
| `POST` | `/api/jev/extract` | Bearer (unless anonymous) | `jev_extract` |
| `POST` | `/api/jev/review` | Bearer (unless anonymous) | `jev_review` |
| `POST` | `/api/jev/gate` | Bearer (unless anonymous) | `jev_gate` |
| `POST` | `/mcp` | Bearer (unless anonymous) | MCP Streamable HTTP |
| `POST` | `/api/session` | anonymous | Admin login (form) |
| `POST` | `/api/session/logout` | cookie | Admin logout |
| `POST` | `/api/setup` | anonymous (loopback setup) | Create the first MCP token |
| `POST` | `/api/tokens` | admin cookie | Issue an MCP token (secret returned once) |
| `POST` | `/api/culture` | anonymous | Set UI culture cookie |

MCP, REST, `/health`, `/scalar`, and `/openapi` return proper status codes and `WWW-Authenticate` on 401; they are not rewritten to HTML error pages.

## Environment variables

Precedence for provider auto-detection (`JEV__PROVIDER` unset or `auto`): **TypeSafe → OpenRouter → Cloudflare → Vercel → compatible**. `JEV__PROVIDER` forces one provider. Default model: `jev-latest` (`JEV__MCP__MODEL`).

| Variable | Default | Purpose |
| --- | --- | --- |
| `ACCESSCONTROL__USERNAME` | (empty) | Admin user. Same as `ACCESSCONTROL:USERNAME` (Development JSON or env). |
| `ACCESSCONTROL__PASSWORD` | (empty) | Admin password. Never commit real values. |
| `JEVMCP_ADMIN_USERNAME` | (empty) | Legacy alias when `ACCESSCONTROL__USERNAME` is unset. |
| `JEVMCP_ADMIN_PASSWORD` | (empty) | Legacy alias when `ACCESSCONTROL__PASSWORD` is unset. |
| `ACCESSCONTROL__ALLOWANONYMOUSMCP` | `false` | Allow `/mcp` and `/api/jev` without Bearer. Not for production. |
| `JEV__PROVIDER` | `auto` | `typesafe`, `openrouter`, `cloudflare`, `vercel`, `compatible`, or `auto`. Same as `JEV:PROVIDER`. Legacy: `JEV_PROVIDER`. |
| `JEV__MCP__MODEL` | `jev-latest` | Jev model id (OpenRouter has no `latest` alias). Same as `JEV:MCP:MODEL`. Legacy: `JEV_MCP_MODEL`. |
| `TYPESAFE__API__KEY` | | TypeSafe direct API key (`TYPESAFE:API:KEY`). Legacy: `TYPESAFE_API_KEY`, `JEV_TYPESAFE_API_KEY`. |
| `TYPESAFE__BASEURL` | `https://api.typesafe.ai` | Origin only; `/v1/systemone` is appended. Legacy: `TYPESAFE_BASE_URL`. |
| `OPENROUTER__API__KEY` | | OpenRouter key (must start with `sk-or-`). Legacy: `OPENROUTER_API_KEY`, `JEV_OPENROUTER_API_KEY`. |
| `CLOUDFLARE__API__TOKEN` | | Cloudflare API token. Legacy: `CLOUDFLARE_API_TOKEN`, `JEV_CLOUDFLARE_API_TOKEN`. |
| `CLOUDFLARE__ACCOUNT__ID` | | Cloudflare account id. Legacy: `CLOUDFLARE_ACCOUNT_ID`, `JEV_CLOUDFLARE_ACCOUNT_ID`. |
| `AIGATEWAY__API__KEY` | | Vercel AI Gateway. Legacy: `AI_GATEWAY_API_KEY`, `JEV_AI_GATEWAY_API_KEY`. |
| `AIGATEWAY__BASEURL` | | Optional gateway origin. Legacy: `AI_GATEWAY_BASE_URL`. |
| `COMPATIBLE__API__KEY` | | Compatible endpoint API key. Legacy: `JEV_API_KEY`. |
| `COMPATIBLE__BASEURL` | | Full POST URL for compatible mode (used verbatim). Legacy: `JEV_API_BASE_URL`. |
| `CALLAUDIT__DATABASEPATH` | `data/jevmcp.db` | SQLite path (`/app/data/jevmcp.db` in Compose). |
| `CALLAUDIT__CAPTUREPAYLOADS` | `false` | Persist request/response bodies in audit. |
| `CALLAUDIT__PAYLOADMAXCHARS` | `4096` | Max captured payload text. |
| `CALLAUDIT__RETENTIONDAYS` | `30` | Delete older rows; `0` disables. |
| `JEVMCP_HOST_PORT` | `10022` | Host port mapping (Compose only; maps to container port **10022**). |
| `ASPNETCORE_URLS` | `http://+:10022` in image | Bind addresses. |

Commented reference list: [`.env.example`](.env.example).

**Local dev:** `appsettings.Development.json` holds the uppercase key tree (`ACCESSCONTROL`, `JEV`, `TYPESAFE`, …) with empty secrets in git. **Docker/production:** `appsettings.json` only carries shared defaults (e.g. `CALLAUDIT`); provider and admin values come from `SECTION__KEY` env vars (see `.env.example`) and legacy jev-mcp names after bind. Token pricing is edited in Settings and stored in SQLite.

## Development

Repository green gate (build, tests, Docker image, HTTP smoke including healthy Compose, authenticated `tools/list`, and persistence across restart):

```powershell
./scripts/green.ps1
```

Requires **.NET 10 SDK** on PATH (or `JEVMCP_DOTNET`). On Windows, run in **Windows PowerShell 5.1** (not `pwsh` per project convention).

`-SkipDocker` skips image build and container smoke for faster local iteration; it does not replace the full gate for release/OpenSpec phases.

The smoke also checks authenticated REST (`POST /api/jev/extract`), OpenAPI, and Scalar.

Run tests only:

```bash
dotnet test JevMcp.slnx
```

## Security notes

- MCP access tokens: **hashed at rest**; plaintext shown only at issuance.
- Provider secrets: environment/configuration only; never in audit DB or admin tables.
- Enable `AllowAnonymousMcp` only on trusted local networks; it opens both `/mcp` and `/api/jev`.
- First-time `/setup` is **loopback-only** when no token or admin password is configured.

## License and attribution

[MIT](LICENSE). This repository is a port of [jev-mcp](https://github.com/jkudish/jev-mcp), copyright [Joey Kudish](https://github.com/jkudish), also MIT. This .NET port is maintained by [Marcelo Toledo](mailto:marcelo.toledo@theronsystems.com.br) at TheronSystems. Tool policy and design follow [TypeSafe cookbooks](https://docs.typesafe.ai/cookbooks). The dashboard uses [Apache ECharts 5.6.0](https://github.com/apache/echarts) (Apache-2.0); see NOTICE alongside the asset under `wwwroot/lib/echarts/`.
