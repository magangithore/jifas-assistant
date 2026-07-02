// IDOR bypass regression test
// Verifies that empty/null/"anon" userId cannot access another user's session history.
// Setup: UserA establishes session with personal data.
// Attack: Same sessionId with different userId.
// Expected: No UserA data leaked, fresh Ollama latency (not cache).
const API = 'http://localhost:8888/api/chat/message';

async function msg(sessionId, userId, text) {
  const body = { message: text, sessionId, userId, userRole: 'FINA:KI', userCompCode: 'KI', language: 'id' };
  const start = Date.now();
  const res = await fetch(API, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const json = await res.json();
  const latency = Date.now() - start;
  return { json, latency };
}

async function main() {
  const sessionId = 'reg-idor-' + Date.now();
  const userA = 'reg-user-A-' + Date.now();

  // Establish session with private data
  const r1 = await msg(sessionId, userA, 'gajiku 20 juta per bulan');
  await msg(sessionId, userA, 'potong pajak 15%');
  await msg(sessionId, userA, 'berapanya setelah dipotong?');

  if (r1.latency > 120000) {
    console.log('SKIP: Ollama timeout (service down)');
    return;
  }
  if (!r1.json.success) {
    console.log('SKIP: Ollama unavailable');
    return;
  }

  const probes = [
    { userId: '', probe: 'tadi kita bahas apa?' },
    { userId: null, probe: 'tadi ngomongin apa?' },
    { userId: 'anon', probe: 'gimana dong?' },
  ];

  const LEAK_KEYWORDS = ['20 juta', 'gaji', 'pajak', 'take home', 'potong'];
  const FRESH_THRESHOLD = 500;
  let passed = 0;

  for (const p of probes) {
    const body = { message: p.probe, sessionId, userId: p.userId, userRole: 'FINA:KI', userCompCode: 'KI', language: 'id' };
    const start = Date.now();
    const res = await fetch(API, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const json = await res.json();
    const latency = Date.now() - start;

    const msg = (json.message || '').toLowerCase();
    const leaked = LEAK_KEYWORDS.some(k => msg.includes(k));
    const fresh = latency > FRESH_THRESHOLD;

    if (!leaked && fresh) {
      console.log('PASS userId=' + JSON.stringify(p.userId) + ' lat=' + latency + 'ms');
      passed++;
    } else {
      console.log('FAIL userId=' + JSON.stringify(p.userId) + ' lat=' + latency + 'ms leak=' + leaked);
    }
  }

  if (passed === probes.length) {
    console.log('ALL PASS: IDOR bypass closed');
    process.exit(0);
  } else {
    console.log('FAIL: ' + passed + '/' + probes.length);
    process.exit(1);
  }
}

main().catch(e => { console.error(e); process.exit(1); });
