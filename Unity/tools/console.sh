#!/bin/sh
# console.sh [N] [log|warning|error] — print the live Editor console.
# Old entries persist: compare timestamps, or pass --json to see them.
U="C:/Users/smith/AppData/Local/Unity/bin/unity.exe"
N="${1:-20}"; L="${2:-warning}"
"$U" --no-banner command console --tail "$N" --level "$L"
