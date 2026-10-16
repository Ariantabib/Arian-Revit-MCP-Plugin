// Stage 3: families, stairs, roof, floor opening, terraces
import { call, log, loadState, saveState, view } from './lib.mjs';
const S = loadState();
const G = 'Ground Floor', F = 'First Floor', R = 'Roof';
const FAM = 'C:/Users/arian/Documents/ArianFamilies/';
for (const f of ['Arian table', 'Arian chair', 'Arian bed', 'Arian sofa', 'Arian wardrobe', 'Arian kitchen base cabinet', 'Arian wc', 'Arian washbasin', 'Arian bathtub', 'Arian ceiling light', 'Arian round column'])
  await call('load_family', { path: FAM + f + '.rfa' }, { soft: true }).then(r => log('load', f, r.error ? 'ERR ' + r.error.slice(0, 80) : 'ok'));
// stairs: U-stair in the stair hall (x 5400..8400, y 6000..10200)
const st = await call('create_u_stairs', { start: [6250, 6400], direction_deg: 90, turn: 'right', base_level: G, top_level: F, width_mm: 1150, gap_mm: 100 });
log('stairs', JSON.stringify(st).slice(0, 300)); S.stairs = st.id ?? st.stairs_id;
saveState(S);
await view('stairs', 'iso_se', { hide_categories: ['Roofs'] });
