// Fix 1: re-place furniture/fins with correct placement, fix family material appearances
import { call, log } from './lib.mjs';
const del = async cat => { const q = await call('query_elements', { category: cat, limit: 500 }); const ids = (q.elements || []).map(e => e.id); if (ids.length) await call('delete_elements', { ids }); log('deleted', cat, ids.length); };
for (const c of ['Furniture', 'Casework', 'Plumbing Fixtures', 'Generic Models']) await del(c);
const mats = [
  ['Foliage Green', 82, 128, 62], ['Tree Bark', 98, 74, 52], ['Cypress Green', 40, 84, 52], ['Shrub Green', 96, 142, 70], ['Fin Timber', 160, 112, 70],
  ['Window Frame Anthracite', 62, 68, 74], ['Door Leaf Oak', 176, 130, 84], ['Door Frame White', 238, 238, 234], ['Door Handle Steel', 190, 192, 196],
];
for (const [name, r, g, b] of mats) await call('create_material', { name, color: { r, g, b } }, { soft: true });
await call('create_material', { name: 'Window Glass', color: { r: 175, g: 210, b: 225 }, transparency: 70, material_class: 'Glass' });
await call('set_material_appearance', { material: 'Window Glass', glossiness: 95, reflectivity: 35 }, { soft: true });
await call('set_material_appearance', { material: 'Pool Water', color: { r: 56, g: 152, b: 186 }, glossiness: 92, reflectivity: 40 }, { soft: true });
await call('set_material_appearance', { material: 'Lawn Grass', color: { r: 104, g: 142, b: 76 }, glossiness: 5 }, { soft: true });
