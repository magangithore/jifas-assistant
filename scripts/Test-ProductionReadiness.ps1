param(
    [switch]$SkipDocker,
    [switch]$SkipDotNet
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-FileExists {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File wajib tidak ditemukan: $Path"
    }
}

function Assert-JsonFile {
    param([string]$Path)

    Assert-FileExists $Path
    try {
        $null = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        Write-Host "OK JSON: $Path"
    }
    catch {
        throw "JSON tidak valid: $Path. $($_.Exception.Message)"
    }
}

function Assert-PowerShellScriptParses {
    param([string]$Path)

    Assert-FileExists $Path
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        (Resolve-Path -LiteralPath $Path),
        [ref]$tokens,
        [ref]$parseErrors
    ) | Out-Null

    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $messages = ($parseErrors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" }) -join "; "
        throw "PowerShell script tidak valid: $Path. $messages"
    }

    Write-Host "OK PowerShell syntax: $Path"
}

function Assert-DoesNotContainSecret {
    param(
        [string]$Path,
        [string[]]$Patterns
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $content = Get-Content -LiteralPath $Path -Raw
    foreach ($pattern in $Patterns) {
        if ($content -match $pattern) {
            throw "Potensi secret terdeteksi di $Path dengan pattern $pattern"
        }
    }
}

Write-Step "Validasi file penting"
$requiredFiles = @(
    "Dockerfile",
    "docker-compose.yml",
    ".dockerignore",
    ".env.example",
    "Jifas.Assistant/Program.cs",
    "Jifas.Assistant/Database/Initialize-PostgresPgvector.sql",
    "Jifas.Assistant/Jifas.Assistant.csproj",
    "Jifas.Assistant.Tests/Jifas.Assistant.Tests.csproj",
    "docs/POSTGRES_PGVECTOR_RUNBOOK.md",
    "docs/DOCKER_REDIS_CACHE.md",
    "docs/AI_QUALITY_RUNBOOK.md",
    "docs/PRODUCTION_READINESS_REPORT_20260603.md",
    "scripts/Run-FullFeatureSmokeTest.ps1",
    "scripts/Run-ChatStressTest.ps1"
)

foreach ($file in $requiredFiles) {
    Assert-FileExists $file
    Write-Host "OK file: $file"
}

$requiredFiles |
    Where-Object { $_ -like "scripts/*.ps1" } |
    ForEach-Object { Assert-PowerShellScriptParses $_ }

Write-Step "Validasi appsettings JSON"
$jsonFiles = @(
    "Jifas.Assistant/appsettings.json",
    "Jifas.Assistant/appsettings.Development.json",
    "Jifas.Assistant/appsettings.Docker.json",
    "Jifas.Assistant/appsettings.Production.json",
    "KBLoader/appsettings.json",
    "KBLoader/appsettings.Development.json"
)

foreach ($file in $jsonFiles) {
    Assert-JsonFile $file
}

Write-Step "Validasi template environment"
$envExample = Get-Content -LiteralPath ".env.example" -Raw
$requiredEnvKeys = @(
    "ConnectionStrings__DefaultConnection",
    "ConnectionStrings__Redis",
    "Admin__ApiKey",
    "Cors__AllowedOrigins__0",
    "Jwt__Enabled",
    "Jwt__Audience",
    "Caching__UseRedis",
    "Jira__BaseUrl",
    "Jira__ProjectKey",
    "Jira__ApiToken",
    "Jira__EnableOfflineFallback",
    "Jira__BypassSslValidation"
)

foreach ($key in $requiredEnvKeys) {
    if ($envExample -notmatch [regex]::Escape($key)) {
        throw "Key environment wajib belum ada di .env.example: $key"
    }
    Write-Host "OK env key: $key"
}

Write-Step "Secret scan dasar"
$secretPatterns = @(
    "ATATT[0-9A-Za-z_\-=]{20,}",
    "nvapi-[0-9A-Za-z_\-]{20,}",
    "sk-[0-9A-Za-z_\-]{20,}",
    "Jira__ApiToken=(?!replace-with-jira-token)(?!\s*$).{20,}",
    '"ApiToken"\s*:\s*"(?!\s*")[^"]{20,}"'
)

$scanFiles = Get-ChildItem -Recurse -File |
    Where-Object {
        $_.FullName -notmatch "\\(\.git|bin|obj|publish|publish-context|logs|reports|\.vs|\.idea)\\" -and
        $_.FullName -ne $PSCommandPath -and
        ($_.Name -eq ".env.example" -or $_.Name -notlike ".env*") -and
        $_.Name -notmatch "^(TEST_REPORT_|LAPORAN_)" -and
        $_.Extension -notin @(".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".pdf", ".zip")
    }

foreach ($file in $scanFiles) {
    Assert-DoesNotContainSecret $file.FullName $secretPatterns
}
Write-Host "OK secret scan dasar"

if (-not $SkipDocker) {
    Write-Step "Validasi Docker Compose"
    docker compose config | Out-Null
    Write-Host "OK docker compose config"
}

if (-not $SkipDotNet) {
    Write-Step "Validasi .NET build dan test"
    dotnet build --no-restore
    dotnet test --no-build --verbosity normal
}

Write-Step "Production readiness gate selesai"
Write-Host "OK semua validasi production readiness berhasil." -ForegroundColor Green
