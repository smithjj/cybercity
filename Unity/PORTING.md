# NEON KAIDO — Unity port status ledger

Plan: `../Unity/PORTING-PLAN.md`. Tooling how-to: `../Unity/EDITOR-CLI.md`.

## Conventions

- **Coordinates.** Sim (JS) `(x,y,z)` == Unity `(x,y,-z)`. All world data generated in C# is stored in
  Unity space; oracle dumps compare in sim space. `GameDebug.ToU(x,y,z)` is the only conversion.
  Yaw: JS forward `(-sin yaw, -cos yaw)` in (x,z); yaw 0 = north = sim -Z = Unity +Z.
  Unity camera yaw = `180 - simYaw`; Euler x>0 looks down.
- **Editor discipline** (see EDITOR-CLI.md): `ev.sh` for eval (~5 s budget → `delayCall` for more);
  `recompile --focus` then wait ~15 s before Play; `editor_stop` before editing scripts;
  Play needs `runInBackground=true` + `QueuePlayerLoopUpdate()`; capture `--save_path` must be a
  Windows path; shader errors → `ShaderUtil.GetShaderMessages`, not the console.
- **Determinism.** All generators use the ported `mulberry32` (first oracle) with the JS seeds.
- **Eval snippet quoting:** single-quoted shell strings; anything with quotes → file + `"$(cat f.cs)"`.
- **6000.6.3f1 API deltas (verified by reflection):** `GraphicsDeviceType` is in namespace
  `UnityEngine.Rendering`, member `Direct3D12` (no D3D12). `EditorBuildSettings.scenes` element type is
  `EditorBuildSettingsScene(path, isActive)` (no SceneReference). `ScreenCapture.CaptureScreenshot(string)`
  only (no 3-arg overload). Hand-rolled `TimeManager.asset` must use class id `!u!5` + `Fixed Timestep:
  m_Count/m_Rate` rational (old `!u!29` id is now OcclusionCullingSettings). `GraphicsSettings.defaultRenderPipeline`
  (currentRenderPipeline is get-only).
- **Editor lifecycle:** never `taskkill` the editor (triggers safe mode next launch — subset of assemblies,
  pipeline server half-broken). Clean quit: `unity command menu "File/Exit"` (returns "Invalid response
  format" — that's the server dying mid-response; the exit is clean). Pipeline server only starts if the
  pipeline package is present at first domain load — after `pipeline install`, restart the editor.
  Console `error` entries with `seeded:true` are stale; trust `groundTruth.compilationFailed`.
- **Stale domain hazard — RESOLVED:** `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` via ev
  picks up script AND shader edits reliably (the earlier "edits ignored" episodes were actually broken
  shader-include chains + cached variants, not file-watching). Restart the editor only for package/
  manifest changes. `Temp/` (incl. oracle dumps) is wiped by a restart — regenerate `dump-*.mjs` outputs
  after any restart.
- **Raw SRP shader boilerplate:** `Common.hlsl` alone lacks SpaceTransforms AND `_WorldSpaceCameraPos`
  AND `UNITY_MATRIX_M`; `SpaceTransforms.hlsl` needs the instancing machinery. Use
  `Assets/Shaders/NkCommon.hlsl` (declares UnityPerDraw cbuffer with unity_ObjectToWorld, unity_MatrixVP,
  _WorldSpaceCameraPos + NkWorld/NkClip manual transforms — Unity's custom-pipeline pattern). HLSL notes:
  no implicit vec2→vec3 promotion (add overloads, declared AFTER the float3 version — earlier declaration
  makes the call resolve to itself with truncation → "recursive functions not allowed"); no scalar-splat
  `float3(x)` constructors (DXC rejects); `ShaderUtil` messages can flag unused variants — trust Peek(render).
- **HDRP exposure sign:** fixedExposure is subtractive — EV=-5 brightens 32×, EV=+5 darkens. Review
  shots currently run at EV=-5; parity exposure for phase 2+ comparison is EV=0 (restore before tuning).
- **Numeric fidelity rules (learned via ground oracle):** (1) all JS-derived constants are C# `double`
  (a `float` const like 0.15f poisons downstream double math by ~6e-9); (2) JS `Math.hypot` == fdlibm
  (`a*sqrt(1+(b/a)²)`) — .NET `Math.Hypot` differs by 1 ulp; use `GroundGeo.Hypot2`; (3) THREE.ShapeGeometry
  = mapbox earcut v3.0.2 (ported: `Core/Earcut.cs`) which FILTERS collinear/duplicate points (66 vs 208
  tris); (4) three's `.rotateX(-PI/2)` leaves float32 artifacts `cos(-PI/2)·y` in positions and `cos(-PI/2)`
  in normal z (survive wherever translate doesn't absorb them) — replicated in the oracle; (5) eval snippets:
  no JsonElement indexer — use `EnumerateArray()` + `GetString()`.
- **5 s eval budget:** heavy calls (pipeline assignment, scene save, color-space switch) time out but keep
  running on the main thread — re-invoke idempotently until done.

## Status

| Phase | System / item | Status | Notes |
|---|---|---|---|
| 0 | Project 6000.6.3f1 HDRP DX12 | done | hand-rolled scaffold, `D.Setup()` finished it; Linear color space; DX12; Main.unity (60° cam at spawn, 200 m ground plane) |
| 0 | tools/ (ev, shot, snap, console, adv, run-oracle, run-oracle-bg) | done | Git Bash, not WSL: `U=C:/Users/.../unity.exe` |
| 0 | GameDebug.cs skeleton | done | PoseCam/Setup/State/Shot real; Run/Advance/At/Hour/Weather/Walk/Drive/Gig stubs. Sim-facing params are `double` (shell literals) |
| 0 | Smoke test (eval→recompile→play→step→shot) | done | frames ticked 935→1775; street.png 110 KB via shot.sh |
| 1 | Scene persists across restarts | done | BuildAtmosphere/BuildGround save Main.unity |
| 1 | mulberry32 oracle | PASS | 512 values bit-exact, 8 seeds |
| 1 | Layout.cs, SimCoord, GameLoop | done | Layout consts are DOUBLE (JS numbers; float consts poison double math) |
| 1 | Atmosphere (day/night, sky, fog, light shafts) | done | skydome + noise byte-exact; fog = HDRP volume approximation; no time-of-day exists in JS |
| 1 | Ground (streets, curbs, markings, puddles) | done | **GroundGeo oracle: all meshes bit-exact**; earcut v3.0.2 ported |
| 1 | Sidewalk/curb/promenade/alley/wall surfaces | done | Surf.shader, 5 modes; tactile strips/dots; caveat: ShaderCheck messages are noise for raw SRP shaders — Peek(render) is the shader oracle |
| 1 | Wet-road HDRP shader | done | full roadMaterial.js port (NEON KAIDO/Road); deviations: analytic lighting + inline fog + approx worley; marking atlas generated (mark_atlas.png) |
| 2 | Buildings (plan/architecture/shops/props/cables) | not started | |
| 2 | Landmarks (rail, skyline, river, bridge, promenade, trees) | not started | skyline = 140 instanced towers |
| 2 | Signage (library, atlas Canvas2D→Texture2D, billboards, lanterns) | not started | JP fonts: Yu Gothic/Meiryo/MS Gothic |
| 2 | Lighting spike + lightformers/bake + light budget | not started | risk R1 |
| 3 | Player KCC + camera rig | not started | KCC map below |
| 3 | Gigs (5 waypoints, beams/rings, credits) | not started | |
| 3 | Vehicles (5 models LOD0-3, traffic, hero car) | not started | |
| 3 | Crowd (2 GLBs, agents, vat) | not started | |
| 4 | Weather (rain, splashes, drips, steam, lightning) | not started | fake light array ported verbatim (24 vec4) |
| 5 | PostFX chain → HDRP volume/RenderFeature | not started | |
| 5 | Audio (C# DSP lib, offline .wav gen, soundscape) | not started | standalone .NET lib first + offline oracle |
| 5 | UI (HUD, menus, minimap, settings) | not started | |
| 6 | Perf + Windows build | not started | |

## KCC param map (Rapier → Unity KinematicCharacterController)

| JS | Unity |
|---|---|
| radius 0.32 | radius 0.32 |
| half 0.58 stand / 0.24 crouch | height = 2×(half+radius) = 2.16 / 1.40 |
| autostep 0.42 / 0.18 | stepOffset 0.42 / 0.18 |
| snapToGround 0.45 | snapping ≈ 0.45 (0 disables; tune) |
| maxSlope 52°, minSlopeSlide 40° | slopeLimit 52; slide in movement code |
| gravity 21, jumpV 6.6, walk 4.3 / sprint 7.8 / crouch 2.1 | movement script |
| eye 1.66 / 1.02, coyote, jump buffer, viewK blend, bob/dip/roll/sway/lookLag | camera rig script |
| `placeAt()` raycast down 60 m | spawn/car-exit/elevator helper |

## Shot list (SHOT_VIEWS; ref panels 1-4 are the visual contract)

| name | pos (sim) | look-at (sim) | fov | content |
|---|---|---|---|---|
| street | [1.5, 1.6, 30] | [0.5, 3.5, -60] | 55 | wet neon avenue (panel 1) |
| overlook | [42, 26, -60] | [90, 22, -200] | 55 | rail + holo billboard + skyline (panel 2) |
| river | [133, 1.7, -10] | [150, 8, -140] | 55 | cherry promenade (panel 3) |
| alley | [-30, 2.2, -100] | [-70, 6.5, -100] | 55 | stairs + red lanterns + wires (panel 4) |
| aerial | [-60, 90, 120] | [40, 10, -200] | 50 | whole city |
| ground | [-4, 0.5, 5] | [2, 1.2, -25] | 60 | low wet road, splashes |

`shot.sh <name> <x> <z> <eyeH> <yaw> <pitch>` takes a direction, not a look-at; yaw/pitch for each
row are computed at first use (yaw = atan2 from pos→look-at in sim space).

## Oracle list

1. `Mulberry32` — bit-exact RNG — **PASS** (512 values, 8 seeds)
2. `NoiseTex` — atmosphere fBm texture — **PASS** (262144 bytes byte-exact)
3. `GroundGeo` — full ground geometry — **PASS** (all 6 meshes + rects + colliders bit-exact)
4. `BuildingsOracle` — transforms + counts (phase 2)
5. `LandmarksOracle` — tower/train/river params (phase 2)
6. `SignageOracle` — sign positions + texture hash (phase 2)
7. `PlayerOracle` — deterministic trajectory (phase 3)
8. `TrafficOracle` / `CrowdOracle` — positions (phase 3)
9. `WeatherOracle` — state + light arrays (phase 4)
10. `AudioOracle` — offline RMS/spectrogram of DSP output (phase 5)

## Phase 2 — buildings (in progress)
- **Studied**: all of src/systems/buildings/ (rng/geom/plan/architecture/materials/index/alley/shops/overlook/props/cables). Design notes compressed into session summaries; JS files remain source of truth.
- **Oracle dump**: `unity/tools/dump-buildings.mjs` runs the REAL JS pipeline headlessly (GLBs stubbed via tagged-geometry trick; instance matrices don't depend on GLB geometry) → `Temp/oracle/buildings.json` (lots w/ hex-bit doubles, bucket stats, lights, props matrices, colliders). Numbers: 2049 lots, 152 buckets, 92/219 lights, 6683 AC + 248 tank + 250 ptank instances, 2605 colliders.
- **C# ported**: `Assets/Game/Buildings/BuildPlan.cs` (BuildingsRng w/ rng.js helpers, BLot/BFront/BStreet, full plan.js flow: heightCap/addLot/carveBlock/bsp/segments/avenue+alley+back regions/overlook/cover).
- **Oracle**: `Assets/Editor/Oracles/BuildingsOracle.cs` — **PASS, 2049/2049 lots bit-exact** (fields incl. seed/roofKind/setback/cover/fronts).
- **CRITICAL LESSONS (Mono float traps)**:
  1. Mono JIT can contract `a + b*c` into FMA (single rounding) — JS never fuses → 1-ulp diffs. Guard: two-step `double m = b*c; return a + m;` in hot rng paths (Range).
  2. **Mono's double PARSING (`double.Parse`/STJ GetDouble) is not correctly rounded** for some edge-case literals — node parsed the same literal fine. NEVER compare floats across node↔C# via parsed decimal strings: dump/compare **IEEE-754 hex bits** (`Float64Array` → BigUint64 hex in JS; `BitConverter.DoubleToInt64Bits` in C#).
  3. Mono's `{v:R}` formatting also prints non-shortest/non-round-trip digits — misleading diagnostics.
- Next: Bucket.cs (geom.js) + Architecture.cs (architecture.js) → bucket-stat oracle (vert/tri/bbox per bucket); then Shops/Props/Alley/Overlook/Cables; then Building/Detail/Glass shaders + facade atlas/interior atlas/shop atlas (compose 4096 atlas in C# editor tool) + noise(512) + GLBs (ac_unit/water_tank/panel_tank); scene build + lights (greedy 11m filter, skip minor).

## Phase 2 — architecture stage VERIFIED (oracle PASS)
- **Ported**: `Assets/Game/Buildings/BuildingGeom.cs` (BBucket/BChunkedBuckets/BGeom: quad/vrect/box/beam/cyl/geom + exact three.js replicas — BoxGeometry, CylinderGeometry(1,1,1,seg,1,false) incl. the repeated-cap-center quirk, setFromUnitVectors w/ 1e-8 opposite branch, Matrix4.compose, Matrix3 invert+transpose normal matrix) and `Assets/Game/Buildings/Architecture.cs` (volumes/setback/overhang/facade vrects/roof/parapet/coping/crowns/LED/aviation/slab bands/pilasters; Styles table; TrimAttr).
- **Oracle**: dump-buildings.mjs `--arch` mode (skips shops/props/alley/overlook/cables) → `Temp/oracle/buildings-arch.json`; BuildingsOracle compares lots + volumes (bit) + 118 buckets: verts/tris/idxsum exact, bbox/pos/nrm/uv/attr sums via hex bits with 1e-12 rel tol, plus per-bucket SORTED-Y arrays compared bit-exact (this found the bug below). **PASS: 2049 lots, 118 buckets identical.**
- **Faithful JS quirks kept**: cyl() ignores r1 (scale [r,len,r]); box() uses world-ish UVs with ox=cx+cz*0.37, oz=cz+cx*0.21; JS `front.baseY >= 0` is FALSE for undefined → BFront.BaseY is double? (null = JS undefined) — the overlook lot's pushed front has no baseY, so its shopfront vrect starts at y0=0, NOT groundH.
- V8 3-arg Math.hypot has NO simple bit-exact C# formula (tested naive/maxscale/sequential-rescale/fdlibm-pair) — using max-scaled (within 1 ulp); oracle tolerance absorbs it. 2-arg = fdlibm (GroundGeo.Hypot2) ✓.
- Debug tooling that worked: `probe-arch.mjs` (per-lot JS primitive logs), C# side via ev.sh writing `Temp/cs_bucket_ys.txt`, diff in node with DataView hex→float.
- Next: Shops/Props/Alley/Overlook/Cables ports (same oracle pattern, full dump), then Building/Detail/Glass shaders + atlases + GLBs, D.BuildBuildings() scene integration, lights (greedy 11m filter).

## Phase 2 — shops stage VERIFIED (oracle PASS: 2049 lots, 131 buckets)
- **Ported**: `Assets/Game/Buildings/Shops.cs` (ShopBuilder: slotUV/4096 atlas slots, weighted type selection per district, hero-ramen + anti-closed-run loop, pillars/fascia/downlights, interiorShell w/ tileWall, glassFront, noren, awning, all 8 shop types). Oracle compares shopType per front + all buckets.
- **BUGS FOUND & FIXED (critical patterns)**:
  1. `lot.wallLayer ?? 1` ≠ `wallLayer == 0 ? 1` — 0 is a valid layer (tile style).
  2. **BuildingsRng was a STRUCT → `var rng = c.Rng` in type methods copied it and forked the RNG stream** (duplicated draws, wrong shop geometry). FIXED: BuildingsRng is now a CLASS (reference semantics, matches the single JS rng object). Sync `c.Rng = rng` before/after type-method section kept as belt-and-braces.
  3. Oracle `ys` bit-compare now tolerates ±1 ulp (V8 3-arg Math.hypot gap in tilted beams). Sums still 1e-12 rel tol.
- Next: props.js port (541 lines) → oracle with full dump incl. instanced GLB matrices; then alley/overlook/cables; then shaders/atlas/GLBs; scene integration.

## Phase 2 — props stage VERIFIED (oracle PASS: 2049 lots, 152 buckets identical)
- **Ported**: `Assets/Game/Buildings/Props.cs` (buildProps: balconies+clutter, hung AC units + instanced matrices, party-wall stacks, drainpipes, ground-floor shutters/doors/posters, roof AC/tanks/stair houses/antennas/dishes/fences/billboards, vending machines, sidewalk clutter; instanced matrices in `BOut.AcM/TankM/PtankM`; 2505 colliders, 92/217 lights).
- **Oracle**: `--props` dump (includes shops+arch) — verts/tris/idxsum exact, pos/nrm/uv/attr/ys sums + instanced matrices (±1 f32 ulp, 1e-9 abs floor) + lights/colliders hex-bit. **PASS after the bug below.**
- **BUG FOUND (critical, affects ALL rotated-geometry normals)**: `BGeom.Matrix3Invert` used the standard ROW-major cofactor formula, but three.js matrices are COLUMN-major — result was `(Mᵀ)⁻¹` instead of `M⁻¹`, i.e. **the normal matrix was the inverse rotation** (normals mirrored across the beam axis). Also the det formula differed (`n11*t11 + n12*t12 + n13*t13` vs three's `n11*t11 + n21*t12 + n31*t13` — different rounding). Fixed by porting three's `Matrix3.invert` element-for-element (n11..n33 naming, t11/t12/t13, exact te placement). Diagnostics that pointed here: JS-only normal multiset had `(0, ±1, ±2.22e-16)` (three's compose double-formula fingerprint) where C# had exact `(0, ±1, 0)`; tilted beam normals appeared sign-flipped between sides. A single-leg hex probe (JS vs C#: q, nm, transformed normals) made it decisive.
- **LESSON — three.js `normalize()` is multiply-by-reciprocal**: `Vector3.normalize`/`Quaternion.normalize` = `divideScalar(l||1)` → `multiplyScalar(1/l)` — `x*(1/l)` ≠ `x/l` (double rounding, 1-ulp diffs). Fixed `SetFromUnitVectors` + `Geom` normal path to `inv = 1/l; v *= inv`. (JS's standalone `norm()` helper in geom.js DOES divide by hypot — mirror per-callsite, not globally.)
- **Probes convention that worked**: JS single-primitive hex probe via node + THREE import; C# via `ev.sh` with `BitConverter.DoubleToInt64Bits(...).ToString("x16")`. Compare q, nm, and one transformed normal — pinpoints which stage diverges.
- All RNG instrumentation removed (C# DrawLog/CylLog/BillboardLog statics, JS __RNGDRAW hook + SEC markers); oracle re-run PASS on cleaned code.
- Next: Alley (`alley.js`, rng 0xA11E), Overlook (`overlook.js`), Cables (`cables.js`, rng 0xCAB1E) → full-buildings oracle; then shaders/atlas/GLBs, `D.BuildBuildings()`, lights.

## Phase 2 — alley stage VERIFIED (oracle PASS: 2049 lots, 152 buckets identical, first honest run)
- **Ported**: `Assets/Game/Buildings/Alley.cs` (buildAlley, rng 0xA11E): pave quads, two stair flights (JS `Math.round` → `Math.Floor(x+0.5)`), ramp colliders with full quaternion `rot`, landing, upper platform (box with `skip: py`), handrails (top rail + posts + lower rail on the two wall sides), potted plants (cyl pots + 3 icosahedron leaves per pot), lit doors + 1 light, clutter loop (beer crates / wheeled bins / buckets / bikes / gas cylinders).
- **Dump/oracle plumbing**: `--alley` flag → `buildings-alley.json`; the JS SHOP-LOOP GATE also had to learn WITH_ALLEY (first dump run silently skipped shops: buckets 140, lights 1/2 — the gate lives above the stage flags). Colliders now serialize a full quaternion `[... , rotY, rot.x, rot.y, rot.z, rot.w]` (0s when absent); C# `BCollider` gained `double[] Rot`; oracle compares indices 7–10 with a length guard.
- **RNG draw-order trap**: JS evaluates `compose(vec3(...), quaternion(...), vec3(...))` args left-to-right — pos draws, THEN euler draws, THEN scale draws. C# must evaluate in the same order (hoist pos into locals before `SetFromEuler`).
- `Math.Sin/Atan2` of the ramp angle matched V8 bit-exactly here (no libm tolerance needed for these two colliders).
- Next: Overlook (`overlook.js`), Cables (`cables.js`, rng 0xCAB1E) → full `buildings.json` oracle; then shaders/atlas/GLBs, `D.BuildBuildings()`, lights.

## Phase 2 — overlook stage VERIFIED (oracle PASS first run: 2049 lots, 152 buckets, lights 92/219, colliders 2514)
- **Ported**: `Assets/Game/Buildings/Overlook.cs` (deck slab + wet deck quad with aWin/aWin2/aWin3, steel brackets, chunky railing w/ rails+posts+balusters, planters, bench, lit door housing + 1 light; deterministic — no RNG).
- Traps handled: overlook lot found via `OverlookDeckY.HasValue` (only that lot gets it); `lot.wallLayer ?? 1` → `lot.WallLayer` directly (always set by architecture; 0 is valid); 2-arg `Math.hypot` → `GroundGeo.Hypot2`; `Math.ceil` → `Math.Ceiling`; JS `k/n` int div → `(double)k/n`.
- `dump-buildings.mjs --overlook` → `buildings-overlook.json` (runs alley too); oracle `withOverlook` implies alley+props+shops; dump preference overlook→alley→props→shops→arch.
- Next: Cables (`cables.js`, rng 0xCAB1E) → full `buildings.json` dump (props+alley+overlook+cables, no flags); then facade/detail/glass shaders, atlas, 3 GLBs, `D.BuildBuildings()`, lights; then phase 2 (landmarks #14, signage #15, lighting spike #16).

## Phase 2 — cables stage VERIFIED (full-buildings oracle PASS: 2049 lots, 152 buckets, lights 92/219, colliders 2605)
- **Ported**: `Assets/Game/Buildings/Cables.cs` (poleLine: poles/crossarms/insulators/braces/transformers/telecom boxes/step bolts/address plates + per-pole collider; wire(): sagged segmented beams via midpoint detail bucket; drops() to facades w/ wall anchors; avenue east/west pole lines w/ avSkip; avenue spans + facade-facade wires; cross-street pole lines ×3; alley criss-cross wires + 14 wall bundles; rng 0xCAB1E).
- **BUG**: pole wires missed the JS `(k === 0 ? 0.8 : 1.0)` top-arm sag factor → C# y-sums ~950 low per avenue detail bucket. Symptom signature: only ys/possum.y fail, RNG draw stream still matches (sag draws are 1-per-wire regardless).
- **Trap — "stale assembly" was really a compile failure**: Cables.cs had 6 CS errors (`PoleTop.Arms` must be `double[][][]` — 3 arms × 3 insulator points); Unity kept the last good DLL and silently ignored RequestScriptCompilation/ImportAsset(ForceUpdate)/ForceReserializeAssets. Diagnose via `console.sh` (`compilationFailed:true`) BEFORE suspecting staleness; `ev.sh` eval also fails with "NeonKaido does not exist" when the game assembly didn't build. Unity editor restart was needed once (asset DB wedge); dumps in `Temp/oracle/` are wiped on restart — regenerate.
- **Oracle hardening**: `Compare` now takes a `Monitor.TryEnter` gate — concurrent eval retries were sharing the static `_sb` and inflating mismatch counts (+184 per duplicate run). Full-dump mode inference: `full = dump.EndsWith("buildings.json")` implies all stages.
- **All buildings stages now bit-exact** (arch, shops, props, alley, overlook, cables). Next: facade/detail/glass shaders (materials.js), atlas, 3 GLBs, `D.BuildBuildings()`, lights (greedy 11m filter); then phase 2 (landmarks #14, signage #15, lighting spike #16).

## Phase 2 — buildings in scene (placeholder materials) DONE
- **Textures imported** (`Assets/Game/Textures/Buildings/`): atlas_albedo/normal, interior_atlas (copied), `noise512.png` (generated with Python — exact hashI/imul emulation of materials.js createNoiseTexture; rows flipped for Unity v=0-bottom), `shop_atlas.png` (4096² gray-fill + bilinear slot composite of the 9 source PNGs — matches JS buildShopAtlas canvas op).
- **GLBs**: `Assets/Game/Models/Buildings/dump/*.bin` (node pre-dump: JSON chunk 20+jsonLen, **BIN data at 20+jsonLen+8** — missing the +8 gave out-of-bounds indices for both tanks); loaded by `BuildingsRuntime.LoadGlbMesh` (f32 pos + u16/u32 idx; verts mirrored (x,y,-z)).
- **`BuildingsRuntime.BuildAll()`** (game asm): fresh plan→arch→shops→props→alley→overlook→cables → 149 chunk meshes (152 − 3 empty), attrs mapped aWin/aCol→UV1, aWin2/aPbr→UV2, aWin3→UV3, others→UV5; winding swap 1↔2 + z-negate on pos/nrm/tan (GroundMeshes convention). Placeholder HDRP/Unlit mats per matKey. **Lights**: greedy 11m filter — must use LINQ `OrderByDescending` (stable, like JS sort); `List.Sort` reordered ties → 91 vs 92. **Instances**: 6683/248/250 GLB instances as child GOs with F·M·F mirror (negate col-2 z + translation z; sim column-major te[12..14] = translation).
- **D.BuildBuildings()** added to GameDebug. Verified: 149 chunks, 7181 instances, 92 lights, preflight HEALTHY, street-view screenshot taken (user reviews visuals).
- **Trap**: eval main-thread cap 5000ms — BuildBuildings (~6s) times out but completes; the CLI RETRIES the command → BuildAll must be idempotent (DestroyImmediate the old root, not Destroy — deferred destroy merged two runs). `Refresh(ForceUpdate)` re-imports assets but does NOT reset static mesh caches — no script change = no domain reload; statics kept broken GLB meshes until loader made stateless.
- **Next**: real shaders — facade (TSL→HLSL: windows+interior mapping), detail (vertex tint + shop atlas), glass; then signage/landmarks phase 2.

## Phase 2 — building shaders ported (Facade/Detail/Glass/AC) + scene live
- **Shaders** (`Assets/Shaders/`): `BuildingsCommon.hlsl` (exact h21/vnoise/fbm3/aaRect from materials.js; NkBPointLights = three.js physical falloff decay=2: `att = 1/max(d²,0.01)·sat(1−(d/range)⁴)²`, uniform arrays `_BLPos[128]/_BLColor[128]/_BLCount`); `Facade.shader` (full port: atlas 4×2 grad-sample anti-repeat, tint palette, window grid + AA rects, interior mapping 4×4 + curtains/blinds/frost/lace, mullions/transom, sills/slab/streaks/topWet, roof puddles, far-AA avgEm/statEm); `Detail.shader` (aCol/aPbr + shop atlas + grime/rust); `Glass.shader` (vnoise smear 0.12..0.3 opacity, transparent ZWrite Off); `AC.shader` (fbm3 grime).
- **Wiring** (GameDebug editor asm — game asm CANNOT use UnityEditor): `BuildingMat()` creates/persists `Assets/Settings/Materials/{Facade,Detail,Glass,AC}.mat` with textures + the Surf-convention uniforms; `TexB()` sets importer sRGB/wrap (noise = linear+Repeat — JS noise tex is RepeatWrapping!); `BuildingsRuntime.BuildAll(mats, acMat)` pushes `_BLPos/_BLColor/_BLCount` from the kept 92 lights.
- **Gotchas**: TEXTURE2D/SAMPLER macros need Common.hlsl included FIRST (decl order matters); `TEXTURE2D_PARAM(tex, samp)` + call-site `TEXTURE2D_ARGS(...)`; redefinition pitfalls (attr T vs tangentWS Tw); ErrorWatch.Clear() (not LogEntries) resets Health; eval timeouts are infra noise in the console — check `compilationFailed` ground truth.
- Scene state: 149 chunk meshes with real materials, 7181 instanced AC/tank GLBs, 92 point lights (+1 Materials array each), preflight HEALTHY. `Temp/shots/buildings-street.png` captured for user review (JS oracle: vite dev server street view).
- **Remaining phase-2**: visual shader tuning vs JS (user review), then landmarks (#14), signage (#15), lighting spike (#16).

## Fog fix: real fogNodeFn ported (user: "dark blue fog on the bottom")
- The JS scene fog is `atmo.js fogNodeFn` (TSL), NOT the flat approximation Surf.shader used: `optical = gd·fd·fx·patch + hazeDensity·rainBoost·fd` where `x = fogFalloff·(P.y−cam.y)` (height DIFFERENCE, slant integral kernel `fx = (1−e^−x)/x`), `gd = fogDensity·e^(−falloff·(cam.y−fogBase))·rainBoost`, `patch = noise(P.xz·0.0035 + cloudOffset·4).r·0.9+0.55`, fog COLOR = `hazeColor(dir with y·0.5) · (1 + e^(−P.y/35)·groundGlow)` (neon ground-glow inscatter!), capped at fogMax=0.985.
- Ported as `NkFogApply(col, P, camPos, patch, rainBoost, fogColor, mid, zenith, mag, org, blue, gHaze, skyB)` in AtmoCommon.hlsl — colors/rainBoost passed as params (shader-side uniforms have inconsistent declaration orders/CBUFFER float4 vs float3; globals in the include would collide). Replaced the old fog block in ALL 6 shaders (Facade/Detail/Glass/AC/Surf/Road) + added `_Noise` texture for the patch to Surf/Road/AC.
- Uniforms set via `GameDebug.ApplyFog()` on ALL materials (incl. already-existing .mat assets — apply on the load path too!). Values: density 0.0022, falloff 0.028, base 0, start 14, haze 0.0002, max 0.985, groundGlow 0.18, rain 1.
- LESSON (again): the asset DB served the pre-edit Surf.shader despite ForceImport+content-change — shader errors referenced the OLD line numbers; only an editor restart cleared it. Also `error 5 = GameDebug.Walk()` enters PLAY mode (edit-mode Input unreliable); `PlayMode.cs` bootstrap attaches `PlayerController` to the camera (retry loop), rebuilds buildings, Esc×2 exits play.

## HDRP Fog was the "opaque dark blue haze" (found by user toggle test)
- Phase-1 `BuildAtmosphere()` tuned HDRP's Fog volume component (meanFreePath 454.5, maximumHeight 35.7) as a stopgap approximation of the JS fog. Once the REAL shader fog (NkFogApply) was ported, HDRP height fog kept rendering UNDER the shader fog -> double fog -> completely opaque dark blue lower half of the view.
- USER FOUND IT by toggling the Fog checkbox in the Inspector (AtmoVolume -> Fog): the haze vanished. Note: `VolumeComponent.active` readback can report False while the component still renders (parameter-level m_OverrideState flags drive it).
- FIX: BuildAtmosphere now sets Fog `m_Enabled=false` (and the asset was force-saved with active=false). Shader fog is authoritative.

## HDRP material array properties cap at first-creation size
- `Property (_BLColor) exceeds previous array size (196 vs 92). Cap to previous size.` — Unity material vector-array properties are capped at the size they were FIRST created at, for the life of the editor session. Growing 92->196->384 requires an editor RESTART.
- After the restart the arrays exist at full size but must be re-pushed (timeout can kill the push at the end of BuildBuildings). Explicit re-push: scan /Buildings + /StreetLamps lights -> BuildingsRuntime.PushLightArrays. Verified count=196.

## Street lamps / utility poles (signage/street.js lamp part)
- `Assets/Game/Signage/StreetLamps.cs` + `Assets/Shaders/Lamp.shader` (vertex-color albedo, _LensIntensity emissive mode) + `tools/dump-signage.mjs` (dumps lamp/pole body+lens from assets/models/signage GLBs, matrixWorld baked).
- Avenue lamps every 26m staggered 13m (warm 1,0.62,0.3 / cool 0.85,0.92,1 every 5th), cross-street lamps (all warm), alley utility poles every 12m alternating sides with lights at 4.5m following the 7m stair rise.
- Lights are REAL Light components (intensity 55/range 22 street, 14/13 alley) under /StreetLamps; PlayerController.EnsureLightArrays scans /Buildings + /StreetLamps; shader arrays raised to [384].
- JS yaw -> Unity rotation is -yaw (mirror F·M·F); light offset rotated by the same quaternion.

## "Flat dark purple road" root cause: uninitialized `screenUV` varying (NaN)
- The Road/Surf vertex shaders use `o.pos` naming, so the automated `OUT.screenUV` wiring regex missed them: `screenUV : TEXCOORD9` was DECLARED but never ASSIGNED. Uninitialized varyings -> undefined/NaN -> any expression reading it (NkPost's vignette `d = length((screenUV-0.5)...`) collapses the color to a constant haze (8,11,32).
- Diagnosed by shader bisection: albedo*3 / col*2 / NkAgxTone(literal) all rendered correctly (proving geometry+albedo+tone fine), but any NkPost call was a constant. The `screenUV` dump test (`half4(IN.screenUV,0,1)`) also showed constant haze -> varying itself broken.
- KEY warning: `Output value 'vert' is not completely initialized` (ShaderUtil.GetShaderMessages) — ALWAYS check this after adding varyings programmatically.
- FIX: `o.screenUV = o.pos.xy / o.pos.w * 0.5 + 0.5;` in Road + Surf verts (Facade/Detail/Glass/AC/Lamp were already wired).
- Also: Road.mat had NO ground textures (older .mat asset predated the texture-binding code; load path early-returned) and fogD=0 — fixed by always re-applying fog+textures (BindRoadTex/BindSurfTex + EnsureGroundMats called from BuildBuildings).
- Bisection technique that worked: replace the frag's final return with intermediate values (albedo*3, col*2, tone-only, constant-input NkPost, varying dump) and screenshot each.

## Signage: billboards + video screens + holograms (signage/billboards.js)
- `Assets/Game/Signage/FacadeAPI.cs` — facade records from csLots; **MUST call `FacadeAPI.CacheFromLots(csLots)` AFTER architecture** (needs lot.Volumes; BuildPlan.Run() alone gives lots with null Volumes -> NRE). Wired into BuildingsRuntime.BuildAll right after the BuildLotArchitecture loop. Collect() returns the cache.
- `Assets/Game/Signage/Billboards.cs` — ALL placement math in SIM space, mirrored to Unity (x,y,-z) at mesh build. Mirai billboard 13x19.5 (mount search over facades: normal·toCam>=0.75, d 30..90, w>=14, h>=24, ang 0.15..0.7 closest to 0.42 — currently falls back to floating position, mounted=False; verify against JS later). Housing/legs/bracing = oriented boxes/bars in one mesh, submesh 0 = screen (Billboard.shader), submesh 1 = steel (LampBody). 4 pink/violet lights 160/24 + KAZE cyan 60/20.
- Shaders: `Billboard.shader` (ken-burns/glitch CA/petals/sweep/roll/rows, tex linear col*col, x2.6), `VideoScreen.shader` (2x2 ads grid cycle 6s + wipe + zoom + scanlines + bar; sd=(seed,inten) in UV2), `Holo.shader` (additive One One, flicker/jitter/scanlines/edge/fresnel x1.6, tint+cell via MaterialPropertyBlock), `NeonQuad.shader` (vertex-tint x tex x _Intensity; used for KAZE TECH letters via PIL-generated kaze_tech.png 1536x256).
- Textures: billboard_mirai.png + ads_grid.png imported sRGB=FALSE (JS loads as data, squares in-shader), Clamp wrap. kaze_tech.png sRGB=true.
- Meshes under /Signage; lights as real Light comps; EnsureLightArrays + re-push scans /Buildings + /StreetLamps + /Signage.
- `_SimTime` is now a GLOBAL (per-material overrides removed from ApplyFog); PlayerController.Update sets Shader.SetGlobalFloat each frame (float cast — CS1503 double->float). Animations live.
- **5s eval timeout aborts long builds mid-way (the tail never runs)**: extract slow tail steps into separate idempotent evals (GameDebug.BuildSignage() public). BuildBuildings itself completes via CLI retries but the FINAL steps are not guaranteed.
- Stale-assembly gotcha: exception stack traces with identical PipelineEval hashes = old assembly still loaded; restart is the only cure (5th time).

## Facade neon signs LIVE (signage/signs.js + populate.js + library.js + atlas.js)
- Atlas via node @napi-rs/canvas (tools/render-atlas.mjs) -> neon_atlas.png + color_atlas.png (4096) + sign_lib.txt (194 rects, flat format).
- FacadeSigns.cs: SignBuilder (Face/Strip/Hbox/Bar/Blade/Flat/TenantStack/Rooftop) + Populate (fascia 85%, blades 45%, tenants 16%, rooftop letters 18%·dense+.04, posters 60% alley/cross) + light budget (top 188 by inten*imp as real Lights under /FacadeSigns).
- NeonSign.shader (modes 0-5 +6 open: tube/lightbox/strip/poster/LED/screen; NkFlicker port), SignHousing.shader (col+mr+grime+fresnel env).
- TSL hash: seed.toUint() TRUNCATES -> (uint)seed in C#, (uint)floor(seed) in HLSL (NOT asuint).
- Scene: 190 children, street bright 28% (was 2%), pink 8.4% + cyan 2.4% pixels. User in walk mode.
- Gotchas: static-member access (SignLib.X not L.X), Flat param is intenO not inten, C# shift needs (int) cast on uint count (C#9), Commit uv1 must be Vector4 (housing mr as Vector4), facade cache must be filled AFTER architecture (CacheFromLots in BuildAll).

## Traffic signals + holo ring + lighting spike (R1)
- `TrafficSignals.cs` + `Signal.shader`: lens mesh w/ per-vertex (phase,lamp) UV2; state via globals _SigA/_SigC/_SigPA/_SigPC (set per frame from PlayerController.Update -> TrafficSignals.Update(t)); CYCLE 22s (car 0-8 G, 8-10.5 Y, else R; ped 0-6 walk, 6-8 blink, else stop; phase 1 = +11s); signal lights (10/13) change color per frame. Bodies = transformed GLB verts into SignHousing mesh. pedR/pedG icons from color atlas via bbox-mapped UVs (SignLib.Misc["pedR"]/["pedG"]).
- `HoloRing.shader` + TrafficSignals.BuildRing: 96-seg open cylinder r=6 h=1.4 at (0,21,cz0) Unity, scrolling misc_holoRing atlas rect, cyan x3, additive.
- **Lighting spike conclusion (R1)**: 412 real Light components; ALL city lighting is shader-array based (custom shaders ignore HDRP lights). A/B: enabled 186fps vs disabled 201fps (~8% free win), render pixel-identical. PlayerController.Start() now disables all real lights (data holders only).
- Gotchas: System.BitConverter needed in game asm; FacadeSigns flicker sync is an INLINE loop in PlayerController (no SyncFlicker method).

## Floating dark cubes over the street — FIXED
- `CtlCube`/`CtlCube2` in Main.unity: phase-1 debug test objects (DefaultHDMaterial — renders dark because all city lighting is shader-based, HDRP lights don't light it) at (3,2.5,14) and (1.5,1.6,40), hovering over the road in the walk area. Deleted in edit mode + SaveOpenScenes. Not created by any code.
- Mirai billboard "floating": JS mount search ALSO fails (verified via node run of the real facade data, 2170 facades) -> floating fallback is JS-faithful; left as-is.
- Diagnostic technique: sweep ALL MeshRenderers for vertices in a world-space band (e.g. y 1.2-2.8, |x|<9) to find stray geometry near the player.

## "Inverted bowl" in scene view — FIXED
- Was `atmo.sky` — phase-1 directional "dim sky light" at the origin; its range gizmo sphere intersected the ground plane (upper half = "inverted bowl", z-clipping vs the grid = "glitchy"). USELESS since all city lighting is shader-based (lighting spike) -> deleted from scene (saved) + BuildAtmosphere creation block removed.
- Gizmo-diagnosis tip: small weird editor-only shapes = Light gizmo spheres; find via FindObjectsOfType<Light>(true) with y filter.

## "Inverted bowl with a camera inside" — 3 duplicate INACTIVE Skydomes
- BuildAtmosphere used GameObject.Find("Skydome") (misses INACTIVE objects) -> re-runs accumulated dead domes. The visible sky is the HDRP volume PhysicallyBasedSky (all domes inactive).
- User saw: selected dome wireframe ("glitchy bowl") + the game-camera gizmo inside its volume ("camera inside the bowl").
- FIX: delete all 3 (saved); BuildAtmosphere now destroys ALL via FindObjectsOfType<Transform>(true).
- Scene-view cleanup done: CtlCubes + atmo.sky light + dead Skydomes all removed.

## Scene-view cleanup round 2 + UV fix
- atmo.sky deleted (phase-1 directional "sky" light; gizmo range sphere at ground = "inverted bowl", z-clip = "glitchy"; useless post-spike).
- 3 duplicate INACTIVE Skydome objects deleted (BuildAtmosphere used GameObject.Find which misses inactive -> accumulated dead domes; visible sky = HDRP volume PBR sky). BuildAtmosphere now destroys ALL via FindObjectsOfType<Transform>(true). NOTE: SaveOpenScenes fails during play mode.
- "Mesh.uv is out of bounds" x2: Billboards MiraiScreen mesh — screen quad had UVs, housing/steel boxes didn't (UV count != vertex count). FIXED on disk (Hbox/Bar add Vector2.zero UVs); pending editor restart to compile (chronic stale-assembly; harmless log spam only).
- Scene-view diagnosis tips: small editor-only shapes = light gizmos (sweep FindObjectsOfType<Light>(true) with y filter); selected-object wireframe + camera gizmo inside = dead hierarchy objects; HandleUtility.PickGameObject unreliable here; FindObjectsOfType with size/position filters finds stray meshes (but NOT DrawMeshInstanced - those have no renderers).

## Lanterns ported (#25 DONE)
- Files: `Assets/Game/Signage/Lanterns.cs` (data from featured.js lantern parts + buildLanterns), shaders `LanternPaper/LanternCaps/LanternPlain.shader`, dump tool `unity/tools/dump-lanterns.mjs` (pos/idx/uv bins for chochin/chochint/andon paper+caps/frame).
- Counts: 74 chōchin (ramen row 6, alley 2-rows, izakaya row 5, alley strings 40) + 1 tall やきとり + 29 andon floor lanterns on the promenade; light-pool contributions via FacadeSigns.AddPoolLight.
- Lantern cells: render-atlas.mjs patched (entries expose .uv not .rect); 14 `lantern|` lines appended to sign_lib.txt (order = JS Set order ら,ー,め,ん,居,酒,屋,祭,焼鳥,赤提灯,やきとり,提灯,御食事,串); SignLib.Lantern + LanternOf().
- Shaders: sway via TEXCOORD1 pivot / TEXCOORD2 cell / TEXCOORD3 lp(seed,glow,swayAmp,kind); angles NEGATED for sim->unity mirror; ink from color atlas .r; kind 0 chōchin / 1 tall / 2 andon washi. GOTCHA: do NOT redeclare `_CloudOffset` (AtmoCommon.hlsl has it, float2).
- PlayerController.EnsureLightArrays: added /FacadeSigns root (188 lights) AND capped push to 384 (shader _BLPos[384] overflows silently break lighting). Sort by (r+g+b)*inten*range, drop tail.
- FacadeSigns.Build now calls Lanterns.Build(root) between Populate and the light budget.
- GOTCHA: `_BLCount` is a SetFloat on materials that declare it — LanternPaper/Caps/Plain don't, so GetFloat("_BLCount")=0 on them is EXPECTED, not a push failure. Verify with /FacadeSigns/SignFaces instead.
- Verified: alley strings 16.3% lit pixels, ramen row face-on 5.6%, andon bright bar (post-grade pushes hot washi to magenta r196 g134 b205 — same as JS).
- PoseCam: unity yaw = 180 - simYaw; pitch>0 looks DOWN (negative pitch = up at sky).

## CRITICAL: OneDrive scene revert
- `Assets/Main.unity` on disk reverted to an Oct 14 06:47 copy (368KB, pre-build) — CtlCubes, dead Skydomes, atmo.sky all came back after an editor restart + BuildBuildings. Cause unclear (OneDrive sync or force-kill). Re-cleaned (deleted 6, SaveScene ok).
- NOTE: saving the scene AFTER BuildBuildings serializes the whole city -> 440MB Main.unity. The established workflow is per-session rebuild, so keep saves minimal.

## Koi ported (#26 DONE)
- Geometry dumped from three.js exactly (lathe body 24 seg + tail fan + pectoral fins, merged, non-indexed 4050 verts) via `unity/tools/dump-koi.mjs` -> koi_pos/nor/idx bins (head at +Z sim; loader mirrors z + flips winding -> head at -Z unity).
- `Assets/Game/Signage/Koi.cs`: 2 specs — orange/white at sim (0,19,-38) r=9 h=2.2 sp=0.22; cyan/pink at (1,23,-90) r=12 h=3 sp=-0.16 (phase 3). Pool lights 25/18 imp 1.5 at swim centres. Per-frame path update from PlayerController.Update (elliptical orbit + bob, heading = path derivative; LookRotation(-Mir(d)) because head is -Z).
- `Assets/Shaders/Koi.shader`: additive, Cull Off, ZWrite Off; tail sway (smoothstep(-3.15,3.5,z)^1.6, x-shift sin(-z*0.9 - t*3.2)*0.55); fresnel^1.5*2.6; dappled patch via NkTslHash(floor(z*1.3)+floor((x+2)*1.6)*13+7); scanlines sin(z*22-t*9)^3; flicker step(0.05, hash(floor(t*11)+23)); fog + NkPost.
- GOTCHA: static _go refs die on play-mode scene reload -> Koi.Update self-heals via GameObject.Find. GOTCHA: never declare float3 _CloudOffset / reuse name 'patch' in frag with fog blocks.
- Verified: koi_0 swimming at (4.9,19,38) in play mode; bright saturated cluster in capture.

## Landmarks 1/9: shared materials + util (#33 DONE)
- Textures: 17 files copied assets/textures/landmarks -> Assets/Game/Textures/Landmarks; _n/_r imported linear (sRGB off, 8 set via TextureImporter).
- `Assets/Game/Landmarks/LmUtil.cs`: LmGeo (pos/nor/uv soup; ApplyMatrix, ComputeVertexNormals area-weighted, FlipWinding, ToUnityMesh with sim->unity mirror + winding flip), UnitBox (three.js BoxGeometry port, 24 verts), LmBatch (per-key parts, TRS/EulToQuat XYZ order, chunked Build), LmUtil.ProfileGeometry ([z,y] polygon extruded along X via Earcut, caps+walls, box-projected UVs), BeamBetween, GroundY (GroundPlan.HeightAt).
- `Assets/Game/Landmarks/LmMats.cs`: AssetDatabase texture loading (color sRGB, _n/_r linear), Surface/Metal/Emissive/BasicEmissive factories matching materials.js opts.
- `Assets/Shaders/LandmarkSurface.shader` (wet PBR + detail resample + macro + streaks), `LandmarkMetal.shader` (procedural grime), `LandmarkEmissive.shader` (_Unlit switch). All with Surf-style lighting/fog/NkPost tail; use local value-noise NkMxNoise approximation of mx_noise_float (2D projection of 3D input — visual approx, noted).
- GOTCHA x2: field + method with same name = CS0102 (Box/BoxGeo); mystery CS1503 at float.IsNaN(double) inside LmUtil fixed by removing the IsNaN ternary — identical construct compiles elsewhere; cause unresolved (Bee cache was also poisoned that session; Library/Bee was nuked + 2 editor restarts).
- Smoke test PASS: 3 shaders ok, profile 36v with UVs, batch 1 part, 3 material factories, GroundY(1.5,-30)=0.00.

## Landmarks 2/9: river + embankments (#34 DONE)
- `Assets/Game/Landmarks/River.cs` + additions to LmUtil (GenCylinder/GenCone/GenSphere/GenTorus, worldUvKeys on Batch.Build). `Assets/Shaders/RiverWater.shader` (flow-noise normals, ripple rings, streaked env reflection — DEVIATION: env-sampled streaks instead of JS planar reflector; deep murky tint, bank darkening, rough-0.07 point-light specular). LandmarkSurface got _WlLevel/_WlAlgae/_WlBelow (waterline algae + stain for embankment walls); LandmarkEmissive got _Grid (lantern-head grid dim 9->2.5).
- Build: 17 meshes 76838 tris — water quad (uv in meters), walls+coping 30m segments x2 banks, drains 12m (skip bridge +-5m), mooring rings 18m, railing posts 2.4m + rails/kerb, 203 stone lanterns (west 4m / east 8m, skip bridge +-3m) with point lights (0xffb36b-ish, 26/30, range 13) under /LandmarksRiver/riverLights; EnsureLightArrays now scans /LandmarksRiver.
- GOTCHAS (cost ~1h): (1) GameDebug created the root as new GameObject("/LandmarksRiver") — literal slash in the NAME breaks GameObject.Find (path vs name) AND EnsureLightArrays scan; fix: name "LandmarksRiver", Find("/LandmarksRiver"). (2) Building while isPlaying=true puts objects in the play scene -> lost on exit; ALWAYS verify isPlaying=false before landmark builds. (3) ev.sh must run from unity/ dir (pipeline discovery is cwd-sensitive) — ev.sh now cds itself. (4) -1e9 float literals in ShaderLab parse as TVAL_ID — write -100000000.
- Also: pipeline server flapped ("no reachable instances", import worker 10054 crashes) after full Library purge — transient; ev.sh works, shot.sh may need retry.
- Verified: 17 meshes in play, lantern heads glow (208,140,203 post-grade pink), water renders dark blue with env reflections; 0.9% glow pixels in the wide riverside view.

## Landmarks 3/9: elevated rail structure (#35 DONE)
- `Assets/Game/Landmarks/Rail.cs` (static parts of rail.js; trains = #36): deck box-girder via ProfileGeometry (8-pt profile, 40m chunks), parapets/caps/fascia/cable troughs, track slabs 5m + continuous rails, 25 pillars every 32m (skip |x|<16 and river zone; explicit -12.5/12.5/river trio) with pier caps, bearings, footings (in-river slabs at waterLevel-1.2), drain pipes; underside ledCool fixtures 10m + ledAccent fascia strips 40m; 24 point lights (0xbfd6ff, 70, 20) x -150..270 step 18; catenary: 16 masts/50m with H-sections, cantilever arms (stagger +-0.2 alternating), insulators, truss crossbeams+diagonals, messenger (sag .55)/contact/droppers/feeders (sag .8) via BeamBetween.
- Constants: Z=-140 H=14 width=9 x -400..400; tracks z=Z-2 (eastbound)/Z+2 (westbound); railTop=H+0.38 (for trains).
- Materials: concrete/concreteDark/slab Surface; rail/darkMetal/steel/wire/insul Metal; ledCool/ledAccent Emissive. worldUV concrete/concreteDark/slab, chunk 80. Root "LandmarksRail" (Find-safe name), EnsureLightArrays scans it. BuildLandmarks now river+rail.
- GOTCHA: RequestScriptCompilation alone sometimes doesn't recompile the editor asm — pair with AssetDatabase.Refresh(ForceUpdate) + verify ScriptAssemblies dll mtime. Play-exit is flaky right after a domain reload — retry until Application.isPlaying confirms EDIT.
- Verified in play: rail root + 91 meshes, cool LED px 0.22% + warm accent px 0.16% in a street view of the viaduct.

## Landmarks 4/9: moving trains (#36 DONE)
- `Assets/Game/Landmarks/Trains.cs` + `Assets/Shaders/TrainCar.shader`. 2 six-car trains (CarLen 20, Speed 21 m/s) on tracks z=Z-2 (east)/Z+2 (west); green/orange stripes; car body = 15-pt profile extruded -9.75..9.75 + cab nose deform; procedural body shader (stainless/corrugation/stripe bands/window band with per-cell passenger silhouettes via NkTslHash, doors at +-2.35/+-7, dark glass, cab windshield + head/tail lamps (white*60 / red*14) + destination sign). Undercarriage + pantograph meshes (c==1||c==4). 8 lights (6 interior spill + 2 headlights) under /LandmarksRail/trainLights. Movement: linear path _span=1000, _period=61.6, phases target heads at x=118/135 at t=2s; Update from PlayerController.
- **CRITICAL BUG + FIX**: play-entry DOMAIN RELOAD wipes statics (_span/_period/_start/_phase=0) -> local=((t+0)%0)=NaN -> position NaN error EVERY FRAME (the 999+ flood). FIX: Heal() recomputes all path constants + Update guards _period<=0/NaN.
- **CRITICAL LESSON — frozen play loop**: when the editor window loses focus with runInBackground off, PlayerController.Update STOPS (SimTime frozen, trains frozen, no errors!). Symptoms looked like paused/wedged. FIX: Application.runInBackground = true + PlayerSettings.runInBackground = true. If _SimTime is frozen during captures, set it FIRST.
- LogEntries.Clear via pipeline does NOT clear what console.sh reads — compare entry timestamps to distinguish stale vs fresh errors.
- Compile wedges: RequestScriptCompilation alone often no-ops; use Refresh(ForceUpdate) + RequestScriptCompilation + verify dll mtime > edit mtime. Bee uses CONTENT hashing (touch alone insufficient — but content changes trigger rebuild).
- Verified: period=61.6 in loaded asm; trains move 21m/s both directions; captures show lit train form filling frame; no NaN after fix.

## Landmarks 5/9: expressway (#37 DONE)
- `Assets/Game/Landmarks/Expressway.cs` + `Assets/Shaders/ExpresswayDeck.shader`. Z=-240 H=22 width=16 x -400..400; 4 lanes (laneW 3.5, left-hand traffic); deck box profile 40m chunks; asphalt with WORLD-SPACE lane markings (dashed at +-laneW via dash=step(0.5,frac(x/12)), solid edges, yellow median) — shader takes _DeckZ=240 (unity), _LaneW; jersey parapets (profile), sound barrier posts 4m + translucent-dark panels + top/bottom rails; ledWarm underside strips 40m; median barrier.
- 22 hammerhead piers every 40m (skip |x|<18 + river zone +-18; explicit -13/13/river trio) — colGeo cylinder(1,1,1,12) scaled (1.6,h,1.9), cap profile -7.2..7.2, in-river footings, bearings, ladder+pipe. 50 lamp posts 32m (k%3 LED vs sodium, emissive*30); 30 point lights (0xdde8ff 220 / 0xffa040 260, range 25, x -180..300) + 19 underside (0xffb070, 70, 18) under /LandmarksExpress/expressLights (scanned by EnsureLightArrays); under-deck sodium strips 12m; 2 sign gantries (-60, 95) with green JP sign panels (LandmarkEmissive unlit green).
- GOTCHA: HLSL has NO local functions — `float f = (float x) {...}` fails with "unexpected token"; use file-scope helpers.
- Enter Play Mode Options set: DisableDomainReload (statics survive play entry now; scene reload still on). Verified setting via EditorSettings.
- Verified in play: 152 meshes 11184 tris; deck band + lamp row lit in play capture (exp2.png).

## Landmarks 6/9: cherry trees + petals (#38 DONE)
- `Assets/Game/Landmarks/Cherry.cs` + shaders `CherryBark/CherryBlossom/CherryPetals.shader`; `tools/dump-cherry.mjs` (minimal GLB parser — JSON chunk + BIN chunk, node matrix transform, non-indexed bins with TEXCOORD_1) dumped 8 bins to `Assets/Game/Models/Landmarks/dump/` (4 variants x bark/blossom; blossom y 1..7, ~10-20k verts each).
- Placement port: mulberry32(4242); z from 36 down step 9+rng*3.5; skip |z-(-260)|<16 (bridge), rail -140 +-6, expressway -240 +-4; x = 129.6+rng*1.4; 46 trees (v 0..3, rot +-0.35, s 0.85..1.15). `Cherry.Spots` (sim x/z) exposed for props (#39).
- Bake: per-variant bins merged into 60m unity-z chunk meshes (trunk + blossom separately, 20 meshes); sim->unity: mirror z (pos+normal), rotY, scale, GroundY base. GOTCHA: GLB TEXCOORD_1 is VEC2 (ReadVec4 halved the array -> IndexOutOfBounds; read as vec2, promote).
- CherryBark: bark tex * (0.62,0.55,0.52) * NkMxNoise(pw*1.3) 0.6..1.1, moss (0.05,0.07,0.03) up*0.35*smoothstep(0.4,0.8,n), rough from _r, wet response, nmap 1.6 tangent-less. CherryBlossom: alphaTest 0.5 Cull Off, uv1.x tint mix(deep pink, near-white), uv1.y inner-canopy AO 0.18..1, edge-on fade smoothstep(0.08,0.38,facing), emissive lantern glow smoothstep(9,2.5,y)*riverSide(128..141) + city pink 0.12. CherryPetals (_Kind 0 falling/1 water/2 ground): 4x4 atlas via uv2.x (uv = uv0*0.25 + (fract(cell/4), 0.75-floor(cell/4)/4)); falling anim in vertex shader (9m loop, 14m wrap, seed spin) driven by uv1 (base xyz + seed); water bob + drift; 5000 falling (rng 99) + 9000 water (rng 5) + 14000 ground (rng 6, JS ground-system decals absent here so we add ours).
- Cast-iron grates: GenCylinder(0.75, 0.75, 0.025, 24) batched -> LandmarkMetal (0.025,0.022,0.02) rough .55 metal .2. Colliders skipped (todo).
- Verified: 46 trees, 20 chunk meshes, 28000 petals; pink blossom pixels in play captures (canopy dim at night — matches JS look).

## Landmarks 7/9: promenade props (#39 DONE)
- `Assets/Game/Landmarks/PromenadeProps.cs` + `Assets/Shaders/PromenadeShrub.shader`. mulberry32(3131); occupied = Cherry.Spots z's; free(z, 2.4) + bridge -260 +-20; loop z 32 -> -492 step 7: k%3==0&&r<0.85 bench(130.6, z); k%5==1 planter(128.8, z, 3.2) with 5 displaced shrubs (GenSphere(1,12,8) stand-in for JS icosahedron — deviation); k%7==2 two mamachari bikes (131.4, z / z-0.7, ry=pi/2+-0.2, tilt -0.12/+0.1); extra bikes at (139.1, -32/ -78). 49 meshes, 13152 tris, chunk 60.
- **KEY INSIGHT (fixed a wrong 'fix')**: LmBatch.Add/Box/AddMatrix take SIM-space coords + JS Euler (ry, rx, rz order) and `ToUnityMesh` mirrors centrally (z-negate + winding swap). Do NOT pre-mirror when feeding LmBatch. Bike matrix: Quaternion.Euler(tilt, ry, 0) matches JS Euler YXZ for rz=0. Materials: wood/concrete/stonewall surfaces (worldUV box-projected), iron/chrome/tire/frame/basket metals, shrub = custom noise shader (JS mx_noise 9 / 2.1 mix greens).
- **Expressway mirror scare**: LmBatch batches WERE correct (Build mirrors); lights placed manually needed -Z. All consistent now.
- Compile wedge again (dll frozen at 00:42 through 2 restarts): root cause this time = real compile error (CS1061 RecomputeNormals -> ComputeVertexNormals) + Bee dag inconsistency after force-kill; remedy: fix error, delete Library/Bee + Library/ScriptAssemblies, restart (~7min).
- Verified: props build + furniture visible along promenade in play captures (props1/props2.png).

## Landmarks 8/9: bridge (#40 DONE)
- `Assets/Game/Landmarks/Bridge.cs` + `Assets/Shaders/BridgeLedArch.shader` + `Assets/Shaders/BridgeGlass.shader`. Z=-260 deckY=8, river walls 140/210, abutments 126/224, hw=6, rise=13. Deck slab+paving, steel edge girders, ledCyan under-strips, railing posts 2m + top rail + glass panels (transparent 0.22 ZWrite Off), cross girders 6m, twin arch ribs (28 beamBetween segments each, steelPaint 0.7/1.0 + ledArch strip below), 13 hanger cables per rib, portal bracing (0.3/0.5/0.7), piers at river walls (footing to waterLevel-3) + abutments, 12 lamp posts (0xffd3a0, 90, 20) + 3 floodlights (0x9fe8ff, 120, 22) under /LandmarksBridge/bridgeLights (scanned), 2 stairs (28 steps, paving treads, concrete stringers, steel handrails, ledWarm strips).
- BridgeLedArch: ph = fract(wp.x*0.02 - _SimTime*0.15), emissive mix(cyan*4, white*9, smoothstep(0.9,1.0,ph)) — travelling pulse. BridgeGlass: Cull Off ZWrite Off blend, fresnel env reflection.
- Light mirror gotcha: JS l.position z = zz - s*0.4 (sim) -> unity = -(zz) + s*0.4; manual light placement must mirror z AND flip the s-term sign.
- Verified: 20 meshes 3448 tris, deck + arch visible in play captures.

## Landmarks 9/9: skyline megatowers (#41 DONE)
- `Assets/Game/Landmarks/Skyline.cs` + `Assets/Shaders/{SkylineTower,SkylineBeacon,SkylineHolo}.shader`. Port of skyline.js: 9 art-directed hero towers + far-bank cluster (70) + north/west mid-rings (130/185) + 140 outer-ring megatowers (bias -z), blocked() = district rect + river +-r + rail/expressway/bridge corridors + tower overlap (t.r + r + 6). **Verified EXACT parity: 303 towers, far-bank blocked=351/400 identical to a node re-run of the JS placement.**
- Unit tower geoms (tri soup, footprint 1x1 y 0..1) built in C#: box/stepped/stepped2 (AddBox = three.js BoxGeometry port with cap winding (A,D,B)/(D,C,B)), taper+cyl+oct (AddCylinder rTop/rBottom + caps, RotY PI/4 or PI/8), twist (18-seg box, twist a=y*1.1, s=1-0.25y, smooth normals). withEdge port: edge=0.5 for roofs else fract(u*mult) with per-tri seam fix; round/neonOK flags. All 7 types merged per-type into single meshes (no instancing) with per-vertex baked attrs: uv1=(edge,round,neonOK), uv2=(seed,hgt,style,neon), uv3=(neonRGB,crown), uv4=(cx,cz,radius,bright) — unity space.
- SkylineTower shader: window grid (cw/fh cells, fwidth AA), ribbon-office multi-bay room logic, floor coherence (dark/lit/mixed floors), blinds+ceiling strip+mullions, residential slab lines + TV-glow windows, dark-style neon fin pinstripes, podium shopfront band (u/7 segments), neon edge strips (edge attr, hgt 0.5-0.97 band), crown light band, base glass w/ noise + mullions, roof flat, farDim (250->1400m to 0.22), metal env. Beacon: blink smoothstep(0.55,0.6,fract(_SimTime*0.75+phase)) red, 0.6..30. Holo: atlas-rect uv, scanlines 380, flicker 14Hz, glitch band, additive.
- Spires (GenCylinder soup 0.25/1.2 x6, scale 1,sh,1 at tower top) via LmBatch; beacons as merged mesh w/ phase in uv1.x; 25 holo quads (7 fixed + 18 procedural on tall south faces + 3 wide ads2) grouped into 3 meshes by atlas (ads/ads2/ads3 — LmMats keys are "ads" NOT "holo_ads").
- **C# GOTCHA OF THE SESSION**: `readonly Mulberry32 _rng` field + Next() mutating struct = defensive copy per call -> EVERY Next() returned the same value -> all 400 cluster attempts hit one blocked point (12 towers). Fix: drop readonly on mutable-struct fields. (Locals are fine.)
- Also: HLSL float3(0.03) single-arg constructor is illegal; LmMats tex key "ads" not "holo_ads"; oct merge must ASSIGN the concatenated edge/round/neonOK arrays.
- Verified: 303 towers / 7 meshes 48k verts / 117 spires / 217 beacons / 25 holos; night skyline renders with purple glow + lit windows in play.

## Integration + verification tour (#42 DONE)
- Full-build state: all 15 roots live in play (Buildings/StreetLamps/Signage/FacadeSigns/Signals/HoloRing/Ground/Skydome/Landmarks{River,Rail,Express,Cherry,Props,Bridge,Skyline}); SimTime advancing; runInBackground on.
- **CRITICAL FIX — cherry GLBs are INDEXED**: dump-cherry.mjs originally ignored `indices`, dumping raw vertex lists -> every trunk/blossom mesh failed "triangle indices must be a multiple of 3" (121 console errors, trees were EMPTY). Fixed dumper to expand indices (Uint32/Uint16) into tri soup + PetalMesh quads now emit 6 soup verts (0,1,2,0,2,3) not 4. Blossom chunk now 115k verts/38.5k tris; pink canopy verified 1.97% in close-up (cherry_fixed.png). LESSON: always check `primitives[].indices` when porting GLBs; a failed set_triangles logs once per mesh but BuildLandmarks returns success strings regardless — grep console after every build.
- Verification tour (play, Temp/shots/): tour1_avenue (neon street canyon), tour2_panorama (skyline + city from above far bank), tour3_river (bank lanterns + water reflections), tour4_rail (viaduct, catenary, train), cherry_fixed (canopy masses).
- Console error sweep post-build: only stale 14:48Z entries; groundTruth consoleErrors=0.

## Parity follow-ups: sky/fog globals + mount check (#43 DONE — landmarks phase complete)
- **SKY/FOG GLOBALS FIX (major)**: Atmo constants (FogColor 0.085,0.058,0.16 / MidColor / ZenithColor 0.010,0.011,0.030 / Glow* / GroundHaze) existed in C# but were NEVER pushed to the shader globals — every NkCommon shader faded to BLACK fog and had zero ambient (train bodies read dark purple, zenith pure black). Fix: `Atmo.PushGlobals()` (new) sets _FogColor/_MidColor/_ZenithColor/_GlowM|O|B/_GroundHaze/_SkyBrightness=1/_Rain/_CloudOffset + ambient = HemiSky(0x4a5a9a)/HemiGround(0x1a1020) * HemiIntensity(0.12, lightning boost) + zero directional (JS has no moon light). Called every frame from PlayerController.Update after _SimTime. Verified: distant band now (60,30,60)/(30,30,90) purple-blue haze instead of black (fog_fixed.png).
- **MOUNT CHECK — parity confirmed, freestanding is CORRECT**: the todo expected mirai mounted=True, but re-running the exact JS mount filter (node, over the exported 2170-facade Unity cache + real overlook [42,26,-60]) yields ZERO candidates (none within 30-90m facing the overlook with w>=14/h>=24) — the JS game ALSO leaves the MIRAI billboard freestanding. Unity's port (Billboards.cs) is faithful; `mounted=False` matches. The earlier 'mounted=True' expectation was a misreading. Facade cache note: FacadeAPI._cache is static — wiped by domain reloads (script recompiles); ALWAYS run BuildBuildings before BuildSignage after any recompile, else '0 facades'.
- **Tooling**: GameDebug.RunAsync(which) — buildings/landmarks/atmosphere/ground/signage/all — runs slow builds via EditorApplication.update (main thread still blocks during the build; kick off then poll D.AsyncBusy/D.AsyncResult). Direct evals >5s time out.
- Editor crash pattern this session: 2 crashes after big synchronous builds — relaunch + rerun the sequence (buildings -> all -> Walk).

## New feature phase 1: traffic (#45 DONE)
- `tools/dump-vehicles.mjs`: vehicle GLBs (lod2 for parked ~22-39k soup verts, lod3 for moving ~10-20k) expanded to ONE merged tri soup per type with per-vertex part tags (uv1.x: 0=paint 1=glass 2=chrome/trim 3=headlamp 4=taillight), wheel verts (uv1.y=1) re-emitted at all 4 slots with axle center (axleY, axleZ sim) in uv2 (3-float stride — NOT vec4!) + manifest JSON (length/wheelRadius/axles). GLB walker handles node hierarchy/TRS/matrices, FLIP (rotY PI) like models.js, wheel-local reuse via invert(FR world).
- `Assets/Shaders/VehicleCar.shader`: one material/draw call per car — tag-based parts, vertex-shader wheel spin around baked axle (rotate (y,z) by _Spin), _PaintColor via MaterialPropertyBlock, _LightLv (headlamp/brake), wet-response paint.
- `Assets/Game/Vehicles/Vehicles.cs`: parked cars (mulberry32 90210, kerbs both sides + cross streets + hero at spawn), Traffic (mulberry32 4242: 2 avenue + 6 cross + 4 express lanes, IDM car-following a=2.2 b=3.5 T=1.2, SIGNAL_CYCLE=30 green-wave offs(z)=(-z/60)*3, stops for avenue+cross, player-obstacle braking), SkyTraffic (mulberry32 777: 5 elliptical loops + 4 straight skyways, 85 AVs). Meshes: shared per type (5 lod2 + 5 lod3), sim->unity mirror at bake (forward becomes +Z => unity yaw = atan2(dir.x, -dir.z) = -yawSim).
- **Integration**: PlayerController.Update -> VehicleTraffic.Update(dt, simT, playerPos); GameDebug.BuildVehicles() + RunAsync("vehicles") + "all" chain.
- **KEY LESSONS**: (1) statics are wiped by ANY script recompile even with DisableDomainReload — Update must rebuild deterministically when statics are null (same seeds => identical fleet); (2) play-mode scene reload kills runtime static GO references — TryLink() re-links car GOs to scene children in creation order; (3) compile requests are DEFERRED while playing — exit play first; (4) eval sandbox has no Transform/GameObject type inference across ternaries — use .transform explicitly.
- Verified: 133 parked + 166 traffic + 85 sky; cars move (avenue c0 +86m/8s ~= 11m/s); VehicleCar shader compiles clean; no console errors.

## New feature phase 2: weather (#44 DONE)
- `Assets/Game/Weather/WeatherDriver.cs`: full port of src/systems/weather (index/common/lightning/lights/heightfield + drips CPU). Static heightfield bake 1024^2 RFloat (span 1024 m, origin (-512,-692) unity): GroundPlan.HeightAt + expressway/rail/bridge decks + building LOT tops (BuildPlan.Lots, fallback Run() if fresh domain). CPU float[] mirror for drips. Lightning = JS strobe port (mulberry32 0x5eed1, multi-pulse envelope, auto-strike every 9-31 s when rain>0.3) -> ctx.weather.lightning (Atmo flash) + _WxLightning. Gust/wind/wetness easing per JS. 24-nearest light gather (2 s scan of light roots) -> _WxLPos/_WxLCol. Drips/streams (512+8x64) CPU: edge search on the height mirror, hang/detach/fall, streak quads written per frame.
- `Assets/Shaders/WxCommon.hlsl` + `WxRain/WxSplashes/WxSteam/WxDrips.shader`: rain (327680 analytic quads, 3 camera shells, wrap + heightfield respawn, streaks lit by 24 lights), splashes (32768 slots x 6 parts: crown + 5 ballistic droplets on the baked surface), steam (3552 quads, 76 emitters ported from steamEmitters, generated puff atlas), drips (CPU-built quads).
- Wiring: GameDebug.BuildWeather() + RunAsync "weather" + "all" chain; PlayerController.Update -> WeatherDriver.Update(dt, simT, pos).
- **KEY LESSONS**: (1) HDRP silently DROPS materials with renderQueue > ~4000 (was 9310) — keep transparent queues 3000-3020; (2) CLI captures don't refresh assets — AssetDatabase.Refresh via ev.sh before testing shader edits; (3) bake order: weather build must Run() BuildPlan if Lots empty (fresh domain); (4) strike envelope is a ~0.5 s strobe — poll fast or force Strike() when verifying.
- Verified: rain visible (mean 63->111, streaks 324 vs 133 baseline), 1024/1024 drips live, lightning pulses fire, steam/splash shaders compile + queue-correct.

## New feature phase 3: crowds (#46 DONE)
- Asset pipeline: `unity/tools/glb2fbx.py` (Blender 5.2 headless, io_scene_gltf2 ext, NLA-stash all 9 actions before FBX export; argv `-- glb out.fbx texdir`). Assets/Game/Models/Crowd/dump/{male,female}.fbx + tex/ (39 crowd textures) + meta JSONs. Unity importer: animationType=Legacy, isReadable=true. Clips are sub-assets `Armature_<sex>|<clip>` (9 each); body = static MeshRenderer "Icosphere" (bone-attached), outfits = SkinnedMeshRenderer `outfit_<name>` (+_lod1), accessories phone/umbrella_black/clear/led.
- `Assets/Shaders/Crowd.shader`: lambert + map + tint (uv2.x flag: 0 body -> _SkinColor, 1 garment -> _Tint), NkBPointLights, NkFogApply/NkPost; `#pragma multi_compile_instancing` + UnityInstancing.hlsl (required by DrawMeshInstanced) + `mat.enableInstancing = true` on all crowd materials (else InvalidOperationException every frame).
- `Assets/Game/Crowd/Crowd.cs`: agents.js port (seed 20771; lanes avenue x±8.15 hw1.45 d1/2.6, cross cz6.5 xIn10.5 hw1.05 d1/9 ×12, promenade x132.8 hw3.8 d1/4.5, alley hw1.55; idler fallback avenue frontage; avoidance grid 2.0m, overtaking, phase=dist/stride, yaw lerp) -> 740 agents (700 walk, 40 idle). Near pool 24 skinned slots (sex-matched assignment! per-garment tinted material instances from characters.js palettes, umbrellas/phone toggles, manual phase: state.time=phase*period + anim.Sample). Far LOD: 72 baked pose meshes (6 per sex-outfit: walk×4 + idle + idle_b; body verts transformed by animated bone matrix, merged w/ outfit, uv2 flag) drawn via Graphics.DrawMeshInstanced chunked 1023, buckets sex:outfit:pose:tint%2.
- Wiring: GameDebug.BuildCrowd() + RunAsync "crowd" + "all"; PlayerController.Update -> Crowd.Update.
- **KEY LESSONS**: (1) `float.IsFinite(h)` takes a FLOAT — passing double = CS1503 (use double.IsFinite); (2) near-slot pool must be sex-matched (outfit mesh names are per-sex); (3) play-mode scene reload destroys the crowd root — self-heal checks GameObject.Find("Crowd")==null (statics survive, slot GOs don't); (4) crowd material instances need enableInstancing; (5) first self-heal build in play can throw transiently — recovers next frame.
- Verified: 740 agents; 20/24 near slots assigned at street positions; A/B toggle diff = 11163 px (crowd visibly rendering); no exceptions in Update.

## New feature phase 4: gameplay layer (#47 DONE)
- Ported gigs.js/zones.js/settings.js (src/systems/player/*) -> `Assets/Game/Gameplay/`:
  - `Gigs.cs`: 5-gig fixer list (ramen/river/alley/overlook/bridge, exact sim positions from Layout constants), waypoint marker (beam/halo/core open cylinders + ring disc + hex inner ring + octahedron diamond, `Assets/Shaders/GigBeam.shader` — fresnel edge + scan + rising bands + 24-dash rotating ring + pulse wave, additive queue Transparent+4), surface snap via GroundPlan.HeightAt, radius 3.2-4m completion, cooldown 4.5s, eddies/cred journal, elevator prompt (F) for the overlook gig, `_GigNear` global fade.
  - `Zones.cs`: 6 districts + backstreets fallback, first-match order preserved (overlook, bridge, alley, riverwalk, market crossing, avenue); 0.7s candidate window before banner.
  - `Settings.cs`: PlayerPrefs-backed (fov/sensitivity/invertY/headBob/cameraSway/minimapRotate/hud/quality).
  - `Gameplay.cs` facade + `GameplayHud.cs` (OnGUI: gig card, eddies/cred, zone banner, toasts, lift prompt — visible in editor Game view; CLI camera captures do NOT include OnGUI).
- Wiring: GameDebug.BuildGameplay() + RunAsync "gameplay"/"all"; PlayerController.Update -> Gameplay.Update + F-lift handler; PlayerController.OnGUI draws HUD; PlayerController.Teleport().
- **LESSONS**: (1) failed Gigs ctor attempts (HDRP rejects bare Light.AddComponent in edit-mode raw scenes -> NRE; removed the point light) leave orphaned "gig-marker" scene objects — Gameplay.Build now sweeps all gig-markers before creating a new one; (2) raw Nk shaders: declare _SimTime + unity_MatrixV yourself (NkCommon only has unity_MatrixVP/unity_ObjectToWorld/_WorldSpaceCameraPos); include core Common.hlsl BEFORE NkCommon for CBUFFER_START; "Queue"="Transparent+4" (raw ints rejected); (3) ev.sh float literals with f suffix can silently fail — use 75.0; (4) forced recompile when Bee refuses: delete Library/ScriptAssemblies/Assembly-CSharp*.dll then Refresh.
- Verified: marker snap (avenue y=0.2, river deck), gig complete (+850 eddies, +4 cred, cooldown->next), zones (NEON AVENUE/LANTERN ALLEY/SAKURA RIVERWALK), settings round-trip, marker beam A/B diff 13915 px.

## New feature phase 5: procedural audio, full parity (#48 DONE)
- The browser audio is 100% procedural DSP (zero samples) — ported all 2262 lines: `Assets/Game/Audio/`
  - `AudioDsp.cs`: dsp.js port — mulberry32 (Math.imul-exact), RBJ biquads (transposed DF2, 7 types, re-settable for time-varying filters), chain/chainPeriodic (warm-up pass for seamless loop filtering), white/pink(Paul Kellet)/brown noise, pinkLoop (2-pass pinking), smoothRandomLoop (cosine-interp seamless control), mix/mixWrap, normalizeRms/normalizePeak, loopify (equal-power tail crossfade), foldTail, fades, noiseClick, bubble (Minnaert), ping, modalBank (2-pole resonators), panGains, polyBLEP addSaw, combDelay.
  - `AudioSynth.cs`: all recipes — street-canyon reverb IR (3-band decay + 18 early refs + flutter), rainFar (pink + 2600/s micro-drops + gust), rainNear (700/s clicks), droplets, metalPatter (3 modal sheet panels + edge drips), carRoof (panel modes + glass ticks), gutter, trafficBed (sub rumble + 15 doppler-panned tyre passbys), sirenWail/HiLo (JP wail 640-1160, 9-harmonic phase-continuous), horns ×3, trainRumble (68Hz boom + 650Hz roar + inverter whine + ta-tan rail joints), crowdBabble (16 formant-synth voices: glottal source, F1/F2/F3 formant tracks, consonant bursts), ramenShop (5 voices + bowl modal clinks + fryer crackle), neonHum A/B, neonZap, vending, wind (gust + modulated bandpass whistle), thunderNear/Mid/Far (noise-grain rolls + sub sweep + crack cluster), footsteps ×8 + puddles ×4, engineLow/High (V6 firing cycles), tireHiss, **music: 48s @ 80BPM dark synthwave** (8 chords, 4×detuned-saw pad through breathing lowpass, 8th-note sub pulses, glassy arp with baked ping-pong echoes, shimmer sines).
  - `AudioBus.cs`: core.js port — background generation on ThreadPool (synth.worker.js pattern), clips -> AudioClip 48k, master soft-limiter via OnAudioFilterRead on the listener (-6dB, ~12:1 power-law), prefs in PlayerPrefs (nk.audio.v1: volume 0.8/muted/music), one-shot spawner with auto-kill.
  - `Soundscape.cs`: soundscape.js port — EmitterPool (neon×4 alternating A/B, vending×2, ramen×2, gutters×2, tireHiss×3 on moving traffic via VehicleTraffic.GetTrafficSample), rain layer gains (0.5·rain^1.2 etc.), traffic/wind/crowd beds (crowd density heuristic + LP 1500-4500Hz), trains (2 moving voices on elevated rail, 25-55s schedule), sirens (45-125s spawn, 6 path templates, occlusion), horns (7-29s, 40-220m), neon zaps (3-15s + dip), thunder on WeatherDriver lightning pulse (2-5s delay), music bus 0.2 (M toggles), 5Hz staggered slow phases, instant self-heal rebuild from cached clips after scene reloads.
  - WeatherDriver hooks: RainNow/LightningNow/GustNow; Vehicles.GetTrafficSample.
- Wiring: GameDebug.AudioStep/BuildAudio + RunAsync "audiostep"; PlayerController.Update -> Soundscape.Update + M music toggle.
- Verified in play: 39/39 buffers generated in ~100s background, 25 sources playing (rainFar .50/rainNear .42 — it was raining, wind, babble, vending ×2, neon ×3, ramen ×2, music .20, a sirenHiLo at .62), limiter on listener, prefs round-trip. **HUD/mix levels visible only via eval; CLI captures carry no audio.**
- LESSONS: DontDestroyOnLoad is illegal in edit mode (guard Application.isPlaying); AudioClip statics don't survive recompiles — background regen makes it self-healing; Soundscape must null-check AudioBus.Root (scene reload) like all other static systems; ev.sh float literals (75f) fail silently — use 75.0; delete Library/ScriptAssemblies/*.dll to force recompile when the pipeline stalls.
