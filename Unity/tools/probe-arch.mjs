// Probe: log every facade-bucket primitive emitted into chunk (0,-1) by the JS architecture stage.
import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture, FACADE_ATTRS } from '../../src/systems/buildings/architecture.js';
import { Bucket, ChunkedBuckets } from '../../src/systems/buildings/geom.js';

const L = LAYOUT;
const { lots } = buildPlan(L);
const seedToId = new Map(lots.map((l) => [l.seed.toFixed(6), l.id]));

const origVRect = Bucket.prototype.vrect;
const origQuad = Bucket.prototype.quad;
const origBox = Bucket.prototype.box;
const WANT = (b) => b.matKey === 'facade' && b.chunk[0] === 0 && b.chunk[1] === -1;
Bucket.prototype.vrect = function (origin, right, w, h, a, u0, v0) {
  if (WANT(this)) console.log(`VRECT seed=${a.aWin[0].toFixed(6)} y ${origin[1]} -> ${origin[1] + h} at x=${origin[0].toFixed(2)} z=${origin[2].toFixed(2)} w=${w.toFixed(2)}`);
  return origVRect.call(this, origin, right, w, h, a, u0, v0);
};
Bucket.prototype.quad = function (p0, p1, p2, p3, uv0, uv1, uv2, uv3, a, n, t) {
  if (WANT(this) && a && a.aWin && a.aWin[3] === 9999) console.log(`QUAD seed=${a.aWin[0].toFixed(6)} ys ${p0[1]},${p1[1]},${p2[1]},${p3[1]} x ${p0[0].toFixed(2)}..${p2[0].toFixed(2)} z ${p0[2].toFixed(2)}..${p0[2] !== p3[2] ? p3[2].toFixed(2) : ''}`);
  return origQuad.call(this, p0, p1, p2, p3, uv0, uv1, uv2, uv3, a, n, t);
};
Bucket.prototype.box = function (cx, cy, cz, sx, sy, sz, a, rotY, skip, uvScale) {
  if (WANT(this)) console.log(`BOX seed=${a && a.aWin ? a.aWin[0].toFixed(6) : '?'} y ${cy - sy / 2} .. ${cy + sy / 2} at x=${cx.toFixed(2)} z=${cz.toFixed(2)} sx=${sx.toFixed(2)} sy=${sy.toFixed(2)} sz=${sz.toFixed(2)}`);
  return origBox.call(this, cx, cy, cz, sx, sy, sz, a, rotY, skip, uvScale);
};

const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) {
  const before = B.map.size;
  buildLotArchitecture(lot, B, out);
  if (lot.x0 + lot.x1 > -10 && lot.x0 + lot.x1 < 200 && lot.z0 + lot.z1 > -200 && lot.z0 + lot.z1 < 10) {
    // per-lot fresh bucket to isolate
    const B2 = new ChunkedBuckets(96);
    const o2 = { colliders: [], lights: [], facades: [] };
    buildLotArchitecture(lot, B2, o2);
    for (const [k, b] of B2.map.entries()) {
      if (!k.includes('facade')) continue;
      const ys = [];
      for (let i = 1; i < b.pos.length; i += 3) { const y = b.pos[i]; if (y > 3.5 && y < 4.6) ys.push(y.toFixed(6)); }
      if (ys.length) console.log(`LOT ${lot.id} gh=${lot.groundH.toFixed(4)} h=${lot.h.toFixed(4)} ys: ${ys.join(',')}`);
    }
  }
}
console.log('[probe done]');
