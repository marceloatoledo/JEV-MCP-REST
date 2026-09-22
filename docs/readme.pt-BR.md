# JEV-MCP

Servidor HTTP do [Model Context Protocol (MCP)](https://modelcontextprotocol.io) que expõe o modelo **Jev** (System One, TypeSafe) como ferramentas de julgamento tipadas, com interface administrativa Blazor e auditoria de chamadas em SQLite.

Port do [`jkudish/jev-mcp`](https://github.com/jkudish/jev-mcp) (MIT, v0.5.0), que é um servidor Node **stdio**. Este projeto usa transporte **Streamable HTTP**, implementa as ferramentas em C# (.NET 10) e adiciona interface administrativa e histórico persistente.

O Jev não gera texto livre: recebe `state` + `questions` tipados e devolve probabilidades calibradas em dezenas a centenas de milissegundos. As ferramentas **não** são passagem direta da API. Cada uma decompõe o problema em perguntas atômicas, valida a resposta do modelo e aplica política em código (`auto` / `review` / `block` / `escalate`). Saída malformada do modelo é tratada como `invalid_response` (fail-closed).

## Comparação com o original

| | jev-mcp | JEV-MCP |
| --- | --- | --- |
| Transporte | stdio (`npx -y @jkudish/jev-mcp`) | Streamable HTTP em `/mcp` |
| Runtime | Node 20 | .NET 10, imagem Docker de runtime (sem SDK na imagem final) |
| UI administrativa | nenhuma | Blazor Server + MudBlazor (dashboard, auditoria, playground, configurações) |
| Histórico | nenhum | SQLite em volume, retenção configurável |
| Autenticação | variáveis de ambiente do processo (stdio) | Bearer token em `/mcp` e `/api/jev`, cookie na admin |

Os dez nomes de ferramentas, esquemas de entrada e payloads JSON coincidem com o original. Um cliente que já chama `jev_verify` continua chamando `jev_verify` sem alteração.

## Recursos

### Servidor MCP

- **Streamable HTTP** em `POST /mcp` com requisições **stateless** (sem sessão MCP no servidor), permitindo várias réplicas atrás de um balanceador sem sticky sessions.
- **Dez ferramentas somente leitura** (veja [Ferramentas MCP](#ferramentas-mcp)) com o mesmo contrato do jev-mcp.
- **Autenticação Bearer** em `/mcp` e `/api/jev` por padrão; acesso anônimo opcional apenas para experimentos locais.
- **Filtro de auditoria de chamadas** registra cada invocação de ferramenta (latência, tokens, provedor, resultado) no SQLite.
- **Endpoint de saúde** em `GET /health` informando configuração do provedor (`ok` vs `degraded`).

### API REST

As dez ferramentas também estão em `POST /api/jev/{name}` com o mesmo JSON snake_case do MCP. `GET /api/jev/tools` lista-as. Documentação interativa: **Scalar** em `/scalar` (OpenAPI em `/openapi/v1.json`). Mesmo Bearer token que `/mcp`.

### Backends do provedor Jev

Resolução automática quando `JEV__PROVIDER` está ausente ou é `auto`, nesta ordem:

1. **TypeSafe** (API direta, recomendado)
2. **OpenRouter**
3. **Cloudflare Workers AI**
4. **Vercel AI Gateway**
5. **Compatible** (URL POST personalizada)

Defina `JEV__PROVIDER` para forçar um único backend. O alias de modelo padrão é `jev-latest` (`JEV__MCP__MODEL` / `JEV:MCP:MODEL`). Chaves de API do provedor nunca são armazenadas no SQLite, enviadas ao navegador ou escritas em logs.

Política de **retry** configurável para chamadas upstream (`JEV:RETRY` na configuração).

### Interface web administrativa

UI Blazor Interactive Server (autenticação por cookie):

| Rota | Finalidade |
| --- | --- |
| `/` | **Dashboard** — resumo de provedor/modelo, volume de chamadas, latência, uso de tokens, custo estimado, séries temporais ECharts (24h / 7d / 30d), filtros de drill-down na auditoria |
| `/audit` | **Log de auditoria** — grade pesquisável de chamadas MCP (ferramenta, provedor, ação, status, intervalo de tempo), detalhe por linha |
| `/playground` | **Playground** — executar qualquer uma das dez ferramentas pela UI com formulários de exemplo (usa credenciais live do provedor) |
| `/readme` | **Documentação** — documentação localizada do projeto na UI admin (`pt-BR`, `en`, `es`; incorporada no build a partir de `docs/readme.*.md` e `README.md` para inglês; recompile após editar) |
| `/settings` | **Configurações** — visão geral de presença de credenciais (mascaradas), precificação de tokens por provedor (USD por milhão de tokens), dias de retenção, captura opcional de payload, toggle de MCP anônimo, **token de acesso MCP** criar / revogar / reativar / excluir |
| `/scalar` | **Referência da API REST** (Scalar) — OpenAPI interativo dos dez endpoints de julgamento |
| `/login` | Login admin usuário/senha |
| `/setup` | Primeiro token MCP em **loopback** quando não há token nem login admin configurados |
| `/tokens` | Redireciona para `/settings` |

**Idiomas da UI:** português (Brasil) padrão, inglês, espanhol (seletor de idioma no layout). A página **Documentação** (`/readme`) segue a mesma cultura.

**Tema:** alternância entre modo claro/escuro.

### Controle de acesso

- **Admin:** usuário + senha de `appsettings.Development.json` localmente ou `ACCESSCONTROL__*` no Docker/produção. Alias legado: `JEVMCP_ADMIN_*`. JSON versionado deixa senhas vazias.
- **Clientes MCP** e **clientes REST:** **tokens Bearer** de longa duração emitidos em Configurações (ou `POST /api/tokens` autenticado como admin); armazenados como hashes SHA-256; segredo completo exibido apenas uma vez na criação.
- **Segurança na inicialização:** binding em endereço **não loopback** sem token MCP ativo, sem credenciais admin e sem MCP anônimo explícito faz a inicialização **falhar** (Docker espera login admin ou token antes de expor a porta).
- Em **loopback**, o app pode iniciar sem credenciais; crie o primeiro token via `/setup` ou configure credenciais admin e emita tokens em Configurações.

### Auditoria de chamadas (SQLite)

- Caminho padrão do banco: `data/jevmcp.db` localmente; Docker Compose: `/app/data/jevmcp.db` no container → `./data/jevmcp.db` no host.
- **Retenção** (`CALLAUDIT__RETENTIONDAYS`, padrão 30); `0` desativa a purga.
- **Captura opcional de payload** (`CALLAUDIT__CAPTUREPAYLOADS`, padrão desligado) com limite de tamanho (`CALLAUDIT__PAYLOADMAXCHARS`, padrão 4096).
- **Grade de precificação de tokens** em Configurações: adicionar, atualizar e excluir provedores. Taxas persistem no SQLite (seed inicial `0.042`; provedores desconhecidos usam esse valor).

### Implantação

- **Docker Compose** com health check, volume nomeado para dados, usuário de runtime não root após entrypoint.
- **Dockerfile multi-stage** (build .NET 10 SDK, runtime aspnet).

## Estrutura da solução

| Projeto | Papel |
| --- | --- |
| `JevMcp.App` | Host ASP.NET Core, UI Blazor, endpoint HTTP MCP, middleware de auth |
| `JevMcp.Tools` | Tipos de ferramentas MCP e serviços de julgamento |
| `JevMcp.Providers` | Clientes upstream Jev (TypeSafe, OpenRouter, Cloudflare, Vercel, compatible) |
| `JevMcp.Core` | Tipos compartilhados, JSON, validação |
| `JevMcp.Data` | Esquema SQLite, tokens de acesso, log de chamadas, configurações operacionais |
| `JevMcp.Tests` | Testes unitários e de integração |

Arquivo de solução: `JevMcp.slnx`. Versão do SDK fixada em `global.json` (.NET 10).

## Ferramentas MCP

Limites (`auto_accept`, `block_at`, `review_at`, cortes de existência) são **pontos de partida** dos [cookbooks TypeSafe](https://docs.typesafe.ai/cookbooks). Ajuste-os nos seus dados antes de usá-los como gates de produção. Veja [como o TypeSafe reporta confiança](https://docs.typesafe.ai/confidence.md).

| Ferramenta | Resumo | Padrão cookbook |
| --- | --- | --- |
| `jev_verify` | Verificar cada afirmação contra evidência; veredito por afirmação (`verified` \| `contradicted` \| `unsupported`), distribuição, confiança, auto vs review | [Citation check](https://docs.typesafe.ai/cookbooks/citation_check) |
| `jev_screen` | Filtrar texto externo antes do contexto do agente: probabilidade de injeção, substância, relevância opcional; recomendação `pass` \| `review` \| `block` \| `skip` | [LLM guardrails](https://docs.typesafe.ai/cookbooks/llm_guardrails) |
| `jev_find` | Escolha semântica do(s) melhor(es) candidato(s) para uma consulta (sem embeddings); inclui se algum candidato realmente responde à consulta | [Semantic find](https://docs.typesafe.ai/cookbooks/semantic_find) |
| `jev_rerank` | Pontuar e ordenar todos os candidatos por relevância em uma requisição | [Rerank](https://docs.typesafe.ai/cookbooks/rerank_typesafe) |
| `jev_classify` | Atribuir em lote itens a um catálogo de rótulos compartilhado com auto vs review (margem + probabilidade top) | — |
| `jev_decide` | Decisão limitada entre 2–6 alternativas com evidência, prioridades, requisitos opcionais, escape hatches | — |
| `jev_compare` | Relação entre dois trechos (`same_fact`, `contradicts`, `different_facts`); julgamentos opcionais por aspecto | — |
| `jev_extract` | Regex fornecida pelo chamador encontra candidatos; Jev escolhe a correspondência verdadeira; valores devolvidos **literalmente** do documento | — |
| `jev_review` | Pontuar patch/diff proposto contra o pedido (rubrica, `safe_to_apply`, `auto` \| `review` \| `escalate`) | — |
| `jev_gate` | `jev_review` mais verificação de afirmações de conclusão contra evidência em uma chamada | — |

### Limites (selecionados)

- `jev_find` / `jev_rerank`: até **250** candidatos; texto truncado em **2000** caracteres por candidato.
- `jev_classify`: até **64** itens por chamada.
- `jev_decide`: **2–6** candidatos; até **3** requisitos.
- `jev_compare`: trechos até **20.000** caracteres cada.
- `jev_extract`: documento até **50.000** caracteres; até **32** campos.
- `jev_review` / `jev_gate`: diff/testes até **50.000** caracteres; evidência do gate limitada a **16** itens e **200.000** caracteres no total.

Nomes de parâmetros nos esquemas das ferramentas usam **snake_case** (ex.: `auto_accept`, `top_k`) para coincidir com o contrato original jev-mcp.

## Executar com Docker

A imagem usa **Dockerfile** multi-stage (build com SDK → runtime `aspnet`, porta **10022**). O [Docker Compose v2](https://docs.docker.com/compose/) compila essa imagem e sobe o app com volume SQLite e health check.

### Pré-requisitos

- **Docker Engine** com **Compose v2** (`docker compose version`).
- Diretório raiz do repositório (onde estão `Dockerfile` e `docker-compose.yml`).

### 1. Criar o `.env`

O Compose lê `.env` para interpolação e repassa chaves de provedor/admin ao container (arquivo no `.gitignore`; não commite).

**Linux / macOS / Git Bash:**

```bash
cp .env.example .env
```

**Windows PowerShell:**

```powershell
Copy-Item .env.example .env
```

Edite `.env` e defina no mínimo:

| Variável | Finalidade |
| --- | --- |
| `ACCESSCONTROL__USERNAME` | Login da UI admin |
| `ACCESSCONTROL__PASSWORD` | Senha da UI admin |
| `TYPESAFE__API__KEY` (ou outro bloco de provedor) | Pelo menos uma credencial do backend Jev |

Opcional: `JEVMCP_HOST_PORT` (padrão **10022** no host). As chaves seguem `SECTION__KEY` como em [`.env.example`](.env.example) e no `appsettings.Development.json` — você pode copiar valores do JSON de Development para o `.env` no Docker.

### 2. Build e subida

```bash
docker compose up --build -d
```

Isso gera a imagem `jevmcp:dev`, publica `http://localhost:10022` (ou seu `JEVMCP_HOST_PORT`) e monta a pasta do host **`./data`** em `/app/data` (SQLite: `./data/jevmcp.db`).

Somente build da imagem (sem container):

```bash
docker build -t jevmcp:dev .
```

### 3. Verificar

```bash
docker compose ps
curl -fsS http://localhost:10022/health
docker compose logs -f jevmcp
```

Aguarde o serviço **healthy** (`GET /health` com 200). O processo escuta em `0.0.0.0:10022` no container e recusa bind público sem login admin, token MCP ativo ou MCP anônimo habilitado.

**UI admin:** `http://localhost:10022` — entre com credenciais do `.env`. Emita tokens MCP em **Configurações** e use-os no cliente MCP.

### Persistência e ciclo de vida

- **SQLite em disco:** no Compose, `CALLAUDIT__DATABASEPATH` é `/app/data/jevmcp.db` no container, espelhado em **`./data/jevmcp.db` no host** (bind mount). Rebuild da imagem (`docker compose build`, `up --build`) **não** apaga essa pasta; só apagar `./data` no host (ou trocar o mount no `docker-compose.yml`) perde o histórico.
- Opcional: volume Docker nomeado fixo em vez de `./data` — veja o comentário no fim de `docker-compose.yml` (`jevmcp_sqlite_data`).
- Após mudar código: `docker compose up --build -d`.

```bash
docker compose down          # para containers; ./data/jevmcp.db permanece no host
docker compose down -v       # só remove volumes nomeados (não usado no bind padrão ./data)
```

Porta no host padrão **10022** (`JEVMCP_HOST_PORT` no `.env`; porta no container sempre **10022**).

## Executar sem Docker

Instale o **.NET 10 SDK**. `dotnet run` usa **Development** e carrega `appsettings.Development.json` (mesmas chaves maiúsculas que `.env.example`, segredos vazios no git). Preencha `USERNAME`, `PASSWORD` e chaves de provedor lá, ou use user secrets. `dotnet run` **não** lê `.env`.

```bash
# Edite src/JevMcp.App/appsettings.Development.json, depois:
dotnet run --project src/JevMcp.App/JevMcp.App.csproj --launch-profile https
```

Em loopback sem credenciais admin, crie o primeiro token MCP em `/setup`.

O perfil **`https`** escuta em `https://localhost:7201` (e `http://localhost:5246`). O perfil **`http`** é só HTTP em `http://localhost:5246`. Caminho SQLite padrão: `data/jevmcp.db` relativo à content root.

## Configuração do cliente MCP

Substitua o servidor stdio original por esta URL. `Authorization` é obrigatório salvo MCP anônimo habilitado.

**Cursor** (`.cursor/mcp.json` ou `~/.cursor/mcp.json`):

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

Use `https://localhost:7201/mcp` localmente com `--launch-profile https`, ou `http://localhost:5246/mcp` com `--launch-profile http`.

Qualquer cliente Streamable HTTP:

```http
POST /mcp HTTP/1.1
Host: localhost:10022
Accept: application/json, text/event-stream
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here
```

(Em dev local com HTTPS, use `Host: localhost:7201` e TLS.)

As dez ferramentas devem aparecer em `tools/list` com os mesmos payloads do jev-mcp.

Os mesmos payloads também estão disponíveis como HTTP JSON:

```http
POST /api/jev/verify HTTP/1.1
Host: localhost:10022
Content-Type: application/json
Authorization: Bearer jevmcp_your_token_here

{"claims":["The sky is blue."],"evidence":"The sky appears blue during the day."}
```

Referência interativa: `http://localhost:10022/scalar` com Docker, ou `https://localhost:7201/scalar` localmente com perfil `https` (OpenAPI em `/openapi/v1.json`).

Não faça commit de tokens completos no git. No Cursor você pode usar interpolação de ambiente, ex.: `Bearer ${env:JEVMCP_TOKEN}`.

## API HTTP

| Método | Caminho | Auth | Finalidade |
| --- | --- | --- | --- |
| `GET` | `/health` | nenhuma | Liveness e status do provedor |
| `GET` | `/scalar` | nenhuma | Referência OpenAPI Scalar |
| `GET` | `/openapi/v1.json` | nenhuma | Documento OpenAPI |
| `GET` | `/api/jev/tools` | Bearer (salvo anônimo) | Catálogo das dez ferramentas |
| `POST` | `/api/jev/verify` | Bearer (salvo anônimo) | `jev_verify` |
| `POST` | `/api/jev/screen` | Bearer (salvo anônimo) | `jev_screen` |
| `POST` | `/api/jev/find` | Bearer (salvo anônimo) | `jev_find` |
| `POST` | `/api/jev/classify` | Bearer (salvo anônimo) | `jev_classify` |
| `POST` | `/api/jev/decide` | Bearer (salvo anônimo) | `jev_decide` |
| `POST` | `/api/jev/rerank` | Bearer (salvo anônimo) | `jev_rerank` |
| `POST` | `/api/jev/compare` | Bearer (salvo anônimo) | `jev_compare` |
| `POST` | `/api/jev/extract` | Bearer (salvo anônimo) | `jev_extract` |
| `POST` | `/api/jev/review` | Bearer (salvo anônimo) | `jev_review` |
| `POST` | `/api/jev/gate` | Bearer (salvo anônimo) | `jev_gate` |
| `POST` | `/mcp` | Bearer (salvo anônimo) | MCP Streamable HTTP |
| `POST` | `/api/session` | anônimo | Login admin (formulário) |
| `POST` | `/api/session/logout` | cookie | Logout admin |
| `POST` | `/api/setup` | anônimo (setup loopback) | Criar o primeiro token MCP |
| `POST` | `/api/tokens` | cookie admin | Emitir token MCP (segredo retornado uma vez) |
| `POST` | `/api/culture` | anônimo | Definir cookie de cultura da UI |

MCP, REST, `/health`, `/scalar` e `/openapi` retornam códigos de status adequados e `WWW-Authenticate` em 401; não são reescritos para páginas de erro HTML.

## Variáveis de ambiente

Precedência para auto-detecção de provedor (`JEV__PROVIDER` não definido ou `auto`): **TypeSafe → OpenRouter → Cloudflare → Vercel → compatible**. `JEV__PROVIDER` força um provedor. Modelo padrão: `jev-latest` (`JEV__MCP__MODEL`).

| Variável | Padrão | Finalidade |
| --- | --- | --- |
| `ACCESSCONTROL__USERNAME` | (vazio) | Usuário admin. Igual a `ACCESSCONTROL:USERNAME` (JSON Development ou env). |
| `ACCESSCONTROL__PASSWORD` | (vazio) | Senha admin. Nunca faça commit de valores reais. |
| `JEVMCP_ADMIN_USERNAME` | (vazio) | Alias legado quando `ACCESSCONTROL__USERNAME` não está definido. |
| `JEVMCP_ADMIN_PASSWORD` | (vazio) | Alias legado quando `ACCESSCONTROL__PASSWORD` não está definido. |
| `ACCESSCONTROL__ALLOWANONYMOUSMCP` | `false` | Permitir `/mcp` e `/api/jev` sem Bearer. Não use em produção. |
| `JEV__PROVIDER` | `auto` | `typesafe`, `openrouter`, `cloudflare`, `vercel`, `compatible` ou `auto`. Igual a `JEV:PROVIDER`. Legado: `JEV_PROVIDER`. |
| `JEV__MCP__MODEL` | `jev-latest` | Id do modelo Jev (OpenRouter não tem alias `latest`). Igual a `JEV:MCP:MODEL`. Legado: `JEV_MCP_MODEL`. |
| `TYPESAFE__API__KEY` | | Chave API direta TypeSafe (`TYPESAFE:API:KEY`). Legado: `TYPESAFE_API_KEY`, `JEV_TYPESAFE_API_KEY`. |
| `TYPESAFE__BASEURL` | `https://api.typesafe.ai` | Apenas origem; `/v1/systemone` é anexado. Legado: `TYPESAFE_BASE_URL`. |
| `OPENROUTER__API__KEY` | | Chave OpenRouter (deve começar com `sk-or-`). Legado: `OPENROUTER_API_KEY`, `JEV_OPENROUTER_API_KEY`. |
| `CLOUDFLARE__API__TOKEN` | | Token API Cloudflare. Legado: `CLOUDFLARE_API_TOKEN`, `JEV_CLOUDFLARE_API_TOKEN`. |
| `CLOUDFLARE__ACCOUNT__ID` | | Id da conta Cloudflare. Legado: `CLOUDFLARE_ACCOUNT_ID`, `JEV_CLOUDFLARE_ACCOUNT_ID`. |
| `AIGATEWAY__API__KEY` | | Vercel AI Gateway. Legado: `AI_GATEWAY_API_KEY`, `JEV_AI_GATEWAY_API_KEY`. |
| `AIGATEWAY__BASEURL` | | Origem opcional do gateway. Legado: `AI_GATEWAY_BASE_URL`. |
| `COMPATIBLE__API__KEY` | | Chave API do endpoint compatible. Legado: `JEV_API_KEY`. |
| `COMPATIBLE__BASEURL` | | URL POST completa para modo compatible (usada literalmente). Legado: `JEV_API_BASE_URL`. |
| `CALLAUDIT__DATABASEPATH` | `data/jevmcp.db` | Caminho SQLite (`/app/data/jevmcp.db` no Compose). |
| `CALLAUDIT__CAPTUREPAYLOADS` | `false` | Persistir corpos de request/response na auditoria. |
| `CALLAUDIT__PAYLOADMAXCHARS` | `4096` | Máximo de texto de payload capturado. |
| `CALLAUDIT__RETENTIONDAYS` | `30` | Excluir linhas mais antigas; `0` desativa. |
| `JEVMCP_HOST_PORT` | `10022` | Mapeamento de porta no host (apenas Compose; mapeia para **10022** no container). |
| `ASPNETCORE_URLS` | `http://+:10022` na imagem | Endereços de bind. |

Lista de referência comentada: [`.env.example`](.env.example).

**Dev local:** `appsettings.Development.json` contém a árvore de chaves maiúsculas (`ACCESSCONTROL`, `JEV`, `TYPESAFE`, …) com segredos vazios no git. **Docker/produção:** `appsettings.json` traz apenas defaults compartilhados (ex.: `CALLAUDIT`); provedor e admin vêm de env vars `SECTION__KEY` (veja `.env.example`) e nomes legados jev-mcp após bind. Precificação de tokens é editada em Configurações e armazenada no SQLite.

## Desenvolvimento

Gate verde do repositório (build, testes, imagem Docker, smoke HTTP incluindo Compose healthy, `tools/list` autenticado e persistência após restart):

```powershell
./scripts/green.ps1
```

Requer **.NET 10 SDK** no PATH (ou `JEVMCP_DOTNET`). No Windows, execute no **Windows PowerShell 5.1** (não `pwsh`, conforme convenção do projeto).

`-SkipDocker` pula build de imagem e smoke de container para iteração local mais rápida; não substitui o gate completo para release/fases OpenSpec.

O smoke também verifica REST autenticado (`POST /api/jev/extract`), OpenAPI e Scalar.

Apenas testes:

```bash
dotnet test JevMcp.slnx
```

## Notas de segurança

- Tokens de acesso MCP: **hash em repouso**; texto plano exibido apenas na emissão.
- Segredos de provedor: apenas ambiente/configuração; nunca no DB de auditoria ou tabelas admin.
- Habilite `AllowAnonymousMcp` apenas em redes locais confiáveis; abre `/mcp` e `/api/jev`.
- `/setup` inicial é **somente loopback** quando não há token nem senha admin configurados.

## Licença e atribuição

[MIT](LICENSE). Este repositório é um port de [jev-mcp](https://github.com/jkudish/jev-mcp), copyright [Joey Kudish](https://github.com/jkudish), também MIT. Este port .NET é mantido por [Marcelo Toledo](mailto:marcelo.toledo@theronsystems.com.br) na TheronSystems. Política e design das ferramentas seguem [cookbooks TypeSafe](https://docs.typesafe.ai/cookbooks). O dashboard usa [Apache ECharts 5.6.0](https://github.com/apache/echarts) (Apache-2.0); veja NOTICE junto ao asset em `wwwroot/lib/echarts/`.
