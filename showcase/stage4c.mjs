// Stage 4c: lights per room (lux method), furniture and sanitary fixtures
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState();
const G = 'Ground Floor', F = 'First Floor';
const lux = { 'Living & Dining': 200, Kitchen: 300, Pantry: 150, Laundry: 200, Study: 300, 'Guest WC': 150, Storage: 100, 'Stair Hall': 100, 'Entrance Hall': 150, 'Master Bedroom': 150, Dressing: 150, 'Master Bath': 200, 'Home Office': 300, Landing: 100, 'Bedroom 2': 150, 'Bedroom 3': 150, Corridor: 100, 'Library Lounge': 200, 'Shared Bath': 200, 'Bedroom 4': 150 };
const ceilIds = (await call('query_elements', { category: 'Ceilings' })).elements.map(e => e.id);
const roomPts = {}; for (const [no, r] of Object.entries(S.rooms)) roomPts[no] = r;
const old = await call('query_elements', { category: 'Lighting Fixtures' });
if (old.elements?.length) await call('delete_elements', { ids: old.elements.map(e => e.id) });
const cmap = {};
for (const cid of ceilIds) {
  const e = await call('get_element', { id: cid });
  const bb = e.bbox_mm;
  for (const [no, r] of Object.entries(S.rooms)) {
    if (!r.pt) continue;
    const lvl = (e.level || '').startsWith('First') ? 'F' : 'G';
    if (!no.startsWith(lvl)) continue;
    if (r.pt[0] >= bb.min[0] && r.pt[0] <= bb.max[0] && r.pt[1] >= bb.min[1] && r.pt[1] <= bb.max[1]) cmap[no] = cid;
  }
}
log('ceiling map', JSON.stringify(cmap));
for (const [no, r] of Object.entries(S.rooms)) {
  const res = await call('place_lights_by_lux', { room_id: r.id, family_type: 'Arian ceiling light: 600x600', target_lux: lux[r.name] ?? 150, lumens_per_fixture: 3200, elevation_mm: 2900, host_id: cmap[no] }, { soft: true });
  log('lights', r.name, res.error ? 'ERR ' + res.error.slice(0, 120) : res.placed + ' fixtures ~' + res.achieved_lux + ' lux');
}
saveState(S);
