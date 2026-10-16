// Stage 4d: furniture, kitchen, sanitary fixtures
import { call, log } from './lib.mjs';
import fs from 'node:fs';
const G = 'Ground Floor', F = 'First Floor';
const items = [];
const add = (type, x, y, rot = 0, level = G) => items.push({ type, point: [x, y], level, rotation_deg: rot });
// ---- living & dining
add('Arian sofa: 3-seat 2300', 3300, 4700, 0); add('Arian sofa: 2-seat 1500', 900, 2700, 90); add('Arian table: 1200x800', 3300, 2900, 0);
add('Arian table: 1800x900', 8700, 2700, 0);
for (const x of [8100, 8700, 9300]) { add('Arian chair: Standard', x, 1900, 180); add('Arian chair: Standard', x, 3500, 0); }
add('Arian chair: Standard', 7500, 2700, 90); add('Arian chair: Standard', 9900, 2700, 270);
add('Arian wardrobe: 2400', 6800, 5700, 0); // TV / display unit against the north wall
// ---- kitchen
for (const x of [12000, 12800, 13600]) add('Arian kitchen base cabinet: 800', x, 3900, 0);
for (const y of [2700, 1900, 1100]) add('Arian kitchen base cabinet: 800', 14700, y, 270);
add('Arian kitchen base cabinet: 1000', 12900, 2100, 0); add('Arian kitchen base cabinet: 1000', 13900, 2100, 0);
// ---- entrance + study + wc
add('Arian table: 1200x800', 9900, 6600, 0); add('Arian wardrobe: 1000', 11200, 9100, 270);
add('Arian table: 1200x800', 1800, 9100, 0); add('Arian chair: Standard', 1800, 8300, 180); add('Arian wardrobe: 1800', 330, 8100, 90);
add('Arian wc: Standard', 4500, 8050, 0); add('Arian washbasin: 550', 3750, 7000, 90);
// ---- first floor: master
add('Arian bed: King 1800', 3000, 3000, 180, F); add('Arian table: 1200x800', 3000, -600, 0, F); add('Arian chair: Standard', 2200, -1000, 270, F);
add('Arian wardrobe: 1800', 330, 5700, 90, F); add('Arian wardrobe: 1200', 330, 5000, 90, F);
add('Arian bathtub: 1700', 900, 8700, 0, F); add('Arian wc: Standard', 2400, 9850, 0, F); add('Arian washbasin: 650', 2800, 8300, 270, F);
// ---- bedrooms
add('Arian bed: Queen 1600', 8400, 1300, 180, F); add('Arian wardrobe: 1800', 10470, 300, 270, F);
add('Arian bed: Double 1400', 12900, 1300, 180, F); add('Arian wardrobe: 1200', 14670, -600, 270, F);
add('Arian bed: Double 1400', 13200, 9000, 180, F); add('Arian wardrobe: 1200', 14670, 7000, 270, F);
// ---- office, library, shared bath
add('Arian table: 1600x800', 4200, 7600, 90, F); add('Arian chair: Standard', 3500, 7600, 90, F); add('Arian wardrobe: 1800', 5250, 9000, 270, F);
add('Arian sofa: 3-seat 2300', 9900, 8900, 0, F); add('Arian table: 1200x800', 9900, 7200, 0, F); add('Arian sofa: 2-seat 1500', 8700, 6500, 90, F);
add('Arian wc: Standard', 13900, 5700, 0, F); add('Arian washbasin: 800', 12000, 5700, 0, F);
const r = await call('place_family_instances', { items });
log('furniture placed', r.placed ?? r.count, 'errors', JSON.stringify(r.errors ?? r.failed ?? []).slice(0, 600));

await call('execute_csharp', { code: fs.readFileSync(new URL('./matassign.cs', import.meta.url), 'utf8'), transaction: true });
log('materials assigned to family types');
