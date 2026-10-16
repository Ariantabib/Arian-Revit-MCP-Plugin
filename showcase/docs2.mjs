// Documentation 2: clean up views (crop, hide clutter), fix tag overlaps
import { call, log } from './lib.mjs';
const plans = ['Ground Floor', 'First Floor', 'Roof Plan'];
for (const v of plans) {
  await call('set_view_crop', { view: v, min: [-4200, -6200], max: [22800, 13200] }, { soft: true });
  await call('set_view_properties', { view: v, crop_active: true, crop_visible: false }, { soft: true });
  for (const c of ['Planting', 'Topography', 'Elevations', 'Sections', 'Entourage']) await call('set_category_visibility', { view: v, categories: [c], visible: false }, { soft: true });
  const t = await call('check_tag_overlaps', { view: v, fix: true }, { soft: true });
  log('tags', v, t.error ? t.error.slice(0, 100) : `${t.overlapping_pairs} overlaps left, moved ${t.moved}`);
}
for (const v of ['South Elevation', 'North Elevation', 'East Elevation', 'West Elevation', 'Section A-A', 'Section B-B']) {
  await call('set_view_properties', { view: v, scale: 100, detail_level: 'medium', display_style: 'shaded' }, { soft: true });
  await call('set_view_environment', { view: v, shadows: 0, background: 'color' }, { soft: true });
}
log('done');
