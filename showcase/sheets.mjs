// Sheets: cover, plans, elevations, sections, renders, schedules
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState();
const mk = async (num, name, views, perSheet = views.length, cols) => {
  const r = await call('create_sheet_set', { views, number_prefix: num.replace(/\d+$/, ''), start_number: Number(num.match(/\d+$/)[0]), per_sheet: perSheet, sheet_name: name }, { soft: true });
  log(num, name, r.error ? 'ERR ' + r.error.slice(0, 160) : JSON.stringify(r.results?.[0]?.layout_notes ?? []) + ' ' + JSON.stringify(r.results?.[0]?.errors ?? []));
  if (cols) await call('arrange_viewports', { sheet: num, columns: cols }, { soft: true });
};
await mk('A-101', 'Ground Floor Plan', ['Ground Floor']);
await mk('A-102', 'First Floor Plan', ['First Floor']);
await mk('A-103', 'Roof Plan', ['Roof Plan']);
await mk('A-201', 'Elevations', ['South Elevation', 'North Elevation', 'East Elevation', 'West Elevation'], 4, 2);
await mk('A-301', 'Sections', ['Section A-A', 'Section B-B'], 2, 1);
await mk('A-401', 'Exterior Views', ['V01 Hero South-East', 'V02 Garden Terrace', 'V03 Entrance North', 'V04 Aerial North-West'], 4, 2);
await mk('A-402', 'Interior Views', ['V05 Living Room', 'V06 Master Bedroom', 'V07 Stair Hall', 'V08 Aerial South-West'], 4, 2);
await mk('S-001', 'Schedules', ['Room Schedule', 'Door Schedule', 'Window Schedule'], 3, 3);
await mk('A-000', 'Cover & Index', ['V08 Aerial South-West'], 1);
// sheet index on the cover
const idx = await call('create_sheet_index', { name: 'Sheet Index', extra_fields: [] }, { soft: true });
log('index', idx.error ? idx.error.slice(0, 100) : 'ok');
await call('create_sheet_set', { views: ['Sheet Index'], number_prefix: 'S-', start_number: 2, per_sheet: 1, sheet_name: 'Drawing Index' }, { soft: true }).then(r => log('index sheet', r.error ? r.error.slice(0, 100) : 'ok'));
await call('add_revision', { description: 'Issued for client presentation', date: '1405/07/24', issued_by: 'Arian Revit MCP', issued_to: 'Client', sheets: ['all'] }, { soft: true }).then(r => log('revision', r.error ? r.error.slice(0, 100) : 'ok'));
await call('set_sheet_dates_jalali', { sheets: ['all'] }, { soft: true }).then(r => log('jalali', r.error ? r.error.slice(0, 100) : JSON.stringify(r)));
for (const [sheet, txt] of [['A-000', 'ویلای آرین — پروژهٔ نمونه با پلاگین Arian Revit MCP'], ['A-101', 'پلان طبقهٔ همکف — مقیاس 1:100'], ['A-102', 'پلان طبقهٔ اول — مقیاس 1:100'], ['A-201', 'نماهای جنوبی، شمالی، شرقی و غربی'], ['A-301', 'برش‌های A-A و B-B']])
  await call('create_text_note_fa', { view: sheet, point: [760, 40], text: txt, size_mm: 5 }, { soft: true }).then(r => log('fa note', sheet, r.error ? r.error.slice(0, 100) : 'ok'));
saveState(S);
