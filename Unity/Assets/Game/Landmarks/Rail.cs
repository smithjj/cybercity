// Port of src/systems/landmarks/rail.js — elevated commuter rail viaduct:
// box-girder deck, parapets, track slabs + rails, pillars (incl. in-river),
// pier caps/bearings/footings, underside LEDs + accent strips + point lights,
// catenary masts/cantilevers/truss + sagging wires. Trains are task #36.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Rail
    {
        // layout.elevatedRail
        public const float Z = -140f, Height = 14f, Width = 9f, XStart = -400f, XEnd = 400f;
        public static readonly (float z, int dir)[] Tracks = { (Z - 2.0f, 1), (Z + 2.0f, -1) };

        static LmGeo _colGeo; // unit cylinder pre-rotated like three.js CylinderGeometry(1,1,1,8).rotateY(PI/8)

        public static string Build(Transform root)
        {
            if (_colGeo == null)
            {
                _colGeo = LmUtil.GenCylinder(1f, 1f, 1f, 8);
                _colGeo.ApplyMatrix(Matrix4x4.TRS(Vector3.zero, LmBatch.EulToQuat(0, Mathf.PI / 8f, 0), Vector3.one));
            }
            float H = Height, Zl = Z, x0 = XStart, x1 = XEnd;
            float halfW = Width / 2f;
            const float riverX = 175f, riverHw = 35f, riverW = -3f;

            var concrete = LmMats.Surface("concrete", tint: new Color(0.95f, 0.93f, 0.9f), uvScale: 0.25f, normalScale: 1.2f, macro: 0.6f);
            var concreteDark = LmMats.Surface("concrete", tint: new Color(0.55f, 0.55f, 0.56f), uvScale: 1f / 3f, normalScale: 1f);
            var slab = LmMats.Surface("concrete", tint: new Color(0.7f, 0.68f, 0.64f), uvScale: 0.5f, streaks: 0f);
            var railMat = LmMats.Metal(new Color(0.5f, 0.48f, 0.46f), 0.25f, 1f);
            var darkMetal = LmMats.Metal(new Color(0.07f, 0.07f, 0.075f), 0.5f);
            var steel = LmMats.Metal(new Color(0.32f, 0.34f, 0.36f), 0.45f, 0.8f);
            var wireMat = LmMats.Metal(new Color(0.08f, 0.08f, 0.08f), 0.4f, 0.6f);
            var insul = LmMats.Metal(new Color(0.45f, 0.3f, 0.2f), 0.25f, 0f);
            var ledCool = LmMats.Emissive(new Color(0.8f, 0.9f, 1f), 9);
            var ledWarm = LmMats.Emissive(new Color(1f, 0.84f, 0.6f), 5);   // continuous parapet edge strips
            var ledAccent = LmMats.Emissive(new Color(1f, 0.82f, 0.6f), 3);
            var mats = new (string key, Material m)[]
            {
                ("concrete", concrete), ("concreteDark", concreteDark), ("slab", slab),
                ("rail", railMat), ("darkMetal", darkMetal), ("steel", steel),
                ("wire", wireMat), ("insul", insul), ("ledCool", ledCool), ("ledAccent", ledAccent), ("ledWarm", ledWarm),
            };
            foreach (var (key, m) in mats) if (m == null) return "rail: material missing (" + key + ")";

            var b = new LmBatch();

            // ---------- deck (box girder), chunked every 40m ----------
            var prof = new Vector2[]
            {
                new Vector2(-halfW, 0), new Vector2(-halfW, -0.28f), new Vector2(-3f, -0.5f),
                new Vector2(-2.4f, -2.5f), new Vector2(2.4f, -2.5f), new Vector2(3f, -0.5f),
                new Vector2(halfW, -0.28f), new Vector2(halfW, 0),
            };
            for (float x = x0; x < x1; x += 40f)
            {
                var g = LmUtil.ProfileGeometry(prof, x + 0.03f, Mathf.Min(x1, x + 40f) - 0.03f);
                b.Add("concrete", g, 0, H, Zl);
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Box("concrete", 40f - 0.06f, 1.25f, 0.24f, x + 20f, H + 0.62f, Zl + s * (halfW - 0.12f));
                    b.Box("darkMetal", 40f - 0.06f, 0.1f, 0.4f, x + 20f, H + 1.27f, Zl + s * (halfW - 0.1f));
                    // legibility edge strips: continuous warm light along BOTH parapet tops so the
                    // overpass reads as structure from the street (fog darkens the deck face)
                    b.Box("ledWarm", 40f - 0.06f, 0.05f, 0.05f, x + 20f, H + 1.33f, Zl + s * (halfW - 0.02f));
                    b.Box("ledWarm", 40f - 0.06f, 0.05f, 0.05f, x + 20f, H - 2.62f, Zl + s * (halfW - 0.02f));
                    b.Box("concreteDark", 40f - 0.06f, 0.32f, 0.12f, x + 20f, H - 0.2f, Zl + s * (halfW + 0.05f));
                    b.Box("concreteDark", 40f - 0.06f, 0.35f, 0.5f, x + 20f, H + 0.17f, Zl + s * (halfW - 0.55f));
                }
            }

            // ---------- track: slab blocks + rails ----------
            foreach (var (tz, _) in Tracks)
            {
                for (float x = x0; x < x1; x += 5f)
                    b.Box("slab", 4.92f, 0.22f, 2.3f, x + 2.5f, H + 0.11f, tz);
                foreach (var s in new[] { -0.5335f, 0.5335f })
                    b.Box("rail", x1 - x0, 0.16f, 0.075f, (x0 + x1) / 2f, H + 0.3f, tz + s);
            }

            // ---------- pillars ----------
            var pillarXs = new System.Collections.Generic.List<float>();
            for (float x = x0 + 16f; x < x1; x += 32f)
            {
                if (Mathf.Abs(x) < 16f) continue;
                if (x > riverX - riverHw - 20f && x < riverX + riverHw + 20f) continue;
                pillarXs.Add(x);
            }
            pillarXs.AddRange(new[] { -12.5f, 12.5f, riverX - riverHw - 15f, riverX, riverX + riverHw + 15f });
            var capProf = new Vector2[]
            {
                new Vector2(-3.6f, 0), new Vector2(-3.6f, -0.6f), new Vector2(-1.4f, -2f),
                new Vector2(1.4f, -2f), new Vector2(1.4f, -2f), new Vector2(3.6f, -0.6f), new Vector2(3.6f, 0),
            };
            foreach (var px in pillarXs)
            {
                bool inRiver = px > riverX - riverHw && px < riverX + riverHw;
                float gy = inRiver ? riverW - 3f : LmUtil.GroundY(px, Zl);
                float top = H - 2.5f - 2f;
                float h = top - gy;
                b.Add("concrete", _colGeo, px, gy + h / 2f, Zl, 0, 0, 0, 1.05f, h, 1.45f);
                b.Add("concrete", LmUtil.ProfileGeometry(capProf, px - 1.2f, px + 1.2f), 0, H - 2.5f, Zl);
                foreach (var s in new[] { -1.6f, 1.6f })
                    b.Box("darkMetal", 0.9f, 0.12f, 0.9f, px, H - 2.56f, Zl + s);
                if (inRiver) b.Box("concrete", 4.5f, 3.6f, 5f, px, riverW - 1.2f, Zl);
                else b.Box("concreteDark", 2.9f, 0.5f, 3.6f, px, gy + 0.2f, Zl);
                b.Box("darkMetal", 0.16f, h, 0.16f, px + 1f, gy + h / 2f, Zl + 1.25f);
            }

            // ---------- underside LED fixtures + accent strips ----------
            for (float x = x0 + 5f; x < x1; x += 10f)
            {
                foreach (var s in new[] { -1.5f, 1.5f })
                    b.Box("ledCool", 3.2f, 0.06f, 0.18f, x, H - 2.53f, Zl + s);
                b.Box("darkMetal", 1.8f, 0.1f, 0.3f, x, H - 2.47f, Zl - 1.5f);
                b.Box("darkMetal", 1.8f, 0.1f, 0.3f, x, H - 2.47f, Zl + 1.5f);
            }
            for (float x = x0; x < x1; x += 40f)
                foreach (var s in new[] { -1f, 1f })
                    b.Box("ledAccent", 39f, 0.035f, 0.03f, x + 20f, H - 0.36f, Zl + s * (halfW + 0.12f));

            // point lights along the underside
            int lightCount = 0;
            var lightsParent = new GameObject("railLights");
            for (float x = -150f; x <= 270f; x += 18f)
            {
                var lg = new GameObject("railL");
                lg.transform.SetParent(lightsParent.transform, false);
                lg.transform.position = new Vector3(x, H - 3.2f, -Zl); // sim -> unity
                var li = lg.AddComponent<Light>();
                li.type = LightType.Point;
                li.color = new Color(0.75f, 0.84f, 1f);
                li.intensity = 70f;
                li.range = 20f;
                lightCount++;
            }

            // ---------- catenary ----------
            var masts = new System.Collections.Generic.List<float>();
            for (float x = x0 + 25f; x < x1; x += 50f) masts.Add(x);
            for (int i = 0; i < masts.Count; i++)
            {
                float mx = masts[i];
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Box("steel", 0.28f, 7.6f, 0.08f, mx, H + 3.8f, Zl + s * 4.05f);
                    b.Box("steel", 0.06f, 7.6f, 0.3f, mx, H + 3.8f, Zl + s * 4.05f);
                    b.Box("steel", 0.5f, 0.3f, 0.5f, mx, H + 0.15f, Zl + s * 4.05f);
                    foreach (var (tz, _) in Tracks)
                    {
                        float stagger = i % 2 == 1 ? 0.2f : -0.2f;
                        var a = new Vector3(mx, H + 6.15f, tz + stagger);
                        LmUtil.BeamBetween(b, "steel", new Vector3(mx, H + 6.6f, tz + stagger), a, 0.06f, 0.06f);
                        LmUtil.BeamBetween(b, "steel", a, new Vector3(mx, H + 5.1f, tz + stagger), 0.04f, 0.04f);
                        b.Box("insul", 0.12f, 0.35f, 0.12f, mx, H + 5.4f, tz + stagger);
                    }
                }
                float ya = H + 6.6f, yb = H + 7.3f;
                foreach (var y in new[] { ya, yb })
                    b.Box("steel", 0.14f, 0.14f, 8.4f, mx, y, Zl);
                for (int k = 0; k < 8; k++)
                {
                    float za = Zl - 4.05f + k * (8.1f / 8f), zb = za + 8.1f / 8f;
                    LmUtil.BeamBetween(b, "steel",
                        new Vector3(mx, k % 2 == 1 ? ya : yb, za),
                        new Vector3(mx, k % 2 == 1 ? yb : ya, zb), 0.05f, 0.05f);
                }
            }

            // sagging wires between masts
            void Wire(float y0, float sag, float z0, float z1, float xa, float xb, float th = 0.022f)
            {
                const int n = 8;
                for (int k = 0; k < n; k++)
                {
                    float t0 = k / (float)n, t1 = (k + 1) / (float)n;
                    float wa = y0 - sag * 4f * t0 * (1f - t0), wb = y0 - sag * 4f * t1 * (1f - t1);
                    LmUtil.BeamBetween(b, "wire",
                        new Vector3(xa + (xb - xa) * t0, wa, z0 + (z1 - z0) * t0),
                        new Vector3(xa + (xb - xa) * t1, wb, z0 + (z1 - z0) * t1), th, th);
                }
            }
            var allM = new System.Collections.Generic.List<float> { x0 };
            allM.AddRange(masts);
            allM.Add(x1);
            for (int i = 0; i < allM.Count - 1; i++)
            {
                float xa = allM[i], xb = allM[i + 1];
                foreach (var (tz, _) in Tracks)
                {
                    float sa = i % 2 == 1 ? -0.2f : 0.2f, sb = -sa;
                    Wire(H + 6.15f, 0.55f, tz + sa, tz + sb, xa, xb);
                    Wire(H + 5.1f, 0f, tz + sa, tz + sb, xa, xb, 0.018f);
                    for (int d = 1; d < 8; d++)
                    {
                        float tt = d / 8f, xm = xa + (xb - xa) * tt, zm = tz + sa + (sb - sa) * tt;
                        float ym = H + 6.15f - 0.55f * 4f * tt * (1f - tt);
                        b.Box("wire", 0.012f, ym - (H + 5.1f), 0.012f, xm, (ym + H + 5.1f) / 2f, zm);
                    }
                }
                foreach (var s in new[] { -1f, 1f })
                    Wire(H + 7.55f, 0.8f, Zl + s * 4.05f, Zl + s * 4.05f, xa, xb, 0.03f);
            }

            lightsParent.transform.SetParent(root, false);
            var built = b.Build(root, "rail_", 80f, new HashSet<string> { "concrete", "concreteDark", "slab" });
            foreach (var go in built)
            {
                var key = go.name.Substring("rail_".Length);
                foreach (var (k, m) in mats)
                    if (k == key) { go.GetComponent<MeshRenderer>().sharedMaterial = m; break; }
            }
            int tris = 0;
            foreach (var go in built)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            return $"rail: {built.Count} meshes {tris} tris, {pillarXs.Count} pillars, {masts.Count} masts, {lightCount} lights";
        }
    }
}
