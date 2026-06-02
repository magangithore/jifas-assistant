param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$QuestionsPath = "Jifas.Assistant/Quality/golden-questions.json",
    [string]$OutputPath = "golden-evaluation-results.json",
    [int]$TopK = 5
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $QuestionsPath)) {
    throw "Golden questions file not found: $QuestionsPath"
}

$questions = Get-Content -Path $QuestionsPath -Raw | ConvertFrom-Json
$results = New-Object System.Collections.Generic.List[object]

foreach ($item in $questions) {
    $body = @{
        query = $item.question
        topK = $TopK
    } | ConvertTo-Json -Depth 5

    $started = Get-Date
    try {
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri "$BaseUrl/api/KnowledgeBaseSearch/query" `
            -ContentType "application/json" `
            -Body $body

        $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds
        $first = $response.results | Select-Object -First 1
        $title = if ($null -ne $first) { [string]$first.title } else { "" }
        $category = if ($null -ne $first) { [string]$first.category } else { "" }
        $score = if ($null -ne $first) { [double]$first.relevanceScore } else { 0 }

        $titleOk = [string]::IsNullOrWhiteSpace($item.expectedTitleContains) -or
            $title.IndexOf([string]$item.expectedTitleContains, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        $categoryOk = [string]::IsNullOrWhiteSpace($item.expectedCategory) -or
            $category.IndexOf([string]$item.expectedCategory, [System.StringComparison]::OrdinalIgnoreCase) -ge 0

        $results.Add([pscustomobject]@{
            id = $item.id
            question = $item.question
            passed = [bool]($response.resultsCount -gt 0 -and $titleOk -and $categoryOk)
            elapsedMs = $elapsedMs
            resultsCount = $response.resultsCount
            topTitle = $title
            topCategory = $category
            topScore = $score
            expectedCategory = $item.expectedCategory
            expectedTitleContains = $item.expectedTitleContains
            error = $null
        })
    }
    catch {
        $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds
        $results.Add([pscustomobject]@{
            id = $item.id
            question = $item.question
            passed = $false
            elapsedMs = $elapsedMs
            resultsCount = 0
            topTitle = ""
            topCategory = ""
            topScore = 0
            expectedCategory = $item.expectedCategory
            expectedTitleContains = $item.expectedTitleContains
            error = $_.Exception.Message
        })
    }
}

$passed = ($results | Where-Object { $_.passed }).Count
$summary = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    baseUrl = $BaseUrl
    total = $results.Count
    passed = $passed
    failed = $results.Count - $passed
    passRate = if ($results.Count -eq 0) { 0 } else { [math]::Round($passed / $results.Count * 100, 2) }
    averageElapsedMs = if ($results.Count -eq 0) { 0 } else { [math]::Round(($results | Measure-Object elapsedMs -Average).Average, 0) }
    results = $results
}

$summary | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 4
