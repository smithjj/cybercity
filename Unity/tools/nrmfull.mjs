import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets, Bucket } from '../../src/systems/buildings/geom.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';
import { writeFileSync } from 'node:fs';
const ctx = { layout: LAYOUT, assets: { gltf: async (p) => ({ scene: { updateMatrixWorld() {}, traverse() {} } }) } };
const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
const shops = new ShopBuilder(B, out);
const OPEN = ['izakaya', 'ramen', 'bar', 'boutique', 'konbini', 'izakaya', 'ramen'];
let prev = null;
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
for (let i = 0; i < b.nrm.length; i += 3) vs.push(hx(b.nrm[i]) + ',' + hx(b.nrm[i+1]) + ',' + hx(b.nrm[i+2]));
vs.sort();
writeFileSync('unity/Temp/js_bucket_nrm.txt', vs.join('\n'));
console.log('js nrm', vs.length);
