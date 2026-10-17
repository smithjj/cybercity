// Oracle dump: runs the REAL buildings JS pipeline headlessly and writes lots/bucket/light/props data
// for the C# BuildingsOracle to compare against. GLBs are stubbed (instance transforms don't depend on them).
import { writeFileSync, mkdirSync } from 'node:fs';
import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { ChunkedBuckets } from '../../src/systems/buildings/geom.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { ShopBuilder } from '../../src/systems/buildings/shops.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { buildAlley } from '../../src/systems/buildings/alley.js';
import { buildOverlook } from '../../src/systems/buildings/overlook.js';
import { buildCables } from '../../src/systems/buildings/cables.js';

const L = LAYOUT;
const ARCH_ONLY = process.argv.includes('--arch');
const WITH_SHOPS = process.argv.includes('--shops');
const WITH_PROPS = process.argv.includes('--props');
const WITH_ALLEY = process.argv.includes('--alley');
const WITH_OVERLOOK = process.argv.includes('--overlook');
// One tagged prim per gltf call so the returned InstancedMeshes can be matched back to their category.
const geoTag = new Map();
const ctx = { layout: L, assets: { gltf: async (path) => {
  const THREE = await import('three/webgpu');
  const geo = new THREE.BufferGeometry();
  geo.userData = { cat: path };
  const fakeMesh = { isMesh: true, geometry: geo, material: { name: path } };
  return { scene: { updateMatrixWorld() {}, traverse(cb) { cb(fakeMesh); } } };
} } };

// ---------- exact index.js sequence ----------
const { lots } = buildPlan(L);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [], props: null };
for (const lot of lots) buildLotArchitecture(lot, B, out);

const shops = new ShopBuilder(B, out);
const OPEN = ['izakaya', 'ramen', 'bar', 'boutique', 'konbini', 'izakaya', 'ramen'];
let prev = null;
for (const lot of lots) {
  for (const f of lot.fronts) {
    if (ARCH_ONLY) break; // shops run in every stage dump except arch-only
    if (!(f.shop && lot.detail === 'hi' && f.baseY >= 0)) continue;
    const cx = (lot.x0 + lot.x1) / 2, cz = (lot.z0 + lot.z1) / 2;
    let type = null;
    if (f.district === 'avenue' && lot.x1 <= -9 && lot.z0 < 26 && lot.z1 > 18) type = 'ramen';
    else if (prev && prev.closed && Math.hypot(prev.x - cx, prev.z - cz) < 16) type = OPEN[lot.id % OPEN.length];
    f.shopType = shops.build(lot, f, { type });
    prev = { x: cx, z: cz, closed: f.shopType === 'shutter' || f.shopType === 'lobby' };
  }
}

const instMeshes = (ARCH_ONLY || WITH_SHOPS) ? [] : await buildProps(ctx, lots, B, out, { wetness: 1 });
if (WITH_ALLEY || WITH_OVERLOOK) buildAlley(ctx, lots, B, out);
if (WITH_OVERLOOK) buildOverlook(ctx, lots, B, out);
if (!ARCH_ONLY && !WITH_SHOPS && !WITH_PROPS && !WITH_ALLEY && !WITH_OVERLOOK) { buildAlley(ctx, lots, B, out); buildOverlook(ctx, lots, B, out); buildCables(ctx, lots, B, out); }

// ---------- lights: raw + greedy kept (exact index.js filter) ----------
const kept = [];
const cand = out.lights.filter((l) => !l.minor).sort((a, b) => b.intensity - a.intensity);
for (const l of cand) {
  if (kept.some((k) => (k.pos[0] - l.pos[0]) ** 2 + (k.pos[1] - l.pos[1]) ** 2 + (k.pos[2] - l.pos[2]) ** 2 < 121)) continue;
  kept.push(l);
}

// ---------- bucket stats ----------
const HB = (v) => new BigUint64Array(new Float64Array([v]).buffer)[0].toString(16).padStart(16, "0");
const sum = (arr) => { let s = 0; for (const v of arr) s += v; return s; };
const buckets = [];
for (const [key, b] of B.map.entries()) {
  const n = b.pos.length / 3;
  let minX = 1e30, minY = 1e30, minZ = 1e30, maxX = -1e30, maxY = -1e30, maxZ = -1e30, sx = 0, sy = 0, sz = 0;
  for (let i = 0; i < b.pos.length; i += 3) {
    const x = b.pos[i], y = b.pos[i + 1], z = b.pos[i + 2];
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    sx += x; sy += y; sz += z;
  }
  const attrSums = {};
  for (const k in b.attrs) attrSums[k] = HB(sum(b.attrs[k]));
  const stats = { key, verts: n, tris: b.idx.length / 3, bbox: [minX, minY, minZ, maxX, maxY, maxZ].map(HB),
    sums: [sx, sy, sz].map(HB), nsum: HB(sum(b.nrm)), uvsum: HB(sum(b.uv)), idxsum: sum(b.idx), attrSums };
  if (n <= 40000) {
    const ys = [];
    for (let i = 1; i < b.pos.length; i += 3) ys.push(b.pos[i]);
    ys.sort((a, b) => a - b);
    stats.ys = ys.map(HB);
  }
  buckets.push(stats);
}
buckets.sort((a, b) => a.key.localeCompare(b.key));

// ---------- exact bit-level dump: all doubles as IEEE-754 hex (immune to Mono float parse/format quirks) ----------

const dump = {
  lots: lots.map((l) => ({
    id: l.id, x0: HB(l.x0), x1: HB(l.x1), z0: HB(l.z0), z1: HB(l.z1), baseY: HB(l.baseY), h: HB(l.h),
    floors: l.floors, floorH: HB(l.floorH), groundH: HB(l.groundH), style: l.style, detail: l.detail,
    kind: l.kind, seed: HB(l.seed), roofKind: HB(l.roofKind), tower: l.tower,
    setback: l.setback ? { atFloor: l.setback.atFloor, inset: HB(l.setback.inset) } : null,
    cover: l.cover ? { nx: HB(l.cover.nx), px: HB(l.cover.px), nz: HB(l.cover.nz), pz: HB(l.cover.pz) } : null,
    volumes: (l.volumes || []).map((v) => [HB(v.x0), HB(v.x1), HB(v.z0), HB(v.z1), HB(v.y0), HB(v.y1)]),
    overhang: l.overhang ?? null, overhangSide: l.overhangSide ?? null,
    overlook: l.overlook ? { deckY: HB(l.overlook.deckY) } : null,
    fronts: (l.fronts || []).map((f) => ({ side: f.side, district: f.district, shop: f.shop, lane: f.lane, baseY: HB(f.baseY ?? 0), shopType: f.shopType ?? null })),
  })),
  buckets,
  lights: { kept: kept.length, raw: out.lights.length, list: kept.map((l) => ({ p: l.pos.map(HB), c: l.color.map(HB), i: HB(l.intensity), d: HB(l.distance) })) },
  props: (() => {
    const pm = { ac: [], tank: [], ptank: [] };
    for (const m of instMeshes) {
      const p = m.geometry.userData?.cat || '';
      const cat = p.includes('ac_unit') ? 'ac' : p.includes('water') ? 'tank' : 'ptank';
      const arr = m.instanceMatrix.array; // Float32 → f32 values
      for (let i = 0; i < arr.length; i++) pm[cat].push(HB(Math.fround(arr[i])));
    }
    return pm;
  })(),
  colliders: out.colliders.map((c) => [HB(c.x), HB(c.y), HB(c.z), HB(c.hx), HB(c.hy), HB(c.hz), HB(c.rotY ?? 0), HB(c.rot ? c.rot.x : 0), HB(c.rot ? c.rot.y : 0), HB(c.rot ? c.rot.z : 0), HB(c.rot ? c.rot.w : 0)]),
  facades: out.facades.length,
};

mkdirSync(new URL('../Temp/oracle/', import.meta.url), { recursive: true });
const OUT = ARCH_ONLY ? '../Temp/oracle/buildings-arch.json' : WITH_SHOPS ? '../Temp/oracle/buildings-shops.json' : WITH_PROPS ? '../Temp/oracle/buildings-props.json' : WITH_ALLEY ? '../Temp/oracle/buildings-alley.json' : WITH_OVERLOOK ? '../Temp/oracle/buildings-overlook.json' : '../Temp/oracle/buildings.json';
writeFileSync(new URL(OUT, import.meta.url), JSON.stringify(dump));
console.log(`[dump-buildings${ARCH_ONLY ? ' ARCH' : WITH_SHOPS ? ' SHOPS' : WITH_PROPS ? ' PROPS' : ''}] lots=${lots.length} buckets=${buckets.length} lights=${kept.length}/${out.lights.length} ac=${dump.props.ac.length / 16} tank=${dump.props.tank.length / 16} ptank=${dump.props.ptank.length / 16} colliders=${dump.colliders.length}`);
