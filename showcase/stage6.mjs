// Stage 6: boundary walls, terrace furniture
import { call, log } from './lib.mjs';
const G = 'Ground Floor';
const wallp = async (pts, h = 1800) => { for (let i = 0; i + 1 < pts.length; i++) { const r = await call('create_wall', { start: pts[i], end: pts[i + 1], wall_type: 'Arian Travertine Wall 250', base_level: G, height_mm: h, base_offset_mm: -250 }, { soft: true }); if (r.error) log('wall', r.error.slice(0, 100)); } };
await wallp([[-7000, 16800], [8300, 16800]]); await wallp([[11500, 16800], [15500, 16800]]); await wallp([[21300, 16800], [26000, 16800]]);
await wallp([[-7000, 16800], [-7000, -9000]], 1500);
const items = []; const add = (type, x, y, rot = 0) => items.push({ type, point: [x, y], level: G, rotation_deg: rot, elevation_mm: 0 });
add('Arian table: 1800x900', 3200, -3200, 0);
for (const x of [2600, 3200, 3800]) { add('Arian chair: Standard', x, -4000, 180); add('Arian chair: Standard', x, -2400, 0); }
add('Arian chair: Standard', 2000, -3200, 90); add('Arian chair: Standard', 4400, -3200, 270);
add('Arian sofa: 2-seat 1500', 7600, -3600, 0); add('Arian table: 1200x800', 7600, -2400, 0);
const r = await call('place_family_instances', { items });
log('terrace furniture', r.placed);
// outdoor pieces sit on the terrace slab (top at 0): nothing to lower
