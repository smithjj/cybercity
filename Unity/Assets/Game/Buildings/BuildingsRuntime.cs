// Buildings runtime: builds all stages into buckets, converts chunk buckets to Unity meshes
// (sim→Unity: (x,y,-z), winding swap 1<->2, tangent z negate — same convention as GroundMeshes),
// spawns point lights (greedy 11m filter), and instanced prop GLBs (F·M·F mirror fix).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NeonKaido
{
    public static class BuildingsRuntime
    {
        /// Must match the _BLPos/_BLColor array size declared in AtmoCommon.hlsl.
        public const int LightArraySize = 384;

        static Transform _root;

        public static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = GameObject.Find("/Buildings");
                    if (go == null) go = new GameObject("Buildings");
                    _root = go.transform;
                }
                return _root;
            }
        }

        // uv-channel mapping for custom vec4 attributes (fixed per matKey)
        static int AttrChannel(string k) => k switch
        {
            "aWin" => 1, "aCol" => 1,
            "aWin2" => 2, "aPbr" => 2,
            "aWin3" => 3,
            _ => 4,
        };

        static Dictionary<string, Material> _ph = new Dictionary<string, Material>();
        static Material PlaceholderMat(string matKey)
        {
            if (_ph.TryGetValue(matKey, out var m)) return m;
            var mat = new Material(Shader.Find("HDRP/Unlit"));
            mat.SetColor("_UnlitColor", matKey switch
            {
                "facade" => new Color(0.42f, 0.4f, 0.45f, 1),
                "detail" => new Color(0.28f, 0.27f, 0.26f, 1),
                "glass" => new Color(0.1f, 0.25f, 0.3f, 1),
                _ => Color.magenta,
            });
            _ph[matKey] = mat;
            return mat;
        }

        static int _builtTris;

        // Glass (and other translucent) quads were merged per 96m map-cell regardless of
        // orientation — a whole block ring of storefront glass became ONE one-sided ribbon
        // (the "ghost train" spanning the road, visible from one side only). Split glass
        // buckets per facade plane: dominant normal axis + quantized plane coordinate.
        static void RefineGlassChunking(Dictionary<string, BBucket> map)
        {
            var finished = new List<string>();
            var ray = map.Keys.ToList();
            foreach (var key in ray)
            {
                if (!key.Contains("|glass")) continue;
                var src = map[key];
                if (src.Idx.Count < 6 * 8) continue; // small buckets stay as-is

                var groups = new Dictionary<string, (BBucket b, List<int> quads)>();
                int nq = src.Idx.Count / 6;
                for (int q = 0; q < nq; q++)
                {
                    int v0 = (int)src.Idx[q * 6];
                    double nx = src.Nrm[v0 * 3], ny = src.Nrm[v0 * 3 + 1], nz = src.Nrm[v0 * 3 + 2];
                    int ax = 0, ax2 = 1;
                    double an = Math.Abs(nx), test = Math.Abs(ny);
                    if (test > an) { an = test; ax = 1; ax2 = 0; }
                    test = Math.Abs(nz); if (test > an) { an = test; ax = 2; ax2 = 0; }
                    double plane = src.Pos[v0 * 3 + ax];
                    string gk = ax + ":" + Math.Round(plane * 4.0) / 4.0;
                    if (!groups.TryGetValue(gk, out var g))
                    {
                        var nb = new BBucket(src.MatKey, src.Defs);
                        g = (nb, new List<int>());
                        groups[gk] = g;
                    }
                    g.quads.Add(q);
                }
                if (groups.Count <= 1) continue;

                finished.Add(key);
                map.Remove(key);
                int gi = 0;
                foreach (var g in groups.Values)
                {
                    var dst = g.b;
                    foreach (var q in g.quads) CopyQuad(src, dst, q * 6);
                    map[key + "#p" + (gi++)] = dst;
                }
            }
            _glassChunks = finished.Count;
        }
        static int _glassChunks;

        static void CopyQuad(BBucket src, BBucket dst, int quadStart)
        {
            // quad layout: 4 verts (i0..i0+3), indices i0,i0+1,i0+2, i0+1,i0+2,i0+3
            int baseIdx = dst.Pos.Count / 3;
            int[] vi = { (int)src.Idx[quadStart], (int)src.Idx[quadStart + 1], (int)src.Idx[quadStart + 2], (int)src.Idx[quadStart + 3] };
            foreach (var vj in vi)
            {
                for (int c = 0; c < 3; c++)
                {
                    dst.Pos.Add(src.Pos[vj * 3 + c]);
                    dst.Nrm.Add(src.Nrm[vj * 3 + c]);
                }
                for (int c = 0; c < 4; c++) dst.Tan.Add(src.Tan[vj * 4 + c]);
                for (int c = 0; c < 2; c++) dst.Uv.Add(src.Uv[vj * 2 + c]);
                foreach (var av in src.Attrs)
                    for (int c = 0; c < 4; c++) dst.Attrs[av.Key].Add(av.Value[vj * 4 + c]);
            }
            dst.Idx.Add(baseIdx); dst.Idx.Add(baseIdx + 1); dst.Idx.Add(baseIdx + 2);
            dst.Idx.Add(baseIdx + 1); dst.Idx.Add(baseIdx + 2); dst.Idx.Add(baseIdx + 3);
        }

        public static string BuildAll(Dictionary<string, Material> mats = null, Material acMat = null)
        {
            // _AgxExposure is published by Atmo.PushGlobals now (it must survive a scene load,
            // which never runs a world build); Atmo owns the value.
            NeonKaido.Atmo.PushGlobals();
            NeonKaido.Atmo.BuildSkyDome();   // camera-tracking sky replaces the JS scene.backgroundNode
            // fresh state each call (DestroyImmediate: eval retries can run this twice in one frame)
            var oldRoot = GameObject.Find("/Buildings");
            if (oldRoot != null) UnityEngine.Object.DestroyImmediate(oldRoot);
            _root = null;
            var root = Root;
            root.name = "Buildings";

            var csLots = BuildPlan.Run();
            var B = new BChunkedBuckets(96);
            var outp = new BOut();
            foreach (var l in csLots) BuildArchitecture.BuildLotArchitecture(l, B, outp);
            // ShopBuilder is what stamps BFront.ShopType, and the facade records copy it into
            // `Shopfront`. Caching before it ran left every facade with Shopfront = false, which
            // silently skipped the ground-floor fascia signs and the crowd's shopfront idlers.
            new ShopBuilder(B, outp).BuildAll(csLots);
            NeonKaido.FacadeAPI.CacheFromLots(csLots); // for signage (billboards) facade records
            Props.BuildProps(B, csLots, outp);
            Alley.BuildAlley(B, outp);
            Overlook.BuildOverlook(B, csLots, outp);
            Cables.BuildCables(B, outp);

            // ---------- materials (editor provides real ones; fallback placeholders) ----------
            bool real = mats != null && acMat != null;

            // ---------- chunk meshes ----------
            int meshes = 0;
            _builtTris = 0;
            RefineGlassChunking(B.Map);
            foreach (var kv in B.Map)
            {
                var b = kv.Value;
                int count = b.Pos.Count / 3;
                if (count == 0) continue;
                var mesh = new Mesh { name = kv.Key };
                var verts = new Vector3[count];
                var nrms = new Vector3[count];
                var tans = new Vector4[count];
                var uvs = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    double x = b.Pos[i * 3], y = b.Pos[i * 3 + 1], z = b.Pos[i * 3 + 2];
                    verts[i] = new Vector3((float)x, (float)y, (float)-z);
                    double nx = b.Nrm[i * 3], ny = b.Nrm[i * 3 + 1], nz = b.Nrm[i * 3 + 2];
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl == 0) nl = 1;
                    nrms[i] = new Vector3((float)(nx / nl), (float)(ny / nl), (float)(-nz / nl));
                    tans[i] = new Vector4((float)b.Tan[i * 4], (float)b.Tan[i * 4 + 1], (float)-b.Tan[i * 4 + 2], (float)b.Tan[i * 4 + 3]);
                    uvs[i] = new Vector2((float)b.Uv[i * 2], (float)b.Uv[i * 2 + 1]);
                }
                // normals come from data (already normalized sim-space); just mirror z (done above)
                mesh.vertices = verts;
                mesh.normals = nrms;
                mesh.tangents = tans;
                mesh.uv = uvs;
                foreach (var akv in b.Attrs)
                {
                    int ch = AttrChannel(akv.Key);
                    var arr = new Vector4[count];
                    for (int i = 0; i < count; i++)
                        arr[i] = new Vector4((float)akv.Value[i * 4], (float)akv.Value[i * 4 + 1], (float)akv.Value[i * 4 + 2], (float)akv.Value[i * 4 + 3]);
                    switch (ch)
                    {
                        case 1: mesh.SetUVs(1, new List<Vector4>(arr)); break;
                        case 2: mesh.SetUVs(2, new List<Vector4>(arr)); break;
                        case 3: mesh.SetUVs(3, new List<Vector4>(arr)); break;
                        case 4: mesh.SetUVs(5, new List<Vector4>(arr)); break;
                    }
                }
                var idx = new int[b.Idx.Count];
                for (int i = 0; i < b.Idx.Count; i += 3)
                {
                    idx[i] = (int)b.Idx[i];
                    idx[i + 1] = (int)b.Idx[i + 2];  // mirror: swap 1<->2
                    idx[i + 2] = (int)b.Idx[i + 1];
                }
                if (count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.triangles = idx;
                mesh.RecalculateBounds();
                _builtTris += idx.Length / 3;

                var go = new GameObject(kv.Key);
                go.transform.SetParent(root, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = real && mats.TryGetValue(b.MatKey, out var m) ? m : PlaceholderMat(b.MatKey);
                if (b.MatKey == "glass") mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshes++;
            }

            // ---------- instanced props (F·M·F mirror fix) ----------
            SpawnInstances(PropsAc(), outp.AcM, "inst_ac", real ? acMat : PlaceholderMat("detail"));
            SpawnInstances(PropsTank(), outp.TankM, "inst_tank", real ? acMat : PlaceholderMat("detail"));
            SpawnInstances(PropsPtank(), outp.PtankM, "inst_ptank", real ? acMat : PlaceholderMat("detail"));

            // ---------- lights: greedy 11m filter ----------
            var cand = new List<BLight>();
            foreach (var l in outp.Lights) if (!l.Minor) cand.Add(l);
            // stable sort (JS Array.sort is stable; List.Sort is NOT — use OrderByDescending)
            cand = cand.OrderByDescending(l => l.Intensity).ToList();
            var kept = new List<BLight>();
            foreach (var l in cand)
            {
                bool clash = false;
                foreach (var k in kept)
                {
                    double dx = k.Pos[0] - l.Pos[0], dy = k.Pos[1] - l.Pos[1], dz = k.Pos[2] - l.Pos[2];
                    if (dx * dx + dy * dy + dz * dz < 121) { clash = true; break; }
                }
                if (!clash) kept.Add(l);
            }
            int lights = 0;
            var blPos = new Vector4[kept.Count];
            var blCol = new Vector4[kept.Count];
            foreach (var l in kept)
            {
                var go = new GameObject($"pl_{lights}");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3((float)l.Pos[0], (float)l.Pos[1], (float)-l.Pos[2]);
                var pl = go.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = new Color((float)l.Color[0], (float)l.Color[1], (float)l.Color[2], 1);
                pl.range = (float)l.Distance;
                pl.intensity = (float)l.Intensity;
                pl.shadows = LightShadows.None;
                blPos[lights] = new Vector4((float)l.Pos[0], (float)l.Pos[1], (float)-l.Pos[2], (float)l.Distance);
                blCol[lights] = new Vector4((float)l.Color[0], (float)l.Color[1], (float)l.Color[2], (float)l.Intensity);
                lights++;
            }
            // push light arrays into the building materials (only real materials know _BLPos)
            PushLightArrays(blPos, blCol, mats != null ? new List<Material>(mats.Values) : new List<Material>(), acMat);

            // physical colliders (BCollider records — matches the Rapier boxes in JS)
            var colRoot = new GameObject("Colliders");
            colRoot.transform.SetParent(root, false);
            colRoot.isStatic = true;
            foreach (var bc in outp.Colliders)
            {
                var go = new GameObject("col");
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3((float)bc.X, (float)bc.Y, (float)-bc.Z);
                if (bc.Rot != null)
                {
                    // bc.Rot is authored in sim space. Mirroring z (sim z -> -z) maps a rotation of
                    // theta about axis a to one of -theta about (a.x, a.y, -a.z), which in quaternion
                    // terms is (x,y,z,w) -> (-x,-y,z,w). Negating z instead (what this did) is not the
                    // same map: it happens to agree for a pure X or Y rotation but flips the sign of a
                    // Z one, so the two alley stair ramps sloped up toward +x while the stairs climb
                    // toward -x. With the ramps inverted the player walked off the bottom of the flight
                    // straight through the steps, which have no colliders of their own.
                    go.transform.rotation = new Quaternion((float)-bc.Rot[0], (float)-bc.Rot[1], (float)bc.Rot[2], (float)bc.Rot[3]);
                }
                else if (bc.RotY != 0) go.transform.rotation = Quaternion.Euler(0, (float)bc.RotY * Mathf.Rad2Deg, 0);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3((float)(bc.Hx * 2), (float)(bc.Hy * 2), (float)(bc.Hz * 2));
            }

            return $"buildings: {meshes} meshes {_builtTris} tris, {kept.Count}/{outp.Lights.Count} lights, {outp.AcM.Count}/{outp.TankM.Count}/{outp.PtankM.Count} inst, {outp.Colliders.Count} colliders";
        }

        static Mesh PropsAc() { return LoadGlbMesh("ac_unit"); }
        static Mesh PropsTank() { return LoadGlbMesh("water_tank"); }
        static Mesh PropsPtank() { return LoadGlbMesh("panel_tank"); }

        static Mesh LoadGlbMesh(string name)
        {
            // pre-dumped raw buffers (node): dump/<name>_pos.bin f32 xyz, _idx.bin u16/u32, _meta.txt "<verts> <idx> <bits>"
            string dir = "Assets/Game/Models/Buildings/dump";
            var meta = System.IO.File.ReadAllText($"{dir}/{name}_meta.txt").Trim().Split(' ');
            int nVerts = int.Parse(meta[0]), nIdx = int.Parse(meta[1]);
            bool u32 = meta[2] == "32";
            var posBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_pos.bin");
            var idxBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_idx.bin");
            var verts = new Vector3[nVerts];
            for (int i = 0; i < nVerts; i++)
            {
                float x = BitConverter.ToSingle(posBytes, i * 12);
                float y = BitConverter.ToSingle(posBytes, i * 12 + 4);
                float z = BitConverter.ToSingle(posBytes, i * 12 + 8);
                verts[i] = new Vector3(x, y, -z); // glTF -z forward ≈ sim z: mirror to Unity
            }
            var idx = new int[nIdx];
            for (int i = 0; i < nIdx; i++) idx[i] = u32 ? (int)BitConverter.ToUInt32(idxBytes, i * 4) : BitConverter.ToUInt16(idxBytes, i * 2);
            var m = new Mesh { name = name };
            m.vertices = verts;
            m.triangles = idx;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// Every root that owns point lights the fake city lighting accumulates. Shared so the
        /// runtime (PlayerController.Start) and the Editor (NkEditorLighting) rank the same set.
        public static readonly string[] SceneLightRoots =
        {
            "/Buildings", "/StreetLamps", "/Signage", "/FacadeSigns",
            "/LandmarksRiver", "/LandmarksRail", "/LandmarksExpress", "/LandmarksBridge",
        };

        /// Rank every light in the city and upload the best `LightArraySize` to every NEON KAIDO
        /// material. The arrays are not serialized, so this has to run again at runtime AND in the
        /// Editor: opening Assets/Main.unity without it shows _BLCount = 0 — a city whose lamps are
        /// visibly lit but cast nothing, because NkBPointLights() breaks out of its loop immediately.
        /// Returns a description for the debug CLI.
        ///
        /// Ranking is by camera distance, not raw brightness: JS lights.js keeps the nearest
        /// `importance*intensity/(1+d²*0.004)` lights pooled every 20 frames. A global brightness
        /// sort instead fills the 384 slots with the avenue's brightest signs and starves any
        /// side street: from the alley the closest shop lanterns (intensity 2-6, range 6) lost to
        /// distant 160-intensity billboard casts, so the walls got no local light at all.
        public static string PushSceneLights(Vector3? camPos = null)
        {
            var lights = new List<Light>();
            foreach (var rn in SceneLightRoots)
            {
                var root = GameObject.Find(rn);
                if (root != null) lights.AddRange(root.GetComponentsInChildren<Light>(true));
            }
            if (lights.Count == 0) return "0 lights (world not built?)";
            int found = lights.Count;
            if (lights.Count > LightArraySize)
            {
                var cam = camPos ?? (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
                var scored = new List<(float s, Light l)>(lights.Count);
                foreach (var l in lights)
                {
                    float d2 = (l.transform.position - cam).sqrMagnitude;
                    float lum = l.color.r + l.color.g + l.color.b;
                    scored.Add((lum * l.intensity * l.range / (1f + d2 * 0.004f), l));
                }
                scored.Sort((a, b) => b.s.CompareTo(a.s));
                lights.Clear();
                for (int i = 0; i < LightArraySize; i++) lights.Add(scored[i].l);
            }
            var pos = new Vector4[lights.Count];
            var col = new Vector4[lights.Count];
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                pos[i] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                col[i] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity);
            }
            PushLightArrays(pos, col, new List<Material>(), null);
            return $"{lights.Count}/{found} lights";
        }

        // SetVectorArray data is NOT serialized in .mat assets — re-push after every reload.
        // Pushes to the given materials PLUS every scene material using a NEON KAIDO shader.
        public static void PushLightArrays(Vector4[] blPos, Vector4[] blCol, List<Material> extra, Material acMat)
        {
            if (blPos == null || blPos.Length == 0) return;
            // The shader declares _BLPos/_BLColor[384] and Unity can never grow a material's
            // SetVectorArray slot: a shorter push is remembered as the array size and every later
            // longer push is capped ("Property (_BLPos) exceeds previous array size (384 vs 92)").
            // StreetLamps pushes its own tiny list during the world build, which used to freeze
            // every material at 92 lights, so the real 384-light push was truncated and most lamps
            // (and cars) cast no light at all. Always upload the full 384, zero-padded, and report
            // the real count so the shader loop still stops early.
            int count = Mathf.Min(blPos.Length, LightArraySize);
            var pos = new Vector4[LightArraySize];
            var col = new Vector4[LightArraySize];
            System.Array.Copy(blPos, pos, count);
            System.Array.Copy(blCol, col, count);
            var done = new HashSet<Material>();
            void Push(Material m)
            {
                if (m == null || !done.Add(m)) return;
                m.SetVectorArray("_BLPos", pos);
                m.SetVectorArray("_BLColor", col);
                m.SetFloat("_BLCount", count);
            }
            foreach (var m in extra) Push(m);
            Push(acMat);
            foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                var sm = mr.sharedMaterial;
                if (sm == null || sm.shader == null) continue;
                if (sm.shader.name.StartsWith("NEON KAIDO/")) Push(sm);
            }
        }

        static void SpawnInstances(Mesh mesh, List<double[]> matrices, string name, Material mat)
        {
            if (mesh == null || matrices == null || matrices.Count == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            foreach (var e in matrices)
            {
                // F·M·F: negate column-2 z and translation z (column-major elements)
                var child = new GameObject("i");
                child.transform.SetParent(go.transform, false);
                child.transform.localPosition = new Vector3((float)e[12], (float)e[13], (float)-e[14]);
                // extract basis after mirror: F·R·F
                var c0 = new Vector3((float)e[0], (float)e[1], (float)-e[2]);
                var c1 = new Vector3((float)e[4], (float)e[5], (float)-e[6]);
                var c2 = new Vector3((float)e[8], (float)e[9], (float)-e[10]);
                var scale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
                var rot = Quaternion.LookRotation(c1.normalized, Vector3.Cross(c2.normalized, c1.normalized));
                child.transform.localRotation = rot;
                child.transform.localScale = scale;
                var mf = child.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
            }
        }
    }
}
