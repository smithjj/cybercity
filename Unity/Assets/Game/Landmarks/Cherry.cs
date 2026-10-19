// Port of src/systems/landmarks/trees.js — cherry trees along the promenade:
// 4 GLB variants (dumped to bins via tools/dump-cherry.mjs), bark + blossom
// cards (uv1 = tint/outerness), cast-iron grates, falling/water/ground petals.
// All placement math in SIM space; mirror at bake. Spots exposed for props (#39).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Cherry
    {
        public struct Spot { public float x, z, rot, s; public int v; }

        public static Spot[] Spots;

        const float PxMin = 128f, PxMax = 140f;
        const float ZStart = 40f, ZEnd = -500f;
        const float BridgeZ = -260f, RailZ = -140f, ExZ = -240f;
        const float WaterY = -3f;

        static Vector3[] ReadVec3(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            int n = bytes.Length / 12;
            var v = new Vector3[n];
            for (int i = 0; i < n; i++)
                v[i] = new Vector3(System.BitConverter.ToSingle(bytes, i * 12),
                    System.BitConverter.ToSingle(bytes, i * 12 + 4),
                    System.BitConverter.ToSingle(bytes, i * 12 + 8));
            return v;
        }
        static Vector2[] ReadVec2(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            int n = bytes.Length / 8;
            var v = new Vector2[n];
            for (int i = 0; i < n; i++)
                v[i] = new Vector2(System.BitConverter.ToSingle(bytes, i * 8),
                    System.BitConverter.ToSingle(bytes, i * 8 + 4));
            return v;
        }
        static Vector4[] ReadVec4(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            int n = bytes.Length / 16;
            var v = new Vector4[n];
            for (int i = 0; i < n; i++)
                v[i] = new Vector4(System.BitConverter.ToSingle(bytes, i * 16),
                    System.BitConverter.ToSingle(bytes, i * 16 + 4),
                    System.BitConverter.ToSingle(bytes, i * 16 + 8),
                    System.BitConverter.ToSingle(bytes, i * 16 + 12));
            return v;
        }

        class Acc
        {
            public List<Vector3> pos = new(), nor = new();
            public List<Vector2> uv0 = new(), uv1 = new();
            public List<Vector2> uv2 = new();
            public int Count => pos.Count;
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                // Chunks routinely exceed 65k verts; UInt16 would wrap the tail indices and
                // stitch distant trees together with long slivers.
                if (Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.vertices = pos.ToArray();
                m.normals = nor.ToArray();
                m.uv = uv0.ToArray();
                m.SetUVs(1, uv1);
                m.SetUVs(2, uv2);
                var idx = new int[Count];
                for (int i = 0; i < Count; i++) idx[i] = i;
                m.triangles = idx;
                m.RecalculateBounds();
                return m;
            }
        }

        public static string Build(Transform root)
        {
            if (LmMats.Tex("bark") == null) LmMats.LoadTextures();
            var barkTex = LmMats.Tex("bark");
            var blossomTex = LmMats.Tex("blossom");
            var petalsTex = LmMats.Tex("petals");
            if (barkTex == null || blossomTex == null || petalsTex == null)
                return "cherry: missing textures";

            // ---- placement (mulberry32(4242), sim space) ----
            var rng = new Mulberry32(4242u);
            var spots = new List<Spot>();
            for (float z = ZStart - 4f; z > ZEnd + 4f; z -= 9f + (float)rng.Next() * 3.5f)
            {
                if (Mathf.Abs(z - BridgeZ) < 16f) continue;
                if (Mathf.Abs(z - RailZ) < 6f || Mathf.Abs(z - ExZ) < 4f) continue;
                float x = PxMin + 1.6f + (float)rng.Next() * 1.4f;
                spots.Add(new Spot
                {
                    x = x, z = z, v = Mathf.Min(3, (int)(rng.Next() * 4)),
                    rot = ((float)rng.Next() - 0.5f) * 0.7f,
                    s = 0.85f + (float)rng.Next() * 0.3f,
                });
            }
            Spots = spots.ToArray();

            var barkMat = LmMats.ShaderMat("NEON KAIDO/CherryBark");
            var blossomMat = LmMats.ShaderMat("NEON KAIDO/CherryBlossom");
            if (barkMat == null || blossomMat == null) return "cherry: missing shaders";
            barkMat.SetTexture("_MainTex", barkTex);
            barkMat.SetTexture("_BumpMap", LmMats.Tex("bark_n"));
            barkMat.SetTexture("_RoughMap", LmMats.Tex("bark_r"));
            blossomMat.SetTexture("_MainTex", blossomTex);

            string dir = "Assets/Game/Models/Landmarks/dump";

            // ---- trunks + blossoms: merge per variant into 60m unity-z chunks ----
            const float ChunkW = 60f;
            var trunks = new Dictionary<int, Acc>();   // chunk index -> acc
            var blossoms = new Dictionary<int, Acc>();
            int bakedTrees = 0;
            for (int vi = 0; vi < 4; vi++)
            {
                var tp = ReadVec3($"{dir}/cherry_{vi}_bark_pos.bin");
                var tn = ReadVec3($"{dir}/cherry_{vi}_bark_nor.bin");
                var tu = ReadVec2($"{dir}/cherry_{vi}_bark_uv0.bin");
                var bp = ReadVec3($"{dir}/cherry_{vi}_blossom_pos.bin");
                var bn = ReadVec3($"{dir}/cherry_{vi}_blossom_nor.bin");
                var bu0 = ReadVec2($"{dir}/cherry_{vi}_blossom_uv0.bin");
                var bu1raw = ReadVec2($"{dir}/cherry_{vi}_blossom_uv1.bin");
                var bu1 = new Vector4[bu1raw.Length];
                for (int i = 0; i < bu1raw.Length; i++) bu1[i] = new Vector4(bu1raw[i].x, bu1raw[i].y, 0, 0);
                foreach (var sp in spots)
                {
                    if (sp.v != vi) continue;
                    // sim->unity transform: mirror z (position + normal), rot around Y, scale
                    float cs = Mathf.Cos(sp.rot), sn = Mathf.Sin(sp.rot);
                    Vector3 T(Vector3 p) // sim p -> unity p
                    {
                        var q = new Vector3(p.x, p.y, -p.z);
                        var r = new Vector3(q.x * cs + q.z * sn, q.y, -q.x * sn + q.z * cs);
                        return r * sp.s + new Vector3(sp.x, LmUtil.GroundY(sp.x, sp.z), -sp.z);
                    }
                    Vector3 N(Vector3 n)
                    {
                        var q = new Vector3(n.x, n.y, -n.z);
                        return new Vector3(q.x * cs + q.z * sn, q.y, -q.x * sn + q.z * cs);
                    }
                    int ci = Mathf.FloorToInt((-sp.z) / ChunkW);
                    if (!trunks.TryGetValue(ci, out var ta)) trunks[ci] = ta = new Acc();
                    if (!blossoms.TryGetValue(ci, out var ba)) blossoms[ci] = ba = new Acc();
                    for (int i = 0; i < tp.Length; i++)
                    {
                        ta.pos.Add(T(tp[i])); ta.nor.Add(N(tn[i])); ta.uv0.Add(tu[i]);
                    }
                    for (int i = 0; i < bp.Length; i++)
                    {
                        var p = T(bp[i]);
                        ba.pos.Add(p); ba.nor.Add(N(bn[i])); ba.uv0.Add(bu0[i]); ba.uv1.Add(bu1[i]);
                        ba.uv2.Add(Vector2.zero);
                    }
                    bakedTrees++;
                }
            }
            int meshCount = 0;
            foreach (var (ci, acc) in trunks)
            {
                var go = new GameObject($"cherry_trunk_{ci}");
                go.transform.SetParent(root, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = acc.ToMesh($"cherry_trunk_{ci}");
                go.AddComponent<MeshRenderer>().sharedMaterial = barkMat;
                meshCount++;
            }
            foreach (var (ci, acc) in blossoms)
            {
                var go = new GameObject($"cherry_blossom_{ci}");
                go.transform.SetParent(root, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = acc.ToMesh($"cherry_blossom_{ci}");
                go.AddComponent<MeshRenderer>().sharedMaterial = blossomMat;
                meshCount++;
            }

            // ---- grates ----
            var grateBatch = new LmBatch();
            var grateGeo = LmUtil.GenCylinder(0.75f, 0.75f, 0.025f, 24);
            foreach (var sp in spots)
                grateBatch.Add("grate", grateGeo, sp.x, LmUtil.GroundY(sp.x, sp.z) + 0.015f, sp.z);
            var grateBuilt = grateBatch.Build(root, "cherry_grate_", 0);
            var grateMat = LmMats.Metal(new Color(0.025f, 0.022f, 0.02f), 0.55f, 0.2f);
            foreach (var go in grateBuilt) go.GetComponent<MeshRenderer>().sharedMaterial = grateMat;

            // ---- petals ----
            int petalCount = 0;
            if (petalsTex != null)
            {
                BuildPetals(root, "falling_petals", PetalMesh(99u, 5000, spots, "falling"), petalsTex);
                petalCount += 5000;
                BuildPetals(root, "water_petals", PetalMesh(5u, 9000, spots, "water"), petalsTex);
                petalCount += 9000;
                // JS owns promenade ground petals in its ground system; our ground has none — add them
                BuildPetals(root, "ground_petals", PetalMesh(6u, 14000, spots, "ground"), petalsTex);
                petalCount += 14000;
            }

            // colliders skipped (player collision separate todo)
            return $"cherry: {bakedTrees} trees, {meshCount} chunk meshes, {grateBuilt.Count} grates, {petalCount} petals";
        }

        static void BuildPetals(Transform root, string name, Mesh mesh, Texture2D tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mat = LmMats.ShaderMat("NEON KAIDO/CherryPetals");
            mat.SetFloat("_Kind", name.StartsWith("falling") ? 0 : name.StartsWith("water") ? 1 : 2);
            mat.SetTexture("_MainTex", tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Mesh PetalMesh(uint seed, int n, List<Spot> spots, string kind)
        {
            var rng = new Mulberry32(seed);
            var pos = new List<Vector3>(n * 4);
            var nor = new List<Vector3>(n * 4);
            var uv0 = new List<Vector2>(n * 4);
            var uv1 = new List<Vector4>(n * 4);
            var uv2 = new List<Vector2>(n * 4);
            var corners = new (float, float)[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
            for (int i = 0; i < n; i++)
            {
                float x, y, z;
                var b4 = new Vector4();
                var sp = spots[Mathf.Min(spots.Count - 1, (int)(rng.Next() * spots.Count))];
                if (kind == "falling")
                {
                    float bx = sp.x + ((float)rng.Next() - 0.3f) * 12f;
                    float bz = sp.z + ((float)rng.Next() - 0.5f) * 12f;
                    float by = (float)rng.Next() * 9f;
                    float sd = (float)rng.Next();
                    int c = (int)(rng.Next() * 16);
                    b4 = new Vector4(bx, by, -bz, sd);
                    foreach (var oi in new[] { 0, 1, 2, 0, 2, 3 })
                    {
                        var (cx, cy) = corners[oi];
                        pos.Add(new Vector3(cx * 0.022f, cy * 0.022f, 0));
                        nor.Add(new Vector3(0, 0, -1));
                        uv0.Add(new Vector2((cx + 1) / 2f, (cy + 1) / 2f));
                        uv1.Add(b4);
                        uv2.Add(new Vector2(c, 0));
                    }
                    continue;
                }
                if (kind == "water")
                {
                    float xw = 175f - 35f;
                    x = xw + 0.5f + Mathf.Pow((float)rng.Next(), 2.2f) * 30f;
                    z = ZStart - (float)rng.Next() * (ZStart - ZEnd);
                    if (rng.Next() < 0.5)
                    {
                        var c2 = spots[Mathf.Min(spots.Count - 1, (int)(rng.Next() * spots.Count))];
                        z = c2.z + ((float)rng.Next() - 0.5f) * 16f;
                        x = xw + 0.4f + Mathf.Pow((float)rng.Next(), 3) * 10f;
                    }
                    y = WaterY + 0.012f;
                }
                else
                {
                    var c2 = spots[Mathf.Min(spots.Count - 1, (int)(rng.Next() * spots.Count))];
                    float r = Mathf.Pow((float)rng.Next(), 0.7f) * 7f;
                    float a = (float)rng.Next() * Mathf.PI * 2f;
                    x = Mathf.Min(PxMax - 0.5f, Mathf.Max(PxMin + 0.2f, c2.x + 2f + Mathf.Cos(a) * r));
                    z = c2.z + Mathf.Sin(a) * r;
                    y = LmUtil.GroundY(x, z) + 0.008f + (float)rng.Next() * 0.004f;
                }
                float s = 0.018f + (float)rng.Next() * 0.012f;
                float an = (float)rng.Next() * Mathf.PI * 2f;
                int cell = (int)(rng.Next() * 16);
                float seedW = (float)rng.Next();
                float cs = Mathf.Cos(an), sn = Mathf.Sin(an);
                foreach (var oi in new[] { 0, 1, 2, 0, 2, 3 })
                {
                    var (cx, cy) = corners[oi];
                    float px = cx * s, pz = cy * s;
                    pos.Add(new Vector3(x + px * cs - pz * sn, y, -z + (px * sn + pz * cs)));
                    nor.Add(Vector3.up);
                    uv0.Add(new Vector2((cx + 1) / 2f, (cy + 1) / 2f));
                    uv1.Add(new Vector4(x, y, -z, seedW));
                    uv2.Add(new Vector2(cell, 0));
                }
            }
            var m = new Mesh { name = kind + "_petals" };
            // 14k ground petals alone are 84k verts; UInt16 wraps the tail and drags petal
            // quads across the whole promenade as long dotted lines.
            if (pos.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = pos.ToArray();
            m.normals = nor.ToArray();
            m.uv = uv0.ToArray();
            m.SetUVs(1, uv1);
            m.SetUVs(2, uv2);
            var idx = new int[pos.Count];
            for (int i = 0; i < pos.Count; i++) idx[i] = i;
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }
}

// touch
