// Dump cherry tree GLBs (assets/models/landmarks/cherry_tree_{0..3}.glb) into
// raw float bins for Unity: per variant, blossom (material 0) and bark
// (material 1), non-indexed, with TEXCOORD_1 (uv1: x=tint, y=outerness).
// Minimal GLB parser — no loader deps.
import { writeFileSync, mkdirSync } from 'node:fs';
const ROOT = 'C:/Users/smith/OneDrive/Documents/GitHub/cybercity';
const OUT = ROOT + '/unity/Assets/Game/Models/Landmarks/dump';
mkdirSync(OUT, { recursive: true });

function matFromNode(n) {
  // column-major 4x4
  const m = n.matrix || null;
  if (!m) return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  return m;
}
function xform(m, x, y, z) {
  return [
    m[0] * x + m[4] * y + m[8] * z + m[12],
    m[1] * x + m[5] * y + m[9] * z + m[13],
    m[2] * x + m[6] * y + m[10] * z + m[14],
  ];
}
const COMP = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const NCOMP = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };

for (let v = 0; v < 4; v++) {
  const fileBuf = (await import('node:fs')).readFileSync(ROOT + '/assets/models/landmarks/cherry_tree_' + v + '.glb');
  const ab = fileBuf.buffer.slice(fileBuf.byteOffset, fileBuf.byteOffset + fileBuf.byteLength);
  const buf = ab;
  const dv = new DataView(buf);
  const jsonLen = dv.getUint32(12, true);
  const json = JSON.parse(Buffer.from(buf, 20, jsonLen).toString());
  // concat binary chunks (usually one)
  let binOffset = 20 + jsonLen;
  let bin = null;
  while (binOffset < buf.byteLength) {
    const cl = dv.getUint32(binOffset, true);
    const ct = dv.getUint32(binOffset + 4, true);
    if (ct === 0x004e4942) bin = Buffer.from(buf, binOffset + 8, cl);
    binOffset += 8 + cl;
  }
  const acc = (i) => {
    const a = json.accessors[i];
    const bv = json.bufferViews[a.bufferView];
    const T = COMP[a.componentType];
    const nc = NCOMP[a.type];
    const off = (bv.byteOffset || 0) + (a.byteOffset || 0);
    const out = new Float32Array(a.count * nc);
    if (T === Float32Array) {
      for (let k = 0; k < a.count * nc; k++) out[k] = bin.readFloatLE(off + k * 4);
    } else {
      for (let k = 0; k < a.count; k++)
        for (let c = 0; c < nc; c++) out[k * nc + c] = bin[off + k * bv.byteStride * nc + c];
    }
    return out;
  };
  for (const which of ['blossom', 'bark']) {
    const mi = which === 'blossom' ? 0 : 1;
    const prim = json.meshes[mi].primitives[0];
    const P = acc(prim.attributes.POSITION);
    const N = acc(prim.attributes.NORMAL);
    const U0 = acc(prim.attributes.TEXCOORD_0);
    const U1 = prim.attributes.TEXCOORD_1 !== undefined ? acc(prim.attributes.TEXCOORD_1) : null;
    // expand indices (these GLBs are indexed) into tri soup
    let idx = null;
    if (prim.indices !== undefined) {
      const ia = json.accessors[prim.indices];
      const bv = json.bufferViews[ia.bufferView];
      const off = (bv.byteOffset || 0) + (ia.byteOffset || 0);
      if (ia.componentType === 5125) idx = new Uint32Array(ia.count);
      else if (ia.componentType === 5123) idx = new Uint16Array(ia.count);
      else idx = new Uint16Array(ia.count);
      for (let k = 0; k < ia.count; k++)
        idx[k] = ia.componentType === 5125 ? bin.readUInt32LE(off + k * 4) : bin.readUInt16LE(off + k * 2);
    }
    const nv = idx ? idx.length : P.length / 3;
    // node transform (single node tree, 2 nodes)
    const node = json.nodes[mi] || json.nodes[0];
    const m = matFromNode(node);
    const Pw = new Float32Array(nv * 3);
    const Nw = new Float32Array(nv * 3);
    const Uw = new Float32Array(nv * 2);
    const U1w = U1 ? new Float32Array(nv * 2) : null;
    for (let k = 0; k < nv; k++) {
      const src = idx ? idx[k] : k;
      const [x, y, z] = xform(m, P[src * 3], P[src * 3 + 1], P[src * 3 + 2]);
      Pw[k * 3] = x; Pw[k * 3 + 1] = y; Pw[k * 3 + 2] = z;
      Nw[k * 3] = N[src * 3]; Nw[k * 3 + 1] = N[src * 3 + 1]; Nw[k * 3 + 2] = N[src * 3 + 2];
      Uw[k * 2] = U0[src * 2]; Uw[k * 2 + 1] = U0[src * 2 + 1];
      if (U1w) { U1w[k * 2] = U1[src * 2]; U1w[k * 2 + 1] = U1[src * 2 + 1]; }
    }
    writeFileSync(`${OUT}/cherry_${v}_${which}_pos.bin`, Buffer.from(Pw.buffer));
    writeFileSync(`${OUT}/cherry_${v}_${which}_nor.bin`, Buffer.from(Nw.buffer));
    writeFileSync(`${OUT}/cherry_${v}_${which}_uv0.bin`, Buffer.from(Uw.buffer));
    if (U1w) writeFileSync(`${OUT}/cherry_${v}_${which}_uv1.bin`, Buffer.from(U1w.buffer));
    else console.log(`WARN variant ${v} ${which}: no uv1`);
    let mny = 1e9, mxy = -1e9;
    for (let k = 1; k < Pw.length; k += 3) { mny = Math.min(mny, Pw[k]); mxy = Math.max(mxy, Pw[k]); }
    console.log(`cherry ${v} ${which}: ${nv} tris-verts, y ${mny.toFixed(2)}..${mxy.toFixed(2)}`);
  }
}
console.log('done');
