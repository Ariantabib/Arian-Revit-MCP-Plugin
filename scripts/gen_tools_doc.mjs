#!/usr/bin/env node
// Generates docs/TOOLS.md from the live server: node scripts/gen_tools_doc.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const URL_ = process.env.ARIAN_REVIT_MCP_URL || 'http://127.0.0.1:8765/mcp';
const res = await fetch(URL_, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }) });
const tools = (await res.json()).result.tools;

const byCat = {};
for (const t of tools) {
  const m = t.description.match(/^\[([^\]]+)\]\s*/);
  const cat = m ? m[1] : 'Other';
  (byCat[cat] ||= []).push({ name: t.name, desc: t.description.replace(/^\[[^\]]+\]\s*/, ''), schema: t.inputSchema, ro: t.annotations?.readOnlyHint, destructive: t.annotations?.destructiveHint });
}
const order = ['Session', 'Query', 'Architecture', 'Structure', 'MEP', 'Family', 'Material', 'View', 'Modify', 'Manage', 'Advanced', 'Custom'];
const cats = Object.keys(byCat).sort((a, b) => (order.indexOf(a) < 0 ? 99 : order.indexOf(a)) - (order.indexOf(b) < 0 ? 99 : order.indexOf(b)));

let md = `# Arian Revit MCP Plugin — tool reference\n\n${tools.length} tools. All lengths are millimetres, angles degrees, areas m², volumes m³.\n\n`;
md += '| Category | Tools |\n|---|---|\n' + cats.map(c => `| ${c} | ${byCat[c].length} |`).join('\n') + '\n\n';
for (const c of cats) {
  md += `## ${c}\n\n`;
  for (const t of byCat[c].sort((a, b) => a.name.localeCompare(b.name))) {
    const props = t.schema?.properties || {};
    const req = new Set(t.schema?.required || []);
    const args = Object.keys(props).filter(k => k !== 'document').map(k => (req.has(k) ? `**${k}**` : k)).join(', ');
    const first = t.desc.split('\n')[0].trim();
    md += `### \`${t.name}\`${t.ro ? ' _(read-only)_' : ''}${t.destructive ? ' _(destructive)_' : ''}\n${first}\n\n` + (args ? `Arguments: ${args}\n\n` : '');
  }
}
const out = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'docs', 'TOOLS.md');
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, md);
console.log('wrote', out, tools.length, 'tools');
