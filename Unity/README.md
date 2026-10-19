# NEON KAIDO — Unity Port

Headless port of the browser game **NEON KAIDO** (three.js WebGPU + TSL nodes,
`../src`) to **Unity 6000.6.3f1 HDRP / DirectX 12**. The JavaScript source is the
single source of truth: every system is ported 1:1 (same generators, same seeds,
same layout constants), then verified against oracle dumps or pixel-compared
captures.

**Status: complete.** All systems ported and verified — ground, buildings,
atmosphere, signage (billboards/screens/holos/neon/lights), street lamps,
signals, river, elevated rail + moving trains, expressway, cherry trees with
petals, promenade props, arch bridge, and the 303-tower skyline with holographic
ads.

## Running it

1. Open this folder (`unity/`) as a project in **Unity 6000.6.3f1** (HDRP, DX12).
2. Open `Assets/Main.unity`.
3. **Play**. The player camera walks the city: `WASD` move, `Shift` run,
   `Space` up, `C` down, mouse look, `Esc` twice to exit.

Everything is generated procedurally at build time — no baked assets. If the
scene looks empty (e.g. after an editor restart), rebuild in this order from the
`NeonKaido.EditorTools.GameDebug` static class (via the editor CLI, see
`EDITOR-CLI.md`):

```
BuildBuildings()   → 149 chunk meshes, 7181 props, 2605 colliders, facade cache
BuildAtmosphere()  → sky dome, fog control, exposure
BuildGround()      → road base, sidewalks, curbs
BuildSignage()     → billboards, facade signs (994), lanterns, koi, holo ring
BuildLandmarks()   → river, rail + trains, expressway, cherry, props, bridge, skyline
```

`GameDebug.RunAsync("buildings" | "all" | ...)` runs the slow ones off the eval
timeout; poll `GameDebug.AsyncBusy` / `AsyncResult`. Always rebuild buildings
before signage after a script recompile (the facade cache is a static).

## How it maps to the JS source

| JS (`../src/systems/`) | Unity (`Assets/Game/`) |
|---|---|
| `ground/`, `Core` oracles | `Core/` (Mulberry32, GroundPlan/Meshes, Layout, Earcut) |
| `buildings/` | `Buildings/` (plan, architecture, props, shops, cables, overlook) |
| `atmosphere/` | `Atmosphere/` (Atmo, AtmoNoise) |
| `signage/` | `Signage/` (billboards, facade signs, lanterns, signals, street lamps, koi) |
| `landmarks/` | `Landmarks/` (river, rail, trains, expressway, cherry, props, bridge, skyline) |
| TSL node materials | hand-written HLSL in `Assets/Shaders/` (~30 shaders) |

Key conventions (full details in `PORTING.md` § Conventions):

- **Coordinates**: sim `(x,y,z)` == Unity `(x,y,-z)`. All placement math is done
  in sim space and mirrored at bake; `LmGeo.ToUnityMesh` does it centrally.
- **Determinism**: every generator uses the ported `mulberry32` with the exact
  JS seeds — placement is bit-comparable with the browser build.
- **Lighting**: real Light components are data holders only; actual lighting
  flows through a 384-entry pooled array (`NkBPointLights` in every shader).

## Documentation

- `WORKFLOW.md` — the operating loop: how to change, verify and persist anything,
  and the traps that waste a session (start here for a handoff).
- `PORTING.md` — the status ledger: per-system entries, verified numbers, and
  every gotcha hit along the way (read this before touching anything).
- `PORTING-PLAN.md` — the phased plan the port followed.
- `EDITOR-CLI.md` — how to drive the editor headlessly (`ev.sh`, captures,
  console checks, compile verification).

## Tooling

- `tools/ev.sh '<C# expr>'` — eval a snippet in the live editor.
- `tools/console.sh [n] [type]` — read editor console entries.
- `tools/nkcap.sh <name>` — capture the game camera without writing into `Assets/`
  (a `--save_path` capture triggers an import that wipes the light arrays).
- `tools/nkpose.sh` / `nk-lights.sh` — freeze the session, pose the camera, and A/B
  a chosen set of point lights; thaw with `nk-lights.sh reset`.
- `tools/dump-*.mjs` — node oracle dumps that mirror the JS systems for
  verification (buildings, ground, signage, koi, cherry, mulberry32, noise).
- `Temp/shots/` — verification captures (avenue, panorama, river, rail, cherry,
  fog checks).
