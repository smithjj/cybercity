// Port of signage/street.js traffic + pedestrian signals + holo text ring.
// Signal lens mesh with per-vertex (phase, lamp); state via 4 globals updated per frame.
// CYCLE = 22s: car 0-8 green, 8-10.5 yellow, else red; ped 0-6 walk, 6-8 blink, else stop; phase 1 = +11s offset.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class TrafficSignals
    {
        const float CYCLE = 22f;

        public static List<Light> SigLights = new List<Light>();
        public static List<int> SigPhases = new List<int>();

        // 0 green, 1 yellow, 2 red
        public static float CarState(int phase, float t)
        {
            float c = Mathf.Repeat(t + 3f, CYCLE);
            float local = phase == 0 ? c : Mathf.Repeat(c + CYCLE / 2f, CYCLE);
            if (local < 8) return 0; if (local < 10.5f) return 1; return 2;
        }

        // 0 walk, 1 blink, 2 stop
        public static float PedState(int phase, float t)
        {
            float c = Mathf.Repeat(t + 3f, CYCLE);
            float local = phase == 0 ? c : Mathf.Repeat(c + CYCLE / 2f, CYCLE);
            if (local < 6) return 0;
            if (local < 8) return Mathf.FloorToInt(local * 4f) % 2 == 1 ? 1 : 0;
            return 2;
        }

        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        public static string Build(Material housing, Material signal)
        {
            var old = GameObject.Find("/Signals");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Signals").transform;

            float avR = (float)Layout.AvenueRoadHalfWidth;
            float csR = (float)Layout.CrossRoadHalfWidth;
            float ex = avR + 0.6f, ez = csR + 0.6f;
            float sideY = (float)Layout.AvenueCurbHeight;

            var SV = new List<Vector3>(); var SI = new List<int>(); var SUV = new List<Vector2>(); var SS = new List<Vector2>();
            var HV = new List<Vector3>(); var HI = new List<int>(); var HC = new List<Color>(); var HMR = new List<Vector4>();

            // oriented box in sim space (mirrors at add time)
            void Box(List<Vector3> vv, List<int> ii, List<Color> cc, List<Vector4> mr, Vector3 c, Vector3 r, Vector3 u, Vector3 n, float exx, float ey, float ezz, Color col, Vector2 mrv)
            {
                var p = new Vector3[8];
                int i = 0;
                for (int sz = -1; sz <= 1; sz += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sx = -1; sx <= 1; sx += 2)
                            p[i++] = c + r * (sx * exx) + u * (sy * ey) + n * (sz * ezz);
                int[][] quads = {
                    new[] { 1, 3, 7, 5 }, new[] { 4, 6, 2, 0 }, new[] { 2, 6, 7, 3 },
                    new[] { 0, 1, 5, 4 }, new[] { 5, 7, 6, 4 }, new[] { 0, 2, 3, 1 }
                };
                int b = vv.Count;
                foreach (var q in quads)
                {
                    foreach (int vi in q) { vv.Add(Mir(p[vi])); cc.Add(col); mr.Add(new Vector4(mrv.x, mrv.y, 0, 0)); }
                    ii.Add(b); ii.Add(b + 1); ii.Add(b + 2); ii.Add(b); ii.Add(b + 2); ii.Add(b + 3);
                    b += 4;
                }
            }

            Mesh Load(string name)
            {
                string dir = "Assets/Game/Models/Signage/dump";
                var meta = System.IO.File.ReadAllText($"{dir}/{name}_meta.txt").Trim().Split(' ');
                int nVerts = int.Parse(meta[0]), nIdx = int.Parse(meta[1]);
                bool u32 = meta[2] == "32";
                var posBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_pos.bin");
                var idxBytes = System.IO.File.ReadAllBytes($"{dir}/{name}_idx.bin");
                var verts = new Vector3[nVerts];
                for (int i = 0; i < nVerts; i++)
                    verts[i] = new Vector3(System.BitConverter.ToSingle(posBytes, i * 12), System.BitConverter.ToSingle(posBytes, i * 12 + 4), -System.BitConverter.ToSingle(posBytes, i * 12 + 8));
                var idx = new int[nIdx];
                for (int i = 0; i < nIdx; i++) idx[i] = u32 ? (int)System.BitConverter.ToUInt32(idxBytes, i * 4) : System.BitConverter.ToUInt16(idxBytes, i * 2);
                var m = new Mesh { name = name };
                m.vertices = verts;
                m.triangles = idx;
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }

            // lens vertices with sig attr; uv mapped into an atlas rect via local bbox
            void AddLens(Mesh g, Vector3 pos, float yawDeg, float phase, float lamp, Vector4? rect)
            {
                var mv = g.vertices;
                var bb = g.bounds;
                int b = SV.Count;
                var rot = Quaternion.Euler(0, yawDeg, 0);
                foreach (var v0 in mv)
                {
                    SV.Add(rot * v0 + Mir(pos));
                    float lu = (v0.x - bb.min.x) / (bb.max.x - bb.min.x + 1e-6f);
                    float lv = (v0.y - bb.min.y) / (bb.max.y - bb.min.y + 1e-6f);
                    if (rect.HasValue)
                        SUV.Add(new Vector2(rect.Value.x + (rect.Value.z - rect.Value.x) * lu, rect.Value.y + (rect.Value.w - rect.Value.y) * lv));
                    else
                        SUV.Add(new Vector2(0, 0));
                    SS.Add(new Vector2(phase, lamp));
                }
                foreach (int t in g.triangles) SI.Add(b + t);
            }

            void SignalAt(float x, float z, float yaw, int phase)
            {
                float y = sideY;
                float yawDeg = -yaw * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, yawDeg, 0);
                AddLens(Load("tsig_lensG"), new Vector3(x, y, z), yawDeg, phase, 0, null);
                AddLens(Load("tsig_lensY"), new Vector3(x, y, z), yawDeg, phase, 1, null);
                AddLens(Load("tsig_lensR"), new Vector3(x, y, z), yawDeg, phase, 2, null);
                // green light at (4, 5.2, 0) local; color updated per frame
                var lp = Mir(rot * new Vector3(4.0f, 5.2f, 0f) + new Vector3(x, y, z));
                var go = new GameObject($"sigL_{x}_{z}_{phase}");
                go.transform.SetParent(root, false);
                go.transform.position = lp;
                var pl = go.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = new Color(0, 1, 0.6f);
                pl.range = 13;
                pl.intensity = 10;
                pl.shadows = LightShadows.None;
                SigLights.Add(pl);
                SigPhases.Add(phase);
            }

            Vector4 RectOf(string key)
            {
                if (!SignLib.Misc.TryGetValue(key, out var d) && !SignLib.Misc.TryGetValue("misc_" + key, out d))
                    return new Vector4(0, 0, 1, 1);
                return new Vector4(d.UV[0], d.UV[1], d.UV[2], d.UV[3]);
            }

            void PedAt(float x, float z, float yaw, int phase)
            {
                float y = sideY;
                float yawDeg = -yaw * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, yawDeg, 0);
                AddLens(Load("psig_pedR"), new Vector3(x, y, z), yawDeg, phase, 3, RectOf("pedR"));
                AddLens(Load("psig_pedG"), new Vector3(x, y, z), yawDeg, phase, 4, RectOf("pedG"));
            }

            // NOTE: the JS bodies go through sb.H with the GLB geometry transformed by m4 (yaw);
            // here the body boxes approximate the housing via the lens mesh's own transform instead.
            // For visual fidelity the body geometry is added as transformed vertices:
            void BodyAt(string name, float x, float z, float yaw, int phase)
            {
                float y = sideY;
                float yawDeg = -yaw * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, yawDeg, 0);
                var g = Load(name);
                int b = HV.Count;
                foreach (var v0 in g.vertices) { HV.Add(rot * v0 + Mir(new Vector3(x, y, z))); HC.Add(new Color(0.42f, 0.43f, 0.43f)); HMR.Add(new Vector4(0.8f, 0.42f, 0, 0)); }
                foreach (int t in g.triangles) HI.Add(b + t);
            }

            foreach (var zc in Layout.CrossZs)
            {
                float zcF = (float)zc;
                // traffic signals (4 per intersection)
                SignalAt(-ex, zcF - ez, 0, 0);
                SignalAt(ex, zcF + ez, Mathf.PI, 0);
                SignalAt(ex, zcF - ez, -Mathf.PI / 2, 1);
                SignalAt(-ex, zcF + ez, Mathf.PI / 2, 1);
                // bodies (transformed GLB geometry into housing mesh)
                BodyAt("tsig_body", -ex, zcF - ez, 0, 0);
                BodyAt("tsig_body", ex, zcF + ez, Mathf.PI, 0);
                BodyAt("tsig_body", ex, zcF - ez, -Mathf.PI / 2, 1);
                BodyAt("tsig_body", -ex, zcF + ez, Mathf.PI / 2, 1);
                // ped signals (8 per intersection; crossing cross street => phase 0, avenue => 1)
                PedAt(-ex - 1.2f, zcF + ez + 0.4f, 0, 0);
                PedAt(-ex - 1.2f, zcF - ez - 0.4f, Mathf.PI, 0);
                PedAt(ex + 1.2f, zcF + ez + 0.4f, 0, 0);
                PedAt(ex + 1.2f, zcF - ez - 0.4f, Mathf.PI, 0);
                PedAt(-ex - 0.4f, zcF + ez + 1.4f, Mathf.PI / 2, 1);
                PedAt(ex + 0.4f, zcF + ez + 1.4f, -Mathf.PI / 2, 1);
                PedAt(-ex - 0.4f, zcF - ez - 1.4f, Mathf.PI / 2, 1);
                PedAt(ex + 0.4f, zcF - ez - 1.4f, -Mathf.PI / 2, 1);

                BodyAt("psig_body", -ex - 1.2f, zcF + ez + 0.4f, 0, 0);
                BodyAt("psig_body", -ex - 1.2f, zcF - ez - 0.4f, Mathf.PI, 0);
                BodyAt("psig_body", ex + 1.2f, zcF + ez + 0.4f, 0, 0);
                BodyAt("psig_body", ex + 1.2f, zcF - ez - 0.4f, Mathf.PI, 0);
                BodyAt("psig_body", -ex - 0.4f, zcF + ez + 1.4f, Mathf.PI / 2, 1);
                BodyAt("psig_body", ex + 0.4f, zcF + ez + 1.4f, -Mathf.PI / 2, 1);
                BodyAt("psig_body", -ex - 0.4f, zcF - ez - 1.4f, Mathf.PI / 2, 1);
                BodyAt("psig_body", ex + 0.4f, zcF - ez - 1.4f, -Mathf.PI / 2, 1);
            }

            Commit(root, "SignalLenses", SV, SI, SUV, SS, signal);
            CommitH(root, "SignalHousing", HV, HI, HC, HMR, housing);

            // holo text ring over the first intersection
            return $"signals: {SigLights.Count} sig lights, lenses {SV.Count}v";
        }

        static void Commit(Transform root, string name, List<Vector3> V, List<int> Ix, List<Vector2> UV, List<Vector2> SD, Material mat)
        {
            if (V.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(V);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            mesh.SetUVs(0, UV);
            mesh.SetUVs(1, SD);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void CommitH(Transform root, string name, List<Vector3> V, List<int> Ix, List<Color> C, List<Vector4> MR, Material mat)
        {
            if (V.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(V);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            mesh.SetColors(C);
            mesh.SetUVs(1, MR);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // holo text ring over the first intersection (mkRingMaterial port)
        public static string BuildRing(Material ring)
        {
            var old = GameObject.Find("/HoloRing");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("HoloRing").transform;
            var V = new List<Vector3>(); var Ix = new List<int>(); var UV = new List<Vector2>();
            int seg = 96;
            float r = 6, h = 1.4f;
            Vector3 c = new Vector3(0, 21, (float)Layout.CrossZs[0]);
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2;
                // three CylinderGeometry: uv.x = around, uv.y = 1 top .. 0 bottom
                V.Add(Mir(c + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r)));
                UV.Add(new Vector2((float)i / seg, 0));
                V.Add(Mir(c + new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r)));
                UV.Add(new Vector2((float)i / seg, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                Ix.Add(b); Ix.Add(b + 1); Ix.Add(b + 3); Ix.Add(b); Ix.Add(b + 3); Ix.Add(b + 2);
            }
            var mesh = new Mesh { name = "HoloRing" };
            mesh.SetVertices(V);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            mesh.SetUVs(0, UV);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("HoloRing");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ring;
            if (SignLib.Misc.TryGetValue("holoRing", out var d))
                ring.SetVector("_Rect", new Vector4(d.UV[0], d.UV[1], d.UV[2], d.UV[3]));
            ring.SetVector("_Tint", new Vector4(0.3f, 0.9f, 1f, 1));
            return "ring ok";
        }

        // per-frame state sync (called from PlayerController.Update)
        public static void Update(float t)
        {
            float a = CarState(0, t), c = CarState(1, t), pa = PedState(0, t), pc = PedState(1, t);
            Shader.SetGlobalVector("_SigA", new Vector4(a, 0, 0, 0));
            Shader.SetGlobalVector("_SigC", new Vector4(c, 0, 0, 0));
            Shader.SetGlobalVector("_SigPA", new Vector4(pa, 0, 0, 0));
            Shader.SetGlobalVector("_SigPC", new Vector4(pc, 0, 0, 0));
            Color grn = new Color(0, 1, 0.6f), yel = new Color(1, 0.55f, 0.05f), red = new Color(1, 0.06f, 0.03f);
            for (int i = 0; i < SigLights.Count; i++)
            {
                if (SigLights[i] == null) continue;
                float s = CarState(SigPhases[i], t);
                SigLights[i].color = s == 0 ? grn : s == 1 ? yel : red;
            }
        }
    }
}
