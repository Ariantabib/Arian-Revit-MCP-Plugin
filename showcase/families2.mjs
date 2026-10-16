// Landscape + facade accessory families: tree, cypress, bush, timber fin
import { call, log } from './lib.mjs';
async function fam(name, template, build) {
  await call('family_create', { name, template }, { soft: true });
  try { await build(name); } finally { }
  const s = await call('family_save', { document: name }, { soft: true });
  const l = await call('family_load_into_project', { document: name, close: true }, { soft: true });
  log(name, l.error ? 'ERR ' + l.error.slice(0, 200) : 'loaded');
}
const rev = (d, pts, mat, name) => call('family_add_revolve', { document: d, profile_rz: pts, angle_deg: 360, axis_xy: [0, 0], material: mat, name });
await fam('Arian Tree Round', 'planting', async d => {
  await call('family_add_material', { document: d, name: 'Foliage Green', color: { r: 82, g: 128, b: 62 } });
  await call('family_add_material', { document: d, name: 'Tree Bark', color: { r: 98, g: 74, b: 52 } });
  await call('family_add_cylinder', { document: d, radius_mm: 160, height_mm: 3300, center: [0, 0], base_z_mm: 0, material: 'Tree Bark', name: 'Trunk' });
  await rev(d, [[0, 2800], [900, 2900], [1800, 3400], [2400, 4300], [2700, 5300], [2400, 6300], [1800, 7100], [900, 7600], [0, 7800]], 'Foliage Green', 'Canopy');
  await call('family_add_type', { document: d, name: 'Tree 7.8m' });
});
await fam('Arian Cypress', 'planting', async d => {
  await call('family_add_material', { document: d, name: 'Cypress Green', color: { r: 40, g: 84, b: 52 } });
  await rev(d, [[0, 200], [250, 300], [550, 1200], [680, 3000], [600, 5000], [380, 6800], [150, 8000], [0, 8400]], 'Cypress Green', 'Cypress');
  await call('family_add_type', { document: d, name: 'Cypress 8.4m' });
});
await fam('Arian Bush', 'planting', async d => {
  await call('family_add_material', { document: d, name: 'Shrub Green', color: { r: 96, g: 142, b: 70 } });
  await rev(d, [[0, 0], [550, 40], [850, 300], [900, 650], [650, 950], [0, 1100]], 'Shrub Green', 'Bush');
  await call('family_add_type', { document: d, name: 'Shrub 1.1m' });
});
// timber fin: generic model box, parametric height
const fin = {
  name: 'Arian Timber Fin', template: 'generic_model', category: 'Generic Models', clear_geometry: true,
  materials: [{ name: 'Fin Timber', color: { r: 160, g: 112, b: 70 } }],
  parameters: [{ name: 'FinW', type: 'length', value: 40 }, { name: 'FinD', type: 'length', value: 220 }, { name: 'FinH', type: 'length', value: 2800 }, { name: 'Timber', type: 'material', value: 'Fin Timber' }],
  types: [{ name: 'Fin 2800', values: { FinH: 2800 } }, { name: 'Fin 1400', values: { FinH: 1400 } }],
  shapes: [{ kind: 'box', name: 'Fin', x: ['-FinW/2', 'FinW/2'], y: ['-FinD/2', 'FinD/2'], z: ['0', 'FinH'], material_parameter: 'Timber' }],
};
const r = await call('family_build_from_spec', { spec: fin }, { soft: true });
log('Arian Timber Fin', r.error ? 'ERR ' + r.error.slice(0, 300) : 'ok flex=' + r.flex_test_passed);
