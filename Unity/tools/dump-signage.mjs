// Dump streetlamp + utility_pole GLBs (signage) into raw buffers for Unity.
// Per top-level node (body/lens): bake node matrixWorld into positions (JS does m4.multiply(mw)).
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';

const ROOT = 'C:/Users/smith/OneDrive/Documents/GitHub/cybercity';
const OUT = ROOT + '/unity/Assets/Game/Models/Signage/dump';
mkdirSync(OUT, { recursive: true });

function dump(file, prefix, wantName) {
  const b = readFileSync(file);
  const jsonLen = b.readUInt32LE(12);
  const json = JSON.parse(b.slice(20, 20 + jsonLen).toString());
  const binOff = 20 + jsonLen + 8; // GLB BIN chunk data starts here

  const nodeWorld = (ni, parent) => {
    const n = json.nodes[ni];
    const m = new Float64Array(16);
    if (n.matrix) { for (let i = 0; i < 16; i++) m[i] = n.matrix[i]; }
    else {
      const q = n.rotation || [0, 0, 0, 1], s = n.scale || [1, 1, 1], t = n.translation || [0, 0, 0];
      const [x, y, z, w] = q;
      const x2 = x + x, y2 = y + y, z2 = z + z;
      const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
      m[0] = (1 - (yy + zz)) * s[0]; m[1] = (xy + wz) * s[0]; m[2] = (xz - wy) * s[0];
      m[4] = (xy - wz) * s[1]; m[5] = (1 - (xx + zz)) * s[1]; m[6] = (yz + wx) * s[1];
      m[8] = (xz + wy) * s[2]; m[9] = (yz - wx) * s[2]; m[10] = (1 - (xx + yy)) * s[2];
      m[12] = t[0]; m[13] = t[1]; m[14] = t[2]; m[15] = 1;
    }
    if (parent) { // out = parent * m (column-major)
      const o = new Float64Array(16);
      for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
        let a = 0;
        for (let k = 0; k < 4; k++) a += parent[k * 4 + r] * m[c * 4 + k];
        o[c * 4 + r] = a;
      }
      return o;
    }
    return m;
  };

  let dumped = 0;
  const walk = (ni, parent) => {
    const n = json.nodes[ni];
    const mw = nodeWorld(ni, parent);
    if (n.mesh !== undefined && n.name === wantName) {
      const mesh = json.meshes[n.mesh];
      let vp = [], ip = [];
      for (const prim of mesh.primitives) {
        const pa = json.accessors[prim.attributes.POSITION];
        const ia = json.accessors[prim.indices];
        const pv = json.bufferViews[pa.bufferView];
        const posOff = binOff + (pv.byteOffset || 0) + (pa.byteOffset || 0);
        const base = vp.length / 3;
        for (let i = 0; i < pa.count; i++) {
          const x = b.readFloatLE(posOff + i * 12), y = b.readFloatLE(posOff + i * 12 + 4), z = b.readFloatLE(posOff + i * 12 + 8);
          vp.push(
            mw[0] * x + mw[4] * y + mw[8] * z + mw[12],
            mw[1] * x + mw[5] * y + mw[9] * z + mw[13],
            mw[2] * x + mw[6] * y + mw[10] * z + mw[14]);
        }
        const iv = json.bufferViews[ia.bufferView];
        const idxOff = binOff + (iv.byteOffset || 0) + (ia.byteOffset || 0);
        for (let i = 0; i < ia.count; i++) {
          const v = ia.componentType === 5121 ? b.readUInt8(idxOff + i) : ia.componentType === 5123 ? b.readUInt16LE(idxOff + i * 2) : b.readUInt32LE(idxOff + i * 4);
          ip.push(base + v);
        }
      }
      const pos = new Float32Array(vp);
      const bits = ip.some((v) => v > 65535) ? 32 : 16;
      const idx = Buffer.alloc(ip.length * (bits / 8));
      for (let i = 0; i < ip.length; i++) bits === 16 ? idx.writeUInt16LE(ip[i], i * 2) : idx.writeUInt32LE(ip[i], i * 4);
      writeFileSync(`${OUT}/${prefix}_pos.bin`, Buffer.from(pos.buffer));
      writeFileSync(`${OUT}/${prefix}_idx.bin`, idx);
      writeFileSync(`${OUT}/${prefix}_meta.txt`, `${vp.length / 3} ${ip.length} ${bits}`);
      console.log(prefix, `${vp.length / 3} verts, ${ip.length} idx, bounds:`,
        [0, 1, 2].map((a) => {
          let mn = 1e9, mx = -1e9;
          for (let i = a; i < vp.length; i += 3) { mn = Math.min(mn, vp[i]); mx = Math.max(mx, vp[i]); }
          return `${mn.toFixed(2)}..${mx.toFixed(2)}`;
        }).join(' '));
      dumped++;
      return;
    }
    for (const c of n.children || []) walk(c, mw);
  };
  const scene = json.scenes[json.scene || 0];
  for (const ni of scene.nodes) walk(ni, null);
  if (!dumped) console.log(prefix, 'NOT FOUND!');
}

for (const [name, pre] of [['streetlamp', 'lamp'], ['utility_pole', 'pole']]) {
  const f = `${ROOT}/assets/models/signage/${name}.glb`;
  dump(f, pre + '_body', 'body');
  dump(f, pre + '_lens', 'lens');
}
// traffic + pedestrian signals
{
  const f = `${ROOT}/assets/models/signage/traffic_signal.glb`;
  for (const n of ['body', 'lensG', 'lensY', 'lensR']) dump(f, 'tsig_' + n, n);
}
{
  const f = `${ROOT}/assets/models/signage/ped_signal.glb`;
  for (const n of ['body', 'pedR', 'pedG']) dump(f, 'psig_' + n, n);
}
