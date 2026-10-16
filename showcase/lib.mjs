// Tiny helper for the showcase build scripts: call MCP tools on the running Revit plugin.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
const URL_ = process.env.ARIAN_REVIT_MCP_URL || 'http://127.0.0.1:8765/mcp';
let n = 0;
export async function call(tool, args = {}, { soft = false } = {}) {
  const res = await fetch(URL_, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: ++n, method: 'tools/call', params: { name: tool, arguments: args } }) });
  const j = await res.json();
  if (j.error) throw new Error(`${tool}: RPC ${JSON.stringify(j.error)}`);
  let text = '', image = null;
  for (const c of j.result.content) {
    if (c.type === 'text') text += c.text;
    else if (c.type === 'image') { image = path.join(os.tmpdir(), `arian-showcase-${Date.now()}-${n}.png`); fs.writeFileSync(image, Buffer.from(c.data, 'base64')); }
  }
  if (j.result.isError) { if (soft) return { error: text }; throw new Error(`${tool}: ${text.slice(0, 600)}`); }
  let data; try { data = JSON.parse(text); } catch { data = { text }; }
  if (image) data.image = image;
  return data;
}
export const log = (...a) => console.log(...a);
const SF = new URL('./state.json', import.meta.url);
export function loadState() { try { return JSON.parse(fs.readFileSync(SF, 'utf8')); } catch { return {}; } }
export function saveState(s) { fs.writeFileSync(SF, JSON.stringify(s, null, 1)); }
export async function view(name, dir = 'iso_se', opts = {}) {
  const r = await call('snapshot_3d', { direction: dir, width_px: 1500, ...opts });
  console.log('SNAP', name, r.image); return r.image;
}
export async function real(name, dir = 'iso_se', width = 1800) {
  await call('snapshot_3d', { direction: dir, width_px: 600 });   // creates/frames the 'Arian Snapshot' view
  await call('set_category_visibility', { view: 'Arian Snapshot', categories: ['Roofs'], visible: true }, { soft: true });
  await call('set_view_visual_style', { view: 'Arian Snapshot', style: 'realistic' });
  const r = await call('render_view_image', { view: 'Arian Snapshot', width_px: width, path: `C:/Users/arian/Documents/ArianRenders/villa/${name}.png` });
  console.log('REAL', name, r.file); return r.file;
}
