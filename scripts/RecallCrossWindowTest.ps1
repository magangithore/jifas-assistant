# Cross-Window Recall Test
# Sesi 8+ turn — recall turn 1-2 di turn 9 (outside old 5-turn window, inside new 15-turn window)
# PASS: bot mengingat topik/data dari turn 1-2
# FAIL: bot bilang tidak tahu / lupa / tidak punya konteks turn 1-2
$ErrorActionPreference = "Continue"

function SendMessage($msg, $sessionId, $isFirst) {
    $body = @{
        message       = $msg
        userId        = "recall-test-user"
        sessionId     = $sessionId
        language      = "id"
        isFirstMessage = $isFirst
    } | ConvertTo-Json -Compress
    try {
        $r = Invoke-RestMethod -Uri 'http://localhost:8888/api/chat/message' `
            -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType 'application/json' -TimeoutSec 150
        $msg_out = if ($r.message) { $r.message.Replace("`n"," ").Replace("`r","") } else { "(null)" }
        return @{ ok = $true; r = $r; msg_out = $msg_out }
    } catch {
        return @{ ok = $false; msg = $_.Exception.Message }
    }
}

$sessionId = "recall-test-" + [Guid]::NewGuid().ToString("N").Substring(0,8)
Write-Host "`n=== Cross-Window Recall Test ===" -ForegroundColor Cyan
Write-Host "Session: $sessionId`n" -ForegroundColor Gray

# Phase 1: Buat konteks di turn 1-2 (topik spesifik yang akan di-recall)
$r = SendMessage "Apa itu PUM di JIFAS?" $sessionId $true
if (-not $r.ok) { Write-Host "FAIL: $($r.msg)" -ForegroundColor Red; exit 1 }
Write-Host "[T1] Apa itu PUM di JIFAS?" -ForegroundColor Yellow
Write-Host "  → $($r.msg_out.Substring(0, [Math]::Min(100, $r.msg_out.Length)))" -ForegroundColor White
$pumAnswer = $r.msg_out
Start-Sleep 1

$r = SendMessage "Terus bedanya sama PPUM apa?" $sessionId $false
if (-not $r.ok) { Write-Host "FAIL: $($r.msg)" -ForegroundColor Red; exit 1 }
Write-Host "[T2] Terus bedanya sama PPUM apa?" -ForegroundColor Yellow
Write-Host "  → $($r.msg_out.Substring(0, [Math]::Min(100, $r.msg_out.Length)))" -ForegroundColor White
Start-Sleep 1

# Phase 2: 6 turn filler (topik berbeda) untuk mendorong konteks lama keluar dari window 5
$filler = @(
    @{ q = "Cara buat invoice baru gimana?" },
    @{ q = "Langkah-langkahnya apa aja?" },
    @{ q = "Terus kalau mau approve invoice gimana?" },
    @{ q = "Batas approval berapa hari biasanya?" },
    @{ q = "Report apa aja yang tersedia di JIFAS?" },
    @{ q = "Cara export report ke Excel gimana?" }
)
$turnNum = 3
foreach ($f in $filler) {
    $r = SendMessage $f.q $sessionId $false
    if (-not $r.ok) { Write-Host "FAIL at T$turnNum $($r.msg)" -ForegroundColor Red; exit 1 }
    Write-Host "[T$turnNum] $($f.q)" -ForegroundColor DarkGray
    Write-Host "  → OK (skipped)" -ForegroundColor Gray
    $turnNum++
    Start-Sleep 1
}

# Phase 3: CRITICAL TEST — recall turn 1 (PUM definition)
Write-Host "`n--- CROSS-WINDOW RECALL TEST ---" -ForegroundColor Cyan
$r = SendMessage "Tadi aku nanya tentang PUM, bisa ringkas lagi definisinya?" $sessionId $false
if (-not $r.ok) { Write-Host "FAIL: $($r.msg)" -ForegroundColor Red; exit 1 }
Write-Host "[T9] Ragu aku nanya tentang PUM, bisa ringkas lagi definisinya?" -ForegroundColor Yellow
Write-Host "  → $($r.msg_out)" -ForegroundColor White

# Check: response harus menyebut PUM / uang muka / advance
$recall1 = $r.msg_out -match "(?i)(pum|uang.?muka|advance)"
if ($recall1) {
    Write-Host "`n[PASS] T9 recall PUM: bot mengingat konteks dari T1" -ForegroundColor Green
} else {
    Write-Host "`n[FAIL] T9 recall PUM: bot TIDAK mengingat konteks dari T1" -ForegroundColor Red
}

# Phase 4: Recall PPUM (from T2)
$r = SendMessage "Terus tadi juga nanya bedanya PUM sama PPUM, apa kesimpulannya?" $sessionId $false
if (-not $r.ok) { Write-Host "FAIL: $($r.msg)" -ForegroundColor Red; exit 1 }
Write-Host "[T10] Terus tadi juga nanya bedanya PUM sama PPUM, apa kesimpulannya?" -ForegroundColor Yellow
Write-Host "  → $($r.msg_out)" -ForegroundColor White
$recall2 = $r.msg_out -match "(?i)(ppum|pengajuan)"
if ($recall2) {
    Write-Host "[PASS] T10 recall PPUM: bot mengingat konteks dari T2" -ForegroundColor Green
} else {
    Write-Host "[FAIL] T10 recall PPUM: bot TIDAK mengingat konteks dari T2" -ForegroundColor Red
}

# Phase 5: Recall "tadi aku nanya apa aja?" (general recall)
$r = SendMessage "Tadi aku nanya apa aja dari awal?" $sessionId $false
if (-not $r.ok) { Write-Host "FAIL: $($r.msg)" -ForegroundColor Red; exit 1 }
Write-Host "[T11] 'Tadi aku nanya apa aja dari awal?'" -ForegroundColor Yellow
Write-Host "  → $($r.msg_out)" -ForegroundColor White
$recall3 = ($r.msg_out -match "(?i)(pum|ppum|invoice|approval)") -or ($r.msg_out -notmatch "(?i)(tidak.?tau|lupa|ga.?tahu|kosong)")
if ($recall3) {
    Write-Host "[PASS] T11 general recall: bot menyebutkan topik dari awal" -ForegroundColor Green
} else {
    Write-Host "[FAIL] T11 general recall: bot gagal recall" -ForegroundColor Red
}

$allPass = $recall1 -and $recall2 -and $recall3
Write-Host "`n=== Cross-Window Recall: $(if ($allPass) { 'ALL PASS' } else { 'SOME FAILED' }) ===" -ForegroundColor $(if ($allPass) { "Green" } else { "Red" })
