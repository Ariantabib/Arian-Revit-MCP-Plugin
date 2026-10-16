#!/usr/bin/env node
// Runs a JSON scenario against the live plugin: node scripts/smoke.mjs scenario.json
// scenario = [ ["tool", {args}, "saveAs"?, {"expectError":"substring"}?, ... ]
//   "saveAs" stores the parsed JSON result so later args can use "$name.path.0.id".
//   {"expectError":"text"} turns the step into a negative test: it passes only if the call fails and the error contains "text" ("" = any error).
//   {"expectContains":"text"} passes only if the result text contains "text".
//   ["__sleep", {"ms":3000}] waits (e.g. after run_dynamo_graph, which is asynchronous).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const URL_ = process.env.ARIAN_REVIT_MCP_URL || 'http://127.0.0.1:8765/mcp';
const steps = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const maxLen = Number(process.env.MAXLEN || 220);
let ok = 0, bad = 0;
const saved = {};

function sub(v) {
  if (typeof v === 'string' && v.startsWith('$')) { const [k, ...p] = v.slice(1).split('.'); let o = saved[k]; for (const x of p) o = o?.[x]; return o; }
  if (Array.isArray(v)) return v.map(sub);
  if (v && typeof v === 'object') return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, sub(x)]));
  return v;
}

for (const st of steps) {
  const [tool, args, saveAs, opts] = st;
  if (tool === '__sleep') { await new Promise(r => setTimeout(r, (args && args.ms) || 1000)); continue; }
  const t0 = Date.now();
  let out;
  try {
    const res = await fetch(URL_, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: tool, arguments: sub(args || {}) } }) });
    const j = await res.json();
    if (j.error) out = { err: JSON.stringify(j.error) };
    else {
      const texts = j.result.content.filter(c => c.type === 'text').map(c => c.text).join('\n');
      const img = j.result.content.find(c => c.type === 'image');
      if (img) { const p = path.join(os.tmpdir(), `arian-smoke-${Date.now()}.png`); fs.writeFileSync(p, Buffer.from(img.data, 'base64')); out = { text: texts + ' IMAGE:' + p }; }
      else out = j.result.isError ? { err: texts } : { text: texts };
      if (!out.err && saveAs) { try { saved[saveAs] = JSON.parse(texts); } catch { } }
    }
  } catch (e) { out = { err: e.message }; }
  const ms = Date.now() - t0;
  if (opts && opts.expectError !== undefined) {
    if (out.err && out.err.includes(opts.expectError)) { ok++; console.log(`✓ ${tool} (${ms}ms): expected error: ${out.err.slice(0, 160).replace(/\n/g, ' ')}`); }
    else { bad++; console.log(`✗ ${tool} (${ms}ms): expected error containing "${opts.expectError}" but got ${out.err ? out.err.slice(0, 300) : 'success: ' + out.text.slice(0, 200)}`); }
    continue;
  }
  if (!out.err && opts && opts.expectContains !== undefined && !out.text.includes(opts.expectContains)) { bad++; console.log(`✗ ${tool} (${ms}ms): result lacks "${opts.expectContains}": ${out.text.slice(0, 300)}`); continue; }
  if (out.err) { bad++; console.log(`✗ ${tool} (${ms}ms): ${out.err.slice(0, 700)}`); }
  else { ok++; console.log(`✓ ${tool} (${ms}ms): ${out.text.slice(0, maxLen).replace(/\n/g, ' ')}`); }
}
console.log(`\n${ok} ok, ${bad} failed`);
