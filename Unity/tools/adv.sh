#!/bin/sh
# adv.sh <totalSeconds> — advance the sim in 1.2s chunks (eval budget is ~5s).
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
cd "$ROOT"
REMAIN="$1"; REMAIN="${REMAIN/./}"  # tenths
TOTAL=$((REMAIN * 10))
DONE=0
while [ "$DONE" -lt "$TOTAL" ]; do
  STEP=1.2
  [ $((TOTAL - DONE)) -lt 12 ] && STEP="$(awk "BEGIN{printf \"%.1f\", ($((TOTAL - DONE)) / 10)}")"
  "$U" --no-banner command eval "NeonKaido.EditorTools.GameDebug.Advance($STEP); return 'adv $STEP';" | head -1
  DONE=$((DONE + 12))
  [ $((TOTAL - DONE)) -lt 0 ] && DONE=$TOTAL
done
echo "advanced $REMAIN s"
