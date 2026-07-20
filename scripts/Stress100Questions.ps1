$baseUrl = "http://localhost:8888"
$questions = @(
    # Invoice
    "Bagaimana cara membuat invoice baru di JIFAS?",
    "Apa alur approval invoice?",
    "Status invoice Need Head Approval artinya apa?",
    "Kenapa invoice tidak bisa di-post?",
    "Perbedaan Invoice dan Payment?",
    "Tax approval di invoice untuk apa?",
    "Bagaimana reject invoice yang salah?",
    "Draft invoice bisa diedit berapa kali?",
    "Posted invoice bisa dibatalkan?",
    "ApprovalIncomplete saat buat invoice?",
    # PUM
    "Cara pengajuan PUM baru?",
    "Apa itu settlement PUM?",
    "Realisasi PUM lebih besar dari pengajuan?",
    "Sisa uang muka PUM harus dikembalikan?",
    "Alur PUM dari awal sampai settlement?",
    "Perbedaan PUM dan Payment?",
    "OLD PUM untuk apa?",
    "Distribusi di PUM maksudnya apa?",
    "Kenapa PUM stuck di Finance Approval?",
    "USRL role di PUM bedanya apa?",
    # Payment
    "Cara bayar invoice lewat JIFAS?",
    "Metode pembayaran apa saja di JIFAS?",
    "BG artinya apa di Payment?",
    "List BG itu fungsinya apa?",
    "PaymentTax bedanya dari Payment biasa?",
    "Alur Payment Invoice?",
    "Head Approval di Payment untuk apa?",
    "Paid status artinya apa?",
    "Kenapa Payment tidak bisa di-post?",
    "Perbedaan Payment dan CashBank?",
    # CashBank
    "Cara input receive di CashBank?",
    "Alur CashBank Receive?",
    "CashBank Payment bedanya dari Payment?",
    "Posted di CashBank artinya apa?",
    "ReceiveTax di CashBank untuk apa?",
    "Kenapa saldo CashBank tidak sesuai?",
    "Approval di CashBank needed approval apa?",
    "Perbedaan CashBank dan Payment?",
    "CashBank setelah Posted bisa diedit?",
    "CashBank Receive vs Payment?",
    # Receiving
    "Cara buat Receive Voucher baru?",
    "Alur Receiving di JIFAS?",
    "ReceiveTax approval untuk apa?",
    "Unidentified RV artinya apa?",
    "Kenapa tax rate salah di receiving?",
    "NPWP vendor harus lengkap?",
    "Perbedaan Receive dan Invoice?",
    "Receive Posted bisa void?",
    "Approval of Unidentified RV?",
    "Finance Checking di Receiving?",
    # Budget
    "Cara input budget di JIFAS?",
    "Budget Committed artinya apa?",
    "Budget Remaining vs Actual?",
    "Over Budget alert muncul dimana?",
    "Budget per cost center?",
    "Revisi budget bagaimana caranya?",
    "Budget Status Committed bagaimana terjadi?",
    "Budget Card fungsinya apa?",
    "Budget Realization bedanya dari Committed?",
    "Over Budget butuh approval apa?",
    # Report
    "Report Daily Cashflow untuk apa?",
    "Inquiry AP artinya apa?",
    "Inquiry AR bedanya dari AP?",
    "Inquiry CB di Report?",
    "Budget Payment Report?",
    "Cashbank Recap bedanya dari Detail?",
    "Deposito Aktif di Report?",
    "Saldo Buku Bank fungsinya?",
    "Realisasi PUM di Report?",
    "Committed Realization itu apa?",
    # Master Data
    "Cara tambah vendor baru?",
    "Company code di Master Data?",
    "Division dan Department bedanya?",
    "COA artinya apa?",
    "Account Period harus buka tutup?",
    "Employee di Master Data untuk apa?",
    "Roles Authorization ada apa saja?",
    "WMTR role bisa apa?",
    "FINA role di JIFAS?",
    "USER role bedanya dari FINA?",
    # Accounting
    "GL di Accounting untuk apa?",
    "AP Accounting bedanya dari AP Report?",
    "AR Accounting fungsinya?",
    "Bulk Posting artinya apa?",
    "Acc Period di Accounting?",
    "Posting dokumen di Accounting?",
    "Perbedaan AP dan AR di Accounting?",
    "GL vs Trial Balance bedanya?",
    "Consolidation Accounting?",
    "Manual Journal di GL?",
    # Login & Access
    "Tidak bisa login JIFAS?",
    "Username untuk login apa?",
    "Password JIFAS apa?",
    "URL login JIFAS untuk KIJ?",
    "URL login untuk GBC?",
    "URL login untuk JI?",
    "URL login untuk BP?",
    "Clear cache browser?",
    "Role tidak muncul di menu?",
    "Akses ditolak di JIFAS?",
    # SPK
    "SPK artinya apa di JIFAS?",
    "Alur SPK dari Draft sampai Confirmed?",
    "SPK Confirmed bisa diedit?",
    "SPK terkait ke dokumen apa?",
    "Perbedaan SPK dan Invoice?",
    "Status Confirmed di SPK?",
    "Draft SPK bisa dihapus?",
    "SPK vs Receiving?",
    "Surat Perintah Kerja?",
    "Kontrak di JIFAS?"
)

$success = 0
$failed = 0
$cacheHit = 0
$errors = @()
$sw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 0; $i -lt $questions.Count; $i++) {
    $q = $questions[$i]
    $userId = "stress-test-{0:D3}" -f $i
    $sessionId = "session-{0}" -f [Guid]::NewGuid().ToString().Substring(0,8)
    $body = @{
        message = $q
        userId  = $userId
        sessionId = $sessionId
        userRole = "USER"
        userCompCode = "KI"
        currentModule = "Home"
        companyId = "KI"
        language = "id"
        isFirstMessage = $true
        context = @{
            activeModule = "Home"
            pageTitle = "Home"
            currentPage = "/Home"
        }
    } | ConvertTo-Json -Compress

    try {
        $r = Invoke-WebRequest -Uri "$baseUrl/api/chat/message" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 90 `
            -UseBasicParsing
        if ($r.StatusCode -eq 200) {
            $j = $r.Content | ConvertFrom-Json
            if ($j.cacheHit -eq $true -or $j.cacheScope -eq "shared") {
                $cacheHit++
            } else {
                $success++
            }
            Write-Host "[$($i+1)/$($questions.Count)] OK - cache:$($j.cacheHit) - ${q:0:40}..."
        } else {
            $failed++
            Write-Host "[$($i+1)/$($questions.Count)] FAIL HTTP $($r.StatusCode) - ${q:0:40}..."
        }
    } catch {
        $failed++
        $errMsg = $_.Exception.Message
        Write-Host "[$($i+1)/$($questions.Count)] ERROR - ${q:0:40}... : $errMsg"
    }
}

$sw.Stop()
Write-Host ""
Write-Host "=================="
Write-Host "Results: $success/$($questions.Count) fresh (non-cache), $cacheHit cached, $failed errors"
Write-Host "Elapsed: $($sw.Elapsed.TotalMinutes.ToString('F1')) minutes"
Write-Host "Avg per request: $([int]($sw.ElapsedMilliseconds / $questions.Count))ms"
