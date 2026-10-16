// Stage 1: project, levels, grids, materials, layered types
import { call, log } from './lib.mjs';
const P = 'Villa Arian';
log('project', JSON.stringify(await call('create_project', { template: 'architectural', name: P })).slice(0, 120));
await call('modify_level', { level: 'Level 1', new_name: 'Ground Floor', elevation_mm: 0 });
await call('modify_level', { level: 'Level 2', new_name: 'First Floor', elevation_mm: 3200 });
await call('create_level', { name: 'Roof', elevation_mm: 6400 });
log('levels', JSON.stringify((await call('list_levels')).levels.map(l => l.name + ':' + l.elevation_mm)));
log('library', JSON.stringify(await call('create_material_library', {})).slice(0, 160));
const M = {
  brick: 'آجر نما - Facing Brick', wool: 'پشم سنگ - Rock Wool', eps: 'پلی‌استایرن - EPS Insulation', block: 'بلوک سیمانی - Concrete Block', gyp: 'گچ و خاک - Gypsum Plaster',
  white: 'رنگ سفید - Paint White', timber: 'چوب راش - Beech Timber', trav: 'سنگ تراورتن - Travertine', rc: 'بتن مسلح - Reinforced Concrete C25', screed: 'ملات ماسه سیمان - Screed',
  tile: 'سرامیک - Ceramic Tile', parquet: 'پارکت چوبی - Wood Parquet', bit: 'ایزوگام - Bitumen Membrane', gypb: 'Gypsum Board', granite: 'سنگ گرانیت - Granite',
};
const L = (m, t, f) => ({ material: M[m], thickness_mm: t, function: f });
const types = [
  ['wall', 'Arian Brick Wall 370', [L('brick', 100, 'finish1'), L('wool', 60, 'insulation'), L('block', 190, 'structure'), L('gyp', 20, 'finish2')]],
  ['wall', 'Arian White Render 310', [L('white', 20, 'finish1'), L('eps', 80, 'insulation'), L('block', 190, 'structure'), L('gyp', 20, 'finish2')]],
  ['wall', 'Arian Timber Clad 320', [L('timber', 30, 'finish1'), L('wool', 80, 'insulation'), L('block', 190, 'structure'), L('gyp', 20, 'finish2')]],
  ['wall', 'Arian Travertine Wall 250', [L('trav', 40, 'finish1'), L('block', 190, 'structure'), L('gyp', 20, 'finish2')]],
  ['wall', 'Arian Interior 150', [L('gyp', 15, 'finish1'), L('block', 120, 'structure'), L('gyp', 15, 'finish2')]],
  ['floor', 'Arian Ground Slab 265', [L('trav', 15, 'finish1'), L('screed', 50, 'substrate'), L('rc', 200, 'structure')]],
  ['floor', 'Arian Upper Slab 265', [L('parquet', 15, 'finish1'), L('screed', 50, 'substrate'), L('rc', 200, 'structure')]],
  ['floor', 'Arian Terrace Slab 280', [L('trav', 30, 'finish1'), L('screed', 50, 'substrate'), L('rc', 200, 'structure')]],
  ['roof', 'Arian Flat Roof 370', [L('bit', 10, 'membrane'), L('eps', 120, 'insulation'), L('screed', 40, 'substrate'), L('rc', 200, 'structure')]],
  ['ceiling', 'Arian Ceiling 15', [L('gypb', 15, 'finish1')]],
];
for (const [kind, name, layers] of types) {
  const r = await call('create_compound_type', { kind, name, layers }, { soft: true });
  log(name, r.error ? 'ERR ' + r.error : 'ok ' + (r.total_thickness_mm ?? ''));
}
