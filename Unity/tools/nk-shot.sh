#!/bin/sh
# nk-shot.sh <name> [simX simZ yaw pitch fov] — pose the play-mode player camera and capture a PNG.
# Writes Unity/Temp/nk/shots/<name>.png (the capture command always lands in Assets/Temp/... first).
U=/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1
NAME="$1"; X="${2:-4.9}"; Z="${3:--40}"; YAW="${4:-0}"; PITCH="${5:-0.02}"; FOV="${6:-55}"
mkdir -p Temp/nk/shots
"$U" --no-banner command eval "var pc = UnityEngine.Object.FindFirstObjectByType<NeonKaido.PlayerController>(); if (pc == null) { NeonKaido.EditorTools.GameDebug.Ensure(); pc = UnityEngine.Object.FindFirstObjectByType<NeonKaido.PlayerController>(); } if (pc == null) return \"no player\"; pc.Teleport($X, 2f, $Z); pc.Yaw = $YAW; pc.Pitch = $PITCH; if (Camera.main) Camera.main.fieldOfView = $FOV; UnityEngine.Application.runInBackground = true; for (int i = 0; i < 40; i++) UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); return \"posed \" + pc.transform.position + \" yaw=\" + pc.Yaw;" 2>&1 | tail -1
sleep 3
"$U" --no-banner command capture_game_view --source camera --width "${W:-1920}" --height "${H:-1080}" --save_path "C:\\Users\\smith\\OneDrive\\Documents\\GitHub\\cybercity\\Unity\\Temp\\nk\\shots\\$NAME.png" 2>&1 | tail -1
if [ -f "Assets/Temp/nk/shots/$NAME.png" ]; then
  cp "Assets/Temp/nk/shots/$NAME.png" "Temp/nk/shots/$NAME.png"
  rm -rf Assets/Temp Assets/Temp.meta
fi
ls -la "Temp/nk/shots/$NAME.png"
