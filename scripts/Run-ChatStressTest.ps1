param(
    [string]$BaseUrl = "http://localhost:8888",
    [int]$VirtualUsers = 50,
    [string]$Question = "Apa itu JIFAS?",
    [string]$OutputDirectory = "reports/stress",
    [int]$TimeoutSeconds = 240,
    [switch]$SkipWarmup
)

$ErrorActionPreference = "Stop"

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = $Values | Sort-Object
    $index = [Math]::Ceiling(($Percentile / 100) * $sorted.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($index, $sorted.Count - 1))
    return [Math]::Round([double]$sorted[$index], 0)
}

function Get-ContainerHealth {
    $names = @("jifas-assistant-api", "jifas-postgres", "jifas-redis")
    $rows = @()

    foreach ($name in $names) {
        try {
            $status = docker inspect --format "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.RestartCount}}" $name 2>$null
            $parts = $status -split "\|"
            $rows += [pscustomobject]@{
                name = $name
                status = $parts[0]
                health = $parts[1]
                restartCount = [int]$parts[2]
            }
        }
        catch {
            $rows += [pscustomobject]@{
                name = $name
                status = "missing"
                health = "missing"
                restartCount = -1
            }
        }
    }

    return $rows
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runId = Get-Date -Format "yyyyMMddHHmmss"
$jsonPath = Join-Path $OutputDirectory "chat-stress-$runId.json"
$mdPath = Join-Path $OutputDirectory "chat-stress-$runId.md"
$endpoint = "$($BaseUrl.TrimEnd('/'))/api/chat/message"

Write-Host "Starting JIFAS chat stress test: $VirtualUsers virtual users -> $endpoint"

$startedAt = Get-Date
$jobs = @()
$warmupResult = $null

if (-not $SkipWarmup) {
    Write-Host "Warming shared response cache before parallel run..."
    $warmupBody = @{
        message = $Question
        userId = "stress-warmup"
        sessionId = "stress-$runId-warmup"
        userRole = "FINA:KI"
        userCompCode = "KI"
        language = "id"
        isFirstMessage = $false
        context = @{
            activeModule = "Home"
            pageTitle = "Stress Warmup"
            currentPage = "/Home"
        }
    } | ConvertTo-Json -Depth 5

    $warmupSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $warmupResponse = Invoke-WebRequest `
            -Method Post `
            -Uri $endpoint `
            -ContentType "application/json" `
            -Body $warmupBody `
            -TimeoutSec $TimeoutSeconds `
            -UseBasicParsing

        $warmupSw.Stop()
        $warmupJson = $warmupResponse.Content | ConvertFrom-Json
        $warmupResult = [pscustomobject]@{
            statusCode = [int]$warmupResponse.StatusCode
            success = if ($null -ne $warmupJson.success) { [bool]$warmupJson.success } else { $false }
            elapsedMs = [int]$warmupSw.ElapsedMilliseconds
            source = if ($warmupJson.source) { [string]$warmupJson.source } else { "" }
            cacheHit = if ($warmupJson.performanceMetrics -and $null -ne $warmupJson.performanceMetrics.wasCacheLit) { [bool]$warmupJson.performanceMetrics.wasCacheLit } else { $false }
            cacheScope = if ($warmupJson.performanceMetrics -and $warmupJson.performanceMetrics.cacheScope) { [string]$warmupJson.performanceMetrics.cacheScope } else { "" }
            error = ""
        }
    }
    catch {
        $warmupSw.Stop()
        $statusCode = 0
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        $warmupResult = [pscustomobject]@{
            statusCode = $statusCode
            success = $false
            elapsedMs = [int]$warmupSw.ElapsedMilliseconds
            source = ""
            cacheHit = $false
            cacheScope = ""
            error = $_.Exception.Message
        }
    }
}

for ($i = 1; $i -le $VirtualUsers; $i++) {
    $vu = $i
    $jobs += Start-Job -ScriptBlock {
        param($Endpoint, $Question, $Vu, $RunId, $TimeoutSeconds)

        $sessionId = "stress-$RunId-vu-$Vu"
        $body = @{
            message = $Question
            userId = "stress-user-$Vu"
            sessionId = $sessionId
            userRole = "FINA:KI"
            userCompCode = "KI"
            language = "id"
            isFirstMessage = $false
            context = @{
                activeModule = "Home"
                pageTitle = "Stress Test"
                currentPage = "/Home"
            }
        } | ConvertTo-Json -Depth 5

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest `
                -Method Post `
                -Uri $Endpoint `
                -ContentType "application/json" `
                -Body $body `
                -TimeoutSec $TimeoutSeconds `
                -UseBasicParsing

            $sw.Stop()
            $json = $null
            try { $json = $response.Content | ConvertFrom-Json } catch { }

            [pscustomobject]@{
                virtualUser = $Vu
                statusCode = [int]$response.StatusCode
                success = if ($null -ne $json.success) { [bool]$json.success } else { $false }
                elapsedMs = [int]$sw.ElapsedMilliseconds
                source = if ($json.source) { [string]$json.source } else { "" }
                confidence = if ($null -ne $json.confidenceScore) { [double]$json.confidenceScore } else { 0 }
                isFromKnowledgeBase = if ($null -ne $json.isFromKnowledgeBase) { [bool]$json.isFromKnowledgeBase } else { $false }
                cacheHit = if ($json.performanceMetrics -and $null -ne $json.performanceMetrics.wasCacheLit) { [bool]$json.performanceMetrics.wasCacheLit } else { $false }
                cacheScope = if ($json.performanceMetrics -and $json.performanceMetrics.cacheScope) { [string]$json.performanceMetrics.cacheScope } else { "" }
                suggestionsMs = if ($json.performanceMetrics -and $null -ne $json.performanceMetrics.suggestionsMs) { [int64]$json.performanceMetrics.suggestionsMs } else { 0 }
                correlationId = if ($json.correlationId) { [string]$json.correlationId } else { "" }
                error = $null
            }
        }
        catch {
            $sw.Stop()
            $statusCode = 0
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }

            [pscustomobject]@{
                virtualUser = $Vu
                statusCode = $statusCode
                success = $false
                elapsedMs = [int]$sw.ElapsedMilliseconds
                source = ""
                confidence = 0
                isFromKnowledgeBase = $false
                cacheHit = $false
                cacheScope = ""
                suggestionsMs = 0
                correlationId = ""
                error = $_.Exception.Message
            }
        }
    } -ArgumentList $endpoint, $Question, $vu, $runId, $TimeoutSeconds
}

$results = Receive-Job -Job $jobs -Wait -AutoRemoveJob
$finishedAt = Get-Date
$durations = @($results | ForEach-Object { [double]$_.elapsedMs })
$containerHealth = @(Get-ContainerHealth)

$monitoringErrorCount = 0
$monitoringTotalCalls = 0
try {
    $monitoring = Invoke-RestMethod -Method Get -Uri "$($BaseUrl.TrimEnd('/'))/api/monitoring/all?minutes=60" -TimeoutSec 20
    if ($monitoring.stats) {
        $monitoringErrorCount = [int]$monitoring.stats.errorCalls
        $monitoringTotalCalls = [int]$monitoring.stats.totalCalls
    }
}
catch {
    Write-Warning "Could not fetch monitoring summary after stress test: $($_.Exception.Message)"
}

$total = $results.Count
$successfulResults = @($results | Where-Object { $_.success -eq $true -and $_.statusCode -ge 200 -and $_.statusCode -lt 300 })
$successCount = $successfulResults.Count
$rateLimited = @($results | Where-Object { $_.statusCode -eq 429 }).Count
$serverErrors = @($results | Where-Object { $_.statusCode -ge 500 }).Count
$clientErrors = @($results | Where-Object { $_.statusCode -ge 400 -and $_.statusCode -lt 500 -and $_.statusCode -ne 429 }).Count
$kbHits = @($results | Where-Object { $_.isFromKnowledgeBase -eq $true }).Count
$cacheHits = @($results | Where-Object { $_.cacheHit -eq $true }).Count
$sharedCacheResponses = @($results | Where-Object { $_.cacheScope -eq "shared" }).Count
$contextualCacheResponses = @($results | Where-Object { $_.cacheScope -eq "contextual" }).Count
$suggestionsTotalMs = @($results | Measure-Object suggestionsMs -Sum).Sum
$avgMs = if ($durations.Count -eq 0) { 0 } else { [Math]::Round(($durations | Measure-Object -Average).Average, 0) }
$maxMs = if ($durations.Count -eq 0) { 0 } else { [Math]::Round(($durations | Measure-Object -Maximum).Maximum, 0) }
$p95Ms = Get-Percentile -Values $durations -Percentile 95
$avgConfidence = if ($results.Count -eq 0) { 0 } else { [Math]::Round((($results | Measure-Object confidence -Average).Average), 4) }
$avgSuccessConfidence = if ($successCount -eq 0) { 0 } else { [Math]::Round((($successfulResults | Measure-Object confidence -Average).Average), 4) }
$sourceGroups = @($results | Group-Object source | Sort-Object Count -Descending | ForEach-Object {
    [pscustomobject]@{ source = if ($_.Name) { $_.Name } else { "(empty)" }; count = $_.Count }
})
$successfulSourceGroups = @($successfulResults | Group-Object source | Sort-Object Count -Descending | ForEach-Object {
    [pscustomobject]@{ source = if ($_.Name) { $_.Name } else { "(empty)" }; count = $_.Count }
})
$sourceStabilityPercent = if ($successCount -eq 0 -or $successfulSourceGroups.Count -eq 0) {
    0
} else {
    [Math]::Round(($successfulSourceGroups[0].count / $successCount) * 100, 1)
}

$allContainersHealthy = @($containerHealth | Where-Object {
    $_.status -ne "running" -or ($_.health -ne "healthy" -and $_.health -ne "none")
}).Count -eq 0
$warmupOk = $SkipWarmup -or ($warmupResult -and $warmupResult.success -eq $true -and $warmupResult.statusCode -ge 200 -and $warmupResult.statusCode -lt 300)
$passed = ($serverErrors -eq 0 -and $rateLimited -eq 0 -and $allContainersHealthy -and $warmupOk)

$summary = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    runId = $runId
    baseUrl = $BaseUrl
    endpoint = $endpoint
    question = $Question
    virtualUsers = $VirtualUsers
    startedAt = $startedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    finishedAt = $finishedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    totalRequests = $total
    successfulResponses = $successCount
    rateLimited429 = $rateLimited
    clientErrorsNon429 = $clientErrors
    serverErrors5xx = $serverErrors
    averageLatencyMs = $avgMs
    p95LatencyMs = $p95Ms
    maxLatencyMs = $maxMs
    kbHits = $kbHits
    cacheHits = $cacheHits
    sharedCacheResponses = $sharedCacheResponses
    contextualCacheResponses = $contextualCacheResponses
    suggestionsTotalMs = $suggestionsTotalMs
    monitoringTotalCallsLast60Minutes = $monitoringTotalCalls
    monitoringErrorCallsLast60Minutes = $monitoringErrorCount
    averageConfidence = $avgConfidence
    averageSuccessConfidence = $avgSuccessConfidence
    successfulSourceStabilityPercent = $sourceStabilityPercent
    warmup = $warmupResult
    sourceDistribution = $sourceGroups
    successfulSourceDistribution = $successfulSourceGroups
    containers = $containerHealth
    warmupOk = $warmupOk
    passed = $passed
    results = @($results | Sort-Object virtualUser)
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

$sourceLines = if ($sourceGroups.Count -gt 0) {
    ($sourceGroups | ForEach-Object { "- $($_.source): $($_.count)" }) -join [Environment]::NewLine
} else {
    "- (no source data)"
}

$successfulSourceLines = if ($successfulSourceGroups.Count -gt 0) {
    ($successfulSourceGroups | ForEach-Object { "- $($_.source): $($_.count)" }) -join [Environment]::NewLine
} else {
    "- (no successful source data)"
}

$containerLines = ($containerHealth | ForEach-Object {
    "- $($_.name): status=$($_.status), health=$($_.health), restarts=$($_.restartCount)"
}) -join [Environment]::NewLine

$markdown = @"
# JIFAS Chat Stress Test

Generated: $($summary.generatedAt)

## Summary

- Virtual users: $VirtualUsers
- Total requests: $total
- Successful responses: $successCount
- HTTP 429 while rate limit disabled: $rateLimited
- HTTP 5xx: $serverErrors
- Non-429 client errors: $clientErrors
- Average latency: $avgMs ms
- P95 latency: $p95Ms ms
- Max latency: $maxMs ms
- KB hits: $kbHits
- Response cache hits: $cacheHits
- Shared cache responses: $sharedCacheResponses
- Contextual cache responses: $contextualCacheResponses
- Suggestion pipeline total time: $suggestionsTotalMs ms
- Monitoring total calls, last 60 minutes: $monitoringTotalCalls
- Monitoring error calls, last 60 minutes: $monitoringErrorCount
- Average confidence, all requests: $avgConfidence
- Average confidence, successful responses: $avgSuccessConfidence
- Successful source stability: $sourceStabilityPercent%
- Passed: $passed

## Warmup

- Enabled: $(-not $SkipWarmup)
- Status: $(if ($warmupResult) { $warmupResult.statusCode } else { "skipped" })
- Success: $(if ($warmupResult) { $warmupResult.success } else { "skipped" })
- Latency: $(if ($warmupResult) { "$($warmupResult.elapsedMs) ms" } else { "skipped" })
- Cache: $(if ($warmupResult) { "$($warmupResult.cacheHit)/$($warmupResult.cacheScope)" } else { "skipped" })

## Source Distribution

$sourceLines

## Successful Source Distribution

$successfulSourceLines

## Container Health After Test

$containerLines

## Acceptance

Stress test passes when warmup succeeds, HTTP 5xx is 0, HTTP 429 is 0 while rate limit is disabled, and API/Postgres/Redis containers remain healthy.

JSON detail: $jsonPath
"@

$markdown | Set-Content -Path $mdPath -Encoding UTF8

Write-Host "Stress summary written:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"

if (-not $passed) {
    throw "Stress test failed: warmupOk=$warmupOk, serverErrors5xx=$serverErrors, rateLimited429=$rateLimited, allContainersHealthy=$allContainersHealthy"
}

Write-Host "Stress test passed."
