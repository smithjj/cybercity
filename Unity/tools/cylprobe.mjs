// Log every cyl() call during buildProps. args: none
import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets, Bucket } from '../../src/systems/buildings/geom.js';
import { writeFileSync } from 'node:fs';
const ctx = { layout: LAYOUT, assets: { gltf: async (p) => ({ scene: { updateMatrixWorld() {}, traverse() {} } }) } };
const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
const lines = [];
const origCyl = Bucket.prototype.cyl;
Bucket.prototype.cyl = function (p0, p1, r, a, seg = 6, r1 = null) {
  lines.push(`CYL ${p0.map(v => v.toFixed(6)).join(',')} | ${p1.map(v => v.toFixed(6)).join(',')} | r=${r} seg=${seg}`);
  return origCyl.call(this, p0, p1, r, a, seg, r1);
};
await buildProps(ctx, lots, B, out, { wetness: 1 });
writeFileSync('unity/Temp/js_cyls.txt', lines.join('\n'));
console.log('js cyls:', lines.length);
