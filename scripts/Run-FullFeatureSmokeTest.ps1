param(
    [string]$BaseUrl = "http://localhost:8888",
    [string]$OutputDirectory = "reports/functional",
    [int]$TimeoutSeconds = 240,
    [switch]$CreateRealJiraTicket,
    [string]$ExistingJiraTicketNumber = "",
    [string]$ExistingJiraTicketUrl = "",
    [string]$ExistingJiraTicketStatus = "Open"
)

$ErrorActionPreference = "Stop"

function New-ChatBody {
    param(
        [string]$Message,
        [string]$UserId,
        [string]$SessionId,
        [string]$Module = "Home",
        [string]$PageTitle = "Smoke Test",
        [string]$CurrentPage = "/Home"
    )

    return @{
        message = $Message
        userId = $UserId
        sessionId = $SessionId
        userRole = "FINA:KI"
        userCompCode = "KI"
        language = "id"
        isFirstMessage = $false
        context = @{
            activeModule = $Module
            pageTitle = $PageTitle
            currentPage = $CurrentPage
        }
    } | ConvertTo-Json -Depth 6
}

function Add-Result {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [string]$Type,
        [bool]$Passed,
        [int]$StatusCode,
        [int64]$ElapsedMs,
        [object]$Data,
        [string]$ErrorMessage,
        [string]$Notes
    )

    $ticket = if ($Data -and $Data.ticket) { $Data.ticket } else { $null }
    $metrics = if ($Data -and $Data.performanceMetrics) { $Data.performanceMetrics } else { $null }

    $Results.Add([pscustomobject]@{
        name = $Name
        type = $Type
        passed = $Passed
        statusCode = $StatusCode
        elapsedMs = $ElapsedMs
        source = if ($Data -and $Data.source) { [string]$Data.source } else { "" }
        success = if ($Data -and $null -ne $Data.success) { [bool]$Data.success } else { $null }
        isFromKnowledgeBase = if ($Data -and $null -ne $Data.isFromKnowledgeBase) { [bool]$Data.isFromKnowledgeBase } else { $null }
        confidence = if ($Data -and $null -ne $Data.confidenceScore) { [double]$Data.confidenceScore } else { $null }
        cacheHit = if ($metrics -and $null -ne $metrics.wasCacheLit) { [bool]$metrics.wasCacheLit } else { $null }
        cacheScope = if ($metrics -and $metrics.cacheScope) { [string]$metrics.cacheScope } else { "" }
        ticketNumber = if ($ticket -and $ticket.ticketNumber) { [string]$ticket.ticketNumber } else { "" }
        ticketStatus = if ($ticket -and $ticket.status) { [string]$ticket.status } else { "" }
        ticketUrl = if ($ticket -and $ticket.url) { [string]$ticket.url } else { "" }
        correlationId = if ($Data -and $Data.correlationId) { [string]$Data.correlationId } else { "" }
        error = $ErrorMessage
        notes = $Notes
    }) | Out-Null
}

function Invoke-EndpointCheck {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [string]$Path
    )

    $uri = "$($BaseUrl.TrimEnd('/'))$Path"
    Write-Host "[$Name] GET $Path"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Method Get -Uri $uri -UseBasicParsing -TimeoutSec 30
        $sw.Stop()
        Add-Result $Results $Name "endpoint" ($response.StatusCode -eq 200) ([int]$response.StatusCode) $sw.ElapsedMilliseconds $null "" "Endpoint reachable"
    }
    catch {
        $sw.Stop()
        $code = if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { [int]$_.Exception.Response.StatusCode } else { 0 }
        Add-Result $Results $Name "endpoint" $false $code $sw.ElapsedMilliseconds $null $_.Exception.Message "Endpoint failed"
    }
}

function Invoke-ChatCheck {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [string]$Message,
        [string]$UserId,
        [string]$SessionId,
        [scriptblock]$Assert,
        [string]$Module = "Home",
        [string]$PageTitle = "Smoke Test",
        [string]$CurrentPage = "/Home"
    )

    $uri = "$($BaseUrl.TrimEnd('/'))/api/chat/message"
    Write-Host "[$Name] CHAT $Message"
    $body = New-ChatBody -Message $Message -UserId $UserId -SessionId $SessionId -Module $Module -PageTitle $PageTitle -CurrentPage $CurrentPage
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Method Post -Uri $uri -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec $TimeoutSeconds
        $sw.Stop()
        $data = $response.Content | ConvertFrom-Json
        $assertResult = if ($Assert) { & $Assert $data } else { [pscustomobject]@{ passed = $true; note = "Chat response received" } }
        Add-Result $Results $Name "chat" ([bool]$assertResult.passed) ([int]$response.StatusCode) $sw.ElapsedMilliseconds $data "" ([string]$assertResult.note)
        return $data
    }
    catch {
        $sw.Stop()
        $code = if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { [int]$_.Exception.Response.StatusCode } else { 0 }
        Add-Result $Results $Name "chat" $false $code $sw.ElapsedMilliseconds $null $_.Exception.Message "Chat request failed"
        return $null
    }
}

function Invoke-JsonPostCheck {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [string]$Path,
        [object]$Body,
        [scriptblock]$Assert
    )

    $uri = "$($BaseUrl.TrimEnd('/'))$Path"
    Write-Host "[$Name] POST $Path"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Method Post -Uri $uri -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 6) -UseBasicParsing -TimeoutSec $TimeoutSeconds
        $sw.Stop()
        $data = $response.Content | ConvertFrom-Json
        $assertResult = if ($Assert) { & $Assert $data } else { [pscustomobject]@{ passed = $true; note = "JSON response received" } }
        Add-Result $Results $Name "endpoint" ([bool]$assertResult.passed) ([int]$response.StatusCode) $sw.ElapsedMilliseconds $data "" ([string]$assertResult.note)
        return $data
    }
    catch {
        $sw.Stop()
        $code = if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { [int]$_.Exception.Response.StatusCode } else { 0 }
        Add-Result $Results $Name "endpoint" $false $code $sw.ElapsedMilliseconds $null $_.Exception.Message "JSON endpoint failed"
        return $null
    }
}

function Get-ContainerHealth {
    $names = @("jifas-assistant-api", "jifas-postgres", "jifas-redis")
    return @($names | ForEach-Object {
        $raw = docker inspect --format "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.RestartCount}}" $_ 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
            [pscustomobject]@{ name = $_; status = "missing"; health = "missing"; restartCount = -1 }
        }
        else {
            $parts = $raw -split "\|"
            [pscustomobject]@{ name = $_; status = $parts[0]; health = $parts[1]; restartCount = [int]$parts[2] }
        }
    })
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$runId = Get-Date -Format "yyyyMMddHHmmss"
$jsonPath = Join-Path $OutputDirectory "full-feature-smoke-$runId.json"
$mdPath = Join-Path $OutputDirectory "full-feature-smoke-$runId.md"
$results = New-Object System.Collections.Generic.List[object]
$userBase = "enterprise-smoke-$runId"

$kbAssert = { param($d) [pscustomobject]@{ passed = ($d.success -eq $true -and $d.isFromKnowledgeBase -eq $true); note = "source=$($d.source), kb=$($d.isFromKnowledgeBase)" } }
$successAssert = { param($d) [pscustomobject]@{ passed = ($d.success -eq $true); note = "source=$($d.source)" } }
$outAssert = { param($d) [pscustomobject]@{ passed = ($d.success -eq $true -and ([string]$d.source) -match "Out of Scope"); note = "source=$($d.source)" } }
$validationAssert = { param($d) [pscustomobject]@{ passed = ($d.success -eq $false -and ([string]$d.source) -match "Input Validation"); note = "expected validation rejection, source=$($d.source)" } }
$cacheAssert = { param($d) [pscustomobject]@{ passed = ($d.success -eq $true -and $d.performanceMetrics.wasCacheLit -eq $true -and $d.performanceMetrics.cacheScope -eq "shared"); note = "cache=$($d.performanceMetrics.wasCacheLit)/$($d.performanceMetrics.cacheScope)" } }

Invoke-EndpointCheck $results "health-api" "/health"
Invoke-EndpointCheck $results "health-chat" "/api/chat/health"
Invoke-EndpointCheck $results "health-kb-search" "/api/KnowledgeBaseSearch/health"
Invoke-EndpointCheck $results "monitoring-dashboard" "/monitoring/index.html"
Invoke-EndpointCheck $results "kb-stats" "/api/kb/stats"

Invoke-ChatCheck $results "kb-overview" "Apa itu JIFAS?" "$userBase-a" "smoke-$runId-kb-a" $kbAssert | Out-Null
Invoke-ChatCheck $results "kb-overview-cache-repeat" "Apa itu JIFAS?" "$userBase-b" "smoke-$runId-kb-b" $cacheAssert | Out-Null
Invoke-ChatCheck $results "knowledge-invoice" "Bagaimana cara membuat invoice di JIFAS?" "$userBase" "smoke-$runId-invoice" $kbAssert "Invoice" "Invoice Entry" "/Invoice/Create" | Out-Null
Invoke-ChatCheck $results "knowledge-pum" "Apa itu PUM dan bagaimana alurnya?" "$userBase" "smoke-$runId-pum" $successAssert "PUM" "PUM" "/PUM" | Out-Null
Invoke-ChatCheck $results "knowledge-payment" "Bagaimana alur payment invoice?" "$userBase" "smoke-$runId-payment" $successAssert "Payment" "Payment" "/Payment" | Out-Null
Invoke-ChatCheck $results "knowledge-report" "Report Daily Cashflow dipakai untuk apa?" "$userBase" "smoke-$runId-report" $successAssert "Report" "Daily Cashflow" "/Report/DailyCashflow" | Out-Null
Invoke-ChatCheck $results "page-navigation" "Tombol approve invoice ada di page mana?" "$userBase" "smoke-$runId-page" $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
Invoke-ChatCheck $results "follow-up-context" "Status apa yang biasanya muncul di halaman itu?" "$userBase" "smoke-$runId-page" $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
Invoke-ChatCheck $results "out-of-scope" "Siapa presiden Amerika sekarang?" "$userBase" "smoke-$runId-oos" $outAssert | Out-Null
Invoke-ChatCheck $results "greeting" "Halo" "$userBase" "smoke-$runId-greeting" $successAssert | Out-Null
Invoke-ChatCheck $results "gratitude" "Terima kasih ya" "$userBase" "smoke-$runId-gratitude" $successAssert | Out-Null
Invoke-ChatCheck $results "input-validation-sql" "Apa itu JIFAS?'; DROP TABLE KnowledgeBaseDocuments; --" "$userBase" "smoke-$runId-validation" $validationAssert | Out-Null

if ($CreateRealJiraTicket -and -not [string]::IsNullOrWhiteSpace($ExistingJiraTicketNumber)) {
    $existingTicketData = [pscustomobject]@{
        success = $true
        source = "Jira Existing Validation"
        ticket = [pscustomobject]@{
            ticketNumber = $ExistingJiraTicketNumber
            status = $ExistingJiraTicketStatus
            url = $ExistingJiraTicketUrl
        }
    }

    Add-Result $results "jira-real-existing-ticket" "chat" $true 200 0 $existingTicketData "" "Existing real Jira ticket reused to avoid duplicate test ticket"
}
elseif ($CreateRealJiraTicket) {
    $ticketSession = "smoke-$runId-jira-real"
    $ticketProblem = "Tolong buat tiket karena [TEST] JIFAS Assistant Enterprise Readiness - tombol approve invoice tidak bisa diklik saat validasi produksi. Tiket ini otomatis untuk validasi integrasi Jira dan boleh ditutup setelah diverifikasi."
    Invoke-ChatCheck $results "jira-real-start" $ticketProblem "$userBase" $ticketSession $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
    $titlePreview = Invoke-ChatCheck $results "jira-real-confirm-create" "Ya, buatkan tiket" "$userBase" $ticketSession $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval"

    if ($titlePreview -and ([string]$titlePreview.message) -notmatch "\[TEST\]") {
        Invoke-ChatCheck $results "jira-real-change-title" "Ubah judul menjadi [TEST] JIFAS Assistant Enterprise Readiness - Approve Invoice" "$userBase" $ticketSession $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
    }

    $realTicketAssert = {
        param($d)
        $ticket = $d.ticket
        $ticketNumber = if ($ticket -and $ticket.ticketNumber) { [string]$ticket.ticketNumber } else { "" }
        $isReal = $ticketNumber -match "^[A-Z]+-\d+$" -and $ticketNumber -notmatch "^OFFLINE-"
        [pscustomobject]@{
            passed = ($d.success -eq $true -and $null -ne $ticket -and $isReal)
            note = "ticket=$ticketNumber, status=$($ticket.status), url=$($ticket.url)"
        }
    }
    Invoke-ChatCheck $results "jira-real-final-create" "Ya, buat tiketnya" "$userBase" $ticketSession $realTicketAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
}
else {
    $ticketSession = "smoke-$runId-ticket-cancel"
    Invoke-ChatCheck $results "ticket-cancel-start" "Buat tiket" "$userBase" $ticketSession $successAssert "Ticket Test" "Ticket Test" "/TicketTest" | Out-Null
    Invoke-ChatCheck $results "ticket-cancel-detail" "Tombol approve invoice tidak bisa diklik saat test monitoring. Ini hanya validasi flow, jangan dibuat final." "$userBase" $ticketSession $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
    Invoke-ChatCheck $results "ticket-cancel-final" "Batal, gajadi buat tiket" "$userBase" $ticketSession $successAssert "Invoice" "Invoice Approval" "/Invoice/Approval" | Out-Null
}

Invoke-JsonPostCheck $results "kb-hybrid-query" "/api/KnowledgeBaseSearch/query" @{ query = "invoice approval"; topK = 5 } { param($d) [pscustomobject]@{ passed = ($d.resultsCount -gt 0); note = "results=$($d.resultsCount)" } } | Out-Null
Invoke-EndpointCheck $results "monitoring-all" "/api/monitoring/all?minutes=60"

$containers = @(Get-ContainerHealth)
$monitoring = $null
try {
    $monitoring = Invoke-RestMethod -Method Get -Uri "$($BaseUrl.TrimEnd('/'))/api/monitoring/all?minutes=60" -TimeoutSec 30
}
catch {
    Write-Warning "Monitoring summary could not be fetched: $($_.Exception.Message)"
}

$total = $results.Count
$passedCount = @($results | Where-Object { $_.passed }).Count
$failedCount = $total - $passedCount
$http429 = @($results | Where-Object { $_.statusCode -eq 429 }).Count
$http5xx = @($results | Where-Object { $_.statusCode -ge 500 }).Count
$kbHits = @($results | Where-Object { $_.isFromKnowledgeBase -eq $true }).Count
$cacheHits = @($results | Where-Object { $_.cacheHit -eq $true }).Count
$realTicket = @($results | Where-Object { $_.ticketNumber -match "^[A-Z]+-\d+$" -and $_.ticketNumber -notmatch "^OFFLINE-" } | Select-Object -First 1)
$containerFailures = @($containers | Where-Object { $_.status -ne "running" -or ($_.health -ne "healthy" -and $_.health -ne "none") -or $_.restartCount -ne 0 }).Count
$jiraTicketNumber = if ($realTicket) { [string]$realTicket.ticketNumber } else { "" }
$jiraTicketUrl = if ($realTicket) { [string]$realTicket.ticketUrl } else { "" }
$monitoringStats = $null
if ($monitoring -and $monitoring.stats) {
    $monitoringStats = $monitoring.stats
}

$summary = New-Object System.Collections.Specialized.OrderedDictionary
$summary["generatedAt"] = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
$summary["baseUrl"] = $BaseUrl
$summary["createRealJiraTicket"] = [bool]$CreateRealJiraTicket
$summary["total"] = $total
$summary["passed"] = $passedCount
$summary["failed"] = $failedCount
$summary["http429"] = $http429
$summary["http5xx"] = $http5xx
$summary["kbHits"] = $kbHits
$summary["cacheHits"] = $cacheHits
$summary["containerFailures"] = $containerFailures
$summary["jiraTicketNumber"] = $jiraTicketNumber
$summary["jiraTicketUrl"] = $jiraTicketUrl
$summary["monitoringStats"] = $monitoringStats
$summary["containers"] = [object]@($containers)
$summary["results"] = [object]$results.ToArray()

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

$resultRows = ($results | ForEach-Object {
    "| $($_.name) | $($_.type) | $($_.passed) | $($_.statusCode) | $($_.elapsedMs) | $($_.source) | $($_.cacheHit) | $($_.cacheScope) | $($_.ticketNumber) | $($_.notes -replace '\|','/') |"
}) -join [Environment]::NewLine

$containerRows = ($containers | ForEach-Object {
    "| $($_.name) | $($_.status) | $($_.health) | $($_.restartCount) |"
}) -join [Environment]::NewLine

$markdown = @"
# JIFAS Full Feature Smoke Test

Generated: $($summary.generatedAt)
Base URL: $BaseUrl

## Summary

- Total checks: $total
- Passed: $passedCount
- Failed: $failedCount
- HTTP 429: $http429
- HTTP 5xx: $http5xx
- KB hits: $kbHits
- Cache hits observed: $cacheHits
- Real Jira ticket requested: $([bool]$CreateRealJiraTicket)
- Jira ticket number: $($summary.jiraTicketNumber)
- Jira ticket URL: $($summary.jiraTicketUrl)
- Container failures: $containerFailures

## Results

| Test | Type | Passed | HTTP | ms | Source | Cache Hit | Cache Scope | Ticket | Notes |
|---|---|---:|---:|---:|---|---|---|---|---|
$resultRows

## Containers

| Container | Status | Health | Restart Count |
|---|---|---|---:|
$containerRows

JSON detail: $jsonPath
"@

$markdown | Set-Content -Path $mdPath -Encoding UTF8

Write-Host "Smoke summary written:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"
Write-Host "SUMMARY total=$total passed=$passedCount failed=$failedCount http429=$http429 http5xx=$http5xx cacheHits=$cacheHits jira=$($summary.jiraTicketNumber)"

if ($failedCount -gt 0 -or $http429 -gt 0 -or $http5xx -gt 0 -or $containerFailures -gt 0) {
    throw "Full feature smoke failed: failed=$failedCount, http429=$http429, http5xx=$http5xx, containerFailures=$containerFailures"
}

if ($CreateRealJiraTicket -and [string]::IsNullOrWhiteSpace($summary.jiraTicketNumber)) {
    throw "Real Jira ticket was requested but no real ticket number was returned."
}

Write-Host "Full feature smoke passed."
