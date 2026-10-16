// Stage 3b: slab opening, roof, terraces, balcony, canopy, carport, doors, windows
import { call, log, loadState, saveState, view } from './lib.mjs';
const S = loadState(); const W = S.walls;
const G = 'Ground Floor', F = 'First Floor', R = 'Roof';
// ---- recreate the first-floor slab with the stairwell opening (stairs head is at y~6670)
const fl = (await call('query_elements', { category: 'Floors' })).elements.find(e => e.level === F);
if (fl) await call('delete_elements', { ids: [fl.id] });
await call('create_floor', { boundary: [[0, -1800], [15000, -1800], [15000, 10200], [0, 10200]], floor_type: 'Arian Upper Slab 265', level: F, structural: true,
  openings: [[[5475, 6700], [8325, 6700], [8325, 10125], [5475, 10125]]] });
await call('create_railing', { path: [[5475, 6700], [6850, 6700]], railing_type: 'Glass Panel - Bottom Fill', level: F }, { soft: true }).then(r => log('guard', r.error ? r.error.slice(0, 120) : 'ok'));
// ---- parapet: raise upper exterior walls 600 above the roof level, then the roof
for (const k of ['u_s', 'u_e', 'u_n1', 'u_n2', 'u_n3', 'u_w']) await call('set_wall_constraints', { ids: [W[k]], top_offset_mm: 600 });
const roof = await call('create_roof', { boundary: [[0, -1800], [15000, -1800], [15000, 10200], [0, 10200]], roof_type: 'Arian Flat Roof 370', level: R, flat: true });
S.roof = roof.id; log('roof', roof.id);
// ---- terraces, canopy, balcony
await call('create_floor', { boundary: [[-1500, -5400], [12300, -5400], [12300, 0], [-1500, 0]], floor_type: 'Arian Terrace Slab 280', level: G, structural: true });
await call('create_floor', { boundary: [[8100, 10200], [11700, 10200], [11700, 12000], [8100, 12000]], floor_type: 'Arian Terrace Slab 280', level: G, offset_mm: 3000, structural: true }); // entrance canopy
await call('create_floor', { boundary: [[8100, 10200], [11700, 10200], [11700, 11800], [8100, 11800]], floor_type: 'Arian Terrace Slab 280', level: F, structural: true });   // balcony
await call('create_railing', { path: [[8100, 10200], [8100, 11800], [11700, 11800], [11700, 10200]], railing_type: 'Glass Panel - Bottom Fill', level: F }, { soft: true }).then(r => log('balcony rail', r.error ? r.error.slice(0, 120) : 'ok'));
// ---- carport (slab roof on four round columns)
await call('create_floor', { boundary: [[15000, 2400], [21300, 2400], [21300, 9000], [15000, 9000]], floor_type: 'Arian Terrace Slab 280', level: F, structural: true });
for (const [x, y] of [[20900, 2800], [20900, 8600], [17500, 2800], [17500, 8600]])
  await call('place_family_instance', { spec: { type: 'Arian round column: D400', point: [x, y], level: G, top_level: F } }, { soft: true }).then(r => r.error && log('col', r.error.slice(0, 120)));
saveState(S);
// ---- doors
const D = async (type, wall, pt, level = G, extra = {}) => {
  const r = await call('place_family_instance', { spec: { type, point: pt, level, host_id: W[wall], ...extra } }, { soft: true });
  if (r.error) log('FAIL', type, wall, pt, r.error.slice(0, 160));
  return r;
};
const d1 = t => 'Arian Villa Door: ' + t, d2 = t => 'Arian Villa Door Double: ' + t, wn = t => 'Arian Villa Window: ' + t;
await D(d2('1800 x 2400'), 'g_n2', [9900, 10200]);
await D(d2('1500 x 2100'), 'gi_y6c', [9900, 6000]);
await D(d2('1800 x 2400'), 'gi_y6b', [6900, 6000]);
await D(d2('1200 x 2100'), 'gi_x8400', [8400, 8100]);
await D(d1('900 x 2100'), 'gi_y6a', [1800, 6000]);
await D(d1('800 x 2100'), 'gi_x5400', [5400, 7200]);
await D(d1('800 x 2100'), 'gi_x5400', [5400, 9300]);
await D(d1('900 x 2100'), 'gi_y4200', [13200, 4200]);
await D(d1('800 x 2100'), 'gi_y6000k', [13200, 6000]);
await call('create_wall_opening', { wall_id: W.gi_x11400a, corner1: [11400, 1000, 0], corner2: [11400, 3400, 2400] }, { soft: true });
// first floor
await D(d1('1000 x 2100'), 'ui_x6000a', [6000, 3300], F);
await D(d1('900 x 2100'), 'ui_y4200a', [1500, 4200], F);
await D(d1('800 x 2100'), 'ui_y7200', [1500, 7200], F);
await D(d1('900 x 2100'), 'ui_x5400', [5400, 5400], F);
await D(d1('900 x 2100'), 'ui_y2400', [7800, 2400], F);
await D(d1('900 x 2100'), 'ui_y2400', [13200, 2400], F);
await call('create_wall_opening', { wall_id: W.ui_x8400, corner1: [8400, 6000, 3200], corner2: [8400, 8400, 5600] }, { soft: true });
await call('create_wall_opening', { wall_id: W.ui_y4200b, corner1: [6300, 4200, 3200], corner2: [8200, 4200, 5600] }, { soft: true });
await D(d1('900 x 2100'), 'ui_x11400', [11400, 7200], F);
await D(d1('800 x 2100'), 'ui_y4200b', [13200, 4200], F);
await D(d1('800 x 2100'), 'ui_y6000', [13200, 6000], F);
await D(d2('1800 x 2400'), 'u_n2', [9900, 10200], F);
// ---- windows
await D(wn('1500 x 1500'), 'g_s3', [13200, 0], G, { elevation_mm: 900 });
await D(wn('2400 x 1500'), 'g_w', [0, 3000], G, { elevation_mm: 600 });
await D(wn('1500 x 1500'), 'g_w', [0, 8100], G, { elevation_mm: 900 });
await D(wn('1200 x 1200'), 'g_e', [15000, 2100], G, { elevation_mm: 1000 });
await D(wn('1200 x 1200'), 'g_e', [15000, 8100], G, { elevation_mm: 1200 });
await D(wn('1200 x 1200'), 'g_n3', [1800, 10200], G, { elevation_mm: 1200 });
await D(wn('1500 x 1500'), 'g_n3', [6900, 10200], G, { elevation_mm: 1000 });
await D(wn('1200 x 1200'), 'g_n1', [13200, 10200], G, { elevation_mm: 1200 });
await D(wn('2400 x 1500'), 'u_s', [1500, -1800], F, { elevation_mm: 700 });
await D(wn('2400 x 1500'), 'u_s', [4500, -1800], F, { elevation_mm: 700 });
await D(wn('2400 x 1500'), 'u_s', [8400, -1800], F, { elevation_mm: 700 });
await D(wn('2400 x 1500'), 'u_s', [12900, -1800], F, { elevation_mm: 700 });
await D(wn('1500 x 1500'), 'u_w', [0, 1200], F, { elevation_mm: 900 });
await D(wn('600 x 600'), 'u_w', [0, 8700], F, { elevation_mm: 1500 });
await D(wn('1200 x 1200'), 'u_e', [15000, 300], F, { elevation_mm: 1000 });
await D(wn('600 x 600'), 'u_e', [15000, 5100], F, { elevation_mm: 1500 });
await D(wn('1500 x 1500'), 'u_e', [15000, 8100], F, { elevation_mm: 900 });
await D(wn('1200 x 1200'), 'u_n3', [4200, 10200], F, { elevation_mm: 900 });
await D(wn('600 x 600'), 'u_n3', [1500, 10200], F, { elevation_mm: 1500 });
await D(wn('1500 x 1500'), 'u_n1', [13200, 10200], F, { elevation_mm: 900 });
saveState(S);
await view('openings', 'iso_se');
await view('openings_nw', 'iso_nw');
