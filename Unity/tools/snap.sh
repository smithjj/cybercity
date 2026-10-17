#!/bin/sh
# snap.sh <name> — save the Game view exactly where the camera is now (no re-posing).
#   SCREEN=1 includes HUD overlays; default is the camera render.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
cd "$ROOT"
NAME="$1"; SRC="${SCREEN:-camera}"
mkdir -p unity/Temp/shots
OUT="$ROOT/unity/Temp/shots/$NAME.png"
"$U" --no-banner command capture_game_view --source "$SRC" --width 1280 --height 720 --save_path "$OUT" 2>&1 | tail -2
if [ -f "unity/Assets/Temp/shots/$NAME.png" ]; then
  mv "unity/Assets/Temp/shots/$NAME.png" "unity/Temp/shots/$NAME.png"
  rm -rf unity/Assets/Temp unity/Assets/Temp.meta
fi
ls -la "unity/Temp/shots/$NAME.png" 2>&1
