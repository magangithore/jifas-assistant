// IDOR bypass re-test v2: field name = "message" (confirmed from dump)
// 1. UserA establish session, chat
// 2. Attacker: same sessionId, userId=""/null/"anon"
// 3. Verify: no UserA data in response.message + latency = fresh (not cache)
const API = 'http://localhost:8888/api/chat/message';

async function msg(sessionId, userId, text) {
  const body = { message: text, sessionId, userId, userRole: 'FINA:KI', userCompCode: 'KI', language: 'id' };
  const start = Date.now();
  const res = await fetch(API, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const json = await res.json();
  const latency = Date.now() - start;
  return { json, latency };
}

function safeStr(v) {
  if (v == null) return '(null)';
  return String(v).substring(0, 300);
}

async function main() {
  console.log('=== IDOR BYPASS v2: field=message ===\n');

  const sessionId = 'bp2-' + Date.now();
  const userA = 'userA-' + Date.now();

  // Phase 1: UserA chat (P1 — check if Ollama is up)
  console.log('[P1] UserA (' + userA + ') establishing session...');
  const r1 = await msg(sessionId, userA, 'gajiku 20 juta per bulan');
  console.log('  latency=' + r1.latency + 'ms');
  console.log('  success=' + r1.json.success);
  console.log('  message=' + safeStr(r1.json.message));
  console.log('  source=' + r1.json.source);
  console.log('  route=' + (r1.json.performanceMetrics ? r1.json.performanceMetrics.route : 'n/a'));

  // Investigate P1 timeout
  if (r1.latency > 120000) {
    console.log('\n  !! P1 latency >120s — Ollama DOWN or TIMEOUT');
    console.log('  This explains why UserA got null — skip rest of test');
    return;
  }
  if (!r1.json.success || r1.json.message === '(null)') {
    console.log('\n  !! P1 failed — Ollama issue: ' + safeStr(r1.json.message));
    return;
  }

  // Phase 1b: fillers
  console.log('\n[P1b] UserA filler messages...');
  await msg(sessionId, userA, 'potong pajak 15% dong');
  await msg(sessionId, userA, 'berapanya setelah dipotong?');
  console.log('  fillers sent');

  // Phase 2: Attack — userId=""
  console.log('\n[P2] ATTACK: userId="" + same sessionId');
  console.log('  probe: "tadi kita bahas apa?"');
  const a1 = await msg(sessionId, '', 'tadi kita bahas apa?');
  console.log('  latency=' + a1.latency + 'ms');
  console.log('  success=' + a1.json.success);
  console.log('  message=' + safeStr(a1.json.message));
  console.log('  isFromKB=' + a1.json.isFromKnowledgeBase);
  console.log('  route=' + (a1.json.performanceMetrics ? a1.json.performanceMetrics.route : 'n/a'));

  // Phase 3: Attack — userId=null
  console.log('\n[P3] ATTACK: userId=null + same sessionId');
  console.log('  probe: "tadi ngomongin apa?"');
  const body3 = { message: 'tadi ngomongin apa?', sessionId, userId: null, userRole: 'FINA:KI', userCompCode: 'KI', language: 'id' };
  const start3 = Date.now();
  const res3 = await fetch(API, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body3) });
  const a2 = await res3.json();
  const lat3 = Date.now() - start3;
  console.log('  latency=' + lat3 + 'ms');
  console.log('  success=' + a2.success);
  console.log('  message=' + safeStr(a2.message));
  console.log('  isFromKB=' + a2.isFromKnowledgeBase);
  console.log('  route=' + (a2.performanceMetrics ? a2.performanceMetrics.route : 'n/a'));

  // Phase 4: Attack — userId="anon"
  console.log('\n[P4] ATTACK: userId="anon" + same sessionId');
  const a3 = await msg(sessionId, 'anon', 'gimana dong?');
  console.log('  latency=' + a3.latency + 'ms');
  console.log('  success=' + a3.json.success);
  console.log('  message=' + safeStr(a3.json.message));
  console.log('  isFromKB=' + a3.json.isFromKnowledgeBase);
  console.log('  route=' + (a3.json.performanceMetrics ? a3.json.performanceMetrics.route : 'n/a'));

  // === JUDGE ===
  console.log('\n=== LEAK DETECTION ===');
  // Keywords from UserA's messages
  const leakKeywords = ['20 juta', 'gaji', 'pajak', 'take home', 'potong'];
  const r1msg = (r1.json.message || '').toLowerCase();
  const a1msg = (a1.json.message || '').toLowerCase();
  const a2msg = (a2.message || '').toLowerCase();
  const a3msg = (a3.json.message || '').toLowerCase();

  function detectLeak(attackMsg, label) {
    const hits = leakKeywords.filter(k => attackMsg.includes(k));
    if (hits.length > 0) {
      console.log('  ' + label + ': LEAK (' + hits.join(', ') + ')');
      return true;
    }
    console.log('  ' + label + ': PASS (no UserA data)');
    return false;
  }

  const leak1 = detectLeak(a1msg, 'userId=""');
  const leak2 = detectLeak(a2msg, 'userId=null');
  const leak3 = detectLeak(a3msg, 'userId="anon"');

  console.log('\n=== LATENCY CHECK (fresh = Ollama call, not cache) ===');
  const FRESH_THRESHOLD = 500; // ms
  function checkFresh(lat, label) {
    if (lat > FRESH_THRESHOLD) console.log('  ' + label + ': FRESH ' + lat + 'ms (PASS)');
    else console.log('  ' + label + ': FAST ' + lat + 'ms (WARN: possible cache?)');
  }
  checkFresh(a1.latency, 'userId=""');
  checkFresh(lat3, 'userId=null');
  checkFresh(a3.latency, 'userId="anon"');

  console.log('\n=== OVERALL ===');
  const pass = !leak1 && !leak2 && !leak3;
  console.log('  RESULT: ' + (pass ? 'PASS' : 'FAIL') + ' — IDOR bypass ' + (pass ? 'CLOSED' : 'OPEN'));
}

main().catch(console.error);
