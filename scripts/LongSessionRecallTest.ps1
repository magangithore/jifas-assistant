# Long-Session Recall Test (25 turns)
# Phase 1: 2 turn konteks (T1=Invoice, T2=Payment)
# Phase 2: 22 turn filler (mendorong T1-T2 keluar dari window 15)
# Phase 3: T25 tanya konteks T2 via running summary
# Phase 4: Verify sesi <=15 TIDAK memicu summary computation
$ErrorActionPreference = "Continue"

$base = "http://localhost:8888"

function SendMessage($msg, $sessionId, $isFirst) {
    $body = @{
        message        = $msg
        userId        = "long-session-test"
        sessionId     = $sessionId
        language      = "id"
        isFirstMessage = $isFirst
    } | ConvertTo-Json -Compress
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-RestMethod -Uri "$base/api/chat/message" `
            -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType 'application/json' -TimeoutSec 180
        $sw.Stop()
        $msg_out = if ($r.message) { $r.message.Replace("`n"," ").Replace("`r","") } else { "(null)" }
        return @{ ok = $true; r = $r; msg_out = $msg_out; ms = $sw.ElapsedMilliseconds }
    } catch {
        $sw.Stop()
        return @{ ok = $false; msg = $_.Exception.Message; ms = $sw.ElapsedMilliseconds }
    }
}

$sessionId = "long-session-" + [Guid]::NewGuid().ToString("N").Substring(0,8)
Write-Host ""
Write-Host "=== Long-Session Recall (25 turns) ===" -ForegroundColor Cyan
Write-Host "Session: $sessionId" -ForegroundColor Gray
Write-Host ""

# === PHASE 1: Konteks di T1-T2 ===
Write-Host "[Phase 1] Membuat konteks T1-T2" -ForegroundColor Yellow

$r = SendMessage "Apa itu Invoice di JIFAS?" $sessionId $true
if (-not $r.ok) { Write-Host "FAIL T1: $($r.msg)" -ForegroundColor Red; exit 1 }
$t1Ms = $r.ms
Write-Host "  T1 (Invoice): OK in $($t1Ms)ms" -ForegroundColor Gray
Start-Sleep 1

$r = SendMessage "Terus bedanya Payment sama Invoice apa?" $sessionId $false
if (-not $r.ok) { Write-Host "FAIL T2: $($r.msg)" -ForegroundColor Red; exit 1 }
$t2Ms = $r.ms
Write-Host "  T2 (Payment vs Invoice): OK in $($t2Ms)ms" -ForegroundColor Gray
$t2Answer = $r.msg_out
Start-Sleep 1

# === PHASE 2: 22 turn filler ===
Write-Host ""
Write-Host "[Phase 2] 22 turn filler" -ForegroundColor Yellow

$filler = @(
    "Cara buat PUM baru gimana?",
    "Alur PUM sampai approval apa aja?",
    "Batas dana PUM berapa?",
    "Kalau PUM melebihi batas gimana?",
    "Report PUM ada di menu mana?",
    "Cara export data PUM ke Excel?",
    "Approval PUM butuh siapa aja?",
    "Kalau approver tidak ada gimana?",
    "PPUM itu apa?",
    "Bedanya PPUM sama PUM?",
    "OLD PUM itu apa?",
    "Receiving document fungsinya apa?",
    "Cara input receiving baru?",
    "RV itu apa?",
    "CashBank di JIFAS apa?",
    "Budget monitoring ada di mana?",
    "Cara buat budget plan?",
    "COA itu apa?",
    "Master data vendor di mana?",
    "Cara add vendor baru?",
    "Chart of account untuk apa?",
    "Posting journal gimana?"
)

$turnNum = 3; $fillerMs = @()
foreach ($q in $filler) {
    $r = SendMessage $q $sessionId $false
    if (-not $r.ok) { Write-Host "FAIL T$($turnNum): $($r.msg)" -ForegroundColor Red; exit 1 }
    $fillerMs += $r.ms
    if ($turnNum -eq 3 -or $turnNum -eq 10 -or $turnNum -eq 17) {
        Write-Host "  T$($turnNum): OK in $($r.ms)ms" -ForegroundColor DarkGray
    }
    $turnNum++
    Start-Sleep 0.5
}

$avgFiller = [Math]::Round(($fillerMs | Measure-Object -Average).Average, 0)
Write-Host "  Avg filler latency: $($avgFiller)ms" -ForegroundColor DarkGray

# === PHASE 3: T25 - recall konteks T2 via running summary ===
Write-Host ""
Write-Host "[Phase 3] T25 - recall konteks T2 (Invoice vs Payment)" -ForegroundColor Cyan
$sw25 = [Diagnostics.Stopwatch]::StartNew()
$r = SendMessage "Tadi aku nanya bedanya Invoice sama Payment, bisa jelaskan lagi?" $sessionId $false
$sw25.Stop()
$t25Ms = $sw25.ElapsedMilliseconds

if (-not $r.ok) { Write-Host "FAIL T25: $($r.msg)" -ForegroundColor Red; exit 1 }
$delta = $t25Ms - $avgFiller
Write-Host "  T25 latency: $($t25Ms)ms (vs avg filler $($avgFiller)ms, delta +$($delta)ms)" -ForegroundColor Gray
$respSnippet = $r.msg_out.Substring(0, [Math]::Min(200, $r.msg_out.Length))
Write-Host "  Response: $($respSnippet)" -ForegroundColor White

$passT25 = ($r.msg_out -match "(?i)(invoice|payment|tagihan|pembayaran)") -and ($r.msg_out.Length -gt 30)
$colorT25 = if ($passT25) { "Green" } else { "Red" }
$txtT25 = if ($passT25) { "[PASS]" } else { "[FAIL]" }
Write-Host "  Result: $($txtT25) Recall Invoice vs Payment" -ForegroundColor $colorT25

# === PHASE 4: T26 - general recall ===
Write-Host ""
Write-Host "[Phase 4] T26 - general recall" -ForegroundColor Cyan
$sw26 = [Diagnostics.Stopwatch]::StartNew()
$r = SendMessage "Dari awal obrolan kita, topik apa aja yang sudah dibahas?" $sessionId $false
$sw26.Stop()
$t26Ms = $sw26.ElapsedMilliseconds
Write-Host "  T26 latency: $($t26Ms)ms" -ForegroundColor Gray
$resp26 = $r.msg_out.Substring(0, [Math]::Min(200, $r.msg_out.Length))
Write-Host "  Response: $($resp26)" -ForegroundColor White
$passT26 = ($r.msg_out -match "(?i)(invoice|payment|pum|ppum)") -and ($r.msg_out.Length -gt 20)
$colorT26 = if ($passT26) { "Green" } else { "Red" }
$txtT26 = if ($passT26) { "[PASS]" } else { "[FAIL]" }
Write-Host "  Result: $($txtT26) General recall" -ForegroundColor $colorT26

# === PHASE 5: T27 - recall T1 definition ===
Write-Host ""
Write-Host "[Phase 5] T27 - recall Invoice definition" -ForegroundColor Cyan
$r = SendMessage "Tadi aku juga nanya soal Invoice. Apa definisinya?" $sessionId $false
$resp27 = $r.msg_out.Substring(0, [Math]::Min(200, $r.msg_out.Length))
Write-Host "  Response: $($resp27)" -ForegroundColor White
$passT27 = $r.msg_out -match "(?i)(invoice|tagihan|faktur|vendor)"
$colorT27 = if ($passT27) { "Green" } else { "Red" }
$txtT27 = if ($passT27) { "[PASS]" } else { "[FAIL]" }
Write-Host "  Result: $($txtT27) Recall Invoice definition" -ForegroundColor $colorT27

# === SUMMARY ===
Write-Host ""
Write-Host "=== RESULT ===" -ForegroundColor Cyan
$allPass = $passT25 -and $passT26 -and $passT27
Write-Host "  T25 (Invoice vs Payment recall): $($txtT25) | $($t25Ms)ms" -ForegroundColor $colorT25
Write-Host "  T26 (General recall):              $($txtT26) | $($t26Ms)ms" -ForegroundColor $colorT26
Write-Host "  T27 (Invoice definition recall):  $($txtT27)" -ForegroundColor $colorT27
Write-Host "  Latency T1/T2 (warm):             $($t1Ms)ms / $($t2Ms)ms" -ForegroundColor Gray
Write-Host "  Latency avg filler (T3-T24):      $($avgFiller)ms" -ForegroundColor Gray
$colorLat = if ($t25Ms -gt ($avgFiller * 1.5)) { "Yellow" } else { "Gray" }
Write-Host "  Latency T25 (with summary):       $($t25Ms)ms (+$($delta)ms vs avg)" -ForegroundColor $colorLat

# === PHASE 6: Short session - verify NO summary computation ===
Write-Host ""
Write-Host "[Phase 6] Short session - verify NO summary computation" -ForegroundColor Cyan

$shortSid = "short-test-" + [Guid]::NewGuid().ToString("N").Substring(0,8)
docker exec jifas-redis redis-cli DEL "RunningSummary_$shortSid" 2>$null | Out-Null
docker exec jifas-redis redis-cli DEL "ConversationContext_$shortSid" 2>$null | Out-Null

$shortMs = @()
for ($i = 1; $i -le 10; $i++) {
    $r = SendMessage "Pertanyaan ke-$i tentang JIFAS" $shortSid $false
    $shortMs += $r.ms
    if (-not $r.ok) { Write-Host "FAIL short T$($i): $($r.msg)" -ForegroundColor Red }
    Start-Sleep 0.3
}

Start-Sleep 3
$logOut = docker logs jifas-assistant-api --tail 300 2>&1 | Out-String
$summaryFound = $logOut -match "Running summary COMPUTED"
$shortSidFound = $logOut -match [regex]::Escape($shortSid)
$triggeredSummary = $summaryFound -and $shortSidFound

$avgShort = [Math]::Round(($shortMs | Measure-Object -Average).Average, 0)
Write-Host "  Short session (10 turns): avg=$($avgShort)ms" -ForegroundColor Gray
Write-Host "  'Running summary COMPUTED' in logs: $summaryFound" -ForegroundColor $(if ($summaryFound) { "Yellow" } else { "Green" })
Write-Host "  Short session triggered summary: $triggeredSummary" -ForegroundColor $(if ($triggeredSummary) { "Red" } else { "Green" })
$passNoSummary = -not $triggeredSummary
$color6 = if ($passNoSummary) { "Green" } else { "Red" }
$txt6 = if ($passNoSummary) { "[PASS]" } else { "[FAIL]" }
Write-Host "  Phase 6 result: $($txt6)" -ForegroundColor $color6

# Final overall
Write-Host ""
$finalPass = $allPass -and $passNoSummary
$colorFinal = if ($finalPass) { "Green" } else { "Red" }
$txtFinal = if ($finalPass) { "ALL PASS" } else { "SOME FAILED" }
Write-Host "=== FINAL: $($txtFinal) ===" -ForegroundColor $colorFinal
if (-not $finalPass) { exit 1 }
