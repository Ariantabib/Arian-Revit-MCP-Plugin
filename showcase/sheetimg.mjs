import { call, log } from './lib.mjs';
const list = (await call('list_sheets', {})).sheets;
for (const num of process.argv.slice(2)) {
  const s = list.find(x => x.number === num);
  const id = s?.id ?? (await call('query_elements', { category: 'Sheets' })).elements?.find(e => e.name?.includes(num))?.id;
  const r = await call('render_view_image', { view: String(id ?? num), width_px: 2400, path: `C:/Users/arian/Documents/ArianRenders/villa/sheet_${num}.png` }, { soft: true });
  log(num, r.error ? 'ERR ' + r.error.slice(0, 120) : r.file);
}
