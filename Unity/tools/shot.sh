#!/bin/sh
# shot.sh <name> <simX> <simZ> <eyeH> <yawDeg> <pitchDeg> [w] [h]
# Places the Main Camera in sim coordinates (yaw 0 = north/-Z, pitch>0 looks down)
# and saves unity/Temp/shots/<name>.png.
#   ADV=<s>  advance the sim first (chunked, see adv.sh)
#   FOV=<n>  camera fov (default 55; SHOT_VIEWS use 55, aerial 50, ground 60)
#   SCREEN=1 capture the Game view incl. HUD overlays instead of the camera render
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
cd "$ROOT"
NAME="$1"; X="$2"; Z="$3"; H="$4"; YAW="$5"; PITCH="$6"; W="${7:-1280}"; HGT="${8:-720}"
FOV="${FOV:-55}"
SRC="camera"; [ -n "${SCREEN:-}" ] && SRC="screen"
[ -n "${ADV:-}" ] && "$ROOT/unity/tools/adv.sh" "$ADV"
# preflight: refuse to capture a broken pipeline
HP=$("$ROOT/unity/tools/preflight.sh" 2>&1)
echo "$HP" | head -3
if echo "$HP" | grep -q "UNHEALTHY"; then echo "PREFLIGHT FAILED — capture skipped"; exit 1; fi
"$U" --no-banner command eval "NeonKaido.EditorTools.GameDebug.PoseCam($X, $Z, $H, $YAW, $PITCH, $FOV); return \"cam ok\";" | head -3
mkdir -p unity/Temp/shots
OUT="$ROOT/unity/Temp/shots/$NAME.png"
"$U" --no-banner command capture_game_view --source "$SRC" --width "$W" --height "$HGT" --save_path "$OUT" 2>&1 | tail -2
# capture sometimes lands in Assets/Temp/shots — move it back
if [ -f "unity/Assets/Temp/shots/$NAME.png" ]; then
  mv "unity/Assets/Temp/shots/$NAME.png" "unity/Temp/shots/$NAME.png"
  rm -rf unity/Assets/Temp unity/Assets/Temp.meta
fi
ls -la "unity/Temp/shots/$NAME.png" 2>&1
