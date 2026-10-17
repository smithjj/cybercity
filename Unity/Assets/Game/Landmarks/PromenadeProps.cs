// Port of src/systems/landmarks/props.js — promenade furniture: benches facing
// the river, stone planters with displaced shrub masses, mamachari bicycles
// (wheels/spokes/frame/basket), placed around the cherry tree line.
// All placement math in SIM space; mirror to UNITY at bake (z -> -z, ry -> -ry,
// rx -> -rx, rz unchanged).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class PromenadeProps
    {
        const float PxMin = 128f, PxMax = 140f, ZStart = 40f, ZEnd = -500f;
        const float BridgeZ = -260f;

        public static string Build(Transform root)
        {
            if (Cherry.Spots == null) return "props: cherry spots missing (build cherry first)";
            var occupied = new List<float>();
            foreach (var s in Cherry.Spots) occupied.Add(s.z);
            var rng = new Mulberry32(3131u);
            var b = new LmBatch();

            bool Free(float z, float r)
            {
                foreach (var oz in occupied) if (Mathf.Abs(oz - z) <= r) return false;
                return Mathf.Abs(z - BridgeZ) > 20f;
            }

            // ---- bench (sim space, ry=0 => faces river) ----
            void Bench(float x, float z)
            {
                float y = LmUtil.GroundY(x, z);
                for (int i = 0; i < 4; i++)
                    b.Box("wood", 0.11f, 0.04f, 1.8f, x + (-0.22f + i * 0.13f), y + 0.45f, z);
                for (int i = 0; i < 3; i++)
                    b.Box("wood", 0.03f, 0.1f, 1.8f, x + (-0.32f - i * 0.025f), y + 0.6f + i * 0.13f, z, 0, 0, -0.18f);
                foreach (var lz in new[] { -0.75f, 0.75f })
                {
                    b.Box("iron", 0.55f, 0.06f, 0.06f, x, y + 0.42f, z + lz);
                    b.Box("iron", 0.06f, 0.44f, 0.06f, x + 0.2f, y + 0.22f, z + lz);
                    b.Box("iron", 0.06f, 0.9f, 0.06f, x - 0.25f, y + 0.45f, z + lz, 0, 0, -0.18f);
                    b.Box("iron", 0.5f, 0.05f, 0.05f, x + 0.05f, y + 0.65f, z + lz * 1.07f);
                }
            }

            // ---- planter + shrubs ----
            var shrubGeo = LmUtil.GenSphere(1f, 12, 8);
            { // displacement (UV-sphere stand-in for the JS icosahedron — visual deviation)
                for (int i = 0; i < shrubGeo.pos.Length; i++)
                {
                    var v = shrubGeo.pos[i];
                    float n = Mathf.Sin(v.x * 7.1f) * Mathf.Cos(v.y * 6.3f) * Mathf.Sin(v.z * 8.7f) * 0.18f
                            + Mathf.Sin(v.x * 17f + v.z * 13f) * 0.06f;
                    shrubGeo.pos[i] = v * (1f + n);
                }
                shrubGeo.ComputeVertexNormals();
            }
            void Planter(float x, float z, float len)
            {
                float y = LmUtil.GroundY(x, z);
                b.Box("stone", 1.2f, 0.6f, len, x, y + 0.3f, z);
                b.Box("coping", 1.3f, 0.08f, len + 0.1f, x, y + 0.62f, z);
                int cnt = Mathf.RoundToInt(len / 0.7f);
                for (int k = 0; k < cnt; k++)
                {
                    float sz = 0.38f + (float)rng.Next() * 0.2f;
                    b.Add("shrub", shrubGeo, x + ((float)rng.Next() - 0.5f) * 0.3f, y + 0.62f + sz * 0.6f,
                        z - len / 2f + 0.35f + k * 0.7f, 0, (float)rng.Next() * 6f, 0, sz * 1.2f, sz, sz * 1.2f);
                }
            }

            // ---- bicycle (local sim space, mirrored, then matrix-placed) ----
            var torus = LmUtil.GenTorus(0.33f, 0.022f, 6, 28);
            var hub = LmUtil.GenCylinder(0.035f, 0.035f, 0.1f, 8);
            var parts = new List<(string key, LmGeo geo)>();
            var bikeB = new LmBatch();
            void Bike(float x, float z, float ry, float tilt)
            {
                float y = LmUtil.GroundY(x, z);
                parts.Clear();
                var sub = new LmBatch();
                foreach (var wx in new[] { -0.52f, 0.52f })
                {
                    sub.Add("tire", torus, wx, 0.35f, 0);
                    sub.Add("chrome", hub, wx, 0.35f, 0, Mathf.PI / 2f, 0, 0);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 8f;
                        LmUtil.BeamBetween(sub, "chrome",
                            new Vector3(wx - Mathf.Cos(a) * 0.31f, 0.35f - Mathf.Sin(a) * 0.31f, 0),
                            new Vector3(wx + Mathf.Cos(a) * 0.31f, 0.35f + Mathf.Sin(a) * 0.31f, 0), 0.006f, 0.006f);
                    }
                }
                var frame = new (Vector3 a, Vector3 c)[]
                {
                    (new Vector3(-0.52f, 0.35f, 0), new Vector3(-0.1f, 0.38f, 0)),
                    (new Vector3(-0.1f, 0.38f, 0), new Vector3(-0.2f, 0.85f, 0)),
                    (new Vector3(-0.1f, 0.38f, 0), new Vector3(0.42f, 0.78f, 0)),
                    (new Vector3(0.42f, 0.78f, 0), new Vector3(0.52f, 0.35f, 0)),
                    (new Vector3(-0.52f, 0.35f, 0), new Vector3(-0.2f, 0.8f, 0)),
                    (new Vector3(0.42f, 0.78f, 0), new Vector3(0.38f, 1.0f, 0)),
                };
                foreach (var (a, c) in frame) LmUtil.BeamBetween(sub, "frame", a, c, 0.035f, 0.035f);
                sub.Box("tire", 0.24f, 0.05f, 0.12f, -0.22f, 0.9f, 0);
                sub.Box("chrome", 0.03f, 0.03f, 0.55f, 0.36f, 1.02f, 0);
                sub.Box("basket", 0.3f, 0.22f, 0.36f, 0.6f, 0.92f, 0);
                sub.Box("frame", 0.55f, 0.02f, 0.12f, -0.55f, 0.72f, 0);
                foreach (var kv in sub.Parts)
                    foreach (var g in kv.Value)
                        b.AddMatrix(kv.Key, g,
                            Matrix4x4.TRS(new Vector3(x, y, z),
                                Quaternion.Euler(tilt * Mathf.Rad2Deg, ry * Mathf.Rad2Deg, 0), Vector3.one));
            }

            // ---- layout along promenade (JS rng call order preserved) ----
            int k = 0;
            for (float z = ZStart - 8f; z > ZEnd + 8f; z -= 7f)
            {
                if (!Free(z, 2.4f)) continue;
                k++;
                float r = (float)rng.Next();
                if (k % 3 == 0 && r < 0.85f) Bench(PxMin + 2.6f, z);
                else if (k % 5 == 1) Planter(PxMin + 0.8f, z, 3.2f);
                else if (k % 7 == 2)
                {
                    Bike(PxMin + 3.4f, z, Mathf.PI / 2f + ((float)rng.Next() - 0.5f) * 0.4f, -0.12f);
                    Bike(PxMin + 3.4f, z - 0.7f, Mathf.PI / 2f + ((float)rng.Next() - 0.5f) * 0.4f, 0.1f);
                }
            }
            Bike(PxMax - 0.9f, -32f, 0.08f, 0.16f);
            Bike(PxMax - 0.9f, -78f, -0.05f, 0.16f);

            var wood = LmMats.Surface("wood", tint: new Color(0.75f, 0.6f, 0.5f), uvScale: 1f / 1.2f);
            var iron = LmMats.Metal(new Color(0.05f, 0.05f, 0.055f), 0.45f, 0.8f);
            var stone = LmMats.Surface("concrete", tint: new Color(0.6f, 0.58f, 0.55f), uvScale: 1f / 1.5f);
            var coping = LmMats.Surface("stonewall", tint: new Color(0.5f, 0.5f, 0.5f), uvScale: 1f / 1.2f);
            var tire = LmMats.Metal(new Color(0.02f, 0.02f, 0.02f), 0.5f, 0f);
            var chrome = LmMats.Metal(new Color(0.7f, 0.7f, 0.72f), 0.15f, 1f);
            var frameM = LmMats.Metal(new Color(0.45f, 0.05f, 0.06f), 0.3f, 0.3f);
            var basket = LmMats.Metal(new Color(0.3f, 0.3f, 0.32f), 0.35f, 0.9f);
            var shrubM = LmMats.ShaderMat("NEON KAIDO/PromenadeShrub");
            if (wood == null || iron == null || stone == null || coping == null || tire == null ||
                chrome == null || frameM == null || basket == null || shrubM == null)
                return "props: material missing";

            var built = b.Build(root, "pp_", 60f, new HashSet<string> { "wood", "stone", "coping" });
            foreach (var go in built)
            {
                var key = go.name.Substring("pp_".Length);
                Material m = key == "wood" ? wood : key == "iron" ? iron : key == "stone" ? stone
                    : key == "coping" ? coping : key == "tire" ? tire : key == "chrome" ? chrome
                    : key == "frame" ? frameM : key == "basket" ? basket : shrubM;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            int tris = 0;
            foreach (var go in built)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            return $"promenade props: {built.Count} meshes, {tris} tris";
        }
    }
}
