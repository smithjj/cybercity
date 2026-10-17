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

        public static string BuildAll(Dictionary<string, Material> mats = null, Material acMat = null)
        {
            Shader.SetGlobalFloat("_AgxExposure", 0.82f); // JS PostFX exposure
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
            NeonKaido.FacadeAPI.CacheFromLots(csLots); // for signage (billboards) facade records
            new ShopBuilder(B, outp).BuildAll(csLots);
            Props.BuildProps(B, csLots, outp);
            Alley.BuildAlley(B, outp);
            Overlook.BuildOverlook(B, csLots, outp);
            Cables.BuildCables(B, outp);

            // ---------- materials (editor provides real ones; fallback placeholders) ----------
            bool real = mats != null && acMat != null;

            // ---------- chunk meshes ----------
            int meshes = 0;
            _builtTris = 0;
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

        // SetVectorArray data is NOT serialized in .mat assets — re-push after every reload.
        // Pushes to the given materials PLUS every scene material using a NEON KAIDO shader.
        public static void PushLightArrays(Vector4[] blPos, Vector4[] blCol, List<Material> extra, Material acMat)
        {
            if (blPos == null || blPos.Length == 0) return;
            var done = new HashSet<Material>();
            void Push(Material m)
            {
                if (m == null || !done.Add(m)) return;
                m.SetVectorArray("_BLPos", blPos);
                m.SetVectorArray("_BLColor", blCol);
                m.SetFloat("_BLCount", blPos.Length);
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
