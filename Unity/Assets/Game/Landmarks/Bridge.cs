// Port of src/systems/landmarks/bridge.js — steel tied-arch bridge across the
// river: concrete deck, painted girders, glass railings, twin lit arches with
// travelling cyan pulse, hanger cables, piers, lamp posts, stairs down to both
// promenades. Sim-space placements (LmBatch mirrors centrally).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Bridge
    {
        public static string Build(Transform root)
        {
            const float Z = -260f, D = 8f;          // layout.bridges[0]
            const float Xw = 140f, Xe = 210f;       // river walls
            const float Xa = Xw - 14f, Xb = Xe + 14f;
            const float Hw = 6f;
            const float Rise = 13f;
            const float WaterY = -3f;

            var b = new LmBatch();
            // deck
            b.Box("concrete", Xb - Xa, 0.45f, Hw * 2f, (Xa + Xb) / 2f, D - 0.22f, Z);
            // walkable deck collider (top at D+0.045)
            {
                var cgo = new GameObject("bridge_deck_col");
                cgo.transform.SetParent(root, false);
                cgo.transform.position = new Vector3((Xa + Xb) / 2f, D - 0.1f, Z);
                var bc = cgo.AddComponent<BoxCollider>();
                bc.size = new Vector3(Xb - Xa, 0.3f, Hw * 2f);
            }
            b.Box("paving", Xb - Xa, 0.05f, Hw * 2f - 0.8f, (Xa + Xb) / 2f, D + 0.02f, Z);
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                b.Box("steelPaint", Xb - Xa, 1.4f, 0.5f, (Xa + Xb) / 2f, D - 0.9f, Z + s * (Hw - 0.25f));
                b.Box("ledCyan", Xb - Xa - 2f, 0.05f, 0.04f, (Xa + Xb) / 2f, D - 1.55f, Z + s * (Hw + 0.02f));
                for (float x = Xa + 1f; x < Xb; x += 2f)
                    b.Box("steelPaint", 0.08f, 1.1f, 0.08f, x, D + 0.55f, Z + s * (Hw - 0.15f));
                b.Box("steelPaint", Xb - Xa, 0.08f, 0.12f, (Xa + Xb) / 2f, D + 1.1f, Z + s * (Hw - 0.15f));
                b.Box("glass", Xb - Xa, 0.9f, 0.03f, (Xa + Xb) / 2f, D + 0.55f, Z + s * (Hw - 0.15f));
            }
            for (float x = Xa + 2f; x < Xb; x += 6f)
                b.Box("steelPaint", 0.4f, 0.9f, Hw * 2f - 0.6f, x, D - 0.85f, Z);

            // arches + hangers
            float Ax0 = Xw + 2f, Ax1 = Xe - 2f;
            Vector3 ArchPt(float t, float zr) => new Vector3(Ax0 + (Ax1 - Ax0) * t, D + Rise * 4f * t * (1f - t), zr);
            for (int si = 0; si < 2; si++)
            {
                float s = si == 0 ? -1f : 1f;
                float zr = Z + s * (Hw - 0.25f);
                const int N = 28;
                for (int i = 0; i < N; i++)
                {
                    var a = ArchPt((float)i / N, zr);
                    var c = ArchPt((float)(i + 1) / N, zr);
                    LmUtil.BeamBetween(b, "steelPaint", a, c, 0.7f, 1.0f);
                    var al = a; al.y -= 0.55f;
                    var cl = c; cl.y -= 0.55f;
                    LmUtil.BeamBetween(b, "ledArch", al, cl, 0.2f, 0.06f);
                }
                for (int i = 1; i < 14; i++)
                {
                    float t = (float)i / 14f;
                    var a = ArchPt(t, zr);
                    b.Box("cable", 0.06f, a.y - D, 0.06f, a.x, (a.y + D) / 2f, zr);
                }
            }
            foreach (var t in new[] { 0.3f, 0.5f, 0.7f })
            {
                var a = ArchPt(t, Z);
                b.Box("steelPaint", 0.5f, 0.6f, Hw * 2f - 0.5f, a.x, a.y, Z);
            }

            // piers + abutments
            foreach (var px in new[] { Xw - 0.5f, Xe + 0.5f })
            {
                float gy = WaterY - 3f;
                b.Box("concrete", 3.0f, D - 1.6f - gy, Hw * 2f + 1f, px, (D - 1.6f + gy) / 2f, Z);
            }
            foreach (var px in new[] { Xa + 1.5f, Xb - 1.5f })
            {
                float gy = LmUtil.GroundY(px, Z);
                b.Box("concrete", 3.0f, D - 1.6f - gy, Hw * 2f, px, (D - 1.6f + gy) / 2f, Z);
            }

            // lamp posts + lights
            var lightsParent = new GameObject("bridgeLights");
            int lampCount = 0;
            for (float x = Xa + 6f; x < Xb - 3f; x += 16f)
            {
                for (int si = 0; si < 2; si++)
                {
                    float s = si == 0 ? -1f : 1f;
                    float zz = Z + s * (Hw - 0.5f);
                    b.Box("steelPaint", 0.14f, 5.2f, 0.14f, x, D + 2.6f, zz);
                    b.Box("steelPaint", 0.5f, 0.18f, 0.5f, x, D + 5.25f, zz);
                    b.Box("lampWarm", 0.42f, 0.06f, 0.42f, x, D + 5.13f, zz);
                    var lg = new GameObject("brL");
                    lg.transform.SetParent(lightsParent.transform, false);
                    lg.transform.position = new Vector3(x, D + 4.8f, -zz + s * 0.4f); // unity: mirror + s-flip
                    var li = lg.AddComponent<Light>();
                    li.type = LightType.Point;
                    li.color = new Color(1f, 0.827f, 0.627f);
                    li.intensity = 90f;
                    li.range = 20f;
                    lampCount++;
                }
            }
            foreach (var x in new[] { Xw + 12f, 175f, Xe - 12f })
            {
                var lg = new GameObject("brFlood");
                lg.transform.SetParent(lightsParent.transform, false);
                lg.transform.position = new Vector3(x, D - 2.2f, -Z);
                var li = lg.AddComponent<Light>();
                li.type = LightType.Point;
                li.color = new Color(0.624f, 0.91f, 1f);
                li.intensity = 120f;
                li.range = 22f;
                lampCount++;
            }

            // stairs down to both promenades
            int stairCount = 0;
            foreach (var st in new (float x, float dir)[] { (Xw - 9f, 1f), (Xe + 9f, -1f) })
            {
                float z0 = Z + Hw + 1f, run = 14f;
                const int steps = 28;
                float gy = LmUtil.GroundY(st.x, z0 + run);
                b.Box("concrete", 3.2f, 0.4f, 3f, st.x, D - 0.2f, Z + Hw + 0.5f);
                for (int i = 0; i < steps; i++)
                {
                    float y = D - (i + 1) * (D - gy) / steps;
                    float z = z0 + (i + 0.5f) * run / steps;
                    b.Box("paving", 3.0f, 0.06f, run / steps + 0.02f, st.x, y + (D - gy) / steps, z);
                    b.Box("concrete", 3.0f, (D - gy) / steps, run / steps, st.x, y + (D - gy) / steps / 2f, z);
                }
                for (int si = 0; si < 2; si++)
                {
                    float s = si == 0 ? -1f : 1f;
                    var a = new Vector3(st.x + s * 1.6f, D + 0.5f, z0);
                    var c = new Vector3(st.x + s * 1.6f, gy + 0.5f, z0 + run);
                    LmUtil.BeamBetween(b, "concrete", a, c, 0.25f, 1.2f);
                    var a2 = a; a2.y += 0.5f;
                    var c2 = c; c2.y += 0.5f;
                    LmUtil.BeamBetween(b, "steelPaint", a2, c2, 0.06f, 0.06f);
                    var a3 = a; a3.y -= 0.35f; a3.x -= s * 0.14f;
                    var c3 = c; c3.y -= 0.35f; c3.x -= s * 0.14f;
                    LmUtil.BeamBetween(b, "ledWarm", a3, c3, 0.02f, 0.04f);
                }
                stairCount++;
            }

            lightsParent.transform.SetParent(root, false);
            var concrete = LmMats.Surface("concrete", tint: new Color(0.8f, 0.8f, 0.8f), uvScale: 1f / 3.5f);
            var paving = LmMats.Surface("stonewall", tint: new Color(0.5f, 0.5f, 0.52f), uvScale: 1f / 1.5f);
            var steelPaint = LmMats.Metal(new Color(0.16f, 0.18f, 0.2f), 0.4f, 0.6f);
            var cable = LmMats.Metal(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.2f);
            var ledCyan = LmMats.Emissive(new Color(0.2f, 0.85f, 1f), 6);
            var ledWarm = LmMats.Emissive(new Color(1f, 0.7f, 0.4f), 4);
            var lampWarm = LmMats.Emissive(new Color(1f, 0.85f, 0.65f), 25);
            var ledArchM = LmMats.ShaderMat("NEON KAIDO/BridgeLedArch");
            var glassM = LmMats.ShaderMat("NEON KAIDO/BridgeGlass");
            if (concrete == null || paving == null || steelPaint == null || cable == null || ledCyan == null ||
                ledWarm == null || lampWarm == null || ledArchM == null || glassM == null)
                return "bridge: material missing";

            var built = b.Build(root, "bridge_", 60f, new HashSet<string> { "concrete", "paving" });
            foreach (var go in built)
            {
                var key = go.name.Substring("bridge_".Length);
                Material m = key == "concrete" ? concrete : key == "paving" ? paving
                    : key == "steelPaint" ? steelPaint : key == "cable" ? cable
                    : key == "ledCyan" ? ledCyan : key == "ledWarm" ? ledWarm
                    : key == "lampWarm" ? lampWarm : key == "ledArch" ? ledArchM : glassM;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            int tris = 0;
            foreach (var go in built)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            return $"bridge: {built.Count} meshes {tris} tris, {stairCount} stairs, {lampCount} lights";
        }
    }
}
