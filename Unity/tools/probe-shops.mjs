// Probe: per-front shop det/glass/facade vert counts (JS side).
import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { ChunkedBuckets } from '../../src/systems/buildings/geom.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';

const L = LAYOUT;
const { lots } = buildPlan(L);
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
    const vBefore = {};
    for (const [k, b] of B.map.entries()) vBefore[k] = b.pos.length;
    f.shopType = shops.build(lot, f, { type });
    const deltas = [];
    for (const [k, b] of B.map.entries()) {
      const d = b.pos.length - (vBefore[k] || 0);
      if (d > 0) deltas.push(`${k}:${d / 3}`);
    }
    console.log(`SHOP lot=${lot.id} side=${f.side} type=${f.shopType} ${deltas.join(' ')}`);
    prev = { x: cx, z: cz, closed: f.shopType === 'shutter' || f.shopType === 'lobby' };
  }
}
