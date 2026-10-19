#!/bin/sh
# nk-lights.sh <mode> — debug lever for the per-material point-light arrays.
#   all    : rank every scene light (Buildings+StreetLamps+Signage+FacadeSigns+Landmarks*), keep 384
#   nolamp : same ranking, but every /StreetLamps light is zeroed (order and count unchanged)
#   lamps  : ONLY the /StreetLamps lights are uploaded (nothing else contributes)
# Also freezes time (stops traffic + weather drift) and hides /Vehicles so a car headlight
# cannot be mistaken for a lamp pool. Leaves the session frozen: reset with `nk-lights.sh reset`.
U=/mnt/c/Users/smith/AppData/Local/Unity/bin/unity.exe
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1
MODE="$1"

PREAMBLE='
UnityEngine.Time.timeScale=0f;
UnityEngine.Shader.SetGlobalFloat("_CarLCount",0f);
foreach(var hn in new string[]{"/VehiclesRoot","/Crowd","/Gameplay"}){ var hg=GameObject.Find(hn); if(hg!=null) hg.SetActive(false); }
var slRoot=GameObject.Find("/StreetLamps");
var lampSet=new System.Collections.Generic.HashSet<Light>();
if(slRoot!=null) foreach(var l in slRoot.GetComponentsInChildren<Light>(true)) lampSet.Add(l);
var roots=new string[]{"/Buildings","/StreetLamps","/Signage","/FacadeSigns","/LandmarksRiver","/LandmarksRail","/LandmarksExpress","/LandmarksBridge"};
var L=new System.Collections.Generic.List<Light>();
foreach(var rn in roots){var r=GameObject.Find(rn); if(r!=null) L.AddRange(r.GetComponentsInChildren<Light>(true));}
'

case "$MODE" in
  reset)
    CODE='UnityEngine.Time.timeScale=1f; foreach(var hn in new string[]{"/VehiclesRoot","/Crowd","/Gameplay"}){ var hg=GameObject.Find(hn); if(hg!=null) hg.SetActive(true); } var pc=UnityEngine.Object.FindFirstObjectByType<NeonKaido.PlayerController>(); if(pc!=null) pc.enabled=true; return "reset";'
    ;;
  all)
    CODE="$PREAMBLE"'
L.Sort((a,b)=>((b.color.r+b.color.g+b.color.b)*b.intensity*b.range).CompareTo((a.color.r+a.color.g+a.color.b)*a.intensity*a.range));
if(L.Count>384) L.RemoveRange(384,L.Count-384);
int nl=0; var pos=new Vector4[L.Count]; var col=new Vector4[L.Count];
for(int i=0;i<L.Count;i++){pos[i]=new Vector4(L[i].transform.position.x,L[i].transform.position.y,L[i].transform.position.z,L[i].range); col[i]=new Vector4(L[i].color.r,L[i].color.g,L[i].color.b,L[i].intensity); if(lampSet.Contains(L[i])) nl++;}
NeonKaido.BuildingsRuntime.PushLightArrays(pos,col,new System.Collections.Generic.List<Material>(),null);
return "all n="+L.Count+" lampsIn="+nl;'
    ;;
  nolamp)
    CODE="$PREAMBLE"'
L.Sort((a,b)=>((b.color.r+b.color.g+b.color.b)*b.intensity*b.range).CompareTo((a.color.r+a.color.g+a.color.b)*a.intensity*a.range));
if(L.Count>384) L.RemoveRange(384,L.Count-384);
int nl=0; var pos=new Vector4[L.Count]; var col=new Vector4[L.Count];
for(int i=0;i<L.Count;i++){bool isL=lampSet.Contains(L[i]); if(isL) nl++; pos[i]=new Vector4(L[i].transform.position.x,L[i].transform.position.y,L[i].transform.position.z,L[i].range); col[i]=new Vector4(L[i].color.r,L[i].color.g,L[i].color.b,isL?0f:L[i].intensity);}
NeonKaido.BuildingsRuntime.PushLightArrays(pos,col,new System.Collections.Generic.List<Material>(),null);
return "nolamp n="+L.Count+" lampsZeroed="+nl;'
    ;;
  lamps)
    CODE="$PREAMBLE"'
var lam=new System.Collections.Generic.List<Light>(lampSet);
if(lam.Count>384) lam.RemoveRange(384,lam.Count-384);
var pos=new Vector4[lam.Count]; var col=new Vector4[lam.Count];
for(int i=0;i<lam.Count;i++){pos[i]=new Vector4(lam[i].transform.position.x,lam[i].transform.position.y,lam[i].transform.position.z,lam[i].range); col[i]=new Vector4(lam[i].color.r,lam[i].color.g,lam[i].color.b,lam[i].intensity);}
NeonKaido.BuildingsRuntime.PushLightArrays(pos,col,new System.Collections.Generic.List<Material>(),null);
return "lampsOnly n="+lam.Count;'
    ;;
  *) echo "usage: nk-lights.sh all|nolamp|lamps|reset"; exit 2;;
esac

"$U" --no-banner command eval "$CODE" 2>&1 | tail -1
