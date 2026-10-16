// Presentation sheets from renders
import { call, log } from './lib.mjs';
const R = 'C:/Users/arian/Documents/ArianRenders/villa/';
const img = n => R + n + ' - 3D View - ' + n.replace(/_/g, ' ') + '.jpg';
const f = (v) => R + v.replace(/ /g, '_') + ' - 3D View - ' + v + '.jpg';
const tb = (await call('list_titleblocks', {})).titleblocks[0].name;
const mk = async (num, name) => { const r = await call('create_sheet', { number: num, name, titleblock: tb }, { soft: true }); if (r.error) log('sheet', num, r.error.slice(0, 100)); };
await mk('A-000', 'Cover'); await mk('A-401', 'Exterior Views'); await mk('A-402', 'Interior & Aerial Views');
await call('place_image_on_sheet', { sheet: 'A-000', path: f('V09 Cover'), center: [395, 335], width_mm: 640 });
await call('create_text_note_fa', { view: 'A-000', point: [700, 62], text: 'ویلای آرین', size_mm: 12 }, { soft: true });
await call('create_text_note_fa', { view: 'A-000', point: [700, 40], text: 'ویلای مسکونی دو طبقه — طراحی، متریال و مستندسازی با پلاگین Arian Revit MCP', size_mm: 4 }, { soft: true });
const board = async (sheet, items) => {
  const pos = [[200, 435], [560, 435], [200, 160], [560, 160]];
  for (let i = 0; i < items.length; i++) {
    await call('place_image_on_sheet', { sheet, path: f(items[i][0]), center: pos[i], width_mm: 330 });
    await call('create_text_note', { view: sheet, point: [pos[i][0] - 160, pos[i][1] - 138], text: items[i][1] }, { soft: true });
  }
};
await board('A-401', [['V01 Hero South-East', 'View 01 - South-east, garden facade'], ['V02 Garden Terrace', 'View 02 - Garden terrace and cantilever'], ['V03 Entrance North', 'View 03 - Entrance'], ['V04 Aerial North-East', 'View 04 - Aerial from north-east']]);
await board('A-402', [['V05 Living Room', 'View 05 - Living and dining'], ['V06 Master Bedroom', 'View 06 - Master bedroom'], ['V07 Stair Hall', 'View 07 - Stair hall'], ['V08 Aerial South-East', 'View 08 - Aerial from south-east']]);
for (const s of ['A-000', 'A-401', 'A-402']) { await call('set_sheet_dates_jalali', { sheets: [s] }, { soft: true }); }
await call('add_revision', { description: 'Presentation sheets', date: '1405/07/24', sheets: ['A-000', 'A-401', 'A-402'] }, { soft: true });
log('done');
