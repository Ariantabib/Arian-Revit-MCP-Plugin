// Stage 2: grids, ground + first floor walls, slabs
import { call, log, loadState, saveState, view } from './lib.mjs';
const S = loadState(); S.walls = {};
const G = 'Ground Floor', F = 'First Floor', R = 'Roof';
const BR = 'Arian Brick Wall 370', WH = 'Arian White Render 310', TI = 'Arian Timber Clad 320', IN = 'Arian Interior 150', TR = 'Arian Travertine Wall 250';
async function wall(name, a, b, type, base, top) {
  const r = await call('create_wall', { start: a, end: b, wall_type: type, base_level: base, top_level: top });
  S.walls[name] = r.id; return r.id;
}
// grids
for (const [n, x] of [['A', 0], ['B', 7800], ['C', 15000]]) await call('create_grid', { start: [x, -4500], end: [x, 12000], name: n });
for (const [n, y] of [['1', 0], ['2', 6000], ['3', 10200]]) await call('create_grid', { start: [-2000, y], end: [17000, y], name: n });
// ---------- ground floor exterior
await wall('g_s1', [0, 0], [1200, 0], BR, G, F);
await wall('g_s3', [10800, 0], [15000, 0], BR, G, F);
await wall('g_e', [15000, 0], [15000, 10200], BR, G, F);
await wall('g_n1', [15000, 10200], [11400, 10200], BR, G, F);
await wall('g_n2', [11400, 10200], [8400, 10200], TI, G, F);
await wall('g_n3', [8400, 10200], [0, 10200], BR, G, F);
await wall('g_w', [0, 10200], [0, 0], BR, G, F);
// curtain wall living / dining
const cw = await call('create_curtain_wall', { start: [1200, 0], end: [10800, 0], base_level: G, top_level: F, vertical_spacing_mm: 1600, horizontal_spacing_mm: 1600, mullion_type: '30mm Square', curtain_wall_type: 'Storefront' }, { soft: true });
log('curtain', JSON.stringify(cw).slice(0, 200)); S.curtain = cw.id ?? cw.wall_id ?? null;
// ---------- ground floor interior
await wall('gi_y6a', [0, 6000], [5400, 6000], IN, G, F);       // living | study+WC (door to study)
await wall('gi_y6b', [5400, 6000], [8400, 6000], IN, G, F);     // living | stair hall (opening)
await wall('gi_y6c', [8400, 6000], [11400, 6000], IN, G, F);    // dining | entrance
await wall('gi_x3600', [3600, 6000], [3600, 10200], IN, G, F);  // study | wc
await wall('gi_x5400', [5400, 6000], [5400, 10200], IN, G, F);  // wc/storage | stair hall
await wall('gi_y8400', [3600, 8400], [5400, 8400], IN, G, F);   // wc | storage
await wall('gi_x8400', [8400, 6000], [8400, 10200], IN, G, F);  // stair hall | entrance
await wall('gi_x11400a', [11400, 0], [11400, 4200], IN, G, F);  // dining | kitchen
await wall('gi_x11400b', [11400, 4200], [11400, 10200], IN, G, F);
await wall('gi_y4200', [11400, 4200], [15000, 4200], IN, G, F);
await wall('gi_y6000k', [11400, 6000], [15000, 6000], IN, G, F);
saveState(S);
// ---------- first floor exterior (cantilevered volume: y from -1800)
await wall('u_s', [0, -1800], [15000, -1800], WH, F, R);
await wall('u_e', [15000, -1800], [15000, 10200], WH, F, R);
await wall('u_n1', [15000, 10200], [11400, 10200], WH, F, R);
await wall('u_n2', [11400, 10200], [8400, 10200], TI, F, R);
await wall('u_n3', [8400, 10200], [0, 10200], WH, F, R);
await wall('u_w', [0, 10200], [0, -1800], WH, F, R);
// first floor interior
await wall('ui_x6000a', [6000, -1800], [6000, 4200], IN, F, R);
await wall('ui_y4200a', [0, 4200], [6000, 4200], IN, F, R);       // master | dressing
await wall('ui_y7200', [0, 7200], [3000, 7200], IN, F, R);        // dressing | bath
await wall('ui_x3000', [3000, 4200], [3000, 10200], IN, F, R);    // suite | office
await wall('ui_x5400', [5400, 4200], [5400, 10200], IN, F, R);    // office | landing
await wall('ui_x10800', [10800, -1800], [10800, 2400], IN, F, R); // bed2 | bed3
await wall('ui_y2400', [6000, 2400], [15000, 2400], IN, F, R);    // beds | corridor
await wall('ui_y4200b', [6000, 4200], [15000, 4200], IN, F, R);   // corridor | north rooms
await wall('ui_x8400', [8400, 4200], [8400, 10200], IN, F, R);    // landing | lounge (opening)
await wall('ui_x11400', [11400, 4200], [11400, 10200], IN, F, R); // lounge | bed4/bath
await wall('ui_y6000', [11400, 6000], [15000, 6000], IN, F, R);   // bath | bed4
saveState(S);
// make every exterior wall face outward (exterior layers outside) and mark interior walls
import fs from 'node:fs';
await call('execute_csharp', { code: fs.readFileSync(new URL('./fixwalls.cs', import.meta.url), 'utf8'), transaction: true });
// ---------- slabs
await call('create_floor', { boundary: [[0, 0], [15000, 0], [15000, 10200], [0, 10200]], floor_type: 'Arian Ground Slab 265', level: G, structural: true });
await call('create_floor', { boundary: [[0, -1800], [15000, -1800], [15000, 10200], [0, 10200]], floor_type: 'Arian Upper Slab 265', level: F, structural: true });
saveState(S);
await view('walls');
