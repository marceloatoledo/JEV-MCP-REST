<#
.SYNOPSIS
    Repository green gate: build, tests, Docker image, and HTTP smoke.

.DESCRIPTION
    No OpenSpec phase is archived unless this script passes.
    Each phase extends the smoke instead of creating its own check.

.PARAMETER SkipDocker
    Skip the image build and smoke. Fast local use, never to close a phase.
#>
[CmdletBinding()]
param(
    [switch]$SkipDocker
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$imageTag = 'jevmcp:dev'
$composeProject = 'JevMcp-green'
$composeFile = Join-Path $repositoryRoot 'docker-compose.yml'
$hostPort = 8099

function Resolve-Dotnet {
    # Needs a dotnet with SDK 10: global.json pins that version.
    if ($env:JevMcp_DOTNET -and (Test-Path $env:JevMcp_DOTNET)) { return $env:JevMcp_DOTNET }

    $candidates = @()
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $candidates += Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) { continue }
        if ((& $candidate --list-sdks) -match '^10\.') { return $candidate }
    }

    throw 'Nenhum dotnet com SDK 10 encontrado. Instale o SDK 10 ou defina JevMcp_DOTNET.'
}

function Invoke-Native {
    # With ErrorActionPreference Stop, native-command stderr becomes a fatal error whenever
    # the caller redirects the script streams. Docker writes progress to stderr, and
    # removing a missing container does too. The exit code decides whether the step passed.
    param([scriptblock]$Action)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Action }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Compose {
    param([string[]]$ComposeArgs)

    docker compose --project-directory $repositoryRoot -f $composeFile -p $composeProject @ComposeArgs
}

function Remove-SmokeStack {
    Invoke-Native { Invoke-Compose @('down', '-v', '--remove-orphans') *> $null }
}

function Get-SmokeContainerId {
    $id = Invoke-Native { Invoke-Compose @('ps', '-q', 'jevmcp') }
    $id = "$id".Trim()
    if (-not $id) { throw 'Smoke falhou: container compose jevmcp nao encontrado.' }
    return $id
}

function Wait-SmokeHealthy {
    param([string]$ContainerId)

    $status = 'unknown'
    foreach ($attempt in 1..40) {
        Start-Sleep -Seconds 2
        $status = Invoke-Native {
            docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $ContainerId
        }
        $status = "$status".Trim()
        if ($status -eq 'healthy') { return }
        if ($status -eq 'unhealthy') { break }
    }

    Invoke-Native { Invoke-Compose @('logs') }
    throw "Smoke falhou: healthcheck nao ficou healthy (status=$status)."
}

function Assert-AuditContainsScreen {
    param([string]$ContainerId)

    # jev_screen without a credential goes through the client, fails, and writes the row.
    foreach ($attempt in 1..20) {
        Invoke-Native {
            docker exec $ContainerId sh -c 'for f in /app/data/jevmcp.db /app/data/jevmcp.db-wal; do [ -f "$f" ] && grep -a -q -F jev_screen "$f" && exit 0; done; exit 1'
        }
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }

    throw 'Smoke falhou: historico de auditoria nao contem jev_screen.'
}

function Invoke-Mcp {
    # Streamable HTTP transport responds in SSE even for a single result:
    # the JSON-RPC envelope is on the `data:` line.
    param(
        [string]$Body,
        [string]$Token
    )

    $headers = @{
        Accept = 'application/json, text/event-stream'
        Authorization = "Bearer $Token"
    }

    $response = Invoke-WebRequest "http://localhost:$hostPort/mcp" `
        -Method Post `
        -Body $Body `
        -ContentType 'application/json' `
        -Headers $headers `
        -UseBasicParsing `
        -TimeoutSec 20

    $line = $response.Content -split "`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1
    $payload = if ($line) { $line.Substring(6) } else { $response.Content }

    return $payload | ConvertFrom-Json
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    Invoke-Native $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name falhou com codigo $LASTEXITCODE." }
}

$dotnet = Resolve-Dotnet
Write-Host "dotnet: $dotnet"
Push-Location $repositoryRoot

try {
    Invoke-Step 'build' { & $dotnet build JevMcp.slnx -warnaserror --nologo }
    Invoke-Step 'test'  { & $dotnet test  JevMcp.slnx --nologo }

    if ($SkipDocker) {
        Write-Host ''
        Write-Host 'Docker pulado a pedido. Isto NAO fecha uma fase.' -ForegroundColor Yellow
        return
    }

    Invoke-Step 'docker build' { docker build -t $imageTag $repositoryRoot }

    Write-Host ''
    Write-Host '==> smoke' -ForegroundColor Cyan
    Remove-SmokeStack
    $smokeUser = 'jevmcp-admin'
    $smokePassword = 'jevmcp_green_admin_password_rotate_me'
    $env:JEVMCP_HOST_PORT = "$hostPort"
    $env:ACCESSCONTROL__USERNAME = $smokeUser
    $env:ACCESSCONTROL__PASSWORD = $smokePassword
    Invoke-Step 'docker compose up' {
        Invoke-Compose @('up', '-d', '--no-build')
    }

    try {
        $containerId = Get-SmokeContainerId
        Wait-SmokeHealthy $containerId

        $procStatus = Invoke-Native { docker exec $containerId cat /proc/1/status } | Out-String
        if ($procStatus -notmatch '(?m)^Uid:\s+(\d+)') {
            throw "Smoke falhou: nao foi possivel ler o uid do processo.`n$procStatus"
        }

        $uid = $Matches[1]
        if ($uid -eq '0') { throw 'Smoke falhou: o processo do container esta como root.' }

        Write-Host "    healthcheck healthy (uid $uid)" -ForegroundColor Green

        $response = Invoke-WebRequest "http://localhost:$hostPort/health" -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -ne 200) { throw 'Smoke falhou: /health nao respondeu 200.' }

        Write-Host '    /health 200' -ForegroundColor Green

        # The container starts without a provider credential: degraded-with-reason is
        # expected; degraded with no reason means resolution was not reported.
        $health = $response.Content | ConvertFrom-Json
        if (-not $health.status) { throw 'Smoke falhou: /health nao reportou status.' }
        if ($health.status -ne 'ok' -and -not $health.error) {
            throw 'Smoke falhou: /health degradado sem motivo de configuracao.'
        }

        Write-Host "    /health status $($health.status)" -ForegroundColor Green
        if ($response.Content -match [regex]::Escape($smokePassword)) {
            throw 'Smoke falhou: /health revelou a senha do admin.'
        }

        $homePage = (Invoke-WebRequest "http://localhost:$hostPort/" -UseBasicParsing -TimeoutSec 10).Content
        if ($homePage -notmatch 'mud-') { throw 'Smoke falhou: a pagina inicial nao renderizou componentes MudBlazor.' }
        if ($homePage -notmatch 'name="username"') { throw 'Smoke falhou: a raiz nao caiu no login.' }

        Write-Host '    raiz redireciona para o login' -ForegroundColor Green

        $unauthStatus = $null
        try {
            Invoke-WebRequest "http://localhost:$hostPort/mcp" `
                -Method Post `
                -Body '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' `
                -ContentType 'application/json' `
                -Headers @{ Accept = 'application/json, text/event-stream' } `
                -UseBasicParsing `
                -TimeoutSec 20 | Out-Null
        }
        catch {
            $webResponse = $_.Exception.Response
            if ($webResponse) { $unauthStatus = [int]$webResponse.StatusCode }
        }

        if ($unauthStatus -ne 401) {
            throw "Smoke falhou: /mcp sem token deveria responder 401, recebeu $unauthStatus."
        }

        Write-Host '    /mcp sem token: 401' -ForegroundColor Green

        $adminSession = $null
        $loginPage = Invoke-WebRequest "http://localhost:$hostPort/login" -UseBasicParsing -SessionVariable adminSession -TimeoutSec 10
        $antiForgery = $null
        if ($loginPage.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') {
            $antiForgery = $Matches[1]
        }
        elseif ($loginPage.Content -match 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') {
            $antiForgery = $Matches[1]
        }

        if ($loginPage.Content -notmatch 'name="username"') {
            throw 'Smoke falhou: /login nao mostrou o campo de usuario.'
        }

        $loginBody = @{ username = $smokeUser; password = $smokePassword }
        if ($antiForgery) { $loginBody['__RequestVerificationToken'] = $antiForgery }

        Invoke-WebRequest "http://localhost:$hostPort/api/session" `
            -Method Post `
            -Body $loginBody `
            -ContentType 'application/x-www-form-urlencoded' `
            -WebSession $adminSession `
            -UseBasicParsing `
            -TimeoutSec 20 | Out-Null

        $issued = Invoke-WebRequest "http://localhost:$hostPort/api/tokens" `
            -Method Post `
            -Body '{"name":"smoke"}' `
            -ContentType 'application/json' `
            -WebSession $adminSession `
            -UseBasicParsing `
            -TimeoutSec 20
        $smokeToken = ($issued.Content | ConvertFrom-Json).token
        if (-not $smokeToken) { throw 'Smoke falhou: POST /api/tokens nao devolveu o token.' }

        Write-Host '    admin emitiu um token MCP' -ForegroundColor Green

        $tools = (Invoke-Mcp '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' $smokeToken).result.tools.name
        $expectedTools = @(
            'jev_verify', 'jev_screen', 'jev_find', 'jev_classify', 'jev_decide',
            'jev_rerank', 'jev_compare', 'jev_extract', 'jev_review', 'jev_gate'
        )
        foreach ($expected in $expectedTools) {
            if ($tools -notcontains $expected) { throw "Smoke falhou: /mcp nao expos a tool $expected." }
        }

        Write-Host "    /mcp tools/list: $($tools.Count) tools" -ForegroundColor Green

        # A field with no regex match never reaches the model, so this call answers
        # with no credential at all: it is the only path that proves end-to-end containment.
        $extract = Invoke-Mcp '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"jev_extract","arguments":{"document":"nothing here","fields":[{"id":"iban","pattern":"[A-Z]{2}[0-9]{20}","description":"The IBAN"}]}}}' $smokeToken
        if ($extract.result.isError) { throw "Smoke falhou: jev_extract sem casamento deveria responder: $($extract.result.content[0].text)" }

        $extracted = $extract.result.content[0].text | ConvertFrom-Json
        if ($extracted.results[0].status -ne 'not_found') {
            throw "Smoke falhou: jev_extract sem casamento retornou $($extracted.results[0].status)."
        }

        Write-Host '    /mcp jev_extract responde sem consultar o modelo' -ForegroundColor Green

        $restUnauth = $null
        try {
            Invoke-WebRequest "http://localhost:$hostPort/api/jev/screen" `
                -Method Post `
                -Body '{"text":"smoke"}' `
                -ContentType 'application/json' `
                -UseBasicParsing `
                -TimeoutSec 20 | Out-Null
        }
        catch {
            $webResponse = $_.Exception.Response
            if ($webResponse) { $restUnauth = [int]$webResponse.StatusCode }
        }

        if ($restUnauth -ne 401) {
            throw "Smoke falhou: /api/jev sem token deveria responder 401, recebeu $restUnauth."
        }

        Write-Host '    /api/jev sem token: 401' -ForegroundColor Green

        $restHeaders = @{ Authorization = "Bearer $smokeToken" }
        $restExtract = Invoke-WebRequest "http://localhost:$hostPort/api/jev/extract" `
            -Method Post `
            -Body '{"document":"nothing here","fields":[{"id":"iban","pattern":"[A-Z]{2}[0-9]{20}","description":"The IBAN"}]}' `
            -ContentType 'application/json' `
            -Headers $restHeaders `
            -UseBasicParsing `
            -TimeoutSec 20
        $restExtracted = $restExtract.Content | ConvertFrom-Json
        if ($restExtracted.results[0].status -ne 'not_found') {
            throw "Smoke falhou: REST jev_extract sem casamento retornou $($restExtracted.results[0].status)."
        }

        Write-Host '    /api/jev/extract responde sem consultar o modelo' -ForegroundColor Green

        $openApi = (Invoke-WebRequest "http://localhost:$hostPort/openapi/v1.json" -UseBasicParsing -TimeoutSec 10).Content
        foreach ($expected in @('/api/jev/verify', '/api/jev/gate', '/health')) {
            if ($openApi -notmatch [regex]::Escape($expected)) {
                throw "Smoke falhou: OpenAPI nao listou $expected."
            }
        }
        if ($openApi -match [regex]::Escape($smokeToken)) {
            throw 'Smoke falhou: OpenAPI revelou o token MCP.'
        }

        Write-Host '    /openapi/v1.json lista as tools REST' -ForegroundColor Green

        $scalar = Invoke-WebRequest "http://localhost:$hostPort/scalar" -UseBasicParsing -TimeoutSec 10
        if ($scalar.StatusCode -ne 200) { throw 'Smoke falhou: /scalar nao respondeu 200.' }
        if ($scalar.Content -notmatch 'scalar') { throw 'Smoke falhou: /scalar nao serviu a UI Scalar.' }

        Write-Host '    /scalar 200' -ForegroundColor Green

        # Without a credential the call must fail and say why. A generic failure would
        # leave the operator unaware that only provider configuration is missing.
        $call = Invoke-Mcp '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"jev_screen","arguments":{"text":"smoke"}}}' $smokeToken
        if (-not $call.result.isError) { throw 'Smoke falhou: tools/call sem credencial deveria falhar.' }

        $message = $call.result.content[0].text
        if ($message -notmatch 'credential') {
            throw "Smoke falhou: erro de tool sem motivo de configuracao: $message"
        }

        Write-Host '    /mcp tools/call reporta a falta de credencial' -ForegroundColor Green

        Assert-AuditContainsScreen $containerId
        Write-Host '    auditoria gravou jev_screen no volume' -ForegroundColor Green

        $areas = @{
            '/' = 'Painel'
            '/audit' = 'Auditoria'
            '/playground' = 'Playground'
            '/settings' = 'TYPESAFE_API_KEY'
        }
        foreach ($path in $areas.Keys) {
            $page = (Invoke-WebRequest "http://localhost:$hostPort$path" -WebSession $adminSession -UseBasicParsing -TimeoutSec 10).Content
            if ($page -notmatch 'mud-') { throw "Smoke falhou: $path nao renderizou MudBlazor." }
            if ($page -notmatch [regex]::Escape($areas[$path])) { throw "Smoke falhou: $path nao mostrou $($areas[$path])." }
            if ($page -match [regex]::Escape($smokeToken)) { throw "Smoke falhou: $path revelou o token MCP." }
            if ($page -match [regex]::Escape($smokePassword)) { throw "Smoke falhou: $path revelou a senha do admin." }
            if ($path -eq '/' -and $page -notmatch 'data-chart') {
                throw 'Smoke falhou: o painel nao renderizou o host ECharts.'
            }
        }

        Write-Host '    admin painel, auditoria, playground e configuracao renderizados' -ForegroundColor Green

        $adminSession.Cookies.SetCookies(
            [Uri]"http://localhost:$hostPort/",
            'JevMcp.culture=c=en|uic=en')
        $enPanel = (Invoke-WebRequest "http://localhost:$hostPort/" -WebSession $adminSession -UseBasicParsing -TimeoutSec 10).Content
        if ($enPanel -notmatch 'Dashboard') { throw 'Smoke falhou: cookie en nao mostrou Dashboard.' }
        if ($enPanel -match [regex]::Escape('Painel')) { throw 'Smoke falhou: cookie en ainda mostrou Painel.' }
        Write-Host '    admin em ingles com cookie de cultura' -ForegroundColor Green

        Invoke-Step 'docker compose restart' {
            Invoke-Compose @('restart', 'jevmcp')
        }

        $containerId = Get-SmokeContainerId
        Wait-SmokeHealthy $containerId
        Assert-AuditContainsScreen $containerId

        $toolsAfterRestart = (Invoke-Mcp '{"jsonrpc":"2.0","id":4,"method":"tools/list"}' $smokeToken).result.tools.name
        foreach ($expected in $expectedTools) {
            if ($toolsAfterRestart -notcontains $expected) {
                throw "Smoke falhou: depois do restart /mcp nao expos a tool $expected."
            }
        }

        Write-Host '    restart preservou historico e tokens' -ForegroundColor Green
    }
    finally {
        Remove-SmokeStack
    }

    Write-Host ''
    Write-Host 'VERDE' -ForegroundColor Green
}
finally {
    Pop-Location
}

