// Port of src/systems/landmarks/expressway.js — elevated expressway: 4-lane deck
// with world-space lane markings, jersey parapets, sound barriers, hammerhead
// piers (incl. in-river), sodium/LED lamp posts, underside lighting, sign gantries.
// Colliders skipped (player collision is a separate todo).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Expressway
    {
        const float Z = -240f, H = 22f, Width = 16f, X0 = -400f, X1 = 400f; // sim z; LmBatch mirrors
        const float RiverX = 175f, RiverHw = 35f, RiverW = -3f;
        const float LaneW = 3.5f;

        public static string Build(Transform root)
        {
            float hw = Width / 2f;
            var asphalt = Shader.Find("NEON KAIDO/ExpresswayDeck");
            if (asphalt == null) return "expressway: deck shader missing";
            var matAsphalt = new Material(asphalt);
            matAsphalt.SetFloat("_DeckZ", -Z); // unity z (mirror)
            matAsphalt.SetFloat("_LaneW", LaneW);
            matAsphalt.SetFloat("_Width", Width);

            var concrete = LmMats.Surface("concrete", tint: new Color(0.88f, 0.87f, 0.85f), uvScale: 1f / 4.5f, normalScale: 1.2f, macro: 0.65f);
            var steel = LmMats.Metal(new Color(0.28f, 0.3f, 0.32f), 0.45f, 0.8f);
            var darkMetal = LmMats.Metal(new Color(0.06f, 0.06f, 0.065f), 0.5f);
            var barrier = LmMats.Metal(new Color(0.05f, 0.065f, 0.07f), 0.12f, 0f);
            var ledWarm = LmMats.Emissive(new Color(1f, 0.75f, 0.45f), 5);
            var lampSodium = LmMats.Emissive(new Color(1f, 0.55f, 0.15f), 30);
            var lampLed = LmMats.Emissive(new Color(0.85f, 0.92f, 1f), 30);
            var sign = LmMats.BasicEmissive(new Color(0.03f, 0.36f, 0.18f), 1f);
            var mats = new (string key, Material m)[]
            {
                ("concrete", concrete), ("asphalt", matAsphalt), ("barrier", barrier), ("sign", sign),
                ("steel", steel), ("darkMetal", darkMetal), ("ledWarm", ledWarm),
                ("lampSodium", lampSodium), ("lampLed", lampLed),
            };
            foreach (var (key, m) in mats) if (m == null) return "expressway: material missing (" + key + ")";

            var b = new LmBatch();

            // ---------- deck + parapets + sound barriers ----------
            var deckProf = new Vector2[]
            {
                new Vector2(-hw, 0), new Vector2(-hw, -0.35f), new Vector2(-hw + 1.2f, -0.7f),
                new Vector2(-5.2f, -2.6f), new Vector2(5.2f, -2.6f),
                new Vector2(hw - 1.2f, -0.7f), new Vector2(hw, -0.35f), new Vector2(hw, 0),
            };
            var parapetProf = new Vector2[]
            {
                new Vector2(-0.3f, 0), new Vector2(0.3f, 0), new Vector2(0.12f, 0.35f),
                new Vector2(0.1f, 1.05f), new Vector2(-0.1f, 1.05f), new Vector2(-0.12f, 0.35f),
            };
            var medianProf = new Vector2[]
            {
                new Vector2(-0.3f, 0), new Vector2(0.3f, 0), new Vector2(0.12f, 0.35f),
                new Vector2(0.1f, 0.85f), new Vector2(-0.1f, 0.85f), new Vector2(-0.12f, 0.35f),
            };
            for (float x = X0; x < X1; x += 40f)
            {
                float xe = Mathf.Min(X1, x + 40f);
                b.Add("concrete", LmUtil.ProfileGeometry(deckProf, x + 0.04f, xe - 0.04f), 0, H - 0.08f, Z);
                b.Box("asphalt", xe - x, 0.08f, Width - 1.0f, (x + xe) / 2f, H - 0.04f, Z);
                for (int si = 0; si < 2; si++)
                {
                    float s = si == 0 ? -1f : 1f;
                    b.Add("concrete", LmUtil.ProfileGeometry(parapetProf, x + 0.04f, xe - 0.04f), 0, H, Z + s * (hw - 0.3f));
                    for (float px = x; px < xe; px += 4f)
                        b.Box("steel", 0.18f, 2.8f, 0.14f, px + 0.1f, H + 2.45f, Z + s * (hw - 0.25f));
                    b.Box("barrier", xe - x - 0.1f, 2.6f, 0.04f, (x + xe) / 2f, H + 2.4f, Z + s * (hw - 0.2f));
                    b.Box("steel", xe - x, 0.12f, 0.2f, (x + xe) / 2f, H + 3.8f, Z + s * (hw - 0.25f));
                    b.Box("steel", xe - x, 0.1f, 0.18f, (x + xe) / 2f, H + 1.08f, Z + s * (hw - 0.25f));
                    b.Box("ledWarm", xe - x - 1f, 0.06f, 0.05f, (x + xe) / 2f, H - 0.45f, Z + s * (hw + 0.02f));
                }
                b.Add("concrete", LmUtil.ProfileGeometry(medianProf, x + 0.04f, xe - 0.04f), 0, H, Z);
            }

            // ---------- piers ----------
            var pierXs = new List<float>();
            for (float x = X0 + 20f; x < X1; x += 40f)
            {
                if (Mathf.Abs(x) < 18f) continue;
                if (x > RiverX - RiverHw - 18f && x < RiverX + RiverHw + 18f) continue;
                pierXs.Add(x);
            }
            pierXs.AddRange(new[] { -13f, 13f, RiverX - RiverHw - 14f, RiverX + RiverHw + 14f, RiverX });
            var colGeo = LmUtil.GenCylinder(1f, 1f, 1f, 12);
            var capProf = new Vector2[]
            {
                new Vector2(-7.2f, 0), new Vector2(-7.2f, -0.8f), new Vector2(-1.8f, -2.6f),
                new Vector2(1.8f, -2.6f), new Vector2(7.2f, -0.8f), new Vector2(7.2f, 0),
            };
            foreach (var px in pierXs)
            {
                bool inRiver = px > RiverX - RiverHw && px < RiverX + RiverHw;
                float gy = inRiver ? RiverW - 3f : LmUtil.GroundY(px, Z);
                float top = H - 2.7f - 2.6f;
                float h = top - gy;
                b.Add("concrete", colGeo, px, gy + h / 2f, Z, 0, 0, 0, 1.6f, h, 1.9f);
                b.Add("concrete", LmUtil.ProfileGeometry(capProf, px - 1.5f, px + 1.5f), 0, H - 2.68f, Z);
                if (inRiver) b.Box("concrete", 5f, 3.6f, 6f, px, RiverW - 1.2f, Z);
                for (int si = 0; si < 2; si++)
                    b.Box("darkMetal", 1f, 0.12f, 1f, px, H - 2.75f, Z + (si == 0 ? -3.5f : 3.5f));
                b.Box("darkMetal", 0.2f, h, 0.2f, px - 1.55f, gy + h / 2f, Z + 0.6f);
            }

            // ---------- lamp posts + lights ----------
            var lightsParent = new GameObject("expressLights");
            int lightCount = 0, k = 0;
            for (float x = X0 + 8f; x < X1; x += 32f, k++)
            {
                for (int si = 0; si < 2; si++)
                {
                    float s = si == 0 ? -1f : 1f;
                    float zx = Z + s * (hw - 0.6f);
                    b.Box("steel", 0.22f, 10.5f, 0.22f, x, H + 5.25f, zx);
                    LmUtil.BeamBetween(b, "steel",
                        new Vector3(x, H + 10.4f, zx), new Vector3(x, H + 10.9f, zx - s * 2.2f), 0.14f, 0.14f);
                    b.Box("steel", 0.9f, 0.22f, 0.45f, x, H + 10.85f, zx - s * 2.6f);
                    b.Box(k % 3 == 0 ? "lampLed" : "lampSodium", 0.75f, 0.05f, 0.35f, x, H + 10.72f, zx - s * 2.6f);
                    if (x > -180f && x < 300f)
                    {
                        var lg = new GameObject("expL");
                        lg.transform.SetParent(lightsParent.transform, false);
                        lg.transform.position = new Vector3(x, H + 10f, -Z);
                        var li = lg.AddComponent<Light>();
                        li.type = LightType.Point;
                        bool led = k % 3 == 0;
                        li.color = led ? new Color(0.867f, 0.91f, 1f) : new Color(1f, 0.627f, 0.25f);
                        li.intensity = led ? 220f : 260f;
                        li.range = 25f;
                        lightCount++;
                    }
                }
            }
            for (float x = -160f; x <= 290f; x += 24f)
            {
                var lg = new GameObject("expUnder");
                lg.transform.SetParent(lightsParent.transform, false);
                lg.transform.position = new Vector3(x, H - 3.6f, -Z);
                var li = lg.AddComponent<Light>();
                li.type = LightType.Point;
                li.color = new Color(1f, 0.69f, 0.44f);
                li.intensity = 70f;
                li.range = 18f;
                lightCount++;
            }
            for (float x = X0 + 6f; x < X1; x += 12f)
                for (int si = 0; si < 2; si++)
                    b.Box("lampSodium", 1.4f, 0.06f, 0.2f, x, H - 2.72f, Z + (si == 0 ? -3.6f : 3.6f));

            // ---------- sign gantries ----------
            foreach (var gx in new[] { -60f, 95f })
            {
                for (int si = 0; si < 2; si++)
                    b.Box("steel", 0.4f, 7.2f, 0.4f, gx, H + 3.6f, Z + (si == 0 ? -(hw - 0.6f) : hw - 0.6f));
                b.Box("steel", 0.4f, 0.4f, Width, gx, H + 7f, Z);
                b.Box("sign", 0.15f, 2.2f, 5.5f, gx + 0.3f, H + 5.9f, Z - 4f);
                b.Box("sign", 0.15f, 2.2f, 5.5f, gx + 0.3f, H + 5.9f, Z + 3.2f);
            }

            lightsParent.transform.SetParent(root, false);
            var built = b.Build(root, "exp_", 80f, new HashSet<string> { "concrete" });
            foreach (var go in built)
            {
                var key = go.name.Substring("exp_".Length);
                foreach (var (k2, m) in mats)
                    if (k2 == key) { go.GetComponent<MeshRenderer>().sharedMaterial = m; break; }
            }
            int tris = 0;
            foreach (var go in built)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            return $"expressway: {built.Count} meshes {tris} tris, {pierXs.Count} piers, {lightCount} lights";
        }
    }
}
