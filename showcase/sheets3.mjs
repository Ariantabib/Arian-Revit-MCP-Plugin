// Presentation sheets built from rendered images
import { call, log } from './lib.mjs';
await call('set_project_info', { values: { 'Project Name': 'Villa Arian', 'Project Number': '2026-ARIAN-01', 'Client Name': 'Arian Showcase', 'Project Address': 'Tehran, Iran', 'Project Status': 'Design Development', Author: 'Arian Revit MCP Plugin', 'Project Issue Date': '1405/07/24' } }, { soft: true }).then(r => log('info', r.error?.slice(0, 120) ?? 'ok'));
// remove the old image-less sheets
const q = await call('query_elements', { category: 'Sheets', limit: 200 });
const del = (q.elements || []).filter(e => /A-000|A-401|A-402|Cover|Exterior Views|Interior Views/.test(e.name || '')).map(e => e.id);
if (del.length) await call('delete_elements', { ids: del }, { soft: true });
log('deleted sheets', del.length);
