$body = @{message='halo';userId='ps-test';sessionId='ps003';isFirstMessage=$true} | ConvertTo-Json
$enc = [System.Text.Encoding]::UTF8
$bytes = $enc.GetBytes($body)
$r = Invoke-RestMethod -Uri 'http://localhost:8888/api/chat/message' -Method POST -ContentType 'application/json; charset=utf-8' -Body $bytes
Write-Host "source: $($r.source)"
Write-Host "confidence: $($r.confidenceScore)"
Write-Host "scopeMs: $($r.performanceMetrics.scopeDetectionMs)"
Write-Host "route: $($r.performanceMetrics.route)"
Write-Host "msg: $($r.message.Substring(0, [Math]::Min(80, $r.message.Length)))"
