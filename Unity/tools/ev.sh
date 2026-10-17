#!/bin/sh
# ev.sh '<C#>' — run C# on the Editor's main thread, print the result.
#   D.X(...) expands to NeonKaido.EditorTools.GameDebug.X(...).
#   Do not name a local variable D (collides with the expansion).
#   W=6000 raises the per-line width (default 700).
#   No output line => the snippet did not compile (check: tools/console.sh 10 error).
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
W="${W:-700}"
cd "$(dirname "$0")/.."   # discovery is cwd-sensitive: run from the unity/ project dir
CODE="$(printf '%s' "$*" | sed -E 's/\bD\./NeonKaido.EditorTools.GameDebug./g')"
"$U" --no-banner command eval "$CODE" 2>&1 | sed -n '1,40p' | cut -c1-"$W"
