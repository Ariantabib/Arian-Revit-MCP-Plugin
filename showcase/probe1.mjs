import { call, log } from './lib.mjs';
log(JSON.stringify(await call('modify_level', { level: 'Level 1', name: 'Ground Floor' })));
log(JSON.stringify(await call('modify_level', { level: 'Level 2', name: 'First Floor', elevation_mm: 3200 })));
log(JSON.stringify(await call('create_level', { name: 'Roof', elevation_mm: 6400 })));
log(JSON.stringify(await call('create_compound_type', { kind: 'wall', name: 'probe wall', layers: [{ material: 'آجر نما - Facing Brick', thickness_mm: 100, function: 'finish1' }, { material: 'پشم سنگ - Rock Wool', thickness_mm: 60, function: 'insulation' }, { material: 'بلوک سیمانی - Concrete Block', thickness_mm: 190, function: 'structure' }, { material: 'گچ و خاک - Gypsum Plaster', thickness_mm: 20, function: 'finish2' }] })));
