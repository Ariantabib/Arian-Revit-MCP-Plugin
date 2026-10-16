import { call, log } from './lib.mjs';
import fs from 'node:fs';
const hideSub = fs.readFileSync(new URL('./hidesub.cs', import.meta.url), 'utf8');
await call('execute_csharp', { code: `
var map = new Dictionary<string,string> { ["A-0"] = "A-000", ["S-1"] = "S-001", ["S-2"] = "S-002" };
foreach (ViewSheet s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))) if (map.TryGetValue(s.SheetNumber, out var n)) s.SheetNumber = n;
return "ok";`, transaction: true });
for (const v of ['South Elevation', 'North Elevation', 'East Elevation', 'West Elevation']) await call('set_view_properties', { view: v, scale: 150 }, { soft: true });
await call('arrange_viewports', { sheet: 'A-201', columns: 2 });
// cover hero view
await call('delete_view', { view: 'V09 Cover' }, { soft: true });
const c = await call('create_3d_view', { name: 'V09 Cover', perspective: true, eye: [-26000, -14000, 9000], target: [8000, 2500, 2500] });
await call('execute_csharp', { code: hideSub, transaction: true }, { soft: true });
for (const cat of ['Levels', 'Grids', 'Elevations', 'Sections', 'Callouts']) await call('set_category_visibility', { view: 'V09 Cover', categories: [cat], visible: false }, { soft: true });
await call('set_view_environment', { view: 'V09 Cover', shadows: 80, background: 'gradient', date: '2026-04-20', hour: 16 });
await call('set_view_visual_style', { view: 'V09 Cover', style: 'realistic' });
await call('place_view_on_sheet', { sheet: 'A-000', view: 'V09 Cover', center: [330, 330] });
await call('place_view_on_sheet', { sheet: 'S-002', view: 'Sheet Index', center: [150, 450] }, { soft: true });
await call('place_view_on_sheet', { sheet: 'S-001', view: 'Room Schedule', center: [150, 350] }, { soft: true }).then(r => log('rs', r.error?.slice(0, 80) ?? 'ok'));
log(JSON.stringify((await call('list_sheets', {})).sheets.map(s => s.number)));
await call('create_text_note_fa', { view: 'A-000', point: [800, 80], text: 'ویلای آرین — پروژهٔ مسکونی دو طبقه', size_mm: 7 }, { soft: true }).then(r => log('fa cover', r.error?.slice(0, 80) ?? 'ok'));
await call('create_text_note_fa', { view: 'A-000', point: [800, 55], text: 'طراحی‌شده با پلاگین Arian Revit MCP — فاز ۲ معماری، متریال و مستندسازی', size_mm: 3.5 }, { soft: true });
