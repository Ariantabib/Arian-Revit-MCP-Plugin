import { call, log } from './lib.mjs';
for (const cat of ['Topography', 'Planting', 'Site']) {
  const r = await call('set_category_visibility', { view: 'V01 Hero South-East', categories: [cat], visible: false }, { soft: true });
  const o = await call('render_view_image', { view: 'V01 Hero South-East', width_px: 1000, path: `C:/Users/arian/Documents/ArianRenders/villa/linetest_${cat}.png` }, { soft: true });
  log(cat, r.error ? r.error.slice(0, 100) : 'hidden', o.file);
  await call('set_category_visibility', { view: 'V01 Hero South-East', categories: [cat], visible: true }, { soft: true });
}
