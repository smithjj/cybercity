// dump-ground.mjs — build the REAL JS ground geometry headlessly (node + three) and dump
// float32 vertex data (hex bits) + rects/colliders for the C# GroundGeo oracle.
//   node unity/tools/dump-ground.mjs
import { writeFileSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';
import { buildGroundGeometry } from '../../src/systems/ground/geometry.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const f64 = new Float64Array(1);
const hex64 = v => { f64[0] = v; return new BigUint64Array(f64.buffer)[0].toString(16).padStart(16, '0'); };
const hex32arr = arr => {
  const f = new Float32Array(arr.length);
  f.set(arr);
  const u = new Uint32Array(f.buffer);
  return Array.from(u, v => v.toString(16).padStart(8, '0'));
};
const rect = r => ({ x0: hex64(r.x0), x1: hex64(r.x1), z0: hex64(r.z0), z1: hex64(r.z1) });
const geom = (name, g) => ({
  name,
  pos: hex32arr(g.attributes.position.array),
  nor: hex32arr(g.attributes.normal.array),
  uv: hex32arr(g.attributes.uv.array),
  idx: g.index ? Array.from(g.index.array) : null,
});

const g = buildGroundGeometry();
const out = {
  geoms: ['base', 'sidewalk', 'curb', 'alley', 'promenade', 'wall'].map(k => geom(k, g[k])),
  baseRects: g.baseRects.map(rect),
  alleyLower: rect(g.alleyLower),
  alleyUpper: rect(g.alleyUpper),
  prom: rect(g.prom),
  swColliders: g.swColliders.map(poly => poly.map(p => [hex64(p[0]), hex64(p[1])])),
};
const outDir = path.join(here, '..', 'Temp', 'oracle');
mkdirSync(outDir, { recursive: true });
writeFileSync(path.join(outDir, 'dump-ground.json'), JSON.stringify(out));
console.log('geoms:', out.geoms.map(x => `${x.name}:${x.pos.length / 3}v`).join(' '), `rects=${out.baseRects.length}`);
