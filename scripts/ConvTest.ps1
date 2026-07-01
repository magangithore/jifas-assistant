# Long conversational test: 18 bubbles in one sessionId
# Tests: KB question, follow-up, "singkat aja", topic change, OOS rejection, "tadi aku nanya apa"
$ErrorActionPreference = "Continue"

function SendMessage($msg, $sessionId, $isFirst) {
    $body = @{
        message     = $msg
        userId      = "stress-test-user"
        sessionId   = $sessionId
        language    = "id"
        isFirstMessage = $isFirst
    } | ConvertTo-Json -Compress
    try {
        $r = Invoke-RestMethod -Uri 'http://localhost:8888/api/chat/message' `
            -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType 'application/json' -TimeoutSec 60
        $msg_out = if ($r.message) { $r.message.Replace("`n"," ").Replace("`r","") } else { "(null)" }
        Write-Host "  [$msg]" -ForegroundColor DarkGray
        Write-Host "    → $($msg_out.Substring(0, [Math]::Min(120, $msg_out.Length)))" -ForegroundColor White
        return $r
    } catch {
        Write-Host "  [$msg]" -ForegroundColor DarkGray
        Write-Host "    → ERROR: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

$sessionId = "conv-test-" + [Guid]::NewGuid().ToString("N").Substring(0,8)
Write-Host "`n=== Conversational Test: 18 bubbles, session=$sessionId ===" -ForegroundColor Cyan

$tests = @(
    @{ msg = "Apa itu JIFAS?";                        first = $true  },
    @{ msg = "terus gunanya buat siapa?";             first = $false },
    @{ msg = "singkat aja";                           first = $false },
    @{ msg = "siapa yang bikin?";                     first = $false },
    @{ msg = "ada modul apa aja?";                     first = $false },
    @{ msg = "caranya buat invoice baru";              first = $false },
    @{ msg = "langkah-langkahnya apa aja?";           first = $false },
    @{ msg = "terus kalau mau approve gimana?";        first = $false },
    @{ msg = "PPUM itu apa?";                         first = $false },
    @{ msg = "bedanya sama PUM apa?";                  first = $false },
    @{ msg = "kamu bisa nulis puisi?";                first = $false },
    @{ msg = "bola basket";                            first = $false },
    @{ msg = "apa itu websocket?";                      first = $false },
    @{ msg = "tadi aku nanya apa?";                    first = $false },
    @{ msg = "bahas yang invoice dulu ya";              first = $false },
    @{ msg = "cara cancel invoice gimana?";            first = $false },
    @{ msg = "kenapa nggak bisa cancel?";              first = $false },
    @{ msg = "terus solusinya apa?";                   first = $false }
)

$pass = 0; $fail = 0
foreach ($t in $tests) {
    $r = SendMessage $t.msg $sessionId $t.first
    if ($r) { $pass++ } else { $fail++ }
    Start-Sleep 1
}

Write-Host "`n=== Result: $pass passed, $fail failed ===" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Yellow" })
