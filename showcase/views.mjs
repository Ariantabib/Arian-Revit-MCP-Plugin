// Perspective camera views + renders
import { call, log } from './lib.mjs';
import fs from 'node:fs';
const hideSub = fs.readFileSync(new URL('./hidesub.cs', import.meta.url), 'utf8');
const cams = {
  'V01 Hero South-East': { eye: [28000, -12000, 3000], target: [6500, 2500, 2800] },
  'V02 Garden Terrace': { eye: [-4500, -10500, 1700], target: [7000, 0, 2300] },
  'V03 Entrance North': { eye: [9900, 27000, 1700], target: [9900, 10200, 2200] },
  'V04 Aerial North-East': { eye: [34000, 25000, 24000], target: [8000, 3500, 1500] },
  'V05 Living Room': { eye: [800, 5600, 1500], target: [8800, 1200, 1100] },
  'V06 Master Bedroom': { eye: [5700, 3900, 4700], target: [1500, -800, 4000] },
  'V07 Stair Hall': { eye: [5700, 6300, 1700], target: [7200, 9000, 2400] },
  'V09 Cover': { eye: [27000, -20000, 7000], target: [7000, 3000, 2800] },
  'V08 Aerial South-East': { eye: [33000, -27000, 22000], target: [8000, 2000, 1500] },
};
for (const [name, c] of Object.entries(cams)) {
  await call('delete_view', { view: name }, { soft: true });
  const r = await call('create_3d_view', { name, perspective: true, eye: c.eye, target: c.target }, { soft: true });
  if (r.error) { log('VIEW FAIL', name, r.error.slice(0, 160)); continue; }
  for (const c of ['Levels', 'Grids', 'Elevations', 'Sections', 'Callouts', 'Scope Boxes', 'Reference Planes']) await call('set_category_visibility', { view: name, categories: [c], visible: false }, { soft: true });
  await call('set_view_environment', { view: name, shadows: name.includes('V05') || name.includes('V06') || name.includes('V07') ? 28 : 80, background: 'gradient', date: '2026-04-20', hour: name.includes('V03') ? 11 : 16 }, { soft: true });
  await call('execute_csharp', { code: hideSub, transaction: true }, { soft: true });
  await call('set_view_visual_style', { view: name, style: 'realistic' });
  await call('set_view_properties', { view: name, detail_level: 'fine' }, { soft: true });
  const out = await call('render_view_image', { view: name, width_px: 2200, path: `C:/Users/arian/Documents/ArianRenders/villa/${name.replace(/ /g, '_')}.png` }, { soft: true });
  log(name, out.error ? 'ERR ' + out.error.slice(0, 150) : out.file);
}
