param(
    [switch]$NoBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$composeArgs = @("--env-file", ".env.docker")
if (Test-Path ".env.docker.local") {
    # File lokal ini berisi secret seperti token Jira dan tidak ikut commit.
    $composeArgs += @("--env-file", ".env.docker.local")
}

function Read-EnvFiles {
    param([string[]]$Paths)

    $values = @{}
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        foreach ($line in Get-Content -LiteralPath $path) {
            $trimmed = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
                continue
            }

            $separator = $trimmed.IndexOf("=")
            if ($separator -le 0) {
                continue
            }

            $key = $trimmed.Substring(0, $separator).Trim()
            $value = $trimmed.Substring($separator + 1).Trim()
            $values[$key] = $value
        }
    }

    return $values
}

function Assert-SecretConfigured {
    param(
        [hashtable]$EnvValues,
        [string]$Key,
        [int]$MinimumLength = 1
    )

    $value = if ($EnvValues.ContainsKey($Key)) { [string]$EnvValues[$Key] } else { "" }
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains("replace-with") -or $value.Length -lt $MinimumLength) {
        throw "Docker production preflight gagal: $Key wajib diisi di .env.docker.local atau secret environment. Nilai secret tidak ditampilkan."
    }
}

$envValues = Read-EnvFiles @(".env.docker", ".env.docker.local")
$environmentName = if ($envValues.ContainsKey("ASPNETCORE_ENVIRONMENT")) { [string]$envValues["ASPNETCORE_ENVIRONMENT"] } else { "Production" }
if ($environmentName -ne "Development") {
    Assert-SecretConfigured $envValues "Admin__ApiKey" 16

    $jwtEnabled = if ($envValues.ContainsKey("Jwt__Enabled")) { [string]$envValues["Jwt__Enabled"] } else { "true" }
    $jwtEnabled = $jwtEnabled.ToLowerInvariant()
    if ($jwtEnabled -eq "true") {
        Assert-SecretConfigured $envValues "Jwt__SigningKey" 32
    }
}

# Test dijalankan sebelum publish agar image Docker tidak membawa build yang rusak.
if (-not $SkipTests) {
    dotnet test --no-restore
}

if (-not $NoBuild) {
    $publishPath = Join-Path $repoRoot "publish"
    if (Test-Path $publishPath) {
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }

    # Dockerfile project ini runtime-only, jadi folder publish harus dibuat dari host.
    dotnet publish .\Jifas.Assistant\Jifas.Assistant.csproj -c Release -o $publishPath
    docker compose @composeArgs build jifas-api
}

# Compose menunggu Postgres dan Redis healthy sebelum API dinyalakan.
docker compose @composeArgs up -d jifas-postgres jifas-redis jifas-api
docker compose @composeArgs ps
