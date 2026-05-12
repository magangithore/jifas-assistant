$base = "http://localhost:5000"

function AskTest {
    param($label, $q, $p)
    $bodyObj = @{
        message    = $q
        userId     = "tester"
        sessionId  = "ses-test"
        activePage = $p
        userRole   = "Admin"
    }
    $body = $bodyObj | ConvertTo-Json -Compress
    try {
        $r = Invoke-RestMethod -Uri "$base/api/chat/message" -Method POST -ContentType "application/json" -Body $body -TimeoutSec 180
        $ans = ($r.message -replace '<think>[\s\S]*?</think>', '').Trim()
        $prev = if ($ans.Length -gt 450) { $ans.Substring(0, 450) + "..." } else { $ans }
        Write-Host "[$label]" -ForegroundColor Cyan
        Write-Host "  Q: $q" -ForegroundColor White
        Write-Host "  A: $prev" -ForegroundColor Green
        Write-Host "  Source=$($r.source) | KB=$($r.isFromKnowledgeBase) | Confidence=$([math]::Round($r.confidenceScore,2))" -ForegroundColor DarkGray
        Write-Host ""
    }
    catch {
        Write-Host "[$label] ERROR: $_" -ForegroundColor Red
        Write-Host ""
    }
}

Write-Host "========== JIFAS AI FULL TEST ==========" -ForegroundColor Yellow
Write-Host ""

AskTest "1 - Identitas AI"     "Halo kamu siapa dan bisa bantu apa?"               "Dashboard"
AskTest "2 - Budget CM CA CY"  "Perbedaan status CM CA CY di budget JIFAS?"        "Budget"
AskTest "3 - Active Page"      "Saya di halaman apa sekarang?"                     "Invoice.Finance"
AskTest "4 - Out of Scope"     "Resepkan nasi goreng untuk saya"                   "Dashboard"
AskTest "5 - PUM"              "Apa itu PUM dan cara pengajuannya?"                "Pum"
AskTest "6 - Login URL"        "Cara login ke JIFAS dan URL nya?"                  "Login"
AskTest "7 - Over Budget"      "Proses approval kalau over budget?"                "OverBudget"
AskTest "8 - Cashbank"         "Fungsi modul cashbank di JIFAS?"                   "Cashbank"
AskTest "9 - Receiving"        "Proses receiving di JIFAS siapa yang approve?"     "Receiving"
AskTest "10 - Page Function"   "Apa yang bisa saya lakukan di halaman ini?"        "Payment.PaymentInvoice"

Write-Host "========== TEST SELESAI ==========" -ForegroundColor Yellow
