# Porting Tidewater from the Browser to Unity

Oct 13, 2026 · @Jesse Smith

## Overview

The Unity port is a line-by-line translation of the browser game, checked against the original with numbers rather than by eye. Tidewater began as a JavaScript game on its own three.js/WebGPU-style engine (`src/`). The port lives in `unity/` as a Unity 6 project using the High Definition Render Pipeline (HDRP), and its goal is full fidelity: same island, same sea, same boats, same economy.

Three ideas carry the method. The JavaScript stays the source of truth. Every ported system is proven against it by an automated comparison. A written ledger records what is done and what is not. The sections below cover each, then the places where Unity forced a deviation.

## The JavaScript is the reference

Every system is ported by reading the original JS file first and keeping its structure, names, constants and comments, so a reader can lay the C# beside the JS and match them line for line. Where something cannot be ported 1:1, the reason is written next to it and in the ledger. Nothing is redesigned during a port: a better idea gets noted and done afterwards, in both games.

Several rules follow from that:

- **Names carry over.** `heightAt` becomes `HeightAt`, constants and fields keep their JS names, and a clash such as `TerrainData` (Unity has its own) is resolved by namespace, not renaming.
- **The game logic is the content; Unity owns the presentation.** HDRP does the lighting, shadows, sky and post-processing, so the web engine's own lighting, tonemapping and anti-aliasing passes are not rebuilt. What is ported is generation, geometry, materials and simulation.
- **Features and fixes land in both games.** A bug fix or new feature goes into the JS and the Unity port together, so the reference never falls behind the port.
- **A system is not "ported" until it is proven.** The next section says how.

## Proof by oracle

A system counts as ported only when an automated comparer reports how closely it matches the JS. The repo holds 33 Node dump scripts (`unity/tools/dump-*.mjs`) and 25 Editor comparers (`unity/Assets/Tidewater/Editor/*Oracle.cs`). Each comparison runs in four steps:

1. A Node script runs the real JS system, with fixed seeds and time steps, and writes its output to disk (`Temp/oracle/<system>`).
2. The matching Editor comparer runs the C# or shader version on the same inputs.
3. It compares the two outputs and writes a report with exact-match counts, not just "looks right".
4. `run-oracle.sh` focuses the Editor, runs the comparer and prints the report, so it can be repeated after any change.

How close "matches" must be depends on the kind of data, and each row of the ledger says which grade it reached:

| Grade | Used for | Example result |
| --- | --- | --- |
| Bit-exact | CPU generation done in doubles | Terrain heightfield and 7 other grids, all identical; shore field, all 1M floats |
| Byte-exact | Baked textures | Terrain normal, splat and detail maps (about 34 MB); the lace foam texture |
| Tolerance | GPU compute with float16 storage | Ocean FFT within 0.01 % of rms after 120 frames, all four cascades |
| Statistical or visual | Dynamic GPU fields and looks | Shore sim, foam and sky are rendered and checked by eye, not compared per pixel |

The JS side of an oracle runs headless on Dawn (a WebGPU implementation), so even the shader-based systems have a reference to compare against. Some results are only comparable on one GPU: the per-wave random in the shore waves amplifies tiny `sin()` differences about 40,000 times, so those heights differ between GPUs. The ledger records this caveat next to the numbers.

## Shaders and rendering

The browser game's shaders are written in WGSL, and the port translates them to HLSL line by line. The repo now has 87 shader files under `unity/Assets/Tidewater/Shaders`, covering the ocean, terrain, boats, village, fish, vegetation and UI.

- **Compute shaders stay compute shaders.** The ocean FFT, shore sim, caustics, spray and breakers run as Unity compute kernels with the same buffer layouts as the WGSL.
- **Surfaces plug into HDRP.** A material is an HDRP shader whose surface function runs the ported code and writes albedo, normal and roughness. The terrain shader is modelled on HDRP's own TerrainLit, using its mesh-modification hook.
- **Per-camera work is done per camera.** Anything that picks geometry per view, such as the CDLOD terrain and water clipmap, runs in `RenderPipelineManager.beginCameraRendering` and draws with that camera, never from `Update`.
- **Gaps in HDRP are filled with extra passes.** Where HDRP lacks something the game needs (wave-aware water lighting, the underwater composite, caustic shafts), the port adds a custom pass or shader term, with a note in the file.

The web engine's pitfalls were written down once, so they were not rediscovered in HLSL: derivatives only in uniform control flow, 16-byte array strides, and filterable-format limits. HLSL brought its own, such as reserved words (`line`) that make a shader silently draw nothing, with the error only showing in the console on first use.

## Numerics and determinism

Bit-exact comparison only works if the C# does the same arithmetic in the same order as the JS, so the port copies four habits from it.

- **Doubles throughout.** JS numbers are 64-bit doubles and round to 32 bits only when stored into a `Float32Array` or `Uint8Array`. The port uses `double` for all arithmetic and `float[]` or `byte[]` only for stored grids, and casts to `double` before subtracting two floats.
- **JS integer semantics, spelled out.** `Math.round` rounds halves up (`MathX.Round`), and `Math.imul` and `>>>` become `unchecked` int and uint operations.
- **Same update order.** The per-frame step in \`PlayerHost\` and \`GameHost\` follows \`App.update\` in the JS (its comments name the JS function they mirror), because the oracles replay a seed and time step and a different order changes the result.
- **Mirror only at the render boundary.** The sim keeps the three.js axes (x east, y up, z south, right-handed). Unity is left-handed, so the flip to Unity's frame, (x, y, z) to (x, y, −z), happens only where geometry and positions reach the renderer.

Where float16 or a different GPU reintroduces rounding, the ledger names the loss instead of hiding it, as with the ocean FFT's float16 textures.

## Workflow and tooling

The port is driven from a command line, with a written ledger as its memory.

- **The ledger.** `unity/PORTING.md` lists every system with its JS source, its Unity files and its state: ported, verified, or not started. It also holds the conventions and a Known issues list, so a new session can pick up where the last one stopped.
- **Driving the Editor.** The Unity CLI recompiles scripts and runs C# in the open Editor (`unity command eval`). Eval has a 5-second limit, so long jobs run through `EditorApplication.delayCall` and write a report file, which `run-oracle.sh` reads.
- **Debug helpers instead of clicking.** An Editor class, `GameDebug`, sets the clock, teleports the player, adds money and fish, advances the sea and presses keys, so a scenario can be set up in one command and repeated.
- **Screenshots as evidence.** `shot.sh` places the camera in the game's own coordinates and saves a picture, and Play mode can capture the game view with the HUD. Systems that cannot be compared numerically, such as the sky, foam and shading, are judged against these pictures beside the browser version.
- **Existing tests stay.** The browser game's own `npm test` suite still runs, and new features and fixes carry a JS test plus the matching Unity check.

## Deviations, gaps and lessons

A 1:1 port is the aim, and the ledger keeps a deviations row beside each system that departs from it. The main departures so far:

- **Sky:** HDRP's physically based sky replaces the JS's Hillaire LUT sky, with the sun, moon, stars and the JS eye-adaptation curve driven from the game clock.
- **Water refraction:** HDRP's colour pyramid replaces the web game's separate refraction pass.
- **Audio:** the recordings are Vorbis, not Opus in Ogg, because Unity cannot import Opus; the same recordings were decoded and re-encoded at the same length.
- **UI:** panels are drawn with Unity's immediate-mode GUI rather than the web UI, with the web fonts carried over.

Still to port, per the ledger's last audit: the reef, debris and the wrack line, island wildlife (gulls, terns, pelicans, sanderlings), ambient particles and lens droplets, the start overlay and gamepad UI, and the sun shadow on the terrain and the water.

Three lessons stand out:

1. **Dump scripts outlive the engine.** Because the JS ground truth is engine-neutral, the dump scripts, mesh files, fonts and audio were reused as they were for an Unreal Engine attempt.
2. **Write the deviation down when you make it.** It turns a bug report later ("the sky looks different") into a known, explained row.
3. **Compare numbers where you can, and keep pictures for the rest.** An exact comparison settles generation code for good, but it cannot judge a dynamic GPU field, which needs side-by-side screenshots.
