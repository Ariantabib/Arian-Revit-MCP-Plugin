// Documentation 1: plans, elevations, sections, tags, dimensions, schedules
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState(); S.docs = {};
const soft = (name, p) => p.then(r => { log(name, r.error ? 'ERR ' + r.error.slice(0, 140) : 'ok'); return r; });
// plans
await soft('plan roof', call('create_floor_plan', { level: 'Roof', name: 'Roof Plan', scale: 100 }, { soft: true }));
for (const [lv, nm] of [['Ground Floor', 'Ground Floor'], ['First Floor', 'First Floor']]) await call('set_view_properties', { view: nm, scale: 100, detail_level: 'fine' }, { soft: true });
// elevations
const E = [['South Elevation', [7500, -9500], 'north'], ['North Elevation', [7500, 15500], 'south'], ['East Elevation', [25000, 4200], 'west'], ['West Elevation', [-9500, 4200], 'east']];
for (const [name, pt, dir] of E) await soft(name, call('create_elevation', { point: pt, direction: dir, plan_view: 'Ground Floor', scale: 100, name }, { soft: true }));
// sections
await soft('Section A-A', call('create_section', { start: [6900, -6000], end: [6900, 12500], bottom_mm: -800, top_mm: 7600, depth_mm: 9000, name: 'Section A-A' }, { soft: true }));
await soft('Section B-B', call('create_section', { start: [-4000, 3000], end: [19000, 3000], bottom_mm: -800, top_mm: 7600, depth_mm: 9000, name: 'Section B-B', flip: true }, { soft: true }));
// tags + dimensions
for (const lv of ['Ground Floor', 'First Floor']) {
  await soft('tag rooms ' + lv, call('tag_rooms', { level: lv, view: lv }, { soft: true }));
  await soft('tag doors ' + lv, call('tag_elements', { view: lv, category: 'Doors' }, { soft: true }));
  await soft('tag windows ' + lv, call('tag_elements', { view: lv, category: 'Windows' }, { soft: true }));
  await soft('dim grids ' + lv, call('dimension_grids', { view: lv, offset_mm: 1500 }, { soft: true }));
  await soft('dim openings ' + lv, call('dimension_openings', { view: lv, offset_mm: 900 }, { soft: true }));
}
// schedules
const sch = [
  ['Rooms', 'Room Schedule', ['Number', 'Name', 'Level', 'Area'], { sort: [{ field: 'Number' }], totals_fields: ['Area'], grand_total: true, itemize: true }],
  ['Doors', 'Door Schedule', ['Mark', 'Type', 'Level', 'Width', 'Height', 'Count'], { itemize: false }],
  ['Windows', 'Window Schedule', ['Type', 'Level', 'Width', 'Height', 'Count'], { itemize: false }],
  ['Walls', 'Wall Takeoff', ['Type', 'Length', 'Area', 'Volume'], { itemize: false, totals_fields: ['Length', 'Area', 'Volume'], grand_total: true }],
  ['Furniture', 'Furniture Schedule', ['Family', 'Type', 'Level', 'Count'], { itemize: false }],
];
for (const [cat, name, fields, extra] of sch) await soft(name, call('create_schedule', { category: cat, name, fields, ...extra }, { soft: true }));
await soft('areas', call('create_areas_from_rooms', { level: 'Ground Floor' }, { soft: true }));
saveState(S);
