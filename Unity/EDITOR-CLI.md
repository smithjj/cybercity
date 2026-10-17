# Driving the Unity Editor from the command line

How the Unity port was built and checked without clicking through the Editor: the `unity` CLI, the helper scripts in
`unity/tools/`, and the gotchas that cost time. `PORTING.md` has the status ledger and conventions; this file is the
how-to for the tooling.

## The CLI

| | |
|---|---|
| Windows path | `C:\Users\smith\AppData\Local\Unity\bin\unity.exe` |
| From WSL | `/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe` (every script in `tools/` sets `U=` to this) |
| Version | `unity 1.0.0-beta.12` (`unity --version`) |
| Editor | Unity 6000.6.3f1, HDRP, project in `unity/` |

Always pass `--no-banner` in scripts. `--json` gives machine-readable output, `--help` lists every command, and
`unity commands` prints a manifest of them all.

The commands used here:

| Command | What it does |
|---|---|
| `unity open [project]` | Opens the project with the right Editor version. Open it once; the rest drive that running Editor. |
| `unity recompile --focus` | Recompiles scripts in the running Editor and prints compile errors. **Use `--focus`** (see below). |
| `unity command eval '<C#>'` | Runs C# on the Editor's main thread and returns a result. The workhorse. |
| `unity command console --tail N --level warning` | Reads the live Editor console. |
| `unity command editor_play` / `editor_stop` | Enters and leaves Play mode. |
| `unity command capture_game_view --source camera\|screen --width W --height H --save_path <Windows path>` | Saves the Game view as a PNG. |
| `unity command` (no name) | Lists every command the connected Editor exposes (`--query`, `--detail compact`). |
| `unity status` / `unity pipeline list` | Shows connected Editors. "No Unity Editor instances found" means no Editor is open on a project with the Pipeline package. |

`unity command` talks to the Pipeline package's HTTP server inside the Editor, so the Editor must be open on the
project. `--timeout <s>` (default 30) and `--detach` (returns a job id; `unity job`) exist for long commands.

## eval

```sh
unity.exe --no-banner command eval 'return UnityEngine.Application.productName;'
```

- The snippet is a method body: end statements with `;` and `return` a string to see a result.
- **Main thread, about a 5-second budget.** Anything longer aborts before the lines after it run. Long work goes through
  `EditorApplication.delayCall` (see Oracles) or is done in chunks (`tools/surf.sh` advances the sea in 1.2 s steps).
- **Quoting.** Use single-quoted shell strings. A `\"` inside a double-quoted bash string breaks the C#. For anything
  with quotes, write the snippet to a file and pass `"$(cat file.cs)"`.
- **Compile errors print no result line**, so a loop that prints nothing usually did not compile. Pipe through
  `| head -5` to see the error.
- `tools/ev.sh '<C#>'` wraps eval, prints only the result, and expands `D.` to `Tidewater.EditorTools.GameDebug.`.
  It truncates at 700 characters; set `W=6000` for more. Do not name a variable `D` (it collides with that expansion).
- Some APIs differ from what you might guess: `HDRenderPipeline.currentAsset` does not exist (use
  `GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset`).

## Recompiling

`unity recompile --focus` compiles and reports errors. Focus matters: an unfocused Editor does not tick
`EditorApplication.delayCall`, so queued work silently waits until the window comes to the front, and focus does not
always survive a domain reload. After a recompile wait about 15 s before entering Play mode, because the Editor is
still reloading its scripts. A recompile resets run-time state (the ocean and its sea state, DontSave objects).

## Play mode

The Editor does not tick `Update` in Edit mode, which is why `Editor/GameDebug.cs` exists: it makes the game objects and
steps `PlayerHost` and the ocean by hand. Play mode does tick, but only while the window is not throttled:

```sh
unity.exe --no-banner command editor_play
unity.exe --no-banner command eval 'UnityEngine.Application.runInBackground = true; for (int i = 0; i < 5; i++) UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); return 1;'
```

Without the second line frames stay at 2. Other rules:

- `editor_stop` before editing scripts. Editing a script during Play triggers a domain reload that nulls
  `GameHost.instance`; restart Play.
- Objects the Editor made are destroyed when Play starts. Call `D.Ensure()` again afterwards.
- `capture_game_view --source screen` includes the IMGUI HUD; `--source camera` does not.
- The game clock runs minutes per real second in Editor steps. `Time.timeScale = 0` then `D.Run(t)` freezes real-time
  stepping for mid-animation shots (set it back to 1). To hold the time of day, set `clock.timeSpeed = 0` before `D.Hour(h)`.

## GameDebug (Editor/GameDebug.cs)

Drives the game without a keyboard. In `ev.sh` snippets write `D.`:

| Call | Does |
|---|---|
| `D.Ensure()` | Makes the Game and Player objects. |
| `D.Run(seconds)` | Advances the ocean and steps `PlayerHost`. |
| `D.At(x, z, yaw)` | Places the player on foot, in sim coordinates. |
| `D.Aboard("deck"\|"helm")` | Boards the boat. |
| `D.Boats("all")`, `D.Money(n)`, `D.Give(id, kg, cm)`, `D.Hour(h)`, `D.Weather("Storm")` | Sets up game state. |
| `D.Tap("KeyE")`, `D.Mouse(0, true)`, `D.Rod()`, `D.AutoFish(true)` | Input and fishing. |
| `D.State()`, `D.Game()` | Reads state. |

Sim and Unity frames differ: sim `(x, y, z)` is Unity `(x, y, -z)`. Player yaw: forward is `(-sin yaw, -cos yaw)`.

## Screenshots

- `tools/shot.sh <name> <simX> <simZ> <eyeHeight> <yawDeg> <pitchDeg> [sea] [w] [h]` places a camera in sim
  coordinates and saves `unity/Temp/shots/<name>.png` (yaw 0 is north, pitch > 0 looks down). `ADV=<s>` runs the sea
  forward first. `EV=auto` leaves exposure to the day-night eye (a fixed `EV=` no longer wins while `DayNight` is in
  the scene, and the eye takes about 10 s of frames to settle).
- `tools/snap.sh <name>` saves the Game view where the camera is now.
- `tools/at-stall.sh`, `whale-shot.sh`, `surf.sh` are the same idea for a stall, the whale and the surf.
- **From WSL, `--save_path` must be a Windows path** (`C:\\Users\\smith\\...`). The capture tool sometimes writes into
  `Assets/Temp/shots/`; the scripts move the file and delete `Assets/Temp` and `Assets/Temp.meta`. Do the same by hand.
- In Play mode the player camera overrides camera placers: place the player with `D.At` instead.
- To inspect a picture, crop and zoom with Python (PIL) rather than viewing the full frame.

## Oracles

An oracle compares the C# with the JS on the same inputs. Pattern:

```sh
node unity/tools/dump-terrain.mjs unity/Temp/oracle/terrain   # run the JS, write its output
unity/tools/run-oracle.sh TerrainOracle                        # run the Editor comparer, print the report
```

- `run-oracle.sh` schedules the comparer with `delayCall` and keeps focusing the Editor until
  `Temp/oracle/report.txt` appears. It sometimes never fires here.
- `run-oracle-bg.sh <Oracle> <dir>` runs the comparer on a worker thread instead, **only for comparers that touch no
  Unity API**. The same idea inline: eval a `Task.Run(() => File.WriteAllText(report, Oracle.Compare(dir)))` and poll
  `Temp/oracle/report.txt`.
- Do not `pkill -f run-oracle.sh` to stop a stuck run.
- Per-system wrappers: `boat-oracle.sh`, `economy-oracle.sh`, `fishing-oracle.sh`, `sky-oracle.sh`, `weather-oracle.sh`,
  `traps-oracle.sh`, `minimap-oracle.sh`, `bindings-oracle.sh`, `soundscape-oracle.sh`.
- Some dumps need a headless GPU (`GPU.init({headless: true})`, run on Dawn). Dumps that import JS from a Windows path
  need `pathToFileURL` (a drive-letter path is not a valid `import()` URL).

## Console and shaders

- `tools/console.sh [N] [log|warning|error]` prints the live console. Old entries stay: compare timestamps, or use
  `unity --json command console --tail 1`.
- **Shader errors only show when the shader is first used**, and a shader that fails to compile just draws nothing. Check
  with `ShaderUtil.GetShaderMessages`, not the console (stale entries). HLSL reserved words such as `line` bite.
- A shader property set from C# but not declared in the shader's `Properties` block is dropped when the material is
  refreshed. Declare every non-array property.
- In the fish shader `discard` must stay inline in the entry function (a helper breaks DXC on dx12).
- After `SceneBuilder.Build()` force a domain reload (edit a script, `recompile --focus`) and render a frame before using
  the scene, or RenderTextures throw `MissingReferenceException`. `Build()` exceeds the 5 s eval limit: run it through
  `delayCall`.

## HDRP and Editor notes

- Sun shadows: 4 cascades (splits 0.05, 0.15, 0.3 of 500 m), 4096 atlas. A caster narrower than a shadow texel (a 4 cm
  rod, about 2.4 cm per texel in cascade 0) aliases and flickers.
- `DayNight` rebuilds its DontSave "Sky Driver" volume on every script reload; toggle `d.enabled = false; d.enabled = true`
  after editing it. It owns the exposure. HDRP `cloudType` is an int (2 = volumetric clouds).
- Volume defaults come from a fresh `VolumeManager.CreateStack()`, not `VolumeManager.instance.stack`.
- The Editor does not advance `Time.frameCount` while unfocused, so per-camera culling and LOD must run on every Game
  camera render, not once per frame.
- IMGUI `GUIStyle`s held in fields reset when a Play session ends (font size 0, black text): rebuild them in a guarded
  `Styles()` method.
- Asset import blocks the Editor for more than 5 s; if an eval times out during an import, retry.
- Character import goes through headless Blender (`tools/characters-to-fbx.py`). Do not drive an open Blender scene through
  the Blender MCP while doing it.
