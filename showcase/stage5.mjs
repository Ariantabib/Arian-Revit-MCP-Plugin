// Stage 5: facade extras, site, landscaping
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState();
const G = 'Ground Floor';
// ---- materials used on site / glazing
await call('create_material', { name: 'Pool Water', color: { r: 70, g: 160, b: 190 }, transparency: 45, material_class: 'Generic' });
await call('create_material', { name: 'Lawn Grass', color: { r: 96, g: 150, b: 68 }, material_class: 'Plant' });
await call('create_material', { name: 'Granite Paving', color: { r: 128, g: 130, b: 134 }, material_class: 'Stone' });
// ---- appearances of the materials that came with the loaded families
for (const [name, r, g, b] of [['Foliage Green', 82, 128, 62], ['Tree Bark', 98, 74, 52], ['Cypress Green', 40, 84, 52], ['Shrub Green', 96, 142, 70], ['Fin Timber', 160, 112, 70], ['Window Frame Anthracite', 62, 68, 74], ['Door Leaf Oak', 176, 130, 84], ['Door Frame White', 238, 238, 234], ['Door Handle Steel', 190, 192, 196]])
  await call('create_material', { name, color: { r, g, b } }, { soft: true });
await call('create_material', { name: 'Window Glass', color: { r: 175, g: 210, b: 225 }, transparency: 70, material_class: 'Glass' });
await call('set_material_appearance', { material: 'Window Glass', glossiness: 95, reflectivity: 35 }, { soft: true });
await call('set_material_appearance', { material: 'Pool Water', color: { r: 56, g: 152, b: 186 }, glossiness: 92, reflectivity: 40 }, { soft: true });
await call('set_material_appearance', { material: 'Lawn Grass', color: { r: 104, g: 142, b: 76 }, glossiness: 5 }, { soft: true });
await call('set_material_appearance', { material: 'آجر نما - Facing Brick', color: { r: 146, g: 96, b: 78 }, glossiness: 8 }, { soft: true });
// ---- curtain wall glazing + mullion materials (type level)
const code = `
var res = new List<string>();
var glass = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name.Contains("Clear Glass"));
var frame = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == "Window Frame Anthracite");
foreach (var t in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CurtainWallPanels).WhereElementIsElementType())
{ var p = t.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); if (p != null && !p.IsReadOnly && glass != null) { p.Set(glass.Id); res.Add("panel " + t.Name); } }
foreach (var t in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CurtainWallMullions).WhereElementIsElementType())
{ var p = t.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); if (p != null && !p.IsReadOnly && frame != null) { p.Set(frame.Id); res.Add("mullion " + t.Name); } }
return res;`;
const cw = await call('execute_csharp', { code, transaction: true }, { soft: true });
log('curtain materials', JSON.stringify(cw).slice(0, 300));
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
const sub = async (name, poly, mat) => {
  const r = await call('create_site_subregion', { boundary: poly, topo_id: topo.id, material: mat }, { soft: true });
  log('subregion', name, r.error ? 'ERR ' + r.error.slice(0, 160) : 'ok');
};
await sub('driveway', [[15500, 17500], [21300, 17500], [21300, 12300], [15500, 12300]], 'Granite Paving');
await sub('entrance walk', [[8300, 12300], [11500, 12300], [11500, 17500], [8300, 17500]], 'سنگ تراورتن - Travertine');
await sub('pool', [[-1500, -9800], [9000, -9800], [9000, -6100], [-1500, -6100]], 'Pool Water');
// ---- trees, cypress, bushes
const P = [];
const t = (type, x, y) => P.push({ type, point: [x, y], level: G, elevation_mm: -250 });
for (const [x, y] of [[-4200, -2500], [-4600, 6500], [24200, 14500], [12800, -10500], [-3500, 13500]]) t('Arian Tree Round: Tree 7.8m', x, y);
for (let y = -12500; y <= 14000; y += 2600) t('Arian Cypress: Cypress 8.4m', -7200, y);
for (let x = -7000; x <= 6000; x += 2400) t('Arian Cypress: Cypress 8.4m', x, 18500);
for (const [x, y] of [[-900, -4200], [11000, -4300], [13200, -1500], [15700, -300], [800, 11400], [6500, 11500], [12300, 11500], [14200, 11500]]) t('Arian Bush: Shrub 1.1m', x, y);
const pr = await call('place_family_instances', { items: P }, { soft: true });
log('plants', pr.placed ?? JSON.stringify(pr).slice(0, 200));
// ---- timber slat screen east of the south terrace
const fins = [];
for (let y = -5100; y <= -2100; y += 330) fins.push({ type: 'Arian Timber Fin: Fin 2800', point: [12150, y], level: G, rotation_deg: 90 });
const fr = await call('place_family_instances', { items: fins }, { soft: true });
log('fins', fr.placed ?? JSON.stringify(fr).slice(0, 200));
saveState(S);
