import { call, log } from './lib.mjs';
await call('set_project_info', { values: { 'Client Name': 'Arian Showcase' } }, { soft: true });
const q = await call('query_elements', { category: 'Sheets', limit: 200 });
const del = (q.elements || []).filter(e => /Cover|Exterior Views|Interior & Aerial/.test(e.name || '')).map(e => e.id);
await call('delete_elements', { ids: del }, { soft: true }); log('deleted', del.length);
