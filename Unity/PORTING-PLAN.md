# NEON KAIDO — Unity Port Implementation Plan

Source: `../src` (three.js WebGPU + TSL, Rapier WASM, Canvas2D signage text, 100% procedural WebAudio, ~17k lines JS, 241 files).
Target: `unity/` — Unity 6000.6.3f1, HDRP, DX12, Windows, driven headlessly by the `unity` CLI per `EDITOR-CLI.md`.
Ledger: `unity/PORTING.md` (to be created) holds the per-system status table + conventions; this file is the plan.

---

## 1. What we are porting

A cyberpunk Japanese street (NEON KAIDO): an 800 m neon avenue, 500 m river with cherry promenade,
elevated rail + expressway, a 1.40-tower skyline ring at 450–1500 m, alley, overlook, bridge.

| System | JS lines (top) | Notes |
|---|---|---|
| atmosphere | ~320 (atmo, environment, lightShaft, testScene) | day/night clock, fog, sky, god rays |
| ground | ~700 (plan, geometry, surfaces, roadMaterial, markings, decals) | procedural streets, wet road |
| buildings | ~1,700 (props 541, shops 380, plan 370, architecture 326, materials 326, geom, cables) | deterministic city generator |
| landmarks | ~1,300 (rail 401, skyline 357, trees 239, river 176, bridge, expressway) | trains, cherry trees, water |
| signage | ~1,100 (billboards 321, library 252, atlas 227, signs 215, lanterns 183) | Canvas2D JP-font neon text, holo billboards |
| vehicles | ~870 (index 240, traffic 235, materials 235, fleet 202, drive 191) | 5 car models LOD0-3, traffic AI, drivable hero car |
| crowd | ~950 (characters 303, index 239, agents 218, vat 210) | 2 15 MB character GLBs, agents |
| weather | ~500 (rain, steam 190, drips 180, lights) | rain, splashes, steam, fake light array |
| player | ~900 (index 458, controller 307, gig, zone, settings) | KCC + FP/TP rig, 5 gigs, credits |
| audio | ~2,600 (synth 855, soundscape 514, dsp 331, probe 240, core 156) | fully procedural DSP, no samples |
| engine/ui | ~900 | WebGPU renderer, postfx 216, HUD 324, minimap 245, menus |

Gameplay: walk/sprint/crouch/jump, drive cars, 5 fixer gigs (waypoint beam + ring → credits + reward),
rain/lightning weather, day-night. No combat, no multiplayer, no save beyond settings.

## 2. Target stack (from EDITOR-CLI.md)

- Editor: Unity `6000.6.3f1` HDRP, DX12, in `unity/`. CLI: `C:\Users\smith\AppData\Local\Unity\bin\unity.exe` (1.0.0-beta.12).
- Workflow: `ev.sh` (eval, ~5 s main-thread budget → `delayCall` for long work), `recompile --focus`
  (wait ~15 s before Play), `editor_play/stop` (Play needs `runInBackground=true` + `QueuePlayerLoopUpdate()`),
  `capture_game_view` (Windows save path; `--source screen` includes IMGUI HUD), `run-oracle.sh` (delayCall comparer
  → `Temp/oracle/report.txt`).
- Known gotchas: shader errors surface only on first use via `ShaderUtil.GetShaderMessages`; failing shader draws
  nothing; property must exist in shader `Properties` block; after `SceneBuilder.Build()` force domain reload +
  render a frame (RenderTextures throw `MissingReferenceException`); Editor culling runs per Game-camera render,
  not per `Time.frameCount`.

**NOTE:** EDITOR-CLI.md's `GameDebug` boat/fishing API is from a prior port. We keep its *pattern*
(`Ensure/Run/At/Hour/Tap/...`, shot.sh, oracles) and design a NEON-KAIDO-specific API (walk, drive, gig, weather).

## 3. Decisions (lock before coding)

- **D1 Coordinates.** JS (x,y,z) → Unity (x,y,−z). Convert once at data entry: a `SimCoord` static helper;
  LAYOUT + all generated world data stored in **Unity space** in C#. Oracle dumps compare in JS space.
  Player yaw forward: JS `(-sin yaw, -cos yaw)` → Unity `(sin yaw, 0, -cos yaw)`... verify in phase 1 oracle.
- **D2 Determinism.** Port `mulberry32` bit-exactly (first oracle, trivial). Every generator (buildings,
  crowd, traffic, signage layout) takes the same seed → identical city → oracle-verifiable against JS dumps.
- **D3 Scene.** One Play scene; all static content generated at startup by `SceneBuilder` (like JS `init`),
  no streaming at first (city fits culling+LOD). Bake option: save built scene to disk if startup > a few s.
- **D4 Generate, don't bake.** Port generators to C# (the design is procedural + deterministic). Only import
  GLB models + textures as assets. This is what makes oracles possible and keeps the city tunable.
- **D5 Lighting.** JS: modest real lights (23 spawn sites in code) + a *shader* light array (weather `MAX_LIGHTS=24`,
  pos/col vec4 uniforms). No HDRP clustered lighting, so: (a) bake neon ambient with lightformers/lightmaps per
  block, (b) dynamic lights only for interactive districts (budget table in phase 2), (c) port the shader light
  arrays verbatim as custom HDRP shader uniforms. **Spike the real-light count early (phase 2) — this is risk R1.**
- **D6 Shaders.** TSL node materials → HDRP (ShaderGraph for simple, raw HLSL for complex). Signature materials to port, in priority order:
  wet road (roadMaterial/wet.js), neon sign emissive/scan, holo billboard, rain/splash, steam, gig beam/ring,
  light shafts, environment. ~30 TSL files total; several are trivial (noise).
- **D7 Physics.** Rapier KCC → Unity `KinematicCharacterController` (param map below). Cars: custom Rigidbody
  drive (port `drive.js`), static colliders generated alongside meshes.
- **D8 Audio.** Port `dsp.js`+`synth.js` as a standalone C# DSP library (no Unity API — testable in plain .NET).
  Generate all buffers **offline at build/editor time** into `.wav` assets (Editor tool, parallel). Runtime
  `soundscape.js`+`probe.js` → AudioMixer + per-source AudioSources steered by camera position. This is the
  cleanest possible audio port: no realtime DSP in play mode.
- **D9 UI.** HUD/menus → UGUI + TextMeshPro; minimap → ortho top-down camera to RenderTexture; settings →
  PlayerPrefs (mirrors `nk.audio.v1` etc.); loading bar → simple Editor-driven progress.
- **D10 Verification.** Two layers: (1) **oracles** — node scripts dump JS system state (`dump-<system>.mjs`,
  headless GPU where needed), editor comparers print `Temp/oracle/report.txt`; (2) **screenshots** — the six
  `SHOT_VIEWS` reproduced with `shot.sh` and diffed against browser reference panels.

### KCC param map (controller.js → Unity KCC)

| JS (Rapier) | Unity `KinematicCharacterController` |
|---|---|
| radius 0.32, half 0.58 stand / 0.24 crouch | `height = 2×half + 2×radius` (2.16 stand / 1.40 crouch), radius 0.32 |
| autostep 0.42 / 0.18 | `stepOffset = 0.42` (crouch 0.18) |
| snapToGround 0.45 | `snapping = 0.45` (Unity: 0 disables; tune) |
| maxSlope 52°, minSlopeSlide 40° | `slopeLimit = 52` (slide handled in movement code) |
| gravity 21, jumpV 6.6, walk 4.3 / sprint 7.8 / crouch 2.1, coyote + jump buffer | port in movement script (not built-in) |
| eye 1.66 / 1.02, FP/TP blend `viewK`, bob/dip/roll/sway/lookLag | camera rig script |
| `placeAt()` raycast down 60 m | spawn/car-exit/elevator helper |

## 4. Phases

### Phase 0 — Bootstrap (0.5–1 d)
1. Create `unity/` (6000.6.3f1, HDRP, DX12, Windows, 60 Hz fixed step). Add the CLI pipeline package per EDITOR-CLI.md.
2. Recreate `unity/tools/`: `ev.sh`, `shot.sh`, `snap.sh`, `console.sh`, `run-oracle.sh` (adapt from reference port).
3. Create `unity/PORTING.md` ledger (per-system status table, conventions, KCC map, shot list).
4. `Editor/GameDebug.cs` skeleton (namespace `NeonKaido.EditorTools`): `Ensure()`, `Run(t)`, `At(x,y,z)`,
   `State()`, `Shot(name)`, `Hour(h)`, `Weather(rain)`, `Walk(...)`, `Drive(...)`, `Gig(n)`.
5. **End-to-end smoke test:** eval → recompile → play → step → screenshot. Everything downstream depends on this.

### Phase 1 — Core + atmosphere + ground (2–3 d) — *visual foundation*
- `GameContext.cs` (ctx equivalent: world, layout, weather, events, quality, isShot), fixed-step driver, asset loader.
- `Layout.cs` = exact port of `LAYOUT` (convert to Unity space once).
- `SimCoord.cs` + **mulberry32 bit-exactness oracle** (first oracle, done before anything else).
- Atmosphere: day/night clock → HDRP sky driver + sun + fog + environment; port `lightShaft` as post/procedural.
- Ground: avenue, cross streets, sidewalks, curbs, markings, puddles (port plan/geometry/surfaces/markings/decals).
- **Wet-road HDRP shader** — first custom shader, the signature look. Spike here (risk R2).
- Oracles: ground geometry (mesh bounds/counts/UVs) + mulberry32. Screenshots: `street`, `ground` views.
- **Milestone: `shot.sh street` matches reference panel 1 (wet neon avenue, no buildings yet acceptable).**

### Phase 2 — Static city: buildings + landmarks + signage (4–7 d) — *largest phase*
- Buildings: port plan/architecture/shops/geom/props/cables with the same seed. Import 3 prop GLBs.
  Shop interiors, rooftop props, cables.
- Landmarks: elevated rail + looped train (rail.js 401), expressway + lights, river + water shader, bridge,
  promenade + cherry trees (4 GLB), **skyline: 140 instanced towers** (GpuInstance or mesh combining; LOD).
- Signage: port `library` (sign definitions) → C#; **atlas: Canvas2D → C# `Texture2D` generation at startup**
  using the same Windows JP fonts (Yu Gothic / Meiryo / MS Gothic); vertical JP text, strokes, paths;
  billboards (holo shader), lanterns/chochin/andon GLBs, streetlamps, utility poles, pedestrian/traffic signals.
- Lighting pass: lightformers + bake per block; dynamic-light budget table; port weather shader light arrays.
- **Lighting spike (R1):** measure real dynamic light count vs HDRP cost on the avenue block; adjust bake/fake split.
- Oracles: building/cable/prop transforms + counts; skyline tower transforms; sign positions + texture hash;
  train position vs t. Screenshots: `overlook`, `river`, `alley`, `aerial`.
- **Milestone: all four city shots match; `GameDebug.Shot("alley")` etc.**

### Phase 3 — Simulation: player, vehicles, crowd (4–6 d)
- Player: KCC script (param map §3), input (WASD/mouse), FP/TP rig (bob/dip/roll/sway/lookLag, viewK blend),
  crouch/jump/coyote/jump-buffer, `PlaceAt` helper.
- Gigs: 5 waypoints (exact JS data: pos/radius/reward/cred/kanji/area), beam+ring shaders,
  credits/reward HUD toast, `elevator:true` variants use `PlaceAt`.
- Vehicles: import 5 models × LOD0-3; traffic AI port (traffic.js 235); drivable hero car (drive.js 191)
  as custom Rigidbody; car enter/exit via `PlaceAt`.
- Crowd: 2 character GLBs (import via GLB; headless Blender only if retargeting fails — see EDITOR-CLI.md),
  port agents/vat/characters (218/210/303).
- Static colliders generated with meshes; player/vehicle/crowd collision layers.
- Oracles: player trajectory over N deterministic steps (foot positions every 60 frames), traffic positions,
  crowd positions. **Milestone: scripted walk→drive→gig-complete runs headless in play mode.**

### Phase 4 — Weather + FX (2–3 d)
- Rain (GPU particle), ground splashes, drips (port drips.js incl. fake-light array), steam, lightning
  (flash + sky event), wetness feedback to road shader; port shared `weather` state to context.
- **Milestone: storm conditions reproduce reference (rain + wetness 1.0 + lightning flash timing).**

### Phase 5 — PostFX, audio, UI polish (3–5 d)
- PostFX chain (PostFX.js 216: tonemap/grade/bloom/etc.) → HDRP Volume + custom RenderFeature(s).
- Audio: standalone C# DSP library (port dsp/synth ~2,200 lines) + Editor tool that renders all buffers to
  `.wav`; offline oracle: RMS/spectrogram dump JS-vs-C#. Runtime soundscape/probe → AudioMixer groups.
  Shot mode = silent (same as JS).
- UI: HUD, menus, minimap (ortho RT camera), settings → UGUI/TMP; PlayerPrefs persistence.
- **Milestone: full golden screenshot suite (all 6 views) + audible city + working HUD.**

### Phase 6 — Performance & release (2–4 d)
- Profile DX12 (frame time, draw calls, light count, shadow cost). Instancing/LOD/occlusion/lightmap passes.
- Windows Standalone build; loading screen; input edge cases; 5 s+ startup → async build or scene save.
- Optional stretch: streaming if the scene proves too big (not expected — 1.5 km radius fits culling).

**Total: ~18–28 focused days** (one person, using the CLI workflow from day one).

## 5. File mapping (JS → C#)

| JS | C# | Phase |
|---|---|---|
| world/layout.js | `Layout.cs` | 1 |
| engine/Engine.js | `GameContext.cs`, `GameLoop.cs` | 0-1 |
| atmosphere/* | `Atmosphere.cs` (sky/sun/fog), `LightShaft.cs` | 1 |
| ground/* | `GroundBuilder.cs`, `RoadMaterial.cs` (shader) | 1 |
| buildings/plan,architecture,shops,geom | `BuildingPlan.cs`, `Architecture.cs`, `Shops.cs` | 2 |
| buildings/props,cables,materials | `Props.cs`, `Cables.cs`, `BuildingMats.cs` | 2 |
| landmarks/rail,skyline,trees,river,bridge,expressway | one class each | 2 |
| signage/library,atlas,signs,billboards,lanterns | `SignLibrary.cs`, `SignAtlas.cs` (Texture2D gen), `Holo.cs` | 2 |
| player/controller,gig,zone,settings | `PlayerController.cs`, `Gigs.cs`, `Settings.cs` | 3 |
| vehicles/fleet,traffic,drive,materials | `Fleet.cs`, `Traffic.cs`, `CarDrive.cs` | 3 |
| crowd/agents,vat,characters | `Crowd.cs`, `Agent.cs`, `Character.cs` | 3 |
| weather/* | `Weather.cs`, `Rain.cs`, `Steam.cs`, `Drips.cs` | 4 |
| engine/PostFX.js | `PostFX.cs` (Volume/RenderFeature) | 5 |
| audio/dsp,synth,soundscape,probe,core | `NkAudio/` (DSP lib) + `Soundscape.cs` | 5 |
| ui/hud,menus,minimap | `UI/` UGUI controllers | 5 |
| materials/wet,noise | HLSL shaders | 1+ |

## 6. Verification strategy

- **Oracles (per system):** node `dump-<system>.mjs` (headless GPU where a pass needs it:
  `GPU.init({headless:true})`, `pathToFileURL` for Windows drive-letter imports) → editor comparer via
  `run-oracle.sh`. Order: mulberry32 → ground → buildings → landmarks → signage → player → traffic → crowd → weather.
- **Screenshots:** the six `SHOT_VIEWS` (street, overlook, river, alley, aerial, ground) via
  `shot.sh <name> [ADV]`; compare against browser reference panels (the four "panel" shots are the visual contract).
- **Play-mode discipline** (EDITOR-CLI.md): `editor_stop` before script edits; `recompile --focus` then wait ~15 s;
  `runInBackground` + `QueuePlayerLoopUpdate` for stepping; `Time.timeScale=0` + `GameDebug.Run(t)` for exact stepping.

## 7. Risks & mitigations

| # | Risk | Mitigation |
|---|---|---|
| R1 | No clustered lighting; real dynamic light count unknown | Lighting spike in phase 2; bake neon ambient (lightformers); keep shader light arrays; per-district dynamic budget |
| R2 | Wet-road reflection fidelity (WebGPU planar/reflection → HDRP) | Phase-1 spike; fallback: HDRP SSR/planar reflection + baked reflections in puddles |
| R3 | Canvas2D JP text → Unity Texture2D parity (vertical text, strokes, fits) | Fonts are Windows system fonts (Yu Gothic/Meiryo/MS Gothic); spike in phase 2; fallback pre-render atlas in Node at build time (asset, not runtime) |
| R4 | Audio DSP port (~2.6k lines) drifts from JS | Port as standalone .NET lib first; offline RMS/spectrum oracle before any runtime wiring |
| R5 | RNG mismatch breaks city determinism | Bit-exact mulberry32 oracle in phase 1, before generators |
| R6 | Headless editor quirks (5 s eval, domain reload, unfocused culling) | Follow EDITOR-CLI.md recipes exactly; they are battle-tested |
| R7 | 237 MB textures slow imports/builds | Import settings (BC7/ASTC, mips, max size) from day one; atlas where JS already does |
| R8 | GLB character/vehicle import (animation rig, LOD) | GLB import is native in Unity 6; headless Blender only as fallback (never drive an open Blender scene via MCP while importing) |
| R9 | Shader port surface (~30 TSL files) | Inventory + priority list above; trivial ones (noise) first, complex (holo, wet) with early spikes |

## 8. Out of scope / open questions

- Targets: assume Windows PC DX12 only (console/mobile later if ever).
- No multiplayer, no networking, no save (only settings) — same as JS.
- Open: loading-screen parity? Input remapping? Controller support? (default: no, unless asked)
- Open: keep `?only=`-style per-system isolation in the port? (recommended: keep it as an editor menu flag — it's
  the cheapest debugging win we get for free from the JS architecture.)
