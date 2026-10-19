// Port of signage/lanterns.js + the lantern data of signage/featured.js.
// Chōchin paper lanterns under awnings and across the alley, tall やきとり lanterns,
// andon floor lanterns on the river promenade. All placement math in SIM space
// (mirrored at mesh build); one mesh per batch (paper/caps/wires/andon/andonFrame),
// sway in the vertex shader around each hang pivot; lights join the shared sign pool.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;

namespace NeonKaido
{
    public static class Lanterns
    {
        const float PI = 3.14159265f;

        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        class RawMesh { public Vector3[] V; public int[] I; public Vector2[] UV; }

        // raw GLB dump: sim space (glTF z), node matrixWorld already baked by dump-lanterns.mjs
        static RawMesh LoadRaw(string name)
        {
            string dir = "Assets/Game/Models/Signage/dump";
            var meta = System.IO.File.ReadAllText($"{dir}/{name}_meta.txt").Trim().Split(' ');
            int nVerts = int.Parse(meta[0]), nIdx = int.Parse(meta[1]);
            bool u32 = meta[2] == "32";
            var pos = System.IO.File.ReadAllBytes($"{dir}/{name}_pos.bin");
            var idx = System.IO.File.ReadAllBytes($"{dir}/{name}_idx.bin");
            var uvb = System.IO.File.ReadAllBytes($"{dir}/{name}_uv.bin");
            var m = new RawMesh { V = new Vector3[nVerts], I = new int[nIdx], UV = new Vector2[nVerts] };
            for (int i = 0; i < nVerts; i++)
            {
                m.V[i] = new Vector3(System.BitConverter.ToSingle(pos, i * 12), System.BitConverter.ToSingle(pos, i * 12 + 4), System.BitConverter.ToSingle(pos, i * 12 + 8));
                m.UV[i] = new Vector2(System.BitConverter.ToSingle(uvb, i * 8), System.BitConverter.ToSingle(uvb, i * 8 + 4));
            }
            for (int i = 0; i < nIdx; i++)
                m.I[i] = u32 ? (int)System.BitConverter.ToUInt32(idx, i * 4) : System.BitConverter.ToUInt16(idx, i * 2);
            return m;
        }

        class LBatch
        {
            public List<Vector3> V = new List<Vector3>();
            public List<int> I = new List<int>();
            public List<Vector2> UV = new List<Vector2>();
            public List<Vector4> Piv = new List<Vector4>(), Cell = new List<Vector4>(), LP = new List<Vector4>();
            public int Count => V.Count;

            // bake sim-space matrix into raw mesh verts (sim), mirror at push
            public void AddGeo(RawMesh src, Matrix4x4 m, Vector3 pivotSim, Vector4 cell, Vector4 lp)
            {
                int b = V.Count;
                for (int i = 0; i < src.V.Length; i++)
                {
                    V.Add(Mir(m.MultiplyPoint3x4(src.V[i])));
                    UV.Add(src.UV[i]);
                    Piv.Add(Mir(pivotSim));
                    Cell.Add(cell);
                    LP.Add(lp);
                }
                foreach (int ix in src.I) I.Add(b + ix);
            }

            // JS Batch.quad(): 4 verts around centre c, axes u/v, half sizes hu/hv, CCW wrt n (sim)
            public void Quad(Vector3 c, Vector3 u, Vector3 v, float hu, float hv, Vector3 n, Vector3 pivotSim, Vector4 cell, Vector4 lp)
            {
                int b = V.Count;
                Vector3 p(int su, int sv) => c + u * (su * hu) + v * (sv * hv);
                V.Add(Mir(p(-1, -1))); UV.Add(new Vector2(0, 0));
                V.Add(Mir(p(1, -1))); UV.Add(new Vector2(hu * 2, 0));
                V.Add(Mir(p(1, 1))); UV.Add(new Vector2(hu * 2, hv * 2));
                V.Add(Mir(p(-1, 1))); UV.Add(new Vector2(0, hv * 2));
                for (int i = 0; i < 4; i++) { Piv.Add(Mir(pivotSim)); Cell.Add(cell); LP.Add(lp); }
                // JS ensures CCW wrt n in sim space; global flip at commit handles the mirror
                var cr = Vector3.Cross(u, v);
                if (Vector3.Dot(cr, n) >= 0) { I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3); }
                else { I.Add(b); I.Add(b + 2); I.Add(b + 1); I.Add(b); I.Add(b + 3); I.Add(b + 2); }
            }

            // JS Batch.box(): centre c, unit axes (x,y,z), half sizes; 6 faces
            public void Box(Vector3 c, Vector3 X, Vector3 Y, Vector3 Z, float hx, float hy, float hz, Vector3 pivotSim, Vector4 cell, Vector4 lp)
            {
                Quad(c + X * hx, Z, Y, hz, hy, X, pivotSim, cell, lp);
                Quad(c - X * hx, -Z, Y, hz, hy, -X, pivotSim, cell, lp);
                Quad(c + Y * hy, X, Z, hx, hz, Y, pivotSim, cell, lp);
                Quad(c - Y * hy, -X, Z, hx, hz, -Y, pivotSim, cell, lp);
                Quad(c + Z * hz, X, Y, hx, hy, Z, pivotSim, cell, lp);
                Quad(c - Z * hz, -X, Y, hx, hy, -Z, pivotSim, cell, lp);
            }
        }

        static Mulberry32 _rnd;
        static double Next() => _rnd.Next();
        static float Range(float a, float b) => (float)(a + (b - a) * Next());

        // JS rnd.pick(arr)
        static string Pick(string[] arr) => arr[(int)(Next() * arr.Length)];

        // JS nearestFacade(): horizontal sim-space distance, minDot on facade normal
        static SFacade Nearest(List<SFacade> fs, Vector3 p, Vector3 n, float minDot, HashSet<SFacade> reserved)
        {
            SFacade best = null; float bd = float.MaxValue;
            foreach (var f in fs)
            {
                if (Vector3.Dot(f.Normal, n) < minDot) continue;
                if (reserved != null && reserved.Contains(f)) continue;
                var dc = new Vector3(f.Center.x - p.x, 0, f.Center.z - p.z);
                float d = dc.magnitude;
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        static float AlongTo(SFacade f, Vector3 p) => Vector3.Dot(p - f.Center, f.Right);

        // JS alleyGroundY(lay, x): stairs from xStart=-40 (y0) down to xEnd=-62 (y=rise 7)
        static float AlleyGroundY(float x)
        {
            const float xs = -40, xe = -62, rise = 7;
            if (x > xs) return 0;
            if (x < xe) return rise;
            return rise * (xs - x) / (xs - xe);
        }

        class Row { public Vector3 p0, p1; public int count; public string[] texts; public float scale; }
        class Str { public Vector3 a, b; public float sag; public int count; }
        class Tall { public Vector3 pos; public string text; }

        static Material _paper, _caps, _wire, _frame;
        static string EnsureMats()
        {
            if (_paper != null) return null;
            var psh = Shader.Find("NEON KAIDO/LanternPaper");
            var csh = Shader.Find("NEON KAIDO/LanternCaps");
            var wsh = Shader.Find("NEON KAIDO/LanternPlain");
            if (psh == null || csh == null || wsh == null) return "lantern shader missing";
            _paper = new Material(psh);
            _caps = new Material(csh);
            _wire = new Material(wsh); _wire.SetColor("_Color", new Color(0x05 / 255f, 0x05 / 255f, 0x05 / 255f));
            _frame = new Material(wsh); _frame.SetColor("_Color", new Color(0x1a / 255f, 0x0e / 255f, 0x08 / 255f));
            var pt = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Textures/Signage/lantern_paper.png");
            var ca = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Textures/Signage/color_atlas.png");
            if (pt == null || ca == null) return "lantern textures missing";
            _paper.SetTexture("_Paper", pt);
            _paper.SetTexture("_ColorAtlas", ca);
            return null;
        }

        static void Commit(Transform root, string name, LBatch b, Material mat)
        {
            var m = new Mesh { name = name };
            // lantern_paper runs to a quarter-million verts; without UInt32 the UInt16 index
            // buffer wraps and shreds the tail into slivers.
            if (b.V.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(b.V);
            // global winding flip for the sim->unity mirror
            var tris = new int[b.I.Count];
            for (int i = 0; i < b.I.Count; i += 3)
            {
                tris[i] = b.I[i]; tris[i + 1] = b.I[i + 2]; tris[i + 2] = b.I[i + 1];
            }
            m.SetTriangles(tris, 0, false);
            m.SetUVs(0, b.UV);
            m.SetUVs(1, b.Piv);
            m.SetUVs(2, b.Cell);
            m.SetUVs(3, b.LP);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(4000, 2000, 4000));
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<MeshFilter>().sharedMesh = m;
        }

        static Vector4 CellOf(string t)
        {
            var d = SignLib.LanternOf(t);
            if (d == null) return new Vector4(0, 0, 0.001f, 0.001f);
            return new Vector4(d.UV[0], d.UV[1], d.UV[2] - d.UV[0], d.UV[3] - d.UV[1]);
        }

        static float YawFor(Vector3 dir) => Mathf.Atan2(dir.x, dir.z);

        static Matrix4x4 SimCompose(Vector3 pos, float yaw, float s)
        {
            return Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0), new Vector3(s, s, s));
        }

        static int _seed, _lightCount;
        static LBatch _paperB, _capsB, _wireB;

        static void Hang(RawMesh src, RawMesh capsSrc, Vector3 pos, float yaw, string text, float s, float glow, int kind, float swayAmp)
        {
            var m = SimCompose(pos, yaw, s);
            var attrs = new Vector4(_seed++, glow, swayAmp, kind);
            var cell = CellOf(text);
            _paperB.AddGeo(src, m, pos, cell, attrs);
            if (capsSrc != null) _capsB.AddGeo(capsSrc, m, pos, cell, attrs);
        }

        static void Wire(Vector3 a, Vector3 b, float sag, float r)
        {
            int N = sag > 0 ? 10 : 1;
            var prev = a;
            for (int i = 1; i <= N; i++)
            {
                float t = (float)i / N;
                var p = Vector3.Lerp(a, b, t); p.y -= sag * 4 * t * (1 - t);
                var d = p - prev; float len = d.magnitude;
                var z = d.normalized;
                var x = Mathf.Abs(z.y) < 0.9f ? Vector3.Cross(new Vector3(0, 1, 0), z).normalized : new Vector3(1, 0, 0);
                var y = Vector3.Cross(z, x);
                var c = (prev + p) * 0.5f;
                _wireB.Box(c, x, y, z, r, r, len / 2, c, Vector4.zero, Vector4.zero);
                prev = p;
            }
        }

        /// Called from FacadeSigns.Build AFTER SignLib.Load + Populate, BEFORE the light budget.
        public static string Build(Transform root)
        {
            var old = new[] { "lantern_paper", "lantern_caps", "lantern_wires", "andon_paper", "andon_frame" };
            foreach (var o in old)
            {
                var g = GameObject.Find("/FacadeSigns/" + o);
                if (g != null) Object.DestroyImmediate(g);
            }

            string err = EnsureMats();
            if (err != null) return "lanterns: " + err;

            _rnd = new Mulberry32(777);
            _seed = 1; _lightCount = 0;
            _paperB = new LBatch(); _capsB = new LBatch(); _wireB = new LBatch();
            var andonB = new LBatch(); var andonFrameB = new LBatch();

            var chochin = LoadRaw("chochin_paper");
            var chochinCaps = LoadRaw("chochin_caps");
            var tall = LoadRaw("chochint_paper");
            var tallCaps = LoadRaw("chochint_caps");
            var andon = LoadRaw("andon_paper");
            var andonFrame = LoadRaw("andon_frame");

            // ---- featured lantern data (lantern-relevant parts of featured.js) ----
            var facades = FacadeAPI.Collect();
            var rows = new List<Row>();
            var strings = new List<Str>();
            var talls = new List<Tall>();
            var reserved = new HashSet<SFacade>();

            // ramen shop row (west avenue facade near the street camera)
            var ramenF = Nearest(facades, new Vector3(-10, 0, 19), new Vector3(1, 0, 0), 0.8f, null);
            if (ramenF != null && ramenF.District == "avenue")
            {
                reserved.Add(ramenF);
                float baseY = ramenF.Center.y - (float)ramenF.Height / 2;
                float a = AlongTo(ramenF, new Vector3(-10, 0, 19));
                float aa = Mathf.Clamp(a, -(float)ramenF.Width / 2 + 2, (float)ramenF.Width / 2 - 2);
                var p0 = ramenF.Center + ramenF.Right * (aa - 2.2f) + ramenF.Normal * 0.7f; p0.y = baseY + 2.95f;
                var p1 = ramenF.Center + ramenF.Right * (aa + 2.6f) + ramenF.Normal * 0.7f; p1.y = baseY + 2.95f;
                rows.Add(new Row { p0 = p0, p1 = p1, count = 6, texts = new[] { "ら", "ー", "め", "ん", "ら", "ー" }, scale = 1.0f });
            }
            // karaoke facade (excluded from the izakaya pick; hero signs come later)
            var karF = Nearest(facades, new Vector3(10, 0, 4), new Vector3(-1, 0, 0), 0.8f, null);
            if (karF != null && karF.District == "avenue") reserved.Add(karF);

            // alley: nomiya tall lantern + strings + 2-lantern rows
            var alleyF = facades.FindAll(f => f.District == "alley");
            const float alZ = -100, alW = 4.5f, alXs = -11, alXe = -90;
            var nomF = Nearest(alleyF, new Vector3(-36, 0, alZ - alW / 2), new Vector3(0, 0, 1), 0.8f, null);
            if (nomF != null)
            {
                reserved.Add(nomF);
                float baseY = nomF.Center.y - (float)nomF.Height / 2;
                float a = AlongTo(nomF, new Vector3(-36, 0, 0));
                var tp = nomF.Center + nomF.Right * (a - 1.7f) + nomF.Normal * 0.5f; tp.y = baseY + 2.6f;
                talls.Add(new Tall { pos = tp, text = "やきとり" });
            }
            for (float x = alXs - 4; x > alXe + 4; x -= 7.5f)
            {
                float gy = AlleyGroundY(x);
                strings.Add(new Str
                {
                    a = new Vector3(x, gy + 4.3f, alZ - alW / 2 + 0.1f),
                    b = new Vector3(x - 1.5f, gy + 4.5f, alZ + alW / 2 - 0.1f),
                    sag = 0.5f, count = 4
                });
            }
            // deviation note: JS consumes the shared SignBuilder rng here; we use the lantern stream (777)
            foreach (var f in alleyF)
            {
                if (Next() < 0.5)
                {
                    float baseY = f.Center.y - (float)f.Height / 2;
                    float a = Range(-(float)f.Width / 3, (float)f.Width / 3);
                    var p0 = f.Center + f.Right * (a - 0.6f) + f.Normal * 0.45f; p0.y = baseY + 2.5f;
                    var p1 = f.Center + f.Right * (a + 0.6f) + f.Normal * 0.45f; p1.y = baseY + 2.5f;
                    rows.Add(new Row { p0 = p0, p1 = p1, count = 2, texts = Next() < 0.5 ? new[] { "居", "酒" } : new[] { "焼鳥", "酒" }, scale = 0.9f });
                }
            }
            // izakaya on the avenue east side
            var izF = Nearest(facades, new Vector3(10, 0, -30), new Vector3(-1, 0, 0), 0.8f, reserved);
            if (izF != null && izF.District == "avenue")
            {
                float baseY = izF.Center.y - (float)izF.Height / 2;
                var p0 = izF.Center + izF.Right * -2.4f + izF.Normal * 0.6f; p0.y = baseY + 2.9f;
                var p1 = izF.Center + izF.Right * 2.4f + izF.Normal * 0.6f; p1.y = baseY + 2.9f;
                rows.Add(new Row { p0 = p0, p1 = p1, count = 5, texts = new[] { "居", "酒", "屋", "酒", "祭" }, scale = 1.0f });
            }

            // ---- rows under awnings ----
            var textsA = new[] { "祭", "酒", "提灯", "居", "串", "焼鳥" };
            foreach (var row in rows)
            {
                var n = Vector3.Cross(new Vector3(0, 1, 0), row.p1 - row.p0).normalized; n = -n;
                Wire(new Vector3(row.p0.x, row.p0.y + 0.18f, row.p0.z), new Vector3(row.p1.x, row.p1.y + 0.18f, row.p1.z), 0, 0.012f);
                for (int i = 0; i < row.count; i++)
                {
                    float t = row.count == 1 ? 0.5f : (float)i / (row.count - 1);
                    var p = Vector3.Lerp(row.p0, row.p1, t); p.y += 0.18f;
                    Hang(chochin, chochinCaps, p, YawFor((row.p1 - row.p0).normalized), row.texts[i % row.texts.Length], row.scale * Range(0.95f, 1.05f), Range(0.9f, 1.1f), 0, 0.8f);
                    if (i % 2 == 0)
                    {
                        var lp = p + new Vector3(0, -0.5f, 0) + n * 0.3f;
                        FacadeSigns.AddPoolLight(lp, 1, 0.22f, 0.06f, 3.5f, 7, 2);
                        _lightCount++;
                    }
                }
            }
            // ---- strings across the alley ----
            foreach (var s in strings)
            {
                Wire(s.a, s.b, s.sag, 0.006f);
                for (int i = 0; i < s.count; i++)
                {
                    float t = (i + 0.5f) / s.count;
                    var p = Vector3.Lerp(s.a, s.b, t); p.y -= s.sag * 4 * t * (1 - t);
                    var dir = s.b - s.a; dir.y = 0; dir.Normalize();
                    var n = new Vector3(-dir.z, 0, dir.x);
                    Hang(chochin, chochinCaps, p, YawFor(n), Pick(textsA), Range(0.75f, 0.9f), Range(0.8f, 1.1f), 0, 1.4f);
                }
                var mid = Vector3.Lerp(s.a, s.b, 0.5f); mid.y -= s.sag + 0.6f;
                FacadeSigns.AddPoolLight(mid, 1, 0.25f, 0.07f, 4, 8, 1.6f);
                _lightCount++;
            }
            // ---- tall cylindrical lanterns ----
            foreach (var tl in talls)
            {
                var p = tl.pos; p.y += 1.95f;
                Hang(tall, tallCaps, p, PI / 2, tl.text, 1, 1.1f, 1, 0.25f);
                FacadeSigns.AddPoolLight(tl.pos + new Vector3(0, 0.6f, 0.6f), 1, 0.25f, 0.06f, 6, 9, 3);
                _lightCount++;
            }
            // ---- andon floor lanterns along the promenade (kind 2, no sway) ----
            {
                const float prXMax = 140, prY = 0, prZs = 40, prZe = -500;
                int k = 0;
                for (float z = prZs - 8.5f; z > prZe; z -= 18)
                {
                    if (Mathf.Abs(-260 - z) < 8) continue; // bridge
                    var pos = new Vector3(prXMax - 0.9f, prY, z);
                    var m = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
                    var attrs = new Vector4(_seed++, 1, 0, 2);
                    var cell = CellOf("祭");
                    andonB.AddGeo(andon, m, pos, cell, attrs);
                    andonFrameB.AddGeo(andonFrame, m, pos, cell, attrs);
                    if (k++ % 1 == 0)
                    {
                        FacadeSigns.AddPoolLight(pos + new Vector3(0, 0.55f, 0), 1, 0.68f, 0.38f, 2.5f, 7, 1.2f);
                        _lightCount++;
                    }
                }
            }

            // ---- commit meshes ----
            if (_paperB.Count > 0) Commit(root, "lantern_paper", _paperB, _paper);
            if (_capsB.Count > 0) Commit(root, "lantern_caps", _capsB, _caps);
            if (_wireB.Count > 0) Commit(root, "lantern_wires", _wireB, _wire);
            if (andonB.Count > 0) Commit(root, "andon_paper", andonB, _paper);
            if (andonFrameB.Count > 0) Commit(root, "andon_frame", andonFrameB, _frame);
            return $"lanterns: {_seed - 1} (lights {_lightCount})";
        }
    }
}

// touch: force reimport
