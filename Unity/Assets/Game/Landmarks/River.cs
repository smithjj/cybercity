// Port of src/systems/landmarks/river.js — reflective water plane, embankment
// walls with coping/drains/mooring rings, promenade railings + stone lanterns
// with point lights. All sim-space math, mirrored at mesh build by LmBatch.
// DEVIATIONS (documented): water reflection = env streaks (no planar reflector);
// lantern head grid via LandmarkEmissive._Grid; colliders skipped (player
// collision is a separate todo).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class River
    {
        // layout constants (layout.js)
        const double XCenter = 175, HalfWidth = 35, WaterLevel = -3.0;
        const double ZStart = 200, ZEnd = -700;
        const double Ps = 40, Pe = -500;        // promenade zStart/zEnd
        const double BridgeZ = -260;

        public static string Build(Transform root)
        {
            var xw = XCenter - HalfWidth;  // 140 (east bank edge of water)
            var xe = XCenter + HalfWidth;  // 210
            var zLen = ZStart - ZEnd + 2;  // water plane length

            // ---------- materials ----------
            var waterMat = LmMats.ShaderMat("NEON KAIDO/RiverWater");
            if (waterMat == null) return "river: water shader missing";
            var wall = LmMats.Surface("stonewall",
                tint: new Color(0.72f, 0.7f, 0.68f), uvScale: 0.41f, normalScale: 1.4f,
                macro: 0.7f, macroFreq: 0.03f, streaks: 0.9f, wetVert: 0.6f, wetDark: 0.5f);
            if (wall != null)
            {
                wall.SetFloat("_WlLevel", (float)WaterLevel);
                wall.SetFloat("_WlAlgae", 0.35f);
                wall.SetFloat("_WlBelow", 0.45f);
            }
            var coping = LmMats.Surface("concrete",
                tint: new Color(0.78f, 0.76f, 0.73f), uvScale: 0.7f, streaks: 0.5f, rough: 0.7f);
            var post = LmMats.Surface("concrete",
                tint: new Color(0.8f, 0.79f, 0.76f), uvScale: 1f, rough: 0.65f);
            var railMetal = LmMats.Metal(new Color(0.1f, 0.1f, 0.11f), 0.3f, 0.9f);
            var darkMetal = LmMats.Metal(new Color(0.12f, 0.1f, 0.09f), 0.5f);
            var lanternStone = LmMats.Surface("concrete",
                tint: new Color(0.6f, 0.58f, 0.55f), uvScale: 1.4f, rough: 0.7f);
            var lanternGlow = LmMats.Emissive(new Color(1f, 0.62f, 0.3f), 9);
            if (lanternGlow != null) lanternGlow.SetFloat("_Grid", 1f);
            var hole = LmMats.Emissive(new Color(0f, 0f, 0f), 0);

            var mats = new (string key, Material m)[]
            {
                ("wall", wall), ("coping", coping), ("post", post), ("drain", post),
                ("hole", hole), ("ring", darkMetal), ("back", darkMetal),
                ("rail", railMetal), ("kerb", post),
                ("plinth", lanternStone), ("shaft", lanternStone), ("cap", lanternStone),
                ("head", lanternGlow), ("lpost", lanternStone), ("roof", lanternStone),
                ("finial", lanternStone),
            };
            foreach (var (key, m) in mats)
                if (m == null) return "river: material missing (" + key + ")";

            var batch = new LmBatch();
            var lightsParent = new GameObject("riverLights");

            // ---------- water plane (quad, uv in meters) ----------
            var wq = new LmGeo
            {
                pos = new Vector3[6],
                uv = new Vector2[6],
                nor = new Vector3[6],
            };
            AddQuad(wq, 0,
                new Vector3((float)(xw - 1), (float)WaterLevel, (float)ZStart),
                new Vector3((float)(xe + 1), (float)WaterLevel, (float)ZStart),
                new Vector3((float)(xe + 1), (float)WaterLevel, (float)ZEnd),
                new Vector3((float)(xw - 1), (float)WaterLevel, (float)ZEnd));
            for (int i = 0; i < 6; i++) wq.nor[i] = Vector3.up;
            batch.AddMatrix("water", wq, Matrix4x4.identity);

            // ---------- embankment walls (both banks) ----------
            var wallBottom = WaterLevel - 2; // -5
            var wallH = -wallBottom;         // 5
            var cylDrain = LmUtil.GenCylinder(0.22f, 0.22f, 0.3f, 12);
            var cylHole = LmUtil.GenCylinder(0.17f, 0.17f, 0.05f, 12);
            var torus = LmUtil.GenTorus(0.22f, 0.06f, 6, 18);
            var sphere = LmUtil.GenSphere(0.05f, 6, 4);
            var coneRoof = LmUtil.GenCone(0.42f, 0.24f, 4);

            int lanternCount = 0;
            var zStepWalls = 30.0;
            for (var z = ZStart; z > ZEnd; z -= zStepWalls)
            {
                var zc = z - zStepWalls / 2;
                foreach (var sx in new[] { xw - 2, xe + 2 })
                {
                    var sign = sx > XCenter ? 1 : -1;
                    batch.Box("wall", 1.2f, (float)wallH, 30f, (float)(sx + 0.6 * sign), (float)(wallBottom + wallH / 2), (float)zc);
                    batch.Box("coping", 1.35f, 0.22f, 30f, (float)(sx + 0.55 * sign), -0.11f, (float)zc);
                    // drains every 12m
                    for (var dz = 0; dz < 30; dz += 12)
                        if (z - dz > ZEnd + 6 && System.Math.Abs(z - dz - BridgeZ) > 5)
                        {
                            batch.Add("drain", cylDrain, (float)(sx + 0.1 * sign), 0.15f, (float)(z - dz), 0, 0, 90 * Mathf.Deg2Rad);
                            batch.Add("hole", cylHole, (float)(sx + 0.28 * sign), 0.15f, (float)(z - dz), 0, 0, 90 * Mathf.Deg2Rad);
                        }
                    // mooring rings every 18m
                    for (var dz = 0; dz < 30; dz += 18)
                        if (z - dz > ZEnd + 6)
                        {
                            batch.Add("ring", torus, (float)(sx + 0.85 * sign), 0.55f, (float)(z - dz), 0, 0, Mathf.PI / 2);
                            batch.Box("back", 0.3f, 0.24f, 0.06f, (float)(sx + 0.71 * sign), 0.55f, (float)(z - dz));
                        }
                }
            }

            // ---------- railings + stone lanterns (both banks) ----------
            for (int bi = 0; bi < 2; bi++)
            {
                var sx = bi == 0 ? xw - 2 : xe + 2;
                var lanternStep = bi == 0 ? 4.0 : 8.0;
                var mid = (Ps + Pe) / 2;
                var pl = Pe - Ps;
                // posts
                for (var z = Ps + 2; z > Pe - 2; z -= 2.4)
                    batch.Box("post", 0.06f, 0.06f, 0.9f, (float)sx, 0.45f, (float)z);
                // rails + kerb
                batch.Box("rail", 0.05f, 0.05f, (float)pl, (float)sx, 0.42f, (float)mid);
                batch.Box("rail", 0.05f, 0.05f, (float)pl, (float)sx, 0.95f, (float)mid);
                batch.Box("kerb", 0.18f, 0.1f, (float)pl, (float)sx, 0.05f, (float)mid);
                // stone lantern bollards
                for (var z = Ps + 1; z > Pe - 1; z -= lanternStep)
                {
                    if (System.Math.Abs(z - BridgeZ) < 3) continue;
                    batch.Box("plinth", 0.4f, 0.18f, 0.4f, (float)sx, 0.09f, (float)z);
                    batch.Add("shaft", LmUtil.GenCylinder(0.07f, 0.08f, 0.7f, 8), (float)sx, 0.55f, (float)z);
                    batch.Box("cap", 0.26f, 0.05f, 0.26f, (float)sx, 0.93f, (float)z);
                    batch.Box("head", 0.24f, 0.3f, 0.24f, (float)sx, 1.13f, (float)z);
                    foreach (var ox in new[] { -0.08f, 0.08f })
                        foreach (var oz in new[] { -0.08f, 0.08f })
                            batch.Box("lpost", 0.03f, 0.34f, 0.03f, (float)sx + ox, 0.99f, (float)z + oz);
                    batch.Add("roof", coneRoof, (float)sx, 1.42f, (float)z, 0, Mathf.PI / 4, 0);
                    batch.Add("finial", sphere, (float)sx, 1.58f, (float)z);
                    lanternCount++;

                    // point light (data holder for the shared light arrays)
                    if (z > -560 && z < 45)
                    {
                        var lg = new GameObject("riverL");
                        lg.transform.SetParent(lightsParent.transform, false);
                        lg.transform.position = new Vector3((float)sx, 1.5f, (float)-z); // unity space
                        var li = lg.AddComponent<Light>();
                        li.type = LightType.Point;
                        li.color = new Color(1f, 0.7f, 0.42f);
                        li.intensity = (float)(bi == 0 ? 26 : 30);
                        li.range = 13;
                    }
                }
            }

            lightsParent.transform.SetParent(root, false);
            var built = batch.Build(root, "river_", 0, new HashSet<string> { "wall", "coping", "post" });
            foreach (var go in built)
                if (go.name.StartsWith("river_water"))
                {
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = waterMat;
                }
                else
                {
                    var key = go.name.Substring("river_".Length);
                    foreach (var (k, m) in mats)
                        if (k == key) { go.GetComponent<MeshRenderer>().sharedMaterial = m; break; }
                }

            int tris = 0;
            foreach (var go in built)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            return $"river: water + {built.Count} meshes {tris} tris, {lanternCount} lanterns, {lanternCount} lights";
        }

        static void AddQuad(LmGeo g, int o, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // tris (a,b,c),(a,c,d) — +Y normal in sim space
            Vector3[] p = { a, b, c, a, c, d };
            Vector2[] u = { g.uv[0], g.uv[1], g.uv[2], g.uv[0], g.uv[2], g.uv[3] };
            for (int i = 0; i < 6; i++) { g.pos[o + i] = p[i]; g.uv[o + i] = u[i]; }
        }
    }
}
