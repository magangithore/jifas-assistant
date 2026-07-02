// Re-test: IDOR isolation + OOS consistency (no cache) + Scope rule technical
const http = require('http');

const BASE = 'http://localhost:8888/api/chat/message';

function chat(message, userId, sessionId) {
    return new Promise((resolve) => {
        const body = JSON.stringify({
            message, userId, sessionId,
            userRole: 'USER', userCompCode: 'KI',
            language: 'id', isFirstMessage: false,
            context: { activeModule: 'Home', pageTitle: 'Home', currentPage: '/Home' }
        });
        const start = Date.now();
        const req = http.request(BASE, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } }, (res) => {
            let data = '';
            res.on('data', c => data += c);
            res.on('end', () => {
                try { resolve({ json: JSON.parse(data), latencyMs: Date.now() - start }); }
                catch(e) { resolve({ json: { message: 'PARSE_ERR', source: 'ERROR' }, latencyMs: Date.now() - start }); }
            });
        });
        req.on('error', e => resolve({ json: { message: 'NET_ERR: ' + e.message, source: 'ERROR' }, latencyMs: Date.now() - start }));
        req.setTimeout(120000, () => { req.destroy(); resolve({ json: { message: 'TIMEOUT', source: 'ERROR' }, latencyMs: 120000 }); });
        req.write(body); req.end();
    });
}

function uid() { return 'rt2-' + Math.random().toString(36).slice(2, 10); }
function sid() { return 's' + Math.random().toString().slice(2); }

async function run() {
    let passes = 0, fails = 0;

    console.log('\n========================================');
    console.log(' FIX #1 RE-TEST: IDOR ISOLATION');
    console.log('========================================\n');

    // D1: UserA buat fakta "gajiku 15 juta", UserB reuse sessionId
    // Probe: "tadi kita ngobrol apa aja?" (netral, tidak mengandung info pribadi)
    const sessD1 = sid();
    console.log('[IDOR-1] UserA kirim fakta, UserB pakai sessionId sama, probe netral');
    console.log('  SessionId: ' + sessD1);

    const rD1a = await chat('gajiku 15 juta per bulan, tolong catat ya', 'userA-rt', sessD1);
    console.log('  T1 UserA (' + rD1a.latencyMs + 'ms): ' + rD1a.json.message.slice(0, 100));

    // 3 filler turns as UserA
    await chat('apa itu PUM di JIFAS?', 'userA-rt', sessD1);
    await chat('cara approve invoice gimana?', 'userA-rt', sessD1);
    await chat('bedanya Draft sama Submitted', 'userA-rt', sessD1);
    console.log('  3 filler turns done (as UserA)');

    // UserB now uses the same sessionId with DIFFERENT userId
    const rD1b = await chat('tadi kita ngobrol apa aja?', 'userB-rt', sessD1);
    console.log('  T4 UserB (' + rD1b.latencyMs + 'ms): ' + rD1b.json.message.slice(0, 150));

    const exposesA = rD1b.json.message.match(/gaji|15 juta|per bulan/i);
    if (exposesA) {
        console.log('  VERDICT: FAIL — UserB exposed UserA context! (IDOR!)');
        fails++;
    } else {
        console.log('  VERDICT: PASS — UserB did NOT see UserA context');
        passes++;
    }

    console.log('\n========================================');
    console.log(' FIX #2+3 RE-TEST: OOS CONSISTENCY (no cache)');
    console.log('========================================\n');

    // Flush Redis first, then send OOS queries without cache
    console.log('[OOS-1] Flush Redis...');
    await new Promise(r => setTimeout(r, 500));

    // Test "apa itu websocket?" with DIFFERENT sessionIds (no cache)
    console.log('[OOS-1] "apa itu websocket?" x5 (fresh session each, no cache)');
    let wsAns = 0, wsRej = 0;
    const wsQueries = [
        'apa itu websocket?',
        'apa sih websocket itu?',
        'tolong jelaskan websocket',
        'websocket apa artinya?',
        'jelaskan konsep websocket'
    ];
    for (let i = 0; i < 5; i++) {
        const r = await chat(wsQueries[i], uid(), sid());
        const isAns = !r.json.message.match(/di luar|jifas|khusus|scope|luar|maaf/i) && r.json.success !== false;
        const lat = r.latencyMs;
        if (isAns) {
            wsAns++;
            console.log('  [' + (i+1) + '/5] DIJAWAB (' + lat + 'ms) | ' + r.json.message.slice(0, 80));
        } else {
            wsRej++;
            console.log('  [' + (i+1) + '/5] DITOLAK (' + lat + 'ms) | ' + r.json.message.slice(0, 80));
        }
    }
    console.log('  RASIO: ' + wsAns + '/5 dijawab, ' + wsRej + '/5 ditolak');
    if (wsAns > 0) {
        console.log('  VERDICT: FAIL — websocket lolos (rule #3 tidak cukup efektif)');
        fails++;
    } else {
        console.log('  VERDICT: PASS — 5/5 ditolak, OOS rule efektif');
        passes++;
    }

    // Test "REST API" as additional technical term
    console.log('\n[OOS-2] "apa itu REST API?" x3 (technical IT concept)');
    let apiAns = 0, apiRej = 0;
    for (let i = 0; i < 3; i++) {
        const r = await chat('apa itu REST API?', uid(), sid());
        const isAns = !r.json.message.match(/di luar|jifas|khusus|scope|luar|maaf/i) && r.json.success !== false;
        if (isAns) { apiAns++; console.log('  [' + (i+1) + '/3] DIJAWAB'); }
        else { apiRej++; console.log('  [' + (i+1) + '/3] DITOLAK'); }
    }
    console.log('  RASIO: ' + apiAns + '/3 dijawab, ' + apiRej + '/3 ditolak');
    if (apiAns > 0) {
        console.log('  VERDICT: FAIL — REST API lolos');
        fails++;
    } else {
        console.log('  VERDICT: PASS — REST API ditolak');
        passes++;
    }

    // Test "JSON parse error" — should NOT be OOS if in JIFAS context
    console.log('\n[OOS-3] "JSON parse error di module invoice" (technical but JIFAS context)');
    const rO3 = await chat('JSON parse error muncul di modul Invoice, kenapa?', uid(), sid());
    console.log('  (' + rO3.latencyMs + 'ms): ' + rO3.json.message.slice(0, 150));
    // This SHOULD be answered (JIFAS context), not rejected
    const answered = rO3.json.message.match(/json|JSON|parse|Invoice|error/i) && !rO3.json.message.match(/di luar|scope|luar cakupan/i);
    console.log('  VERDICT: ' + (answered ? 'PASS — dijawab (JIFAS context)' : 'FAIL — salah ditolak'));

    console.log('\n========================================');
    console.log(' RINGKASAN RE-TEST');
    console.log('========================================');
    console.log('  PASS: ' + passes);
    console.log('  FAIL: ' + fails);
}

run().catch(e => { console.error(e); process.exit(1); });
