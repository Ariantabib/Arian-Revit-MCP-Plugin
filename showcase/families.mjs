// Detailed door/window families for the villa (frames, leaves, glass, handles) built with family_build_from_spec
import { call, log } from './lib.mjs';
const T = (name, Width, Height, extra = {}) => ({ name, values: { Width, Height, ...extra } });
const win = {
  name: 'Arian Villa Window', template: 'window', category: 'Windows', clear_geometry: false,
  use_parameters: ['Width', 'Height', 'Default Sill Height'],
  materials: [{ name: 'Window Frame Anthracite', color: { r: 62, g: 68, b: 74 } }, { name: 'Window Glass', color: { r: 190, g: 220, b: 235 }, transparency: 75 }],
  parameters: [
    { name: 'FrameW', type: 'length', value: 60 }, { name: 'FrameD', type: 'length', value: 110 }, { name: 'GlassT', type: 'length', value: 24 }, { name: 'MullW', type: 'length', value: 50 },
    { name: 'Frame Material', type: 'material', value: 'Window Frame Anthracite' }, { name: 'Glass Material', type: 'material', value: 'Window Glass' },
  ],
  types: [T('600 x 600', 600, 600, { MullW: 1 }), T('1200 x 1200', 1200, 1200), T('1500 x 1500', 1500, 1500), T('2400 x 1500', 2400, 1500, { MullW: 50 }), T('1200 x 2400', 1200, 2400), T('1500 x 2400', 1500, 2400)],
  shapes: [
    { kind: 'box', name: 'JambL', subcategory: 'Frame', x: ['-Width/2', '-Width/2+FrameW'], y: ['-FrameD/2', 'FrameD/2'], z: ['[Default Sill Height]', '[Default Sill Height]+Height'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'JambR', subcategory: 'Frame', x: ['Width/2-FrameW', 'Width/2'], y: ['-FrameD/2', 'FrameD/2'], z: ['[Default Sill Height]', '[Default Sill Height]+Height'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'Head', subcategory: 'Frame', x: ['-Width/2', 'Width/2'], y: ['-FrameD/2', 'FrameD/2'], z: ['[Default Sill Height]+Height-FrameW', '[Default Sill Height]+Height'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'SillFrame', subcategory: 'Frame', x: ['-Width/2', 'Width/2'], y: ['-FrameD/2', 'FrameD/2'], z: ['[Default Sill Height]', '[Default Sill Height]+FrameW'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'Mullion', subcategory: 'Frame', x: ['-MullW/2', 'MullW/2'], y: ['-FrameD/2+15', 'FrameD/2-15'], z: ['[Default Sill Height]+FrameW', '[Default Sill Height]+Height-FrameW'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'Glass', subcategory: 'Glass', x: ['-Width/2+FrameW', 'Width/2-FrameW'], y: ['-GlassT/2', 'GlassT/2'], z: ['[Default Sill Height]+FrameW', '[Default Sill Height]+Height-FrameW'], material_parameter: 'Glass Material' },
    { kind: 'box', name: 'SillBoard', subcategory: 'Frame', x: ['-Width/2-40', 'Width/2+40'], y: ['-FrameD/2-70', 'FrameD/2+70'], z: ['[Default Sill Height]-35', '[Default Sill Height]'], material_parameter: 'Frame Material' },
  ],
};
function door(name, types, double) {
  const half = double ? 'Gap' : null;
  const leaves = double
    ? [{ x: ['-Width/2+FrameW+Gap', '-Gap'], n: 'LeafL' }, { x: ['Gap', 'Width/2-FrameW-Gap'], n: 'LeafR' }]
    : [{ x: ['-Width/2+FrameW+Gap', 'Width/2-FrameW-Gap'], n: 'Leaf' }];
  const shapes = [
    { kind: 'box', name: 'JambL', subcategory: 'Frame', x: ['-Width/2', '-Width/2+FrameW'], y: ['-FrameD/2', 'FrameD/2'], z: ['0', 'Height'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'JambR', subcategory: 'Frame', x: ['Width/2-FrameW', 'Width/2'], y: ['-FrameD/2', 'FrameD/2'], z: ['0', 'Height'], material_parameter: 'Frame Material' },
    { kind: 'box', name: 'Head', subcategory: 'Frame', x: ['-Width/2', 'Width/2'], y: ['-FrameD/2', 'FrameD/2'], z: ['Height-FrameW', 'Height'], material_parameter: 'Frame Material' },
    ...leaves.map(l => ({ kind: 'box', name: l.n, subcategory: 'Panel', x: l.x, y: ['-LeafT/2', 'LeafT/2'], z: ['Gap', 'Height-FrameW-Gap'], material_parameter: 'Door Material' })),
  ];
  if (double) {
    shapes.push({ kind: 'box', name: 'HandleL', subcategory: 'Handle', x: ['-120', '-40'], y: ['-LeafT/2-55', 'LeafT/2+55'], z: ['1000', '1040'], material_parameter: 'Handle Material' });
    shapes.push({ kind: 'box', name: 'HandleR', subcategory: 'Handle', x: ['40', '120'], y: ['-LeafT/2-55', 'LeafT/2+55'], z: ['1000', '1040'], material_parameter: 'Handle Material' });
  } else shapes.push({ kind: 'box', name: 'Handle', subcategory: 'Handle', x: ['Width*0.36', 'Width*0.44'], y: ['-LeafT/2-55', 'LeafT/2+55'], z: ['1000', '1040'], material_parameter: 'Handle Material' });
  return {
    name, template: 'door', category: 'Doors', clear_geometry: false, use_parameters: ['Width', 'Height'],
    materials: [{ name: 'Door Leaf Oak', color: { r: 176, g: 130, b: 84 } }, { name: 'Door Frame White', color: { r: 238, g: 238, b: 234 } }, { name: 'Door Handle Steel', color: { r: 190, g: 192, b: 196 } }],
    parameters: [{ name: 'FrameW', type: 'length', value: 50 }, { name: 'FrameD', type: 'length', value: 120 }, { name: 'LeafT', type: 'length', value: 44 }, { name: 'Gap', type: 'length', value: 6 },
      { name: 'Door Material', type: 'material', value: 'Door Leaf Oak' }, { name: 'Frame Material', type: 'material', value: 'Door Frame White' }, { name: 'Handle Material', type: 'material', value: 'Door Handle Steel' }],
    types, shapes,
  };
}
const specs = [win,
  door('Arian Villa Door', [T('800 x 2100', 800, 2100), T('900 x 2100', 900, 2100), T('1000 x 2100', 1000, 2100), T('900 x 2400', 900, 2400)], false),
  door('Arian Villa Door Double', [T('1200 x 2100', 1200, 2100), T('1500 x 2100', 1500, 2100), T('1800 x 2400', 1800, 2400)], true)];
for (const s of specs) {
  const r = await call('family_build_from_spec', { spec: s }, { soft: true });
  log(s.name, r.error ? 'ERR ' + r.error.slice(0, 700) : 'ok flex=' + r.flex_test_passed);
  for (const n of r.notes ?? []) log('   -', n.split(String.fromCharCode(13))[0]);
}
