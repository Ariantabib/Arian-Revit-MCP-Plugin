import { call, log } from './lib.mjs';
const q = await call('query_elements', { category: 'Planting', limit: 500 });
const ids = q.elements.map(e => e.id);
const r = await call('move_elements', { ids, vector: [0, 0, Number(process.argv[2] ?? -250)] });
log('moved', ids.length, JSON.stringify(r).slice(0, 100));
