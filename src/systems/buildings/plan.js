// District plan: turns LAYOUT into a deterministic list of building lots.
// Lot = { id, x0,x1,z0,z1, baseY, h, floors, floorH, groundH, style, detail:'hi'|'mid'|'lo',
//         fronts: [{ side:'nx'|'px'|'nz'|'pz', district, shop, lane }], setback, kind }
import { mulberry32 } from './rng.js';

const EPS = 0.05;

export const STYLES = {
  // layer = wall texture atlas layer, kind = window layout (0 punched, 1 ribbon, 2 curtain wall)
  tile:      { layer: 0, texScale: 0.9, kind: 0, bay: [2.4, 3.4], ww: [0.5, 0.72], wh: [0.48, 0.6], balcony: 0.5, ac: 0.7, frame: 'alu' },
  whitetile: { layer: 6, texScale: 1.2, kind: 0, bay: [2.6, 3.6], ww: [0.55, 0.8], wh: [0.5, 0.62], balcony: 0.75, ac: 0.8, frame: 'alu' },
  concrete:  { layer: 1, texScale: 4.0, kind: 0, bay: [2.8, 4.2], ww: [0.45, 0.7], wh: [0.45, 0.58], balcony: 0.3, ac: 0.6, frame: 'dark' },
  ribbon:    { layer: 1, texScale: 4.0, kind: 1, bay: [1.6, 2.4], ww: [1, 1], wh: [0.5, 0.62], balcony: 0, ac: 0.3, frame: 'dark' },
  render:    { layer: 3, texScale: 3.0, kind: 0, bay: [2.2, 3.2], ww: [0.4, 0.6], wh: [0.42, 0.55], balcony: 0.25, ac: 0.9, frame: 'alu' },
  corrugated:{ layer: 2, texScale: 2.5, kind: 0, bay: [2.4, 3.4], ww: [0.35, 0.55], wh: [0.4, 0.5], balcony: 0, ac: 0.8, frame: 'rust' },
  metal:     { layer: 4, texScale: 3.6, kind: 1, bay: [1.8, 2.6], ww: [1, 1], wh: [0.55, 0.7], balcony: 0, ac: 0.2, frame: 'dark' },
  brick:     { layer: 5, texScale: 1.6, kind: 0, bay: [2.4, 3.2], ww: [0.45, 0.62], wh: [0.48, 0.58], balcony: 0.35, ac: 0.7, frame: 'dark' },
  glass:     { layer: 4, texScale: 3.6, kind: 2, bay: [1.5, 1.8], ww: [1, 1], wh: [1, 1], balcony: 0, ac: 0.0, frame: 'dark' },
};

const FRONT_STYLES = [['tile', 3], ['whitetile', 2.5], ['concrete', 2], ['render', 2.5], ['brick', 1.5], ['metal', 1.2], ['ribbon', 1.2], ['corrugated', 0.8], ['glass', 0.5]];
const TOWER_STYLES = [['glass', 3], ['metal', 2], ['ribbon', 2], ['concrete', 1.5], ['whitetile', 1.5], ['tile', 1]];

export function buildPlan(L) {
  const rng = mulberry32(0xC17E);
  const lots = [];
  const av = L.avenue;
  const front = L.blocks.frontageOffset; // 10
  const crossHalf = L.crossStreets.roadHalfWidth + L.crossStreets.sidewalkWidth; // 8
  const alley = L.alley;
  const alleyHalf = alley.width / 2;
  const railHalf = L.elevatedRail.width / 2 + 1.5;
  const expHalf = L.expressway.width / 2 + 2;
  const riverBankW = L.promenade.xMin - 4; // 124
  const riverBankE = L.river.xCenter + L.river.halfWidth + 6; // 216
  const ov = L.overlook.position;

  // --------- height caps (landmark corridors & overlook view cone) ---------
  const ovLook = L.overlook.lookAt;
  const ovDir = norm2([ovLook[0] - ov[0], ovLook[2] - ov[2]]);
  function heightCap(x0, x1, z0, z1) {
    const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
    let cap = 999;
    // overlook view cone
    const dx = cx - ov[0], dz = cz - ov[2];
    const d = Math.hypot(dx, dz);
    if (d > 8 && d < 260) {
      const c = (dx * ovDir[0] + dz * ovDir[1]) / d;
      const side = dx * -ovDir[1] + dz * ovDir[0]; // >0 => left of view? (perp)
      if (c > Math.cos(0.62) && !(side < -0 && c < Math.cos(0.42))) cap = Math.min(cap, 12 + d * 0.035);
    }
    // south of the avenue start: keep low so the aerial view (from z=+120) sees over it into the district
    if (cz > 45) cap = Math.min(cap, 14 + Math.max(0, cz - 45) * 0.12);
    // aerial view corridor: keep the south-west avenue blocks moderate so the avenue canyon reads from above
    if (cz > -70 && cz < 45 && cx < -6 && cx > -140) cap = Math.min(cap, 30);
    // under elevated rail / expressway: keep below decks (they are owned by landmarks)
    const nearZ = (zc, half) => z0 < zc + half && z1 > zc - half;
    if (nearZ(L.elevatedRail.z, railHalf + 2)) cap = Math.min(cap, L.elevatedRail.height - 3);
    if (nearZ(L.expressway.z, expHalf + 2)) cap = Math.min(cap, L.expressway.height - 4);
    return cap;
  }

  function pickStyle(table, w, floors) {
    let s = rng.weighted(table);
    if (s === 'corrugated' && floors > 5) s = 'render';
    if (s === 'glass' && (w < 9 || floors < 6)) s = 'metal';
    return s;
  }

  let nextId = 0;
  function addLot(r, opts) {
    const w = Math.min(r.x1 - r.x0, r.z1 - r.z0);
    if (w < 3.5) return null;
    const floorH = opts.floorH ?? rng.range(3.0, 3.45);
    let floors = opts.floors;
    const groundH = opts.groundH ?? rng.range(3.9, 4.6);
    let h = groundH + (floors - 1) * floorH;
    const cap = heightCap(r.x0, r.x1, r.z0, r.z1);
    if (h > cap) {
      floors = Math.max(1, Math.floor((cap - groundH) / floorH) + 1);
      h = groundH + (floors - 1) * floorH;
    }
    const style = opts.style || pickStyle(opts.tower ? TOWER_STYLES : FRONT_STYLES, w, floors);
    const lot = {
      id: nextId++, x0: r.x0, x1: r.x1, z0: r.z0, z1: r.z1, baseY: opts.baseY || 0,
      floors, floorH, groundH, h, style, detail: opts.detail || 'lo', fronts: [], tower: !!opts.tower,
      seed: rng(), setback: null, roofKind: rng(),
    };
    // stepped setback for taller buildings
    if (floors >= 9 && rng.chance(opts.tower ? 0.55 : 0.35)) {
      const sf = Math.floor(floors * rng.range(0.45, 0.75));
      lot.setback = { atFloor: sf, inset: rng.range(1.5, 3.5) };
    }
    lots.push(lot);
    return lot;
  }

  // Carve a rectangular block: frontage strips along street sides (in order), interior BSP towers.
  // streets: [{ side, district, shop, lane, depth:[a,b], width:[a,b], floors:[a,b], detail }]
  function carveBlock(rect, streetsIn, cfg = {}) {
    const seenSides = new Set();
    const streets = streetsIn.filter((s) => (seenSides.has(s.side) ? false : (seenSides.add(s.side), true)));
    let rem = { ...rect };
    const made = [];
    for (const st of streets) {
      const len = (st.side === 'nx' || st.side === 'px') ? rem.z1 - rem.z0 : rem.x1 - rem.x0;
      const maxD = ((st.side === 'nx' || st.side === 'px') ? rem.x1 - rem.x0 : rem.z1 - rem.z0);
      if (len < 4 || maxD < 4) continue;
      let d = Math.min(rng.range(st.depth[0], st.depth[1]), maxD);
      if (maxD - d < 8) d = maxD; // don't leave slivers
      // subdivide along the strip
      let t = 0;
      const widths = [];
      while (t < len - 0.01) {
        let w = rng.range(st.width[0], st.width[1]);
        if (len - t - w < st.width[0]) w = len - t;
        widths.push(w); t += w;
      }
      t = 0;
      for (const w of widths) {
        let r;
        // per-lot depth jitter for back edge variety (only when strip is not full depth)
        const dj = d >= maxD ? d : Math.max(4, Math.min(maxD, d + rng.range(-2.5, 2.5)));
        if (st.side === 'nx') r = { x0: rem.x0, x1: rem.x0 + dj, z0: rem.z0 + t, z1: rem.z0 + t + w };
        else if (st.side === 'px') r = { x0: rem.x1 - dj, x1: rem.x1, z0: rem.z0 + t, z1: rem.z0 + t + w };
        else if (st.side === 'nz') r = { x0: rem.x0 + t, x1: rem.x0 + t + w, z0: rem.z0, z1: rem.z0 + dj };
        else r = { x0: rem.x0 + t, x1: rem.x0 + t + w, z0: rem.z1 - dj, z1: rem.z1 };
        t += w;
        const lot = addLot(r, {
          floors: rng.int(st.floors[0], st.floors[1]), detail: st.detail, baseY: st.baseY,
          groundH: st.groundH, style: st.style,
        });
        if (lot) made.push(lot);
      }
      // the strip consumed d (use max jitter extent conservatively: interior starts after d+2.5 if jittered)
      const used = d >= maxD ? d : d + 2.5;
      if (st.side === 'nx') rem.x0 += used; else if (st.side === 'px') rem.x1 -= used;
      else if (st.side === 'nz') rem.z0 += used; else rem.z1 -= used;
      if (rem.x1 - rem.x0 < 4 || rem.z1 - rem.z0 < 4) { rem = null; break; }
    }
    // gap fill between jittered strips and interior: the interior rect is shrunk; fill it with towers
    if (rem && cfg.interior !== false) {
      bsp(rem, cfg.towerSize || [12, 26], (r) => {
        const lot = addLot(r, {
          floors: rng.int(...(cfg.towerFloors || [8, 20])), detail: cfg.towerDetail || 'lo', tower: true,
        });
        if (lot) made.push(lot);
      });
    }
    // assign fronts: any lot touching the original rect boundary on a street side
    for (const lot of made) {
      for (const st of streets) {
        const touch = st.side === 'nx' ? Math.abs(lot.x0 - rect.x0) < EPS
          : st.side === 'px' ? Math.abs(lot.x1 - rect.x1) < EPS
          : st.side === 'nz' ? Math.abs(lot.z0 - rect.z0) < EPS
          : Math.abs(lot.z1 - rect.z1) < EPS;
        if (touch) lot.fronts.push({ side: st.side, district: st.district, shop: !!st.shop, lane: !!st.lane, baseY: st.baseY || 0 });
      }
      if (lot.fronts.length && lot.tower) lot.tower = false;
    }
    // fill jitter gaps: towers behind strips leave voids of up to 2.5m — extend strip lots backwards to touch
    return made;
  }

  function bsp(r, size, cb) {
    const w = r.x1 - r.x0, d = r.z1 - r.z0;
    if (w < 4 || d < 4) return;
    if (w <= size[1] && d <= size[1] && (rng() < 0.4 || (w < size[0] * 2 && d < size[0] * 2))) { cb(r); return; }
    if (w >= d) {
      if (w < size[0] * 2) { cb(r); return; }
      const s = r.x0 + w * rng.range(0.35, 0.65);
      bsp({ ...r, x1: s }, size, cb); bsp({ ...r, x0: s }, size, cb);
    } else {
      if (d < size[0] * 2) { cb(r); return; }
      const s = r.z0 + d * rng.range(0.35, 0.65);
      bsp({ ...r, z1: s }, size, cb); bsp({ ...r, z0: s }, size, cb);
    }
  }

  // ---------------- Z segmentation along the avenue ----------------
  const zCuts = [];
  for (const z of L.crossStreets.zs) zCuts.push([z - crossHalf, z + crossHalf, 'cross']);
  zCuts.push([L.elevatedRail.z - railHalf, L.elevatedRail.z + railHalf, 'rail']);
  zCuts.push([L.expressway.z - expHalf, L.expressway.z + expHalf, 'exp']);
  function segments(zA, zB, extraCuts = []) { // zA > zB (south to north), returns [{z0,z1, nKind, sKind}]
    const cuts = [...zCuts, ...extraCuts].sort((a, b) => b[1] - a[1]);
    const segs = [];
    let cur = zA, curKind = 'end';
    for (const c of cuts) {
      if (c[1] > cur || c[0] < zB) continue;
      if (cur - c[1] > 4) segs.push({ z0: c[1], z1: cur, nKind: c[2], sKind: curKind });
      cur = c[0]; curKind = c[2];
    }
    if (cur - zB > 4) segs.push({ z0: zB, z1: cur, nKind: 'end', sKind: curKind });
    return segs;
  }
  const isStreet = (k) => k === 'cross' || k === 'alleyS' || k === 'alleyN' || k === 'lane' || k === 'end';
  const kindDistrict = (k) => (k === 'alleyS' || k === 'alleyN') ? 'alley' : 'cross';

  // ---------------- Avenue blocks (both sides) ----------------
  const backLaneX = 38, backLaneW = 5;
  for (const sideSign of [1, -1]) {
    const extra = sideSign < 0 ? [[alley.z - alleyHalf, alley.z + alleyHalf, 'alley']] : [];
    for (const seg of segments(av.zStart, av.zEnd, extra)) {
      const x0 = sideSign > 0 ? front : -backLaneX, x1 = sideSign > 0 ? backLaneX : -front;
      const rect = { x0, x1, z0: seg.z0, z1: seg.z1 };
      const isAlleySeg = sideSign < 0 && (seg.nKind === 'alley' || seg.sKind === 'alley');
      const streets = [];
      const avSide = sideSign > 0 ? 'nx' : 'px';
      streets.push({ side: avSide, district: 'avenue', shop: true, depth: [11, 16], width: [6, 13], floors: [4, 13], detail: 'hi' });
      const kN = seg.nKind === 'alley' ? 'alleyS' : seg.nKind; // block north edge (z0) faces street to north
      const kS = seg.sKind === 'alley' ? 'alleyN' : seg.sKind;
      if (isStreet(kN) && kN !== 'end') streets.push({ side: 'nz', district: kindDistrict(kN), shop: true, depth: [8, 14], width: isAlleySeg ? [4, 7] : [6, 12], floors: isAlleySeg ? [2, 6] : [3, 10], detail: 'hi' });
      if (isStreet(kS) && kS !== 'end') streets.push({ side: 'pz', district: kindDistrict(kS), shop: true, depth: [8, 14], width: isAlleySeg ? [4, 7] : [6, 12], floors: isAlleySeg ? [2, 6] : [3, 10], detail: 'hi' });
      // rail / expressway edges are not streets but still visible: treat as lanes w/o shops
      if (seg.nKind === 'rail' || seg.nKind === 'exp') streets.push({ side: 'nz', district: 'cross', shop: false, lane: true, depth: [8, 12], width: [6, 12], floors: [2, 4], detail: 'mid' });
      if (seg.sKind === 'rail' || seg.sKind === 'exp') streets.push({ side: 'pz', district: 'cross', shop: false, lane: true, depth: [8, 12], width: [6, 12], floors: [2, 4], detail: 'mid' });
      // back lane
      streets.push({ side: sideSign > 0 ? 'px' : 'nx', district: 'cross', shop: false, lane: true, depth: [6, 9], width: [7, 14], floors: [3, 8], detail: 'mid' });
      // special: the alley-adjacent west segments get extended alley handled separately below
      carveBlock(rect, streets, { towerFloors: [10, 22], towerDetail: 'mid', towerSize: [12, 22] });
    }
  }

  // ---------------- Alley district (west, beyond the back lane) ----------------
  // Lots on both sides of the alley from the back lane to the alley end, plus a closing building.
  const aX0 = -alley.xEnd > 0 ? alley.xEnd : alley.xEnd; // -90
  const alleyEndX = alley.xEnd - 7; // closing building west edge
  {
    const zs = alley.z + alleyHalf, zn = alley.z - alleyHalf;
    const south = { x0: alley.xEnd, x1: -backLaneX - backLaneW, z0: zs, z1: L.crossStreets.zs[0] - crossHalf };
    const north = { x0: alley.xEnd, x1: -backLaneX - backLaneW, z0: L.elevatedRail.z + railHalf, z1: zn };
    const st = (side, baseY) => ({ side, district: 'alley', shop: true, depth: [7, 11], width: [3.6, 6.5], floors: [2, 5], detail: 'hi' });
    carveBlock(south, [st('nz'), { side: 'pz', district: 'cross', shop: true, depth: [8, 12], width: [6, 11], floors: [3, 9], detail: 'hi' },
      { side: 'px', district: 'cross', shop: false, lane: true, depth: [6, 9], width: [6, 11], floors: [3, 6], detail: 'mid' }],
      { towerFloors: [6, 14], towerDetail: 'mid', towerSize: [10, 18] });
    carveBlock(north, [st('pz'), { side: 'nz', district: 'cross', shop: false, lane: true, depth: [8, 10], width: [6, 11], floors: [2, 4], detail: 'mid' },
      { side: 'px', district: 'cross', shop: false, lane: true, depth: [6, 9], width: [6, 11], floors: [3, 6], detail: 'mid' }],
      { towerFloors: [6, 14], towerDetail: 'mid', towerSize: [10, 18] });
    // closing building at the alley end (upper level y = rise)
    const endLot = addLot({ x0: alleyEndX, x1: alley.xEnd, z0: north.z1 - 8, z1: south.z0 + 8 }, { floors: 6, detail: 'hi', style: 'render', groundH: 4.2 });
    if (endLot) endLot.fronts.push({ side: 'px', district: 'alley', shop: true, baseY: alley.stairs.rise });
  }
  // mark alley lots' fronts baseY according to stairs position
  for (const lot of lots) for (const f of lot.fronts) {
    if (f.district !== 'alley') continue;
    const cx = (lot.x0 + lot.x1) / 2;
    const s = alley.stairs;
    if (cx < s.xEnd) f.baseY = s.rise; else if (cx < s.xStart) f.baseY = -1; // -1 = along the stairs (no shopfront)
  }

  // ---------------- Back regions ----------------
  // generic grid filler for a big rect with lanes; streetsFor(rect) returns sides that face real streets.
  function fillRegion(R, laneXs, laneZs, laneW, cfgFn) {
    const xs = [R.x0, ...laneXs.flatMap((x) => [x - laneW / 2, x + laneW / 2]), R.x1].sort((a, b) => a - b);
    const zsAll = [R.z0, ...laneZs.flatMap((z) => [z[0], z[1]]), R.z1].sort((a, b) => a - b);
    for (let i = 0; i < xs.length - 1; i += 2) {
      for (let j = 0; j < zsAll.length - 1; j += 2) {
        const r = { x0: xs[i], x1: xs[i + 1], z0: zsAll[j], z1: zsAll[j + 1] };
        if (r.x1 - r.x0 < 6 || r.z1 - r.z0 < 6) continue;
        const c = cfgFn(r);
        if (c) carveBlock(r, c.streets, c.cfg);
      }
    }
  }
  const crossZ = L.crossStreets.zs;
  const zGap = (zc, half) => [zc - half, zc + half];
  const nearCross = (z, side) => crossZ.some((cz) => Math.abs((side === 'nz' ? z - (cz + crossHalf) : (cz - crossHalf) - z)) < 0.5);
  function backCfg(r, detailNear, distFn) {
    const dist = distFn(r);
    const detail = dist < detailNear ? 'mid' : 'lo';
    const streets = [];
    const crossShop = dist < 90;
    const wN = nearCross(r.z0, 'nz'), wS = nearCross(r.z1, 'pz');
    if (wN) streets.push({ side: 'nz', district: 'cross', shop: crossShop, depth: [9, 14], width: [6, 13], floors: [3, 10], detail: crossShop ? 'hi' : detail });
    if (wS) streets.push({ side: 'pz', district: 'cross', shop: crossShop, depth: [9, 14], width: [6, 13], floors: [3, 10], detail: crossShop ? 'hi' : detail });
    if (!wN) streets.push({ side: 'nz', district: 'cross', shop: false, lane: true, depth: [8, 13], width: [6, 13], floors: [2, 8], detail });
    if (!wS) streets.push({ side: 'pz', district: 'cross', shop: false, lane: true, depth: [8, 13], width: [6, 13], floors: [2, 8], detail });
    streets.push({ side: 'nx', district: 'cross', shop: false, lane: true, depth: [8, 13], width: [6, 13], floors: [2, 9], detail });
    streets.push({ side: 'px', district: 'cross', shop: false, lane: true, depth: [8, 13], width: [6, 13], floors: [2, 9], detail });
    return { streets, cfg: { towerFloors: dist < 120 ? [6, 18] : [8, 26], towerDetail: detail, towerSize: [12, 24] } };
  }

  const backZCuts = [
    ...crossZ.map((z) => zGap(z, crossHalf)),
    zGap(L.elevatedRail.z, railHalf), zGap(L.expressway.z, expHalf),
    zGap(L.bridges[0].z, 6), zGap(-5, 3), zGap(-110, 3), zGap(-215, 3), zGap(-355, 3), zGap(-395, 3), zGap(25, 3),
  ];
  // East back region: x from back lane to the riverbank
  {
    const R = { x0: backLaneX + backLaneW, x1: riverBankW, z0: av.zEnd, z1: av.zStart };
    fillRegion(R, [84], backZCuts.filter((g) => g[0] > R.z0 && g[1] < R.z1), 6, (r) => {
      // overlook building occupies its own lot
      const c = backCfg(r, 160, (q) => Math.abs((q.x0 + q.x1) / 2));
      if (Math.abs(r.x1 - riverBankW) < EPS) c.streets.unshift({ side: 'px', district: 'river', shop: false, depth: [9, 14], width: [7, 14], floors: [4, 12], detail: 'mid' });
      return c;
    });
  }
  // West back region (excluding alley district)
  {
    const R = { x0: -200, x1: -backLaneX - backLaneW, z0: av.zEnd, z1: av.zStart };
    fillRegion(R, [-96, -150], backZCuts.filter((g) => g[0] > R.z0 && g[1] < R.z1), 6, (r) => {
      // skip the alley district rect
      const cz = (r.z0 + r.z1) / 2, cx = (r.x0 + r.x1) / 2;
      if (cz > L.elevatedRail.z && cz < crossZ[0] && cx > alleyEndX - 1) return null;
      return backCfg(r, 140, (q) => Math.abs((q.x0 + q.x1) / 2));
    });
  }
  // South fill beyond the avenue start (closing the view) and north beyond the avenue end
  {
    const Rs = { x0: -200, x1: riverBankW, z0: av.zStart + 6, z1: 170 };
    fillRegion(Rs, [-96, -40, 40, 84], [zGap(110, 3)], 6, (r) => backCfg(r, 0, () => 999));
    const Rn = { x0: -200, x1: riverBankW, z0: -560, z1: av.zEnd - 6 };
    fillRegion(Rn, [-96, -40, 0, 40, 84], [zGap(-470, 3)], 6, (r) => backCfg(r, 0, () => 999));
  }
  // East bank of the river (far, low detail) facing the river
  {
    const R = { x0: riverBankE, x1: 400, z0: -700, z1: 170 };
    fillRegion(R, [270, 330], [zGap(-480, 4), zGap(-330, 4), zGap(-200, 4), zGap(-140, railHalf + 2), zGap(-240, expHalf + 2), zGap(-60, 4), zGap(60, 4)], 8, (r) => {
      const c = backCfg(r, 0, () => 999);
      c.cfg.towerFloors = [10, 34];
      if (Math.abs(r.x0 - riverBankE) < EPS) c.streets.unshift({ side: 'nx', district: 'river', shop: false, depth: [10, 16], width: [8, 16], floors: [5, 16], detail: 'lo' });
      return c;
    });
  }

  // ---------------- Overlook building ----------------
  // remove lots overlapping the overlook footprint, then add the special building
  const ovFoot = { x0: ov[0] - 8, x1: ov[0] + 9, z0: ov[2] + crossHalf + 0.0, z1: ov[2] + crossHalf + 22 };
  for (let i = lots.length - 1; i >= 0; i--) {
    const l = lots[i];
    if (l.x0 < ovFoot.x1 && l.x1 > ovFoot.x0 && l.z0 < ovFoot.z1 && l.z1 > ovFoot.z0) lots.splice(i, 1);
  }
  const deckY = ov[1] - 1.6;
  const ovLot = addLot(ovFoot, { floors: 7, floorH: (deckY - 4.4) / 6, groundH: 4.4, style: 'tile', detail: 'hi' });
  ovLot.fronts.push({ side: 'nz', district: 'cross', shop: true });
  ovLot.overlook = { deckY };
  ovLot.setback = null;

  // ---------------- Side exposure: party walls vs open sides (open sides get real windows) ----------------
  const G = 32, grid = new Map();
  const key = (i, j) => i * 100003 + j;
  for (const l of lots) {
    for (let i = Math.floor(l.x0 / G); i <= Math.floor(l.x1 / G); i++) for (let j = Math.floor(l.z0 / G); j <= Math.floor(l.z1 / G); j++) {
      const k = key(i, j); if (!grid.has(k)) grid.set(k, []); grid.get(k).push(l);
    }
  }
  for (const l of lots) {
    l.cover = {};
    const near = new Set();
    for (let i = Math.floor((l.x0 - 1) / G); i <= Math.floor((l.x1 + 1) / G); i++) for (let j = Math.floor((l.z0 - 1) / G); j <= Math.floor((l.z1 + 1) / G); j++) for (const o of grid.get(key(i, j)) || []) if (o !== l) near.add(o);
    for (const side of ['nx', 'px', 'nz', 'pz']) {
      let cov = 0;
      const len = (side === 'nx' || side === 'px') ? l.z1 - l.z0 : l.x1 - l.x0;
      for (const o of near) {
        let touch, a0, a1;
        if (side === 'nx') { touch = Math.abs(o.x1 - l.x0) < 1.0; a0 = Math.max(o.z0, l.z0); a1 = Math.min(o.z1, l.z1); }
        else if (side === 'px') { touch = Math.abs(o.x0 - l.x1) < 1.0; a0 = Math.max(o.z0, l.z0); a1 = Math.min(o.z1, l.z1); }
        else if (side === 'nz') { touch = Math.abs(o.z1 - l.z0) < 1.0; a0 = Math.max(o.x0, l.x0); a1 = Math.min(o.x1, l.x1); }
        else { touch = Math.abs(o.z0 - l.z1) < 1.0; a0 = Math.max(o.x0, l.x0); a1 = Math.min(o.x1, l.x1); }
        // only counts as a party wall if the neighbour is roughly as tall
        if (touch && a1 > a0 && o.h > l.h * 0.6) cov += a1 - a0;
      }
      l.cover[side] = Math.min(1, cov / Math.max(len, 0.1));
    }
  }
  return { lots, heightCap };
}

function norm2(v) { const l = Math.hypot(v[0], v[1]); return [v[0] / l, v[1] / l]; }
