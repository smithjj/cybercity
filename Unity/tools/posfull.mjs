import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets } from '../../src/systems/buildings/geom.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';
import { writeFileSync, readFileSync } from 'node:fs';
// reuse the shops loop from dump-buildings.mjs by importing it? simpler: copy minimal loop
const ctx = { layout: LAYOUT, assets: { gltf: async (p) => ({ scene: { updateMatrixWorld() {}, traverse() {} } }) } };
const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
const src = readFileSync(new URL('./dump-buildings.mjs', import.meta.url), 'utf8');
// eval the shops loop by importing the real pipeline pieces: replicate loop from dump-buildings
const shops = new ShopBuilder(B, out);
let prev = null;
const OPEN = ['izakaya', 'ramen', 'bar', 'boutique', 'konbini', 'izakaya', 'ramen'];
for (const lot of lots) {
  for (const f of lot.fronts) {
    if (!(f.shop && lot.detail === 'hi' && f.baseY >= 0)) continue;
    const cx = (lot.x0 + lot.x1) / 2, cz = (lot.z0 + lot.z1) / 2;
    let type = null;
    if (f.district === 'avenue' && lot.x1 <= -9 && lot.z0 < 26 && lot.z1 > 18) type = 'ramen';
    else if (prev && prev.closed && Math.hypot(prev.x - cx, prev.z - cz) < 16) type = OPEN[lot.id % OPEN.length];
    f.shopType = shops.build(lot, f, { type });
    prev = { x: cx, z: cz, closed: f.shopType === 'shutter' || f.shopType === 'lobby' };
  }
}
await buildProps(ctx, lots, B, out, { wetness: 1 });
const buf = new ArrayBuffer(8), dv = new DataView(buf);
const hx = v => { dv.setFloat64(0, v); return dv.getBigUint64(0).toString(16).padStart(16, '0'); };
const b = B.map.get('-1,-1|detail');
const vs = [];
for (let i = 0; i < b.pos.length; i += 3) vs.push(hx(b.pos[i]) + ',' + hx(b.pos[i+1]) + ',' + hx(b.pos[i+2]));
vs.sort();
writeFileSync('unity/Temp/js_bucket_pos2.txt', vs.join('\n'));
console.log('js verts', vs.length);
