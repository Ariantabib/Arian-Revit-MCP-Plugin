// Stage 4b: finishes, ceilings, furniture, fixtures, lights
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState();
const G = 'Ground Floor', F = 'First Floor';
for (const lv of [G, F]) {
  const r = await call('apply_room_finishes', { level: lv, floor_finish: 'Wood parquet', wall_finish: 'Paint - white', ceiling_finish: 'Gypsum board - painted', base_finish: 'Wood skirting 100',
    by_name: { Bath: { floor: 'Porcelain tile 60x60', wall: 'Ceramic tile to 2.1 m', base: 'Tile skirting' }, WC: { floor: 'Porcelain tile 60x60', wall: 'Ceramic tile to 2.1 m' }, Kitchen: { floor: 'Porcelain tile 60x120', wall: 'Paint + splashback tile' },
      Living: { floor: 'Travertine 60x60', wall: 'Paint - white' }, Hall: { floor: 'Travertine 60x60' }, Laundry: { floor: 'Porcelain tile 60x60' }, Pantry: { floor: 'Porcelain tile 60x60' }, Storage: { floor: 'Porcelain tile 60x60' } } });
  log('finishes', lv, JSON.stringify(r).slice(0, 120));
}
const cg = await call('create_ceilings_from_rooms', { level: G, ceiling_type: 'Compound Ceiling: Plain', height_offset_mm: 2900 }, { soft: true });
log('ceilings G', JSON.stringify(cg).slice(0, 400));
const cf = await call('create_ceilings_from_rooms', { level: F, ceiling_type: 'Compound Ceiling: Plain', height_offset_mm: 2900 }, { soft: true });
log('ceilings F', JSON.stringify(cf).slice(0, 200));
