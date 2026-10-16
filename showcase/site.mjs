// Re-create topography + subregions (grade 250 below the ground slab so terraces read clearly)
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState(); const G = 'Ground Floor';
const old = await call('query_elements', { category: 'Topography' }); if (old.elements?.length) await call('delete_elements', { ids: old.elements.map(e => e.id) });
// ---- topography with building pad, paving, pool
const pts = [];
for (let x = -9000; x <= 29000; x += 4000) for (let y = -15000; y <= 20000; y += 5000) {
  let z = -250; if (y < -5800) z = -250 - Math.min(250, (-5800 - y) * 0.03); if (y > 13000) z = -250 + Math.min(900, (y - 13000) * 0.14);
  if (x < -5000 || x > 24000) z += 120; pts.push([x, y, Math.round(z)]);
}
for (const [x, y] of [[-60000, -60000], [-60000, 0], [-60000, 60000], [0, -60000], [0, 60000], [60000, -60000], [60000, 0], [60000, 60000], [-30000, -30000], [-30000, 30000], [30000, -30000], [30000, 30000], [-30000, 0], [30000, 0], [0, -30000], [0, 30000]]) pts.push([x, y, -250 + (Math.abs(y) > 40000 ? 400 : 0)]);
const topo = await call('create_toposurface', { points: pts });
S.topo = topo.id; log('topo', topo.id);
await call('set_element_material', { ids: [topo.id], material: 'Lawn Grass' }, { soft: true }).then(r => r.error && log('topo mat', r.error.slice(0, 120)));
const pad = await call('create_building_pad', { boundary: [[-1800, -5700], [21500, -5700], [21500, 12300], [-1800, 12300]], level: G }, { soft: true });
log('pad', pad.error ? pad.error.slice(0, 200) : 'ok');
const sub = async (name, poly, mat) => {
  const r = await call('create_site_subregion', { boundary: poly, topo_id: topo.id, material: mat }, { soft: true });
  log('subregion', name, r.error ? 'ERR ' + r.error.slice(0, 160) : 'ok');
};
await sub('driveway', [[15500, 17500], [21300, 17500], [21300, 12300], [15500, 12300]], 'Granite Paving');
await sub('entrance walk', [[8300, 12300], [11500, 12300], [11500, 17500], [8300, 17500]], 'سنگ تراورتن - Travertine');
await sub('pool', [[-1500, -9800], [9000, -9800], [9000, -6100], [-1500, -6100]], 'Pool Water');
saveState(S);
