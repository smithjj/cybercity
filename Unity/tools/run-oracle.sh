#!/bin/sh
# run-oracle.sh <OracleClass> [arg] — run an Editor comparer via delayCall and keep
# the Editor focused (recompile --focus is the focus trick) until
# unity/Temp/oracle/report.txt appears, then print it.
# Comparer entry point: NeonKaido.EditorTools.Oracles.<Class>.Compare(string arg)
# It sometimes never fires — then use run-oracle-bg.sh if the comparer touches no Unity API.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
cd "$ROOT"
CLS="$1"; ARG="${2:-}"
REPORT="unity/Temp/oracle/report.txt"
SNIP="unity/Temp/oracle/_run.cs"
mkdir -p unity/Temp/oracle
rm -f "$REPORT"
cat > "$SNIP" <<SNIP
System.IO.Directory.CreateDirectory("Temp/oracle");
UnityEditor.EditorApplication.delayCall += () =>
{
  try { NeonKaido.EditorTools.Oracles.$CLS.Compare("$ARG"); }
  catch (System.Exception e) { System.IO.File.WriteAllText("Temp/oracle/report.txt", e.ToString()); }
};
return "queued $CLS";
SNIP
"$U" --no-banner command eval "$(cat "$SNIP")" | head -3
i=0
while [ "$i" -lt 40 ] && [ ! -f "$REPORT" ]; do
  i=$((i + 1)); sleep 2
  "$U" --no-banner recompile --focus >/dev/null 2>&1 || true
done
if [ -f "$REPORT" ]; then cat "$REPORT"; else echo "ORACLE TIMEOUT: no report after $((i * 2))s (do not pkill this script; inspect Temp/oracle/)"; exit 1; fi
