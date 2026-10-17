#!/bin/sh
# run-oracle-bg.sh <OracleClass> [arg] — run the comparer on a worker thread (Task.Run).
# ONLY for comparers that touch no Unity API (they run off the main thread).
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
cd "$ROOT"
CLS="$1"; ARG="${2:-}"
REPORT="unity/Temp/oracle/report.txt"
SNIP="unity/Temp/oracle/_run_bg.cs"
mkdir -p unity/Temp/oracle
rm -f "$REPORT"
cat > "$SNIP" <<SNIP
System.Threading.Tasks.Task.Run(() =>
{
  try { NeonKaido.EditorTools.Oracles.$CLS.Compare("$ARG"); }
  catch (System.Exception e) { System.IO.File.WriteAllText("Temp/oracle/report.txt", e.ToString()); }
});
return "queued(bg) $CLS";
SNIP
"$U" --no-banner command eval "$(cat "$SNIP")" | head -3
i=0
while [ "$i" -lt 120 ] && [ ! -f "$REPORT" ]; do
  i=$((i + 1)); sleep 2
done
if [ -f "$REPORT" ]; then cat "$REPORT"; else echo "ORACLE TIMEOUT (bg): no report after $((i * 2))s"; exit 1; fi
