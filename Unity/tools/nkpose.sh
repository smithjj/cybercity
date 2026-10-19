#!/bin/sh
# nkpose.sh <unityX> <unityZ> <yawRad> <pitchRad> [fov] — pose the play-mode camera and FREEZE the session:
# the PlayerController is left disabled (nothing re-pushes the light arrays / advances weather or
# traffic) and /VehiclesRoot /Crowd /Gameplay are hidden so no car headlight can be mistaken for a
# lamp pool. Re-pose with nkpose.sh, thaw with `nk-lights.sh reset`.
#   Coordinates are UNITY space, not sim: sim (x, y, z) -> unity (x, y, -z), so pass -simZ here.
#   yaw 0 = face north (+Z, down the avenue); positive pitch = look down.
# NOTE: float literals need the f suffix — a bare `-0.38*Mathf.Rad2Deg` makes the whole eval
# snippet fail to compile with a confusing "Unreachable code detected" diagnostic.
U=/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe
cd "$(cd "$(dirname "$0")/.." && pwd)" || exit 1
X="$1"; Z="$2"; YAW="$3"; PITCH="$4"; FOV="${5:-70}"
"$U" --no-banner command eval "
UnityEngine.Time.timeScale=0f;
var pc=UnityEngine.Object.FindFirstObjectByType<NeonKaido.PlayerController>();
if(pc==null) return \"no player\";
pc.Teleport(${X}f, 2f, ${Z}f);
pc.enabled=false;
var cam=Camera.main;
cam.fieldOfView=${FOV}f;
cam.transform.position=pc.transform.position;
cam.transform.rotation=Quaternion.Euler(${PITCH}f*Mathf.Rad2Deg, ${YAW}f*Mathf.Rad2Deg, 0f);
foreach(var hn in new string[]{\"/VehiclesRoot\",\"/Crowd\",\"/Gameplay\"}){ var hg=GameObject.Find(hn); if(hg!=null) hg.SetActive(false); }
UnityEngine.Shader.SetGlobalFloat(\"_CarLCount\",0f);
return \"posed cam=\"+cam.transform.position+\" euler=\"+cam.transform.eulerAngles.ToString(\"F1\")+\" fov=\"+cam.fieldOfView;" 2>&1 | tail -1
