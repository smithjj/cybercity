import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets, Bucket } from '../../src/systems/buildings/geom.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';
// instrument beam to log the tilted ones
const log = [];
const ob = Bucket.prototype.beam;
Bucket.prototype.beam = function (a, b, r, attr) {
  const dy = b[1] - a[1], dz = b[2] - a[2], dx = b[0] - a[0];
  const len = Math.hypot(dx, dy, dz);
  if (len > 1e-4 && Math.abs(dy / len) > 0.05 && Math.abs(dy / len) < 0.95 && Math.abs(dx) < 0.1)
    log.push(`BEAM ${a.map(v=>v.toFixed(3))} -> ${b.map(v=>v.toFixed(3))} r=${r}`);
  return ob.call(this, a, b, r, attr);
};
const ctx = { layout: LAYOUT, assets: { gltf: async (p) => ({ scene: { updateMatrixWorld() {}, traverse() {} } }) } };
const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
await buildProps(ctx, lots, B, out, { wetness: 1 });
console.log('tilted beams:', log.length);
for (const l of log.slice(0, 6)) console.log(l);
