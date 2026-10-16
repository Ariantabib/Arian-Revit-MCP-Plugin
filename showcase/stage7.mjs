// Stage 7: roof terrace deck with furniture
import { call, log } from './lib.mjs';
const R = 'Roof';
await call('create_floor', { boundary: [[8600, 4400], [14700, 4400], [14700, 9900], [8600, 9900]], floor_type: 'Arian Upper Slab 265', level: R, offset_mm: 520, structural: false });
const items = []; const add = (type, x, y, rot = 0) => items.push({ type, point: [x, y], level: R, rotation_deg: rot, elevation_mm: 520 });
add('Arian table: 1800x900', 11600, 6800, 0);
for (const x of [11000, 11600, 12200]) { add('Arian chair: Standard', x, 5950, 180); add('Arian chair: Standard', x, 7650, 0); }
add('Arian sofa: 3-seat 2300', 11600, 9300, 0); add('Arian sofa: 2-seat 1500', 9200, 7000, 90);
add('Arian bed: Single 900', 13900, 8600, 90); add('Arian bed: Single 900', 13900, 7500, 90);
for (const [x, y] of [[9000, 4800], [14300, 4800], [9000, 9500], [14300, 9500]]) items.push({ type: 'Arian Bush: Shrub 1.1m', point: [x, y], level: R, elevation_mm: 520 });
const r = await call('place_family_instances', { items }, { soft: true });
log('roof terrace', r.placed ?? JSON.stringify(r).slice(0, 200));
