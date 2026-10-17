// Dump vehicle GLBs (lod2) into Unity-ready bins: ONE merged tri soup per car
// type with per-vertex part tags. Mirrors vehicles/models.js extract():
// - body parts (non-wheel_*): transformed by FLIP (rotY PI) * worldMatrix
// - wheel parts: only wheel_XX slot nodes; wheel_FR copy is taken in wheel-local
//   space, then re-emitted at EACH slot's world position with the axle center
//   (y, z after FLIP) baked per vertex for shader spin.
// Output per type <T>: pos/nor/uv/tag bins + manifest JSON.
// Tag encoding (uv1.x): 0=paint 1=glass 2=chrome|trim|rubber|metal 3=headlamp 4=taillight
// uv1.y: 1 for wheel verts. uv2: (axleY, axleZ, 0, 0) for wheel verts.
// Car frame after FLIP: forward = -Z (JS convention). Unity mirrors z at bake.
import { writeFileSync, mkdirSync, readFileSync } from 'node:fs';
const ROOT = 'C:/Users/smith/OneDrive/Documents/GitHub/cybercity';
const OUT = ROOT + '/unity/Assets/Game/Models/Vehicles/dump';
mkdirSync(OUT, { recursive: true });

const TYPES = ['hero', 'kei', 'taxi', 'van', 'av'];
const TAG = { paint: 0, glass: 1, chrome: 2, trim: 2, rubber: 2, metal: 2, lights_head: 3, lights_tail: 4 };

function loadGlb(path) {
  const buf = readBuf(path);
  const dv = new DataView(buf);
  const jsonLen = dv.getUint32(12, true);
  const json = JSON.parse(Buffer.from(buf, 20, jsonLen).toString());
  let binOffset = 20 + jsonLen, bin = null;
  while (binOffset < buf.byteLength) {
    const cl = dv.getUint32(binOffset, true);
    const ct = dv.getUint32(binOffset + 4, true);
    if (ct === 0x004e4942) bin = Buffer.from(buf, binOffset + 8, cl);
    binOffset += 8 + cl;
  }
  return { json, bin };
}
function readBuf(p) {
  const b = readFileSync(p);
  return b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength);
}
const COMP = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const NCOMP = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };
function acc(json, bin, i) {
  const a = json.accessors[i];
  const bv = json.bufferViews[a.bufferView];
  const T = COMP[a.componentType];
  const nc = NCOMP[a.type];
  const off = (bv.byteOffset || 0) + (a.byteOffset || 0);
  const out = new Float32Array(a.count * nc);
  if (T === Float32Array) for (let k = 0; k < a.count * nc; k++) out[k] = bin.readFloatLE(off + k * 4);
  else for (let k = 0; k < a.count; k++) for (let c = 0; c < nc; c++) out[k * nc + c] = bin[off + k * bv.byteStride * nc + c];
  return out;
}
function indices(json, bin, prim) {
  if (prim.indices === undefined) return null;
  const ia = json.accessors[prim.indices];
  const bv = json.bufferViews[ia.bufferView];
  const off = (bv.byteOffset || 0) + (ia.byteOffset || 0);
  const out = new Uint32Array(ia.count);
  for (let k = 0; k < ia.count; k++)
    out[k] = ia.componentType === 5125 ? bin.readUInt32LE(off + k * 4) : bin.readUInt16LE(off + k * 2);
  return out;
}
const ident = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
function matMul(a, b) {
  const o = new Array(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
    o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
  }
  return o;
}
function xform(m, x, y, z) {
  return [m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]];
}
function xformN(m, x, y, z) { // normals: upper 3x3 (assume uniform-ish scale)
  return [m[0] * x + m[4] * y + m[8] * z, m[1] * x + m[5] * y + m[9] * z, m[2] * x + m[6] * y + m[10] * z];
}
function nodeMatrix(n) {
  if (n.matrix) return n.matrix;
  const m = ident.slice();
  // TRS
  const t = n.translation || [0, 0, 0], q = n.rotation || [0, 0, 0, 1], s = n.scale || [1, 1, 1];
  const [x, y, z, w] = q;
  const r = [
    1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
    2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
    2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
    0, 0, 0, 1,
  ];
  for (let c = 0; c < 3; c++)
    for (let rr = 0; rr < 3; rr++)
      m[c * 4 + rr] = r[c * 4 + rr] * s[c];
  m[12] = t[0]; m[13] = t[1]; m[14] = t[2];
  return m;
}
const FLIP = (() => { const c = Math.cos(Math.PI), s = Math.sin(Math.PI); return [c,0,-s,0, 0,1,0,0, s,0,c,0, 0,0,0,1]; })();

function dumpOne(type, LOD, suffix) {
  const { json, bin } = loadGlb(ROOT + `/assets/models/vehicles/${type}${LOD}.glb`);
  // world matrices
  const world = new Map();
  const parentOf = new Map();
  const walk = (ni, parent) => {
    const n = json.nodes[ni];
    const m = matMul(parent, nodeMatrix(n));
    world.set(ni, m);
    (n.children || []).forEach((c) => { parentOf.set(c, ni); walk(c, m); });
  };
  (json.scenes[json.scene || 0].nodes || []).forEach((n) => walk(n, ident));
  // find wheel slot nodes (top-level wheel_*)
  const wheelNodes = [];
  for (let i = 0; i < json.nodes.length; i++) {
    const n = json.nodes[i];
    if (/^wheel_/.test(n.name || '') && !/^wheel_/.test((json.nodes[n.parent ?? -1] || {}).name || '')) wheelNodes.push(i);
  }
  const slots = wheelNodes.map((ni) => {
    const m = matMul(FLIP, world.get(ni));
    return { name: json.nodes[ni].name, pos: [m[12], m[13], m[14]], ni, m };
  });
  const isWheelDesc = (ni) => { for (let p = ni; p != null; p = parentOf.get(p) ?? null) if (wheelNodes.includes(p)) return p; return null; };
  const frSlot = slots.find((s) => /FR$/.test(s.name)) || slots[0];

  const out = { pos: [], nor: [], uv: [], tag: [], axle: [] };
  const emitPrim = (meshNi, prim, matName, worldM, axYZ) => {
    const P = acc(json, bin, prim.attributes.POSITION);
    const N = prim.attributes.NORMAL !== undefined ? acc(json, bin, prim.attributes.NORMAL) : null;
    const U = prim.attributes.TEXCOORD_0 !== undefined ? acc(json, bin, prim.attributes.TEXCOORD_0) : null;
    const idx = indices(json, bin, prim) ?? Uint32Array.from({ length: P.length / 3 }, (_, k) => k);
    const tag = TAG[matName] ?? 2;
    for (const ii of idx) {
      const [x, y, z] = xform(worldM, P[ii * 3], P[ii * 3 + 1], P[ii * 3 + 2]);
      let n = N ? xformN(worldM, N[ii * 3], N[ii * 3 + 1], N[ii * 3 + 2]) : [0, 1, 0];
      const nl = Math.hypot(n[0], n[1], n[2]) || 1;
      out.pos.push(x, y, z);
      out.nor.push(n[0] / nl, n[1] / nl, n[2] / nl);
      out.uv.push(U ? U[ii * 2] : 0, U ? U[ii * 2 + 1] : 0);
      out.tag.push(tag, axYZ ? 1 : 0);
      out.axle.push(axYZ ? axYZ[0] : 0, axYZ ? axYZ[1] : 0, 0);
    }
  };
  for (let ni = 0; ni < json.nodes.length; ni++) {
    const n = json.nodes[ni];
    if (n.mesh === undefined) continue;
    const wn = isWheelDesc(ni);
    const mesh = json.meshes[n.mesh];
    for (const prim of mesh.primitives) {
      const matName = prim.material !== undefined ? (json.materials[prim.material].name || 'trim').replace(/\.\d+$/, '') : 'trim';
      if (wn) {
        if (wn !== frSlot.ni) continue; // shared wheel mesh: take one copy
        // wheel-local -> wheel world (per slot), tagged with each slot's axle
        const local = matMul(matMul(ident, invert(world.get(wn))), world.get(ni));
        for (const s of slots) {
          const m = matMul(matMul(FLIP, world.get(s.ni)), local);
          emitPrim(ni, prim, matName, m, [s.pos[1], s.pos[2]]);
        }
      } else {
        emitPrim(ni, prim, matName, matMul(FLIP, world.get(ni)), null);
      }
    }
  }
  // manifest from body box
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9, minZ = 1e9, maxZ = -1e9;
  for (let i = 0; i < out.pos.length; i += 3) {
    minX = Math.min(minX, out.pos[i]); maxX = Math.max(maxX, out.pos[i]);
    minY = Math.min(minY, out.pos[i + 1]); maxY = Math.max(maxY, out.pos[i + 1]);
    minZ = Math.min(minZ, out.pos[i + 2]); maxZ = Math.max(maxZ, out.pos[i + 2]);
  }
  const manifest = {
    length: maxZ - minZ, width: maxX - minX, height: maxY - minY,
    min: [minX, minY, minZ], max: [maxX, maxY, maxZ],
    axleF: slots.find((s) => /F[LR]$/.test(s.name))?.pos[2] ?? -maxZ * 0.3,
    axleR: slots.find((s) => !/F[LR]$/.test(s.name))?.pos[2] ?? maxZ * 0.3,
    wheelRadius: (slots[0] || { pos: [0, 0.33, 0] }).pos[1],
    verts: out.pos.length / 3,
  };
  const F = (arr, name) => writeFileSync(`${OUT}/veh_${type}${suffix}_${name}.bin`, Buffer.from(Float32Array.from(arr).buffer));
  F(out.pos, 'pos'); F(out.nor, 'nor'); F(out.uv, 'uv'); F(out.tag, 'tag'); F(out.axle, 'axle');
  writeFileSync(`${OUT}/veh_${type}${suffix}.json`, JSON.stringify(manifest));
  console.log(type + suffix, manifest.verts, 'tris-verts', 'len', manifest.length.toFixed(2), 'wheelR', manifest.wheelRadius.toFixed(2), 'slots', slots.length);
}
for (const type of TYPES) { dumpOne(type, '_lod2', ''); dumpOne(type, '_lod3', '_l3'); }
function invert(m) {
  // general 4x4 inverse via adjugate (column-major)
  const a = m, o = new Array(16);
  const inv = (i, j) => {
    const r0 = [0, 1, 2, 3].filter((r) => r !== Math.floor(i / 4));
    const c0 = [0, 1, 2, 3].filter((c) => c !== i % 4);
    let d = 0;
    const sub = (r, c) => a[c0[c] * 4 + r0[r]];
    const det2 = (r, c) => {
      const m2 = [[sub(r === 0 ? 1 : 0, c === 0 ? 1 : 0), sub(r === 0 ? 1 : 0, c === 1 ? 1 : 2)], [sub(r === 1 ? 1 : 2, c === 0 ? 1 : 0), sub(r === 1 ? 1 : 2, c === 1 ? 1 : 2)]];
      return 0; // unused path
    };
    return d;
  };
  // simpler: use cofactor approach numerically
  const A = [[a[0], a[4], a[8], a[12]], [a[1], a[5], a[9], a[13]], [a[2], a[6], a[10], a[14]], [a[3], a[7], a[11], a[15]]];
  const I = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]];
  // gauss
  const M = A.map((r, i) => [...r, ...I[i]]);
  for (let c = 0; c < 4; c++) {
    let p = c;
    for (let r = c + 1; r < 4; r++) if (Math.abs(M[r][c]) > Math.abs(M[p][c])) p = r;
    [M[c], M[p]] = [M[p], M[c]];
    const piv = M[c][c];
    for (let k = 0; k < 8; k++) M[c][k] /= piv;
    for (let r = 0; r < 4; r++) if (r !== c) { const f = M[r][c]; for (let k = 0; k < 8; k++) M[r][k] -= f * M[c][k]; }
  }
  for (let r = 0; r < 4; r++) for (let c = 0; c < 4; c++) o[c * 4 + r] = M[r][4 + c];
  return o;
}
console.log('done');
