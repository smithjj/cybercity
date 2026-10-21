# NEON KAIDO — Unity port: working method

How to make a change to the port, verify it, and not corrupt the project. This is the operating
loop, not the API surface: `README.md` orients, `PORTING.md` is the per-system status ledger plus
every gotcha hit so far, `EDITOR-CLI.md` is the command reference. Read `PORTING.md` before touching
anything; read this before *running* anything.

---

## 0. The one fact that explains most confusion

The world is **baked into `Assets/Main.unity`** (~1.03 GB, gitignored). There is no runtime world
build. Every mesh, collider, light, and per-vertex datum you see came from
`GameDebug.RunAsync(...)` followed by `save_scene`.

Two consequences:

1. Changing generation code does **nothing** to the scene until you re-run the build *and* save.
   The scene can happily render a weeks-old world while the code looks correct — and it will do so
   again the moment it reloads from disk.
2. Anything not serializable — light arrays, shader globals, `MaterialPropertyBlock`s — is **gone on
   load** and has to be pushed again by something at load time (§5).

The corollary for diagnosis: before believing either the code or the picture, work out which of the
two is stale.

---

## 1. Layout

| Path | What it is |
|---|---|
| `src/` (repo root) | The browser game. The **reference implementation**; when the two disagree, `src/` is right unless proven otherwise. |
| `Unity/Assets/Game/**` | Runtime code, by system: `Buildings/`, `Signage/`, `Vehicles/`, `Gameplay/`, `Weather/`, `Landmarks/`, `Crowd/`, `Audio/`, `Core/`. |
| `Unity/Assets/Shaders/*.shader` | Custom HLSL. Mostly ignores Unity's own lighting; see §5. |
| `Unity/Assets/Editor/GameDebug.cs` | The build + debug facade that everything drives from the CLI. |
| `Unity/Assets/Editor/NkEditorLighting.cs` | Edit-mode push of the light arrays and atmosphere globals, plus the post-import re-push. |
| `Unity/Assets/Settings/Materials/*.mat` | Tracked, so edits there are durable. The scene is not. |
| `Unity/Temp/` | Scratch and gitignored: captures, oracle dumps, decompressed logs. |

---

## 2. Driving the editor

Binary, from WSL: `/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe`.

`Unity/tools/` is split, and this matters: the `nk*` helpers use the `/mnt/c/...` path and run under
WSL. The older ones (`ev.sh`, `console.sh`, `shot.sh`, `snap.sh`, `preflight.sh`, `run-oracle.sh`)
hardcode `C:/Users/...` and only work from a Windows shell — under WSL they fail with
`not found`.

Two budgets:

- `eval` runs on the main thread with about **5 s**. Exceed it and you get
  `Main thread operation timed out after 5000ms`. Never `Thread.Sleep` inside an eval.
- Long work goes through `GameDebug.RunAsync(which)`, which defers to an `EditorApplication.update`
  callback. Fire it, then poll `AsyncBusy` / `AsyncResult`.

Targets: `buildings | landmarks | atmosphere | ground | signage | vehicles | weather | crowd | gameplay | audiostep | all`.
**`all` does not include buildings** — run `buildings` first when a change touches them.

```sh
U=/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe
$U --no-banner command eval 'return NeonKaido.EditorTools.GameDebug.RunAsync("buildings");'
sleep 60
$U --no-banner command eval 'return "busy="+NeonKaido.EditorTools.GameDebug.AsyncBusy+" | "+NeonKaido.EditorTools.GameDebug.AsyncResult;'
```

Also useful: `GameDebug.Ensure()` (creates the `/Game` context object),
`NeonKaido.Gameplay.Describe()` (current gig / zone / HUD state), and `GameDebug.Health()` (pipeline
health — what `preflight.sh` checks).

### Eval-snippet traps

Both of these cost real time and both produce a diagnostic that has nothing to do with the problem:

- **Float literals need the `f` suffix.** `-0.38*Mathf.Rad2Deg` fails to compile and the error reads
  `Unreachable code detected (line 14, col 12)`.
- **Don't write `if (pc.enabled) pc.enabled = false;`** — same bogus diagnostic. A plain
  `pc.enabled=false;` works.

---

## 3. The two loops

### A. World change

Generation code, layout, props, anything baked into the scene:

1. edit → `$U --no-banner recompile --focus`
2. confirm the compile is clean (§4)
3. `RunAsync("buildings")` → poll; then `RunAsync("all")` → poll
4. verify **before** saving (§4)
5. `$U --no-banner command save_scene --timeout 1800` — minutes, ~1 GB. Do it once, and do not stack
   heavy work on the editor while it runs; it has crashed under load before.
6. `open_scene --path Assets/Main.unity --timeout 1800` and spot-check that the change survived

### B. Code-only change

Shaders, runtime behaviour, HUD, gameplay logic: recompile and Play. Nothing needs baking. But note
that shader *properties* live on the material assets, and anything derived from generated data only
shows up through loop A.

---

## 4. Verification, in increasing order of trust

Cheap and decisive beats "looks right".

- **Compile.** `$U --no-banner command console --tail 10 --level error`, and read
  `groundTruth.compilationFailed` / `compiling` in the JSON rather than trusting the entry list.
- **Numeric probe over a picture.** A small `eval` settled every real bug this session:
  - a *single* sign quad's UVs — a union over all 8320 verts spans the whole atlas even when every
    quad is wrong, so the union is not a staleness test;
  - `Physics.Raycast` down the alley stairs to read the actual collision surface against
    `GroundPlan.HeightAt`;
  - a census of colliders by rotation type (2647 axis-aligned, 98 yaw-only, 2 tilted — which is how
    the stairs bug was scoped down to the only two quaternion-rotated colliders in the city).
- **Diff frames.** Capture twice with `tools/nkcap.sh` and compare in Python/PIL: a mean difference
  plus a pixel count over a threshold is far more honest than eyeballing two PNGs. A lamp A/B that
  "looked fine" turned out to be a zero-difference null result until the capture stopped writing into
  `Assets/`.
- **Reload from disk.** `open_scene`, then re-probe. This is the only proof that a bake persisted.
- **Probe what the renderer actually draws, not what you think it should.** The crowd looked absent
  because it was *1.6 cm* tall: `Renderer.bounds` on a skinned mesh reports bind-pose bounds, and this
  FBX carries a 100x armature scale, so a 1.7 m character reports 197 m and any "normalise to 1.7 m"
  math shrinks it by 100. Bake the mesh (`SkinnedMeshRenderer.BakeMesh` + the renderer's
  `localToWorldMatrix`) or measure the skeleton's bone span instead.

Do not verify by looking at the viewport alone until §5 is satisfied.

---

## 5. The lighting is not serializable — read this before judging the port

`_BLPos` / `_BLColor` / `_BLCount` are uploaded with `SetVectorArray` on each material (plus
`_CarLPos` / `_CarLColor` / `_CarLCount`), and the fog/haze constants are shader globals. None of it
survives a scene load, and `.mat` assets do not serialize it either. On a freshly loaded scene there
are **zero point lights**: `NkBPointLights()` breaks out of its loop immediately, so lamp lenses look
lit (their emissive lives in the material) while casting nothing, sign backing panels go black, and
distance falls to black. That is exactly the "broken in the editor but fine in Play" report.

Two mitigations, both in the editor assembly:

- **`NkEditorLighting`** pushes `Atmo.PushGlobals()` + `BuildingsRuntime.PushSceneLights()` on scene
  open/save/hierarchy change, so the edit-mode viewport is representative of the game.
- **`NkLightArrayGuard`** is an `AssetPostprocessor`: any asset import reimports the dirty `.mat`
  assets and wipes every runtime-set property, so it re-pushes after imports.

The same class of loss applies **per object**: a `MaterialPropertyBlock` is runtime state, not scene
data. Cars keep paint, wheel spin and lamp level in one, so a loaded scene showed every car black
with dead head/tail lamps — the car *lighting* (fake point lights, beam cones, headlight pools) is
also rebuilt per frame by `VehicleTraffic.Update`, which the Editor never calls. `CarVisual`
serializes the three values and `VehicleTraffic.PreviewVisuals` rebuilds the rest from them;
`NkEditorLighting` calls it on push and after ~25 m of camera travel. Any future per-object look
should go through a serialized component for the same reason.

The crowd loses state the same way, in three separate steps that all look like "the feature was never
built": runtime `Material`s (and `Object.Instantiate` of one — **instantiate does not copy
`hideFlags`**) are destroyed by the next scene load while the `HideAndDontSave` hierarchy survives
holding dead references; and `Shader.Find` can return null while a scene is still loading, which
bakes a material that renders magenta forever. So: mark every runtime material `DontSave`, route
creation through one guarded accessor that makes `Build` bail out and retry on the next tick, derive
"is it built" from the root reference rather than a cached bool, and rebuild on `sceneOpened`.
Related trap: `FindObjectsByType` does **not** enumerate `HideAndDontSave` objects — a "0 roots"
probe is a lie, while `GameObject.Find` and the static reference still see them.

The same trap catches anything that builds itself into the scene with `HideAndDontSave`
materials: the gig marker was saved into `Main.unity`, so after every load its beam/ring
renderers pointed at null materials and drew magenta. Runtime-only systems (crowd, car beam rigs,
gig marker) must be `HideAndDontSave` **and** rebuilt by the editor push — and `Resources.FindObjectsOfTypeAll`
is the only enumerator that sees them, so sweeps written against `FindObjectsByType` silently miss
the stale copies.

**Consequence for captures.** Never use `capture_game_view --save_path`. It forces the PNG into
`Assets/Temp/`, which triggers exactly that import, wipes the light arrays mid-session and races the
guard. `tools/nkcap.sh` takes the command's inline base64 instead and never touches the asset
database. `--source screen` includes the HUD; `--source camera` does not.

---

## 6. Measuring lighting without lying to yourself

Weather and traffic re-randomise on every Play, so two frames from different sessions are not
comparable. `tools/nk-lights.sh <mode>` freezes `Time.timeScale`, hides `/VehiclesRoot` `/Crowd`
`/Gameplay`, zeroes `_CarLCount`, and pushes a chosen light set:

| mode | effect |
|---|---|
| `all` | rank every scene light, keep the best 384 |
| `nolamp` | same ranking and order, every `/StreetLamps` light zeroed |
| `lamps` | only the street lamps contribute |
| `reset` | thaw: everything re-enabled, `timeScale = 1` |

- Always `reset` before handing the session back.
- **Never save while the debug levers are set** — a save bakes inactive roots and a disabled
  `PlayerController` into the scene. (A `RunAsync("buildings")` rebuild does incidentally recreate
  those roots active.)

`tools/nkpose.sh <unityX> <unityZ> <yawRad> <pitchRad> [fov]` poses the play camera and freezes the
session. It takes **Unity** coordinates — sim `(x, y, z)` maps to Unity `(x, y, −z)`, so pass `−simZ`
— and radians. Yaw 0 faces north (+Z, down the avenue); positive pitch looks down.

---

## 7. Conventions

- Coordinates: sim `(x, y, z)` → Unity `(x, y, −z)`. North is **+Z**.
- `GameDebug.PoseCam` takes sim yaw and negates it internally. `PlayerController.Yaw` / `.Pitch` are
  radians, and positive pitch looks **down**.
- **A sim heading is not a Unity yaw.** Mirroring z means a sim heading θ faces `(sinθ, −cosθ)` in
  Unity, so an object whose mesh faces +Z at identity needs yaw **π − θ**; a mesh authored facing −Z
  needs **−θ** (that is what the signage/lamp code uses). Getting this wrong is invisible on
  x-aligned objects and 180° wrong on z-aligned ones — the crowd "walked backwards" down the avenue
  for exactly this reason. Check which way the mesh faces before choosing.
- `PlayerController.Teleport(x, y, z)` takes Unity coords, starts at `y`, and raycasts down to ground.
- `GroundPlan.HeightAt(simX, simZ)` is the analytic ground profile used for snapping, not a model of
  the geometry: it treats the alley staircase as one 22 m ramp while the visual geometry is two
  flights with a landing. Do not use it to validate collision.
- Tilted colliders: `BCollider.Rot` is a **sim-space** quaternion. Mirroring z maps a rotation of θ
  about axis `a` to −θ about `(a.x, a.y, −a.z)`, i.e. `(x, y, z, w) → (−x, −y, z, w)`. Negating `z`
  instead is a different map that flips the sign of a Z rotation — that was the alley stairs bug
  (walk straight through the steps). `RotY` is a separate yaw-only path.

---

## 8. Comparing against the browser

- `src/` is the reference. Port numbers and behaviour from it; don't invent.
- Canonical framings are in `src/world/layout.js` → `SHOT_VIEWS`: `street`, `overlook`, `river`,
  `alley`, `aerial`, `ground`. Each carries a `desc` naming the reference panel. Pose Unity to the
  mirrored position and compare like for like.
- **Oracles are the strongest tool here.** `node Unity/tools/dump-*.mjs` runs the *actual JS*
  headlessly into `Temp/oracle/*.json`; `run-oracle.sh <OracleClass> [arg]` then compares the C#
  output against it, often to the last bit of a double. Use it before trusting any generation
  rewrite, and note it only covers the systems with a dump: buildings (incl. alley / overlook /
  props / shops stages), ground geometry, mulberry32, noise textures.
- Running the browser for a screenshot is impractical in this environment: it needs WebGPU, and
  headless Chrome renders the loading screen and never finishes the world build under software
  rasterisation. Ask the user for a browser screenshot rather than burning time on it. There is no
  alley reference image in the repo.

---

## 9. Traps that cost time

| Symptom | Cause |
|---|---|
| Port renders an old world while the code looks right | stale bake — rebuild **and** save (§0) |
| Signs show the whole atlas / glyph gibberish | per-quad UVs are local 0..1; check one quad, not the union |
| Viewport has no point lights, black sign backings | light arrays not pushed (§5) |
| City goes dark a second into a capture | asset import wiped the arrays; use `nkcap.sh`, not `--save_path` |
| `GetFloat("_BLCount")` logs an error | it isn't in `Properties{}`; `GetVectorArray` does not error, `GetFloat` does — don't probe with `GetFloat` |
| `Unreachable code detected (line 14, col 12)` | missing `f` suffix, or `if (x) x=false;` in an eval |
| Two captures disagree wildly | weather/traffic re-randomised between sessions |
| Player walks through the stairs | visual steps have no colliders; the ramp collider mirror is wrong (§7) |
| Trees/rails stretch into long slivers, a dotted grid crawls over the paving | a merged mesh passed 65,535 verts without `indexFormat = UInt32`; UInt16 indices wrap and weld distant triangles (see PORTING.md) |
| A billboard/screen is a black slab from the front although its texture is bound | the sim→Unity mirror flipped the winding; reverse the triangle order (a Cull Off diagnostic material proves it) |
| A material value looks right in-session then goes black/zero after `open_scene` | the property is not declared in the shader's `Properties{}`; an HLSL uniform alone is dropped on reload |
| After a scene reload the city looks washed lavender or black | `_AgxExposure` was never published by `Atmo.PushGlobals` (it used to be build-only); it must read 0.82 |
| Steam/fog renders as hard-edged translucent rectangles | the weather puff atlas is a runtime texture lost on scene load, so the shader samples white alpha (see PORTING.md) |
| A side street is lit by other neighbourhoods' lights but has none of its own | the 384-light pool is distance-ranked now; it must be re-pushed after camera moves (Play, Editor tick) |
| Cars are black with dead head/tail lamps in the Editor but fine in Play | paint/spin/lamp level live in a `MaterialPropertyBlock` (not serialized) and `VehicleTraffic.Update` never runs outside Play; see `CarVisual` + `PreviewVisuals` |
| Car beams/pools disappear after leaving Play mode | their `DontSave` material is destroyed with Play while the static `_beamInit`/`_poolInit` survive (no domain reload); reset the flag when the material is null |
| Lantern kanji render as hollow boxes | the headless atlas renderer had no mincho font, so `FONTS.mincho` fell back to a non-CJK serif and drew `.notdef`; register a serif CJK face (SimSun here) and alias it to "MS Mincho"/"Yu Mincho", then re-run `tools/render-atlas.mjs` |
| A hand-placed "hero" sign is missing and every nearby generated sign differs from the reference | the whole `featured.js` pass is missing: it runs *first* on the shared signage rnd stream and reserves its facades, so skipping it shifts every populate draw |
| Lanterns hang on the wrong side of the map | `Lanterns` mixed Unity-space `f.Center/f.Right/f.Normal` into sim-space math (and `Hang` mirrors again); always use `CS()/RS()/NS()` |
| `unity.exe: not found` from a tool | that tool hardcodes `C:/...`; run it from Windows, or use an `nk*` one |

---

## 10. Repo hygiene

- Never commit `Unity/Assets/Main.unity` (gitignored) or `Assets/Temp*`.
- Keep root junk out of commits: `nul`, `screenshot-browsergame.png`, `NEON-AUDIT-browser.md`,
  `Unity/F8-dump.txt`, `Unity/unity-*-screenshot*.png`, `Unity/pinktexture-cars.png`, session exports.
- Commit style is imperative subject plus a body carrying the *why* and the evidence — measured
  numbers, before/after tables, and the reasoning that scoped the bug. The history doubles as the
  design record, so write it for whoever reads it next.
- `Unity/Temp/` is scratch; captures, dumps and decompressed logs live there.

---

## 11. Handoff checklist

Before ending a session:

- [ ] editor in **edit mode**, not Play — a Play session leaves the debug levers set
- [ ] `tools/nk-lights.sh reset` if anything was frozen or hidden
- [ ] every world rebuild saved via `save_scene` and re-verified by `open_scene`
- [ ] `console --level error` clean apart from known noise
- [ ] `git status` shows only intentional changes
- [ ] open threads and next steps written down (here, in `PORTING.md`, or in the commit body)
