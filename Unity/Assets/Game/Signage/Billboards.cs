// Port of signage/billboards.js: 未来へ Mirai animated billboard (housing/legs/LED strips/steel +
// 4 pink-violet pool lights), KAZE TECH letters (light 60/20), corner video screens (up to 7,
// purple 70/22), 3 floating idol holograms (additive, 30/18 lights).
// ALL placement math runs in SIM coordinates (like the JS), then mirrored to Unity (x,y,-z).
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class Billboards
    {
        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);


        public static string Build(Material mirai, Material screen, Material holo, Material neonQuad, Material steelBody)
        {
            var old = GameObject.Find("/Signage");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Signage").transform;

            var facades = FacadeAPI.Collect();

            Vector3 UP = new Vector3(0, 1, 0);
            Vector3 ov = new Vector3((float)Layout.OverlookPosition[0], (float)Layout.OverlookPosition[1], (float)Layout.OverlookPosition[2]);
            Vector3 look = new Vector3((float)Layout.OverlookLookAt[0], (float)Layout.OverlookLookAt[1], (float)Layout.OverlookLookAt[2]);
            Vector3 fwd = look - ov; fwd.y = 0; fwd.Normalize();
            Vector3 right = Vector3.Cross(fwd, UP).normalized; // sim cross (right-handed like three)
            float bbW = 13, bbH = 19.5f;
            Vector3 center = ov + fwd * 50 + right * -14.5f;
            Vector3 normal = ov - center; normal.y = 0; normal.Normalize();

            // prefer a real facade roughly facing the overlook near that spot
            SFacade mounted = null; float mS = float.MaxValue;
            foreach (var f in facades)
            {
                Vector3 toCam = ov - f.CS(); float d = toCam.magnitude; toCam.Normalize();
                if (Vector3.Dot(f.NS(), toCam) < 0.75f || d < 30 || d > 90 || f.Width < bbW + 1 || f.Height < 24) continue;
                Vector3 dir = f.CS() - ov; dir.y = 0; dir.Normalize();
                float ang = Mathf.Atan2(Vector3.Cross(dir, fwd).y, Vector3.Dot(dir, fwd)); // + = left of forward
                if (ang < 0.15f || ang > 0.7f) continue;
                if (mounted == null || Mathf.Abs(ang - 0.42f) < mS) { mounted = f; mS = Mathf.Abs(ang - 0.42f); }
            }
            if (mounted != null) { center = mounted.CS() + mounted.NS() * 0.6f; normal = mounted.NS(); }
            center.y = ov.y - 2;

            // makeFacade: right = cross(UP, n).normalize()
            Vector3 bRight = Vector3.Cross(UP, normal).normalized;

            var V = new List<Vector3>(); var Ix = new List<int>(); var C = new List<Color>(); var UV = new List<Vector2>();
            var NV = new List<Vector3>(); var NI = new List<int>(); var NC = new List<Color>(); var NUV = new List<Vector2>();
            var lights = new List<(Vector3 pos, Color col, float inten, float range)>();

            void Quad(List<Vector3> vv, List<int> ii, List<Color> cc, List<Vector2> uu, Vector3 c, Vector3 r, Vector3 u, float hw, float hh, Color tint)
            {
                int b = vv.Count;
                Vector3[] p = { c - r * hw - u * hh, c + r * hw - u * hh, c + r * hw + u * hh, c - r * hw + u * hh };
                Vector2[] t = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                for (int i = 0; i < 4; i++) { vv.Add(Mir(p[i])); cc.Add(tint); uu.Add(t[i]); }
                // The sim->unity mirror flips handedness, so reverse the winding or the front
                // face ends up pointing away from the camera (Cull Back hides it).
                ii.Add(b); ii.Add(b + 2); ii.Add(b + 1); ii.Add(b); ii.Add(b + 3); ii.Add(b + 2);
            }

            // oriented box in sim space
            void Hbox(Vector3 c, Vector3 r, Vector3 u, Vector3 n, float ex, float ey, float ez, Color col)
            {
                var p = new Vector3[8];
                int i = 0;
                for (int sz = -1; sz <= 1; sz += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sx = -1; sx <= 1; sx += 2)
                            p[i++] = c + r * (sx * ex) + u * (sy * ey) + n * (sz * ez);
                int[][] quads = {
                    new[] { 1, 3, 7, 5 }, new[] { 4, 6, 2, 0 }, new[] { 2, 6, 7, 3 },
                    new[] { 0, 1, 5, 4 }, new[] { 5, 7, 6, 4 }, new[] { 0, 2, 3, 1 }
                };
                int b = V.Count;
                foreach (var q in quads)
                {
                    foreach (int vi in q) { V.Add(Mir(p[vi])); C.Add(col); UV.Add(Vector2.zero); }
                    // mirrored z flips handedness -> reverse each triangle (front face outward)
                    Ix.Add(b); Ix.Add(b + 2); Ix.Add(b + 1); Ix.Add(b); Ix.Add(b + 3); Ix.Add(b + 2);
                    b += 4;
                }
            }

            // box beam between two sim points
            void Bar(Vector3 bot, Vector3 top, float r, Color col)
            {
                Vector3 axis = top - bot;
                float len = axis.magnitude; if (len < 1e-4f) return;
                axis /= len;
                Vector3 side = Mathf.Abs(Vector3.Dot(axis, UP)) > 0.9f ? new Vector3(1, 0, 0) : UP;
                Vector3 t1 = Vector3.Cross(axis, side).normalized * r;
                Vector3 t2 = Vector3.Cross(axis, t1).normalized * r;
                var p = new Vector3[8];
                int i = 0;
                for (int e = 0; e < 2; e++)
                {
                    Vector3 c = e == 0 ? bot : top;
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sx = -1; sx <= 1; sx += 2)
                            p[i++] = c + t1 * (sx * r) + t2 * (sy * r);
                }
                int[][] quads = {
                    new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 },
                    new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }
                };
                int b = V.Count;
                foreach (var q in quads)
                {
                    foreach (int vi in q) { V.Add(Mir(p[vi])); C.Add(col); UV.Add(Vector2.zero); }
                    Ix.Add(b); Ix.Add(b + 2); Ix.Add(b + 1); Ix.Add(b); Ix.Add(b + 3); Ix.Add(b + 2);
                    b += 4;
                }
            }

            Color hcol = new Color(0.03f, 0.03f, 0.035f);
            Color steel = new Color(0.1f, 0.1f, 0.11f);
            Color pink = new Color(1.0f, 0.25f, 0.6f);

            // ---------- Mirai billboard ----------
            {
                Vector3 fc = center + normal * 0.42f;
                Quad(V, Ix, C, UV, fc, bRight, UP, bbW / 2, bbH / 2, Color.white); // screen
                Hbox(center, bRight, UP, normal, bbW / 2 + 0.35f, bbH / 2 + 0.35f, 0.4f, hcol); // cabinet
                for (int s = -1; s <= 1; s += 2)
                {
                    Hbox(fc + UP * (s * (bbH / 2 + 0.17f)), bRight, UP, normal, bbW / 2 + 0.35f, 0.17f, 0.05f, hcol);
                    Hbox(fc + bRight * (s * (bbW / 2 + 0.17f)), bRight, UP, normal, 0.17f, bbH / 2, 0.05f, hcol);
                    // LED edge strip (emissive): use NeonQuad mesh list with vertex tint x3
                    Quad(NV, NI, NC, NUV, fc + UP * (s * (bbH / 2 + 0.36f)), bRight, normal, bbW / 2 + 0.35f, 0.02f, pink * 3f);
                }
                // steel legs + cross bracing
                Vector3 back = center + normal * -0.6f;
                for (int k = 0; k < 2; k++)
                {
                    float s = k == 0 ? -0.38f : 0.38f;
                    Vector3 top = back + bRight * (s * bbW); top.y = center.y + bbH / 2;
                    Vector3 bot = top; bot.y = 0;
                    Bar(bot, top, 0.32f, steel);
                }
                for (float y = 2; y < center.y - bbH / 2 - 0.5f; y += 4)
                {
                    Vector3 a = back + bRight * (-0.38f * bbW); a.y = y;
                    Vector3 b = back + bRight * (0.38f * bbW); b.y = y + 4;
                    Bar(a, b, 0.08f, steel);
                    Vector3 a2 = a; a2.y = y + 4;
                    Vector3 b2 = b; b2.y = y;
                    Bar(a2, b2, 0.08f, steel);
                }
                var casts = new[] {
                    (-0.3f, -0.3f, new Color(1f, 0.3f, 0.7f)),
                    (0.3f, -0.3f, new Color(0.7f, 0.3f, 1f)),
                    (0.0f, 0.3f, new Color(1f, 0.45f, 0.8f)),
                    (0.0f, -0.55f, new Color(0.9f, 0.35f, 0.9f)),
                };
                foreach (var (sx, sy, c) in casts)
                    lights.Add((center + bRight * (sx * bbW) + normal * 4 + Vector3.up * (sy * bbH), c, 160f, 24f));
            }

            // ---------- KAZE TECH letters above the billboard ----------
            {
                // kaze_tech.png is a 1536x256 sheet with the glyphs in a 341..1184 x 64..190 box;
                // the JS draws the channel-letter rect, not the whole sheet, so crop and size the
                // quad to the glyphs (aspect 6.76) instead of leaving a dark letterboxed bar.
                const float glyphAspect = 6.762f;
                float kW = 15, kH = kW / glyphAspect;
                Vector3 kc = center + normal * 0.3f; kc.y = center.y + bbH / 2 + 1.4f + kH / 2;
                Quad(NV, NI, NC, NUV, kc, bRight, UP, kW / 2, kH / 2, Color.white);
                for (int i = NUV.Count - 4; i < NUV.Count; i++)
                    NUV[i] = new Vector2(Mathf.Lerp(341f / 1536f, 1184f / 1536f, NUV[i].x),
                                         Mathf.Lerp(0.258f, 0.75f, NUV[i].y));
                Vector3 rc = center + bbH / 2 * Vector3.up;
                Bar(rc + normal * -0.3f + bRight * (-kW * 0.4f), kc + normal * -0.3f + bRight * (-kW * 0.4f), 0.06f, steel);
                Bar(rc + normal * -0.3f + bRight * (kW * 0.4f), kc + normal * -0.3f + bRight * (kW * 0.4f), 0.06f, steel);
                Bar(kc + normal * -0.3f + bRight * (-kW / 2), kc + normal * -0.3f + bRight * (kW / 2), 0.05f, steel);
                lights.Add((kc + normal * 2, new Color(0.35f, 0.9f, 1f), 60f, 20f));
            }

            // ---------- corner video screens ----------
            int sIdx = 0;
            var SV = new List<Vector3>(); var SI = new List<int>(); var SUV = new List<Vector2>(); var SSD = new List<Vector2>();
            foreach (var f in facades)
            {
                if (sIdx > 6) break;
                bool crossOrAve = f.District == "cross" || f.District == "avenue";
                if (!crossOrAve || f.NS().z <= 0.8f || Mathf.Abs(f.CS().x) >= 40 || f.Height <= 20) continue;
                if (Mathf.Abs(f.CS().x) - (float)f.Width / 2 >= 14) continue;
                float w = Mathf.Min((float)(f.Width * 0.7), 9), h = w;
                Vector3 c = f.CS() + f.NS() * 0.35f;
                c.y = Mathf.Min((float)(f.CS().y + f.Height / 2 - h / 2 - 1.5), 12 + h / 2 + (sIdx % 3) * 3);
                Hbox(c + f.NS() * -0.15f, f.RS(), UP, f.NS(), w / 2 + 0.25f, h / 2 + 0.25f, 0.2f, hcol);
                // screen quad with sd in uv2
                {
                    Vector3 sc = c + f.NS() * 0.06f;
                    int b = SV.Count;
                    var corners = new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) };
                    foreach (var (su, sv) in corners)
                    {
                        SV.Add(Mir(sc + f.RS() * (su * w / 2) + UP * (sv * h / 2)));
                        SUV.Add(new Vector2((su + 1) / 2, (sv + 1) / 2));
                        SSD.Add(new Vector2(sIdx * 1.7f, 2.4f));
                    }
                    SI.Add(b); SI.Add(b + 2); SI.Add(b + 1); SI.Add(b); SI.Add(b + 3); SI.Add(b + 2);
                }
                lights.Add((c + f.NS() * 4, new Color(0.8f, 0.5f, 1f), 70f, 22f));
                sIdx++;
            }

            // ---------- floating holograms over the avenue ----------
            var cz = Layout.CrossZs;
            var holoSpecs = new[]
            {
                (new Vector2(1, 0), new Vector3(-3, 15, (float)cz[0] - 4), 9f, 9f, new Color(1f, 0.4f, 0.9f), 0.15f),
                (new Vector2(0, 1), new Vector3(4, 17, (float)cz[1] + 3), 12f, 12f, new Color(1f, 0.5f, 0.25f), -0.1f),
                (new Vector2(0, 0), new Vector3(-2, 14, (float)cz[2]), 10f, 10f, new Color(0.3f, 1f, 0.8f), 0f),
            };
            int hi = 0;
            foreach (var (cell, pos, w, h, tint, yaw) in holoSpecs)
            {
                var go = new GameObject($"holo_{hi}");
                go.transform.SetParent(root, false);
                go.transform.position = Mir(pos);
                go.transform.rotation = Quaternion.Euler(0, -yaw * Mathf.Rad2Deg, 0);
                var mf = go.AddComponent<MeshFilter>();
                var m = new Mesh();
                var vv = new List<Vector3>(); var ii = new List<int>(); var uu = new List<Vector2>(); var cc = new List<Color>();
                Quad(vv, ii, cc, uu, Vector3.zero, Vector3.right, Vector3.up, w / 2, h / 2, Color.white);
                m.SetVertices(vv); m.SetIndices(ii, MeshTopology.Triangles, 0); m.SetUVs(0, uu);
                m.RecalculateNormals(); m.RecalculateBounds();
                mf.sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = holo;
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_Tint", new Vector4(tint.r, tint.g, tint.b, 1));
                mpb.SetVector("_Cell", new Vector4(cell.x, cell.y, 0, 0));
                mr.SetPropertyBlock(mpb);
                lights.Add((pos + Vector3.down * (h / 2), tint, 30f, 18f));
                hi++;
            }

            // commit merged meshes — MiraiScreen: submesh 0 = screen quad (mirai), submesh 1 = housing/steel boxes
            {
                var mesh = new Mesh { name = "MiraiScreen" };
                mesh.SetVertices(V);
                mesh.SetColors(C);
                mesh.SetUVs(0, UV);
                mesh.subMeshCount = 2;
                mesh.SetIndices(Ix.GetRange(0, 6), MeshTopology.Triangles, 0);
                mesh.SetIndices(Ix.GetRange(6, Ix.Count - 6), MeshTopology.Triangles, 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = new GameObject("MiraiScreen");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { mirai, steelBody };
            }
            Commit(root, "KazeTech", NV, NI, NC, NUV, null, neonQuad);
            Commit(root, "VideoScreens", SV, SI, null, SUV, SSD, screen);

            // lights
            for (int j = 0; j < lights.Count; j++)
            {
                var (p, c, inten, range) = lights[j];
                var go = new GameObject($"sgnL_{j}");
                go.transform.SetParent(root, false);
                go.transform.position = Mir(p);
                var pl = go.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = c;
                pl.range = range;
                pl.intensity = inten;
                pl.shadows = LightShadows.None;
            }

            // re-push full light arrays (buildings + lamps + signage)
            var all = new List<Light>();
            foreach (var rn in new[] { "/Buildings", "/StreetLamps", "/Signage" })
            {
                var r = GameObject.Find(rn);
                if (r != null) all.AddRange(r.GetComponentsInChildren<Light>());
            }
            if (all.Count > 0)
            {
                var blPos = new Vector4[all.Count];
                var blCol = new Vector4[all.Count];
                for (int j = 0; j < all.Count; j++)
                {
                    var l = all[j];
                    blPos[j] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                    blCol[j] = new Vector4(l.color.r, l.color.g, l.color.b, l.intensity);
                }
                BuildingsRuntime.PushLightArrays(blPos, blCol, new List<Material>(), null);
            }
            return $"signage: mirai mounted={mounted != null} screens={sIdx} holos=3 lights={lights.Count}";
        }

        static void Commit(Transform root, string name, List<Vector3> V, List<int> Ix, List<Color> C, List<Vector2> UV, List<Vector2> UV2, Material mat)
        {
            if (V.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(V);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            if (C != null) mesh.SetColors(C); else { var w = new List<Color>(V.Count); for (int i = 0; i < V.Count; i++) w.Add(Color.white); mesh.SetColors(w); }
            if (UV != null) mesh.SetUVs(0, UV);
            if (UV2 != null) mesh.SetUVs(1, UV2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
