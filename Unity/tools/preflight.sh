#!/bin/sh
# preflight.sh — verify the pipeline is healthy BEFORE capturing/screenshots.
# Prints D.Health() and exits nonzero when unhealthy (console errors since last
# ClearErrors, shader messages, camera without HDAdditionalCameraData, missing scene
# objects). shot.sh runs this automatically.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
OUT=$("$ROOT/unity/tools/ev.sh" 'return D.Health();' 2>&1 | sed 's/\\r\\n/\n/g; s/\\n/\n/g')
echo "$OUT"
echo "$OUT" | grep -q "UNHEALTHY" && exit 1
exit 0
