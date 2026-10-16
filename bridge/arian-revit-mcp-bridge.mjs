#!/usr/bin/env node
// Arian Revit MCP — stdio <-> HTTP bridge.
// Lets MCP clients that only speak stdio (e.g. Claude Desktop) talk to the HTTP server that runs inside Revit.
// Zero dependencies. Requires Node 18+.
//
// Environment:
//   ARIAN_REVIT_MCP_URL         default http://127.0.0.1:8765/mcp
//   ARIAN_REVIT_MCP_TOKEN       optional bearer token (if set in the plugin settings)
//   ARIAN_REVIT_MCP_CATEGORIES  optional comma list to restrict tools, e.g. "Query,Architecture,Structure"

import readline from 'node:readline';

const BASE = process.env.ARIAN_REVIT_MCP_URL || 'http://127.0.0.1:8765/mcp';
const TOKEN = process.env.ARIAN_REVIT_MCP_TOKEN || '';
const CATS = process.env.ARIAN_REVIT_MCP_CATEGORIES || '';
const URL_ = CATS ? `${BASE}${BASE.includes('?') ? '&' : '?'}categories=${encodeURIComponent(CATS)}` : BASE;

const log = (...a) => process.stderr.write('[arian-bridge] ' + a.join(' ') + '\n');

function send(obj) {
  process.stdout.write(JSON.stringify(obj) + '\n');
}

async function forward(msg) {
  const isRequest = msg && msg.id !== undefined && msg.method;
  try {
    const headers = { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' };
    if (TOKEN) headers['Authorization'] = 'Bearer ' + TOKEN;
    const res = await fetch(URL_, { method: 'POST', headers, body: JSON.stringify(msg) });
    if (res.status === 202 || res.status === 204) return; // notification accepted
    const text = await res.text();
    if (!res.ok) throw new Error(`HTTP ${res.status}: ${text.slice(0, 300)}`);
    if (!text) return;
    let payload = text;
    const ct = res.headers.get('content-type') || '';
    if (ct.includes('text/event-stream')) {
      // take every "data:" line as a JSON-RPC message
      for (const line of text.split(/\r?\n/)) {
        if (line.startsWith('data:')) {
          const d = line.slice(5).trim();
          if (d) send(JSON.parse(d));
        }
      }
      return;
    }
    const parsed = JSON.parse(payload);
    if (Array.isArray(parsed)) parsed.forEach(send); else send(parsed);
  } catch (e) {
    log('forward failed:', e.message);
    if (isRequest) {
      send({
        jsonrpc: '2.0',
        id: msg.id,
        error: {
          code: -32000,
          message:
            'Cannot reach the Arian Revit MCP server at ' + BASE + '. Open Revit 2027, go to the "Arian" ribbon tab and click "Start Server". (' + e.message + ')',
        },
      });
    }
  }
}

const rl = readline.createInterface({ input: process.stdin, terminal: false });
let pending = 0;
let closed = false;
rl.on('line', (line) => {
  line = line.trim();
  if (!line) return;
  let msg;
  try { msg = JSON.parse(line); } catch { log('bad json from client'); return; }
  pending++;
  forward(msg).finally(() => { pending--; if (closed && pending === 0) finish(); });
});
rl.on('close', () => { closed = true; if (pending === 0) finish(); });
// exit after a short delay so Node's HTTP handles are closed first (avoids a libuv assertion on Windows)
function finish() { setTimeout(() => process.exit(0), 300); }
