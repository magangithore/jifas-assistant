// Step 1: Dump 1 full response to verify field name (message vs response vs ???)
const API = 'http://localhost:8888/api/chat/message';

async function msg(sessionId, userId, text) {
  const body = {
    message: text,
    sessionId: sessionId,
    userId: userId,
    userRole: 'FINA:KI',
    userCompCode: 'KI',
    language: 'id'
  };
  const start = Date.now();
  const res = await fetch(API, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  const json = await res.json();
  const latency = Date.now() - start;
  return { json, latency };
}

async function main() {
  console.log('=== DUMP: verify response field name ===\n');

  const sessionId = 'dump-test-' + Date.now();
  const userId = 'dump-user-' + Date.now();

  // Single message
  const r = await msg(sessionId, userId, 'apa itu websocket?');
  console.log('latency=' + r.latency + 'ms');
  console.log('full JSON:');
  console.log(JSON.stringify(r.json, null, 2));

  // List all top-level keys
  console.log('\ntop-level keys: ' + Object.keys(r.json).join(', '));

  // Check for message/response/answer/text
  const candidates = ['message', 'response', 'answer', 'text', 'content', 'result'];
  for (const k of candidates) {
    if (r.json[k] !== undefined) {
      console.log(`field "${k}" = "${String(r.json[k]).substring(0, 200)}"`);
    }
  }
}

main().catch(console.error);
