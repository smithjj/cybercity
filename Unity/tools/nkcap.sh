#!/bin/sh
# nkcap.sh <name> — capture the game camera to Unity/Temp/nk/shots/<name>.png (no Editor round-trip).
#
# Deliberately NOT using --save_path: that always lands the PNG in Assets/Temp/, and any write under
# Assets/ triggers an import. The import reimports the dirty .mat assets and wipes the runtime-set
# light arrays, so the city lost its point lights a second into every capture session — and
# NkLightArrayGuard then re-pushed them, racing the capture. The inline base64 result never touches
# the asset database.
U=/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe
cd "$(cd "$(dirname "$0")/.." && pwd)" || exit 1
NAME="$1"
mkdir -p Temp/nk/shots
"$U" --no-banner command capture_game_view --source camera \
  --width "${W:-1920}" --height "${H:-1080}" --result-only --json 2>/dev/null \
| OUT="Temp/nk/shots/$NAME.png" python3 -c '
import base64, json, os, sys
try:
    d = json.load(sys.stdin)
    raw = base64.b64decode(d["base64"])
except Exception as e:
    sys.exit("capture failed: %s" % e)
open(os.environ["OUT"], "wb").write(raw)
print("%sx%s  %s bytes -> %s" % (d["width"], d["height"], d["bytes"], os.environ["OUT"]))
'
ls -la "Temp/nk/shots/$NAME.png"
