// Stage 4: rooms (with separation lines at open connections), finishes, ceilings
import { call, log, loadState, saveState } from './lib.mjs';
const S = loadState();
const G = 'Ground Floor', F = 'First Floor';
// room separation lines across the open connections
await call('create_room_separation', { level: G, points: [[11400, 1000], [11400, 3400]] });
await call('create_room_separation', { level: F, points: [[8400, 6000], [8400, 8400]] });
await call('create_room_separation', { level: F, points: [[6300, 4200], [8200, 4200]] });
const rooms = [
  [G, 'Living & Dining', 'G01', [5700, 3000]], [G, 'Kitchen', 'G02', [13200, 2100]], [G, 'Pantry', 'G03', [13200, 5100]], [G, 'Laundry', 'G04', [13200, 8100]],
  [G, 'Study', 'G05', [1800, 8100]], [G, 'Guest WC', 'G06', [4500, 7200]], [G, 'Storage', 'G07', [4500, 9300]], [G, 'Stair Hall', 'G08', [6900, 8400]], [G, 'Entrance Hall', 'G09', [9900, 8100]],
  [F, 'Master Bedroom', 'F01', [3000, 1200]], [F, 'Dressing', 'F02', [1500, 5700]], [F, 'Master Bath', 'F03', [1500, 8700]], [F, 'Home Office', 'F04', [4200, 7200]],
  [F, 'Landing', 'F05', [6900, 5400]], [F, 'Bedroom 2', 'F06', [8400, 300]], [F, 'Bedroom 3', 'F07', [12900, 300]], [F, 'Corridor', 'F08', [10500, 3300]],
  [F, 'Library Lounge', 'F09', [9900, 7200]], [F, 'Shared Bath', 'F10', [13200, 5100]], [F, 'Bedroom 4', 'F11', [13200, 8100]],
];
S.rooms = {};
for (const [lv, name, no, pt] of rooms) {
  const r = await call('create_room', { point: pt, level: lv, name, number: no }, { soft: true });
  if (r.error) log('ROOM FAIL', name, r.error.slice(0, 150)); else { S.rooms[no] = { id: r.id, area: r.area_m2, name, pt, lv }; log(no, name, r.area_m2, 'm2', r.enclosed === false ? 'NOT ENCLOSED' : ''); }
}
saveState(S);
