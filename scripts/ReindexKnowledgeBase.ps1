param(
    [string]$ConnectionString = "Host=localhost;Port=5432;Database=jifas_assistant;Username=jifas;Password=jifas_dev_password",
    [string]$OllamaBaseUrl = "http://10.0.12.54:11434",
    [string]$EmbeddingModel = "qwen3-embedding:4b",
    [int]$EmbeddingDimensions = 2560,
    [int]$EmbeddingTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$loaderProject = Join-Path $repoRoot "KBLoader/KBLoader.csproj"
$loaderDir = Join-Path $repoRoot "KBLoader"

if (-not (Test-Path $loaderProject)) {
    throw "KBLoader project not found: $loaderProject"
}

$env:ConnectionStrings__DefaultConnection = $ConnectionString
$env:Ollama__BaseUrl = $OllamaBaseUrl
$env:Embedding__Model = $EmbeddingModel
$env:Embedding__Dimensions = [string]$EmbeddingDimensions
$env:Embedding__TimeoutSeconds = [string]$EmbeddingTimeoutSeconds

Write-Host "Reindexing JIFAS knowledge base into PostgreSQL pgvector..." -ForegroundColor Cyan
Write-Host "Database : $ConnectionString" -ForegroundColor DarkGray
Write-Host "Ollama   : $OllamaBaseUrl" -ForegroundColor DarkGray
Write-Host "Model    : $EmbeddingModel ($EmbeddingDimensions dims)" -ForegroundColor DarkGray
Write-Host "Timeout  : $EmbeddingTimeoutSeconds seconds" -ForegroundColor DarkGray

Push-Location $loaderDir
try {
    dotnet run --project $loaderProject -- --yes
}
finally {
    Pop-Location
}
