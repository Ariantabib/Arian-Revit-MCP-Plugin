import { call, log } from './lib.mjs';
const fins = [];
for (let y = -5100; y <= -2100; y += 330) fins.push({ type: 'Arian Timber Fin: Fin 2800', point: [12150, y], level: 'Ground Floor', rotation_deg: 90 });
const fr = await call('place_family_instances', { items: fins });
log('fins', fr.placed ?? JSON.stringify(fr).slice(0, 200));
