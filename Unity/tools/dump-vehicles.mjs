// Dump vehicle GLBs into Unity-ready bins: ONE merged tri soup per car type/LOD
// with per-vertex part tags. Mirrors vehicles/models.js extract():
// - body parts (non-wheel_*): transformed by ROT(YAW) * worldMatrix
// - wheel parts: re-emitted at their slot's world position with the axle center
//   (y, z after rotation) baked per vertex for shader spin.
// Output per type <T>: pos/nor/uv/tag bins + manifest JSON.
// Tag encoding (uv1.x): 0=paint 1=glass 2=chrome|trim|rubber|metal 3=headlamp 4=taillight
// uv1.y: 1 for wheel verts. uv2: (axleY, axleZ, 0, 0) for wheel verts.
// Car frame after rotation: forward = -Z (JS convention). Unity mirrors z at bake.
//
// The kei car uses the three supplied N-Box LODs:
//   opus-Honda_NBox_Custom_LOD1.glb -> veh_kei_*     (parked / medium)
//   opus-Honda_NBox_Custom_LOD2.glb -> veh_kei_l3_*  (traffic / lowest)
//   opus-Honda_NBox_Custom_LOD0.glb -> veh_kei_l0_*  (showroom + delivery / highest)
// The other four car types keep their original lod2/lod3 dumps.
import { writeFileSync, mkdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '../..');
const OUT = join(ROOT, 'Unity/Assets/Game/Models/Vehicles/dump');
mkdirSync(OUT, { recursive: true });

const TYPES = ['hero', 'kei', 'taxi', 'van', 'av'];
const TAG = { paint: 0, glass: 1, chrome: 2, trim: 2, rubber: 2, metal: 2, lights_head: 3, lights_tail: 4 };

// The N-Box LODs are authored front +X. RotY(+PI/2) maps +X -> -Z (baked forward)
// and +Z -> +X (car right), matching the port's convention after the Unity z-mirror.
const KEI = {
  sources: [
    { src: 'assets/models/vehicles/opus-Honda_NBox_Custom_LOD1.glb', suffix: '' },
    { src: 'assets/models/vehicles/opus-Honda_NBox_Custom_LOD2.glb', suffix: '_l3' },
    { src: 'assets/models/vehicles/opus-Honda_NBox_Custom_LOD0.glb', suffix: '_l0' },
  ],
  yaw: Math.PI / 2,
  wheel: /^wheel[\s_]/i,
  perSlotWheels: true,
  // Translucent glass needs the interior behind it, and the glass triangles must
  // come last so they blend over the already-drawn body/interior in one draw.
  glassLast: true,
  // The LODs merge the body into one mesh (no per-node parts), so tags are
  // material-name based. Wheel materials all collapse to trim (2), as in the port.
  tags: {
    'Paint': 0,
    'Mirror cap': 0,
    'Glass front door': 1,
    'Glass privacy': 1,
    'Glass windshield': 1,
    'Glass dark': 1,
    'Glass front': 1,
    'Chrome': 2,
    'Black matte': 2,
    'Black gloss': 2,
    'Mirror base plastic': 2,
    'Mirror cap lower': 2,
    'Mirror glass': 2,
    'Turn lamp lens': 2,
    'Plate yellow': 2,
    'Rubber seal': 2,
    'Alloy graphite': 2,
    'Alloy machined': 2,
    'Alloy mid': 2,
    'Wheel cap': 2,
    'Brake steel': 2,
    'Brake caliper': 2,
    'Tire rubber': 2,
    'Interior floor': 2,
    'Interior door card': 2,
    'Interior headliner': 2,
    'Interior dark': 2,
    'Seat fabric': 2,
    'Dash plastic': 2,
    'Screen': 2,
    'Leather': 2,
    'Headlight lens': 3,
    'Projector lens': 3,
    'Light guide': 3,
    'Tail lamp': 4,
    'Tail lamp glow': 4,
  },
  // The glass is translucent now, so the interior is visible and must be kept.
  skip: new Set(),
  // Body width below the mirrors (1.524 m). The full bbox is 1.900 m because the
  // mirrors stick out; BuildWheels uses width * 0.44 for the wheel colliders.
  widthMaxY: 1.1,
};

function legacyConfig(type) {
  return {
    sources: [
      { src: `assets/models/vehicles/${type}_lod2.glb`, suffix: '' },
      { src: `assets/models/vehicles/${type}_lod3.glb`, suffix: '_l3' },
    ],
    yaw: Math.PI,
    wheel: /^wheel_/,
    perSlotWheels: false,
    tags: null,
    skip: null,
    widthMaxY: null,
  };
}

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
function rotY(a) {
  const c = Math.cos(a), s = Math.sin(a);
  return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1];
}

function dumpOne(type, source, suffix, cfg) {
  const { json, bin } = loadGlb(source);
  const yaw = rotY(cfg.yaw);
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
  const wheelRe = cfg.wheel || /^wheel_/;
  const wheelNodes = [];
  for (let i = 0; i < json.nodes.length; i++) {
    const n = json.nodes[i];
    if (wheelRe.test(n.name || '') && !wheelRe.test((json.nodes[n.parent ?? -1] || {}).name || '')) wheelNodes.push(i);
  }
  const slots = wheelNodes.map((ni) => {
    const m = matMul(yaw, world.get(ni));
    return { name: json.nodes[ni].name, pos: [m[12], m[13], m[14]], ni, m };
  });
  if (slots.length === 0) console.warn('WARN no wheel slots:', type, suffix, source);
  const slotByNode = new Map(slots.map((s) => [s.ni, s]));
  const isWheelDesc = (ni) => { for (let p = ni; p != null; p = parentOf.get(p) ?? null) if (wheelNodes.includes(p)) return p; return null; };
  const frSlot = slots.find((s) => /FR$/.test(s.name)) || slots[0];

  const out = { pos: [], nor: [], uv: [], tag: [], axle: [] };
  let widthMinX = Infinity, widthMaxX = -Infinity;
  const unmapped = new Set();
  const emitPrim = (meshNi, prim, rawMatName, worldM, axYZ, isWheel, wantGlass) => {
    const matName = (rawMatName || 'trim').replace(/\.\d+$/, '');
    if (cfg.skip && cfg.skip.has(matName)) return;
    let tag;
    if (cfg.tags) {
      if (cfg.tags[matName] !== undefined) tag = cfg.tags[matName];
      else {
        tag = TAG[matName] ?? 2;
        if (TAG[matName] === undefined) unmapped.add(matName);
      }
    } else {
      tag = TAG[matName] ?? 2;
    }
    // Two-phase emission for the transparent car shader: all opaque body/interior
    // triangles first, then the glass, so the glass can blend over them in one draw.
    if (cfg.glassLast && (tag === 1) !== wantGlass) return;
    const P = acc(json, bin, prim.attributes.POSITION);
    const N = prim.attributes.NORMAL !== undefined ? acc(json, bin, prim.attributes.NORMAL) : null;
    const U = prim.attributes.TEXCOORD_0 !== undefined ? acc(json, bin, prim.attributes.TEXCOORD_0) : null;
    const idx = indices(json, bin, prim) ?? Uint32Array.from({ length: P.length / 3 }, (_, k) => k);
    for (const ii of idx) {
      const [x, y, z] = xform(worldM, P[ii * 3], P[ii * 3 + 1], P[ii * 3 + 2]);
      let n = N ? xformN(worldM, N[ii * 3], N[ii * 3 + 1], N[ii * 3 + 2]) : [0, 1, 0];
      const nl = Math.hypot(n[0], n[1], n[2]) || 1;
      out.pos.push(x, y, z);
      out.nor.push(n[0] / nl, n[1] / nl, n[2] / nl);
      out.uv.push(U ? U[ii * 2] : 0, U ? U[ii * 2 + 1] : 0);
      out.tag.push(tag, axYZ ? 1 : 0);
      out.axle.push(axYZ ? axYZ[0] : 0, axYZ ? axYZ[1] : 0, 0);
      if (!isWheel && tag !== 1 && cfg.widthMaxY != null && y <= cfg.widthMaxY) {
        if (x < widthMinX) widthMinX = x;
        if (x > widthMaxX) widthMaxX = x;
      }
    }
  };
  const emitAll = (wantGlass) => {
    for (let ni = 0; ni < json.nodes.length; ni++) {
      const n = json.nodes[ni];
      if (n.mesh === undefined) continue;
      const wn = isWheelDesc(ni);
      const mesh = json.meshes[n.mesh];
      for (const prim of mesh.primitives) {
        const matName = prim.material !== undefined ? (json.materials[prim.material].name || 'trim') : 'trim';
        if (wn != null) {
          if (cfg.perSlotWheels) {
            // Emit each slot's own (possibly mirrored L/R) wheel mesh at its slot.
            const s = slotByNode.get(wn);
            if (!s) continue;
            const local = matMul(invert(world.get(wn)), world.get(ni));
            const m = matMul(s.m, local);
            emitPrim(ni, prim, matName, m, [s.pos[1], s.pos[2]], true, wantGlass);
          } else {
            // Legacy shared wheel mesh: take one copy (FR) and re-emit at each slot.
            if (!frSlot || wn !== frSlot.ni) continue;
            const local = matMul(invert(world.get(wn)), world.get(ni));
            for (const s of slots) {
              const m = matMul(s.m, local);
              emitPrim(ni, prim, matName, m, [s.pos[1], s.pos[2]], true, wantGlass);
            }
          }
        } else {
          emitPrim(ni, prim, matName, matMul(yaw, world.get(ni)), null, false, wantGlass);
        }
      }
    }
  };
  emitAll(false);
  if (cfg.glassLast) emitAll(true);
  // manifest from the baked bbox; width can be restricted to the body below the mirrors
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9, minZ = 1e9, maxZ = -1e9;
  for (let i = 0; i < out.pos.length; i += 3) {
    minX = Math.min(minX, out.pos[i]); maxX = Math.max(maxX, out.pos[i]);
    minY = Math.min(minY, out.pos[i + 1]); maxY = Math.max(maxY, out.pos[i + 1]);
    minZ = Math.min(minZ, out.pos[i + 2]); maxZ = Math.max(maxZ, out.pos[i + 2]);
  }
  const bodyWidth = (cfg.widthMaxY != null && isFinite(widthMinX) && widthMaxX > widthMinX)
    ? widthMaxX - widthMinX
    : maxX - minX;
  const manifest = {
    length: maxZ - minZ,
    width: bodyWidth,
    height: maxY - minY,
    min: [minX, minY, minZ],
    max: [maxX, maxY, maxZ],
    axleF: slots.find((s) => /F[LR]$/.test(s.name))?.pos[2] ?? -maxZ * 0.3,
    axleR: slots.find((s) => !/F[LR]$/.test(s.name))?.pos[2] ?? maxZ * 0.3,
    wheelRadius: (slots[0] || { pos: [0, 0.33, 0] }).pos[1],
    verts: out.pos.length / 3,
  };
  const F = (arr, name) => writeFileSync(join(OUT, `veh_${type}${suffix}_${name}.bin`), Buffer.from(Float32Array.from(arr).buffer));
  F(out.pos, 'pos'); F(out.nor, 'nor'); F(out.uv, 'uv'); F(out.tag, 'tag'); F(out.axle, 'axle');
  writeFileSync(join(OUT, `veh_${type}${suffix}.json`), JSON.stringify(manifest));
  console.log(type + suffix, source.slice(ROOT.length + 1), manifest.verts, 'verts', 'len', manifest.length.toFixed(3),
    'w', manifest.width.toFixed(3), 'h', manifest.height.toFixed(3), 'wheelR', manifest.wheelRadius.toFixed(3),
    'axles', manifest.axleF.toFixed(2), manifest.axleR.toFixed(2), 'slots', slots.length);
  if (unmapped.size) console.warn('  unmapped materials (tagged 2):', [...unmapped].join(', '));
}

// --only <type> limits the run to a single car type.
const onlyIdx = process.argv.indexOf('--only');
const only = onlyIdx >= 0 ? process.argv[onlyIdx + 1] : null;
if (only && !TYPES.includes(only)) {
  console.error(`unknown type '${only}'; expected one of ${TYPES.join(', ')}`);
  process.exit(1);
}
for (const type of TYPES) {
  if (only && type !== only) continue;
  const cfg = type === 'kei' ? KEI : legacyConfig(type);
  for (const s of cfg.sources) dumpOne(type, join(ROOT, s.src), s.suffix, cfg);
}
console.log('done');

function invert(m) {
  // general 4x4 inverse via gauss-jordan (column-major)
  const a = m, o = new Array(16);
  const A = [[a[0], a[4], a[8], a[12]], [a[1], a[5], a[9], a[13]], [a[2], a[6], a[10], a[14]], [a[3], a[7], a[11], a[15]]];
  const I = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]];
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
