#!/usr/bin/env node
// Developer helper: call one MCP tool on the running plugin.
//   node scripts/call.mjs <tool> '<json args>'         (args may be omitted)
//   node scripts/call.mjs --file <tool> <args.json>
// Images are saved to %TEMP%/arian-image-N.png and the path is printed.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const URL_ = process.env.ARIAN_REVIT_MCP_URL || 'http://127.0.0.1:8765/mcp';
let [, , a, b, c] = process.argv;
let tool, args;
if (a === '--cs') { // run a C# file through execute_csharp; optional 3rd arg "tx" keeps the single-transaction wrapper
  tool = 'execute_csharp'; args = { code: fs.readFileSync(b, 'utf8'), transaction: c === 'tx' };
} else if (a === '--file') { tool = b; args = JSON.parse(fs.readFileSync(c, 'utf8')); }
else { tool = a; args = b ? JSON.parse(b) : {}; }

const body = tool === 'tools/list'
  ? { jsonrpc: '2.0', id: 1, method: 'tools/list' }
  : { jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: tool, arguments: args } };
const res = await fetch(URL_, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const j = await res.json();
if (j.error) { console.log('RPC ERROR', JSON.stringify(j.error)); process.exit(1); }
if (tool === 'tools/list') { for (const t of j.result.tools) console.log(t.name.padEnd(34), t.description.slice(0, 90)); console.log(j.result.tools.length, 'tools'); process.exit(0); }
let n = 0;
for (const c of j.result.content) {
  if (c.type === 'text') console.log((j.result.isError ? 'ERROR: ' : '') + c.text);
  else if (c.type === 'image') { const p = path.join(os.tmpdir(), `arian-image-${Date.now()}-${n++}.png`); fs.writeFileSync(p, Buffer.from(c.data, 'base64')); console.log('IMAGE saved:', p); }
}
