import { call, log } from './lib.mjs';
const views = (await call('list_views', {})).views?.filter(v => v.name?.startsWith('V0')) ?? [];
for (const v of views.length ? views.map(x => x.name) : ['V01 Hero South-East']) {
  for (const c of ['Levels', 'Grids', 'Elevations', 'Sections', 'Property Lines', 'Cameras', 'Callouts', 'Scope Boxes', 'Reference Planes']) {
    const r = await call('set_category_visibility', { view: v, categories: [c], visible: false }, { soft: true });
    if (r.error && v.startsWith('V01')) log(c, 'ERR', r.error.slice(0, 80));
  }
}
log('ok', views.length);
