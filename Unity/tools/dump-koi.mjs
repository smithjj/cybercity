// Dump the holo-koi geometry (lathe body + tail + pectoral fins) from three.js
// — exactly the construction in signage/billboards.js — into raw bins for Unity.
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync } from 'node:fs';
const require = createRequire(import.meta.url);
const ROOT = 'C:/Users/smith/OneDrive/Documents/GitHub/cybercity';
const THREE = require(ROOT + '/node_modules/three/build/three.cjs');
const { mergeGeometries } = require(ROOT + '/node_modules/three/examples/jsm/utils/BufferGeometryUtils.js');

const OUT = ROOT + '/unity/Assets/Game/Models/Signage/dump';
mkdirSync(OUT, { recursive: true });

const Lk = 7;
const prof = [];
for (let i = 0; i <= 24; i++) {
  const s = i / 24; // 0 tail .. 1 head
  const r = 0.95 * Math.pow(Math.sin(Math.PI * Math.min(1, s * 1.05)), 0.8) * (0.35 + 0.65 * s) + 0.03;
  prof.push(new THREE.Vector2(r, (s - 0.5) * Lk));
}
const body = new THREE.LatheGeometry(prof, 28);
body.rotateX(Math.PI / 2); // length along +Z (head at +Z)
body.scale(1, 0.75, 1);
const fin = (pts) => { const sh = new THREE.Shape(pts.map(([x, y]) => new THREE.Vector2(x, y))); return new THREE.ShapeGeometry(sh, 6); };
const tail = fin([[0, 0], [1.6, -1.8], [0.4, -1.3], [0, -1.6], [-0.4, -1.3], [-1.6, -1.8]]);
tail.rotateX(-Math.PI / 2); tail.translate(0, 0, -Lk / 2 + 0.2);
tail.rotateZ(Math.PI / 2);
const finL = fin([[0, 0], [1.4, -0.6], [1.0, 0.4]]); finL.rotateX(-Math.PI / 2); finL.translate(0.5, -0.3, 1.0);
const finR = finL.clone(); finR.scale(-1, 1, 1);
const strip = (g) => { const o = g.index ? g.toNonIndexed() : g; for (const k of Object.keys(o.attributes)) if (!['position', 'normal', 'uv'].includes(k)) o.deleteAttribute(k); if (!o.attributes.normal) o.computeVertexNormals(); return o; };
const koiGeo = mergeGeometries([strip(body), strip(tail), strip(finL), strip(finR)]).toNonIndexed();

const pos = koiGeo.attributes.position.array;
const nor = koiGeo.attributes.normal.array;
const nVerts = pos.length / 3;
const idx = [];
for (let i = 0; i < nVerts; i += 3) idx.push(i, i + 1, i + 2);

writeFileSync(OUT + '/koi_pos.bin', Buffer.from(new Float32Array(pos).buffer));
writeFileSync(OUT + '/koi_nor.bin', Buffer.from(new Float32Array(nor).buffer));
const ib = Buffer.alloc(idx.length * 4);
for (let i = 0; i < idx.length; i++) ib.writeUInt32LE(idx[i], i * 4);
writeFileSync(OUT + '/koi_idx.bin', ib);
writeFileSync(OUT + '/koi_meta.txt', `${nVerts} ${idx.length} 32`);

let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
for (let i = 0; i < pos.length; i += 3)
  for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], pos[i + k]); mx[k] = Math.max(mx[k], pos[i + k]); }
console.log(`koi: ${nVerts} verts, ${idx.length} idx, bounds x ${mn[0].toFixed(2)}..${mx[0].toFixed(2)} y ${mn[1].toFixed(2)}..${mx[1].toFixed(2)} z ${mn[2].toFixed(2)}..${mx[2].toFixed(2)}`);
