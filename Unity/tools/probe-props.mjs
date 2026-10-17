// Per-lot props detail-vert counts (for oracle debugging)
import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets } from '../../src/systems/buildings/geom.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';
const OPEN = ['ramen', 'izakaya', 'bar', 'boutique'];
import { writeFileSync } from 'node:fs';

const ctx = { layout: LAYOUT, assets: { gltf: async (path) => {
  const THREE = await import('three/webgpu');
  const geo = new THREE.BufferGeometry();
  geo.userData = { cat: path };
  const fakeMesh = { isMesh: true, geometry: geo, material: { name: path } };
  return { scene: { updateMatrixWorld() {}, traverse(cb) { cb(fakeMesh); } } };
} } };

const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
const shops = new ShopBuilder(B, out);
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
const before = new Map();
for (const [k, b] of B.map.entries()) before.set(k, b.pos.length / 3);
const inst = await buildProps(ctx, lots, B, out, { wetness: 1 });
const sb = [];
for (const lot of lots) {
  const key = Math.floor((lot.x0 + lot.x1) / 2 / 96) + ',' + Math.floor((lot.z0 + lot.z1) / 2 / 96);
  const bk = key + '|detail';
  const b = B.map.get(bk);
  const v = b ? b.pos.length / 3 : 0;
  const pv = before.get(bk) || 0;
  sb.push(`LOT ${lot.id} bucket=${bk} propsVerts=${v - pv} ac=${inst.length}\n`);
}
writeFileSync('unity/Temp/js_lotprops.txt', sb.join(''));
console.log('done, ac=', inst.length);
