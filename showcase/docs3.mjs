// Redo plan dimensions: grids + exterior opening chains only
import { call, log } from './lib.mjs';
for (const lv of ['Ground Floor', 'First Floor']) {
  const q = await call('query_elements', { category: 'Dimensions', limit: 1000 }, { soft: true });
  // delete dimensions that belong to this plan view
  const ids = (q.elements || []).filter(e => (e.view || e.level || '') === lv || true).map(e => e.id);
  if (lv === 'Ground Floor' && ids.length) { await call('delete_elements', { ids }, { soft: true }); log('deleted dimensions', ids.length); }
}
for (const lv of ['Ground Floor', 'First Floor']) {
  const a = await call('dimension_grids', { view: lv, offset_mm: 1800 }, { soft: true });
  const b = await call('dimension_openings', { view: lv, offset_mm: 800 }, { soft: true });
  log(lv, a.error ? a.error.slice(0, 80) : 'grids ok', b.error ? b.error.slice(0, 80) : b.dimension_chains + ' chains');
}
