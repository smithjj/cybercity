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
for (const lot of lots) for (const f of lot.fronts) {
  if (!(f.shop && lot.detail === 'hi' && f.baseY >= 0)) continue;
  const cx = (lot.x0 + lot.x1) / 2, cz = (lot.z0 + lot.z1) / 2;
  let type = null;
  if (f.district === 'avenue' && lot.x1 <= -9 && lot.z0 < 26 && lot.z1 > 18) type = 'ramen';
  else if (prev && prev.closed && Math.hypot(prev.x - cx, prev.z - cz) < 16) type = OPEN[lot.id % OPEN.length];
  f.shopType = shops.build(lot, f, { type });
  prev = { x: cx, z: cz, closed: f.shopType === 'shutter' || f.shopType === 'lobby' };
}
// instrument: wrap the roof beam calls? simpler: patch Math.min? no — patch Bucket.prototype.beam to catch legs r=0.12
const log = [];
const ob = Bucket.prototype.beam;
Bucket.prototype.beam = function (a, b, r, attr) {
  if (r === 0.12) log.push(`LEG ${a.map(v=>v.toFixed(4)).join(',')} -> ${b.map(v=>v.toFixed(4)).join(',')}`);
  return ob.call(this, a, b, r, attr);
};
await buildProps(ctx, lots, B, out, { wetness: 1 });
writeFileSync('unity/Temp/js_bb.txt', log.join('\n'));
console.log('js legs', log.length);
