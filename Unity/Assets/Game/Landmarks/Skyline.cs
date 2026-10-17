// Port of src/systems/landmarks/skyline.js — outer-ring megatowers + hero
// towers + clusters, spires, blinking beacons, holographic ads. Tower unit
// geometries carry an 'edge' coordinate; per-tower attributes baked per vertex
// (no GPU instancing). Sim-space placement, mirrored at bake like Cherry.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public class Skyline
    {
        public class Tower
        {
            public float x, z, w, d, h, rot, seed, bright, r;
            public string type; public int style, neon, crown; public bool spire;
        }

        Mulberry32 _rng; // NOT readonly: mutable struct — readonly forces defensive copies, Next() would never advance
        readonly List<Tower> _towers = new();
        readonly List<string> _dbgFirst3 = new();

        Skyline() { _rng = new Mulberry32(90210u); }

        static readonly string[] GeoNames = { "box", "stepped", "stepped2", "taper", "cyl", "twist", "oct" };
        static readonly Color[] Neon = {
            new(1f, 0.1f, 0.55f), new(0.1f, 0.85f, 1f), new(1f, 0.5f, 0.1f),
            new(0.7f, 0.2f, 1f), new(0.2f, 1f, 0.6f), new(1f, 0.2f, 0.15f),
        };

        void Add(float x, float z, float w, float d, float h, string type, int style,
            float? rot = null, int? neon = null, int? crown = null, bool? spire = null)
        {
            float r = rot ?? ((_rng.Next() < 0.7) ? 0f : (float)_rng.Next() * Mathf.PI);
            int n = neon ?? (int)(_rng.Next() * Neon.Length);
            int c = crown ?? (_rng.Next() < 0.25 ? 1 : 0);
            bool sp = spire ?? (_rng.Next() < 0.4);
            _towers.Add(new Tower
            {
                x = x, z = z, w = w, d = d, h = h, type = type, style = style, rot = r, neon = n,
                crown = c, spire = sp, seed = (float)_rng.Next() * 100f, bright = 0.35f + (float)_rng.Next() * 0.8f,
                r = Mathf.Max(w, d) * 0.6f,
            });
        }

        bool Blocked(float x, float z, float r)
        {
            if (x > -215 - r && x < 148 + r && z > -440 - r && z < 75 + r) return true;              // district
            if (x > 175 - 35 - 8 - r && x < 175 + 35 + 10 + r) return true;                          // river
            if (Mathf.Abs(z - -140f) < 10 + r) return true;                                          // rail
            if (Mathf.Abs(z - -240f) < 14 + r) return true;                                          // expressway
            if (Mathf.Abs(z - -260f) < 14 + r && x < 270) return true;                               // bridge
            foreach (var t in _towers)
            {
                float dx = t.x - x, dz = t.z - z;
                if (dx * dx + dz * dz < (t.r + r + 6) * (t.r + r + 6)) return true;
            }
            return false;
        }

        // ---------- unit geometry helpers (tri soup, footprint 1x1, y 0..1) ----------
        class Geo
        {
            public List<Vector3> pos = new(), nor = new();
            public List<Vector2> uv = new();
            public float[] edge, round, neonOK;

            public int VertCount => pos.Count;
        }

        static void AddBox(Geo g, float w, float h, float d, float y0, int segH = 1)
        {
            // three.js BoxGeometry port: 6 faces, uv per face, soup (2 tris per grid cell)
            (Vector3 u, Vector3 v, Vector3 wdir, float wlen, float vlen, float woff)[] faces = {
                (new Vector3(1,0,0), new Vector3(0,-1,0), new Vector3(0,0,1), w, h, d),   // +z
                (new Vector3(-1,0,0), new Vector3(0,-1,0), new Vector3(0,0,-1), w, h, d), // -z
                (new Vector3(1,0,0), new Vector3(0,0,1), new Vector3(0,1,0), w, d, h),    // +y
                (new Vector3(1,0,0), new Vector3(0,0,-1), new Vector3(0,-1,0), w, d, h),  // -y
                (new Vector3(0,0,-1), new Vector3(0,-1,0), new Vector3(1,0,0), d, h, w),  // +x
                (new Vector3(0,0,1), new Vector3(0,-1,0), new Vector3(-1,0,0), d, h, w),  // -x
            };
            foreach (var f in faces)
            {
                bool cap = Mathf.Abs(f.wdir.y) > 0.5f;
                int rows = cap ? 1 : segH;
                for (int iy = 0; iy < rows; iy++)
                {
                    float y0g = (float)iy / rows, y1g = (float)(iy + 1) / rows;
                    Vector3 A, B, C, D;
                    if (cap)
                    {
                        float yy = y0 + (f.wdir.y > 0 ? h : 0);
                        A = f.u * (-f.wlen / 2) + f.v * (-f.vlen / 2) + new Vector3(0, yy, 0);
                        B = f.u * (f.wlen / 2) + f.v * (-f.vlen / 2) + new Vector3(0, yy, 0);
                        C = f.u * (f.wlen / 2) + f.v * (f.vlen / 2) + new Vector3(0, yy, 0);
                        D = f.u * (-f.wlen / 2) + f.v * (f.vlen / 2) + new Vector3(0, yy, 0);
                        // UnitBox winding: (A,D,B) + (D,C,B) top; reversed for bottom
                        if (f.wdir.y > 0)
                        {
                            Emit(g, A, D, B, f.wdir, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0));
                            Emit(g, D, C, B, f.wdir, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                        }
                        else
                        {
                            Emit(g, A, B, D, f.wdir, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1));
                            Emit(g, A, C, B, f.wdir, new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, 0));
                        }
                        continue;
                    }
                    float yb = y0 + y0g * h, yt = y0 + y1g * h;
                    var off = f.wdir * f.woff * 0.5f;
                    A = f.u * (-f.wlen / 2) + new Vector3(0, yb, 0) + off;
                    B = f.u * (f.wlen / 2) + new Vector3(0, yb, 0) + off;
                    C = f.u * (f.wlen / 2) + new Vector3(0, yt, 0) + off;
                    D = f.u * (-f.wlen / 2) + new Vector3(0, yt, 0) + off;
                    Emit(g, A, B, C, f.wdir, new Vector2(0, 1 - y1g), new Vector2(1, 1 - y1g), new Vector2(1, 1 - y0g));
                    Emit(g, A, C, D, f.wdir, new Vector2(0, 1 - y1g), new Vector2(1, 1 - y0g), new Vector2(0, 1 - y0g));
                }
            }
        }

        static void Emit(Geo g, Vector3 a, Vector3 b2, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            g.pos.Add(a); g.nor.Add(n); g.uv.Add(ua);
            g.pos.Add(b2); g.nor.Add(n); g.uv.Add(ub);
            g.pos.Add(c); g.nor.Add(n); g.uv.Add(uc);
        }

        static void AddCylinder(Geo g, float rTop, float rBottom, float y0, float h, int segs, bool caps = true)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = (float)i / segs * Mathf.PI * 2f, a1 = (float)(i + 1) / segs * Mathf.PI * 2f;
                // three.js CylinderGeometry: x = r*sin(a), z = r*cos(a); uv u = i/segs, v = 1 - y/height
                Vector3 P(float a, float r, float y) => new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
                Vector3 N(float a, float rT, float rB)
                {
                    float slope = (rB - rT) / h;
                    var n = new Vector3(Mathf.Sin(a), slope, Mathf.Cos(a));
                    return n.normalized;
                }
                var p00 = P(a0, rBottom, y0); var p01 = P(a1, rBottom, y0);
                var p10 = P(a0, rTop, y0 + h); var p11 = P(a1, rTop, y0 + h);
                var n00 = N(a0, rTop, rBottom); var n01 = N(a1, rTop, rBottom);
                float u0 = (float)i / segs, u1 = (float)(i + 1) / segs;
                // v: three.js v=1 at top? torso: v goes 0 at bottom row? (uv.y = 1 - v index...) use v0=0 bottom
                g.pos.Add(p00); g.nor.Add(n00); g.uv.Add(new Vector2(u0, 0));
                g.pos.Add(p01); g.nor.Add(n01); g.uv.Add(new Vector2(u1, 0));
                g.pos.Add(p11); g.nor.Add(n01); g.uv.Add(new Vector2(u1, 1));
                g.pos.Add(p00); g.nor.Add(n00); g.uv.Add(new Vector2(u0, 0));
                g.pos.Add(p11); g.nor.Add(n01); g.uv.Add(new Vector2(u1, 1));
                g.pos.Add(p10); g.nor.Add(n00); g.uv.Add(new Vector2(u0, 1));
            }
            if (caps)
            {
                // top cap (fan)
                int n = 8;
                for (int i = 0; i < n; i++)
                {
                    float a0 = (float)i / n * Mathf.PI * 2f, a1 = (float)(i + 1) / n * Mathf.PI * 2f;
                    var c = new Vector3(0, y0 + h, 0);
                    var p0 = new Vector3(Mathf.Sin(a0) * rTop, y0 + h, Mathf.Cos(a0) * rTop);
                    var p1 = new Vector3(Mathf.Sin(a1) * rTop, y0 + h, Mathf.Cos(a1) * rTop);
                    var up = Vector3.up;
                    g.pos.Add(c); g.nor.Add(up); g.uv.Add(new Vector2(0.5f, 0.5f));
                    g.pos.Add(p0); g.nor.Add(up); g.uv.Add(new Vector2(0, 0));
                    g.pos.Add(p1); g.nor.Add(up); g.uv.Add(new Vector2(1, 0));
                    // bottom cap (reversed)
                    var c2 = new Vector3(0, y0, 0);
                    var q0 = new Vector3(Mathf.Sin(a0) * rBottom, y0, Mathf.Cos(a0) * rBottom);
                    var q1 = new Vector3(Mathf.Sin(a1) * rBottom, y0, Mathf.Cos(a1) * rBottom);
                    var dn = Vector3.down;
                    g.pos.Add(c2); g.nor.Add(dn); g.uv.Add(new Vector2(0.5f, 0.5f));
                    g.pos.Add(q1); g.nor.Add(dn); g.uv.Add(new Vector2(1, 0));
                    g.pos.Add(q0); g.nor.Add(dn); g.uv.Add(new Vector2(0, 0));
                }
            }
        }

        static void RotY(Geo g, float ang)
        {
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            for (int i = 0; i < g.pos.Count; i++)
            {
                var p = g.pos[i]; g.pos[i] = new Vector3(p.x * cs + p.z * sn, p.y, -p.x * sn + p.z * cs);
                var n = g.nor[i]; g.nor[i] = new Vector3(n.x * cs + n.z * sn, n.y, -n.x * sn + n.z * cs);
            }
        }

        static void Translate(Geo g, float dx, float dy, float dz)
        {
            for (int i = 0; i < g.pos.Count; i++)
                g.pos[i] += new Vector3(dx, dy, dz);
        }

        // port of withEdge(): per-vertex edge coord across a facade (fract(u*mult)),
        // roof verts (|ny|>0.7) get 0.5; seam fix per triangle; round + neonOK flags
        static void WithEdge(Geo g, float mult = 1, float round = 0, float neon = 1)
        {
            int n = g.VertCount;
            g.edge = new float[n]; g.round = new float[n]; g.neonOK = new float[n];
            for (int i = 0; i < n; i++)
            {
                g.round[i] = round; g.neonOK[i] = neon;
                g.edge[i] = Mathf.Abs(g.nor[i].y) > 0.7f ? 0.5f : (g.uv[i].x * mult) % 1.0000001f;
            }
            for (int i = 0; i < n; i += 3)
            {
                float a = g.uv[i].x * mult, b2 = g.uv[i + 1].x * mult, c = g.uv[i + 2].x * mult;
                float basem = Mathf.Floor(Mathf.Min(a, Mathf.Min(b2, c)) + 1e-4f);
                if (Mathf.Abs(g.nor[i].y) <= 0.7f)
                    for (int k = 0; k < 3; k++) g.edge[i + k] = g.uv[i + k].x * mult - basem;
            }
        }

        static Dictionary<string, Geo> TowerGeos()
        {
            var geos = new Dictionary<string, Geo>();
            {
                var g = new Geo(); AddBox(g, 1, 1, 1, 0); WithEdge(g); geos["box"] = g;
            }
            {
                var g = new Geo(); AddBox(g, 1, 0.56f, 1, 0); AddBox(g, 0.8f, 0.28f, 0.8f, 0.56f); AddBox(g, 0.58f, 0.16f, 0.58f, 0.84f);
                WithEdge(g); geos["stepped"] = g;
            }
            {
                var g = new Geo(); AddBox(g, 1, 0.7f, 0.75f, 0); AddBox(g, 0.7f, 0.3f, 1.0f, 0.0f); AddBox(g, 0.6f, 0.3f, 0.55f, 0.7f);
                WithEdge(g); geos["stepped2"] = g;
            }
            {
                var g = new Geo(); AddCylinder(g, 0.36f, 0.7071f, 0, 1f, 4); RotY(g, Mathf.PI / 4); Translate(g, 0, 0.5f, 0);
                WithEdge(g, 4, 0, 0); geos["taper"] = g;
            }
            {
                var g = new Geo(); AddCylinder(g, 0.5f, 0.5f, 0, 1f, 20); Translate(g, 0, 0.5f, 0);
                WithEdge(g, 6, 1); geos["cyl"] = g;
            }
            {
                // twisted box: 18 height segments
                var g = new Geo(); AddBox(g, 1, 1, 1, 0, segH: 18);
                for (int i = 0; i < g.pos.Count; i++)
                {
                    var p = g.pos[i];
                    float a = p.y * 1.1f, s = 1 - 0.25f * p.y;
                    float x = p.x * s, z = p.z * s;
                    g.pos[i] = new Vector3(x * Mathf.Cos(a) - z * Mathf.Sin(a), p.y, x * Mathf.Sin(a) + z * Mathf.Cos(a));
                }
                SmoothNormals(g);
                WithEdge(g, 1, 0, 0); geos["twist"] = g;
            }
            {
                var g = new Geo(); AddCylinder(g, 0.5f, 0.5f, 0, 0.9f, 8); RotY(g, Mathf.PI / 8); Translate(g, 0, 0.45f, 0);
                WithEdge(g, 8);
                var g2 = new Geo(); AddCylinder(g2, 0.25f, 0.42f, 0, 0.1f, 8); RotY(g2, Mathf.PI / 8); Translate(g2, 0, 0.95f, 0);
                WithEdge(g2, 8);
                g.pos.AddRange(g2.pos); g.nor.AddRange(g2.nor); g.uv.AddRange(g2.uv);
                var e = new float[g.edge.Length + g2.edge.Length];
                g.edge.CopyTo(e, 0); g2.edge.CopyTo(e, g.edge.Length); g.edge = e;
                var r = new float[e.Length]; g.round.CopyTo(r, 0); g2.round.CopyTo(r, g.round.Length); g.round = r;
                var nk = new float[e.Length]; g.neonOK.CopyTo(nk, 0); g2.neonOK.CopyTo(nk, g.neonOK.Length); g.neonOK = nk;
                geos["oct"] = g;
            }
            return geos;
        }

        static void SmoothNormals(Geo g)
        {
            var acc = new Dictionary<Vector3, Vector3>();
            for (int i = 0; i < g.pos.Count; i += 3)
            {
                var a = g.pos[i]; var b2 = g.pos[i + 1]; var c = g.pos[i + 2];
                var fn = Vector3.Cross(b2 - a, c - a).normalized;
                foreach (var p in new[] { a, b2, c })
                    acc[p] = acc.TryGetValue(p, out var v) ? v + fn : fn;
            }
            for (int i = 0; i < g.pos.Count; i++)
                g.nor[i] = acc[g.pos[i]].normalized;
        }

        // ---------- build ----------
        public static string Build(Transform root)
        {
            var S = new Skyline();
            // hero towers
            S.Add(268, -330, 46, 40, 300, "stepped", 0, rot: 0, neon: 0, crown: 1, spire: true);
            S.Add(330, -470, 40, 40, 360, "taper", 1, rot: 0.2f, neon: 1, crown: 1, spire: true);
            S.Add(255, -120, 34, 30, 170, "box", 3, rot: 0, neon: 3, crown: 0, spire: false);
            S.Add(300, -40, 38, 32, 210, "oct", 0, rot: 0, neon: 2, crown: 1, spire: true);
            S.Add(250, 40, 30, 30, 120, "box", 2, rot: 0, neon: 4);
            S.Add(380, -250, 44, 44, 420, "twist", 1, rot: 0.4f, neon: 1, crown: 1, spire: true);
            S.Add(60, -620, 50, 46, 480, "stepped", 0, rot: 0, neon: 0, crown: 1, spire: true);
            S.Add(-120, -560, 40, 40, 330, "cyl", 3, neon: 1, crown: 1, spire: true);
            S.Add(180, -760, 44, 44, 440, "taper", 1, neon: 5, crown: 1, spire: true);
            // far bank cluster
            int fbTries = 0, fbBlocked = 0;
            for (int i = 0; i < 400 && S._towers.Count < 70; i++)
            {
                fbTries++;
                float x = 240 + (float)S._rng.Next() * 220, z = 120 - (float)S._rng.Next() * 920;
                float w = 18 + (float)S._rng.Next() * 26, d = 18 + (float)S._rng.Next() * 22;
                if (S.Blocked(x, z, Mathf.Max(w, d) * 0.6f)) { fbBlocked++; continue; }
                bool near = x < 300;
                float h = near ? 40 + (float)S._rng.Next() * 120 : 70 + (float)S._rng.Next() * 220;
                if (S._dbgFirst3.Count < 3) S._dbgFirst3.Add($"({x:F0},{z:F0})");
                S.Add(x, z, w, d, h, GeoNames[(int)(S._rng.Next() * GeoNames.Length)], (int)(S._rng.Next() * 4));
                if (near && S._rng.Next() < 0.6)
                {
                    var last = S._towers[S._towers.Count - 1];
                    S.Add(x, z, w * 1.5f, d * 1.4f, 10 + (float)S._rng.Next() * 8, "box", 4, rot: last.rot, spire: false, crown: 0);
                }
            }
            // north/west mid-ring
            for (int i = 0; i < 600 && S._towers.Count < 130; i++)
            {
                float x = -450 + (float)S._rng.Next() * 900, z = -440 - (float)S._rng.Next() * 500;
                float w = 20 + (float)S._rng.Next() * 30, d = 20 + (float)S._rng.Next() * 30;
                if (S.Blocked(x, z, Mathf.Max(w, d) * 0.6f)) continue;
                float dd = -z - 440;
                S.Add(x, z, w * 0.85f, d * 0.85f, 30 + (float)S._rng.Next() * Mathf.Min(280, 30 + dd * 0.55f),
                    GeoNames[(int)(S._rng.Next() * GeoNames.Length)], (int)(S._rng.Next() * 4));
            }
            for (int i = 0; i < 400 && S._towers.Count < 185; i++)
            {
                float x = -230 - (float)S._rng.Next() * 250, z = 150 - (float)S._rng.Next() * 600;
                float w = 20 + (float)S._rng.Next() * 26, d = 20 + (float)S._rng.Next() * 26;
                if (S.Blocked(x, z, Mathf.Max(w, d) * 0.6f)) continue;
                S.Add(x, z, w, d, 40 + (float)S._rng.Next() * 160,
                    GeoNames[(int)(S._rng.Next() * GeoNames.Length)], (int)(S._rng.Next() * 4));
            }
            // outer ring of megatowers
            int placed = 0;
            for (int i = 0; i < 3000 && placed < 140; i++)
            {
                float ang = (float)S._rng.Next() * Mathf.PI * 2f;
                if (S._rng.Next() < 0.55) ang = -Mathf.PI * 0.5f + ((float)S._rng.Next() - 0.5f) * Mathf.PI * 1.1f;
                float rad = 450 + Mathf.Pow((float)S._rng.Next(), 0.8f) * (1500 - 450);
                float x = Mathf.Cos(ang) * rad + 100, z = Mathf.Sin(ang) * rad - 150;
                float w = 30 + (float)S._rng.Next() * 50, d = 30 + (float)S._rng.Next() * 50;
                if (S.Blocked(x, z, Mathf.Max(w, d) * 0.6f)) continue;
                float hh = 150 + Mathf.Pow((float)S._rng.Next(), 1.5f) * (520 - 150);
                S.Add(x, z, w, d, hh, GeoNames[(int)(S._rng.Next() * GeoNames.Length)], (int)(S._rng.Next() * 4));
                placed++;
            }

            var geos = TowerGeos();

            // ---- per-type merged meshes with baked attributes (mirror at bake) ----
            var towerMat = LmMats.ShaderMat("NEON KAIDO/SkylineTower");
            if (towerMat == null) return "skyline: tower shader missing";
            int meshCount = 0, vertTotal = 0;
            foreach (var name in GeoNames)
            {
                var list = S._towers.FindAll(t => t.type == name);
                if (list.Count == 0) continue;
                var g = geos[name];
                var pos = new List<Vector3>(list.Count * g.VertCount);
                var nor = new List<Vector3>(list.Count * g.VertCount);
                var uv0 = new List<Vector2>(list.Count * g.VertCount);
                var uv1 = new List<Vector4>(list.Count * g.VertCount);
                var uv2 = new List<Vector4>(list.Count * g.VertCount);
                var uv3 = new List<Vector4>(list.Count * g.VertCount);
                var uv4 = new List<Vector4>(list.Count * g.VertCount);
                foreach (var t in list)
                {
                    if (g.edge == null || g.edge.Length < g.VertCount)
                        return $"skyline: BAD geo {name} edge={g.edge?.Length ?? -1} verts={g.VertCount}";
                    float cs = Mathf.Cos(t.rot), sn = Mathf.Sin(t.rot);
                    Vector3 T(Vector3 p)
                    {
                        float lx = p.x * t.w, ly = p.y * t.h, lz = p.z * t.d;
                        float rx = lx * cs + lz * sn, rz = -lx * sn + lz * cs;
                        return new Vector3(t.x + rx, ly, -t.z - rz);
                    }
                    Vector3 N(Vector3 n)
                    {
                        float nx = n.x * cs + n.z * sn, nz = -n.x * sn + n.z * cs;
                        return new Vector3(nx, n.y, -nz);
                    }
                    var nc = Neon[t.neon];
                    int baseI = pos.Count;
                    for (int i = 0; i < g.VertCount; i += 3)
                    {
                        var a = T(g.pos[i]); var b2 = T(g.pos[i + 1]); var c = T(g.pos[i + 2]);
                        pos.Add(a); pos.Add(c); pos.Add(b2); // winding flip
                        nor.Add(N(g.nor[i])); nor.Add(N(g.nor[i + 2])); nor.Add(N(g.nor[i + 1]));
                        uv0.Add(g.uv[i]); uv0.Add(g.uv[i + 2]); uv0.Add(g.uv[i + 1]);
                        for (int k = 0; k < 3; k++)
                        {
                            int src = i + k;
                            uv1.Add(new Vector4(g.edge[src], g.round[src], g.neonOK[src], 0));
                            uv2.Add(new Vector4(t.seed, t.h, t.style, t.neon));
                            uv3.Add(new Vector4(nc.r, nc.g, nc.b, t.crown));
                            uv4.Add(new Vector4(t.x, -t.z, (t.w + t.d) / 4f, t.bright));
                        }
                    }
                }
                var m = new Mesh { name = "skyline_" + name };
                m.vertices = pos.ToArray(); m.normals = nor.ToArray();
                m.uv = uv0.ToArray();
                m.SetUVs(1, uv1); m.SetUVs(2, uv2); m.SetUVs(3, uv3); m.SetUVs(4, uv4);
                var idx = new int[pos.Count];
                for (int i = 0; i < pos.Count; i++) idx[i] = i;
                m.triangles = idx;
                m.RecalculateBounds();
                var go = new GameObject("skyline_" + name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = towerMat;
                meshCount++; vertTotal += pos.Count;
            }

            // ---- spires ----
            var spireBatch = new LmBatch();
            var spireGeo = new Geo(); AddCylinder(spireGeo, 0.25f, 1.2f, 0, 1f, 6);
            var spireLm = new LmGeo { pos = spireGeo.pos.ToArray(), nor = spireGeo.nor.ToArray(), uv = spireGeo.uv.ToArray() };
            int spireCount = 0;
            foreach (var t in S._towers)
            {
                if (!t.spire) continue;
                float sh = 20 + (t.seed % 1) * 50 + t.h * 0.08f;
                spireBatch.Add("spire", spireLm, t.x, t.h, t.z, 0, 0, 0, 1, sh, 1);
                spireCount++;
            }
            var spireBuilt = spireBatch.Build(root, "skyline_spire_", 0);
            var spireMat = LmMats.Metal(new Color(0.102f, 0.102f, 0.118f), 0.5f, 0.8f);
            foreach (var go in spireBuilt) go.GetComponent<MeshRenderer>().sharedMaterial = spireMat;

            // ---- beacons ----
            var beaconPhase = new List<float>();
            var beaconPos = new List<Vector3>();
            foreach (var t in S._towers)
            {
                if (t.spire)
                {
                    float sh = 20 + (t.seed % 1) * 50 + t.h * 0.08f;
                    beaconPos.Add(new Vector3(t.x, t.h + sh, -t.z));
                    beaconPhase.Add((t.seed * 0.37f) % 1f);
                    continue;
                }
                if (t.h < 260) continue;
                float hw2 = t.w * 0.35f, hd = t.d * 0.35f;
                float topScale = t.type == "taper" ? 0.5f : t.type == "stepped" ? 0.58f : t.type == "twist" ? 0.75f : 1f;
                float c = Mathf.Cos(t.rot), sn = Mathf.Sin(t.rot);
                foreach (var (ax, az) in new[] { (-1f, -1f), (1f, 1f) })
                {
                    float lx = ax * hw2 * topScale, lz = az * hd * topScale;
                    beaconPos.Add(new Vector3(t.x + lx * c + lz * sn, t.h + 0.5f, -(t.z - lx * sn + lz * c)));
                    beaconPhase.Add((t.seed * 0.37f + ax * 0.5f) % 1f);
                }
            }
            if (beaconPos.Count > 0)
            {
                var bGeo = new Geo(); AddCylinder(bGeo, 1f, 1f, 0, 2f, 4); // diamond-ish blob stand-in for octahedron
                var bMat = LmMats.ShaderMat("NEON KAIDO/SkylineBeacon");
                var bPos = new List<Vector3>(); var bNor = new List<Vector3>(); var bUv = new List<Vector2>(); var bUv1 = new List<Vector4>();
                for (int bi = 0; bi < beaconPos.Count; bi++)
                {
                    float d = Mathf.Sqrt(beaconPos[bi].x * beaconPos[bi].x + beaconPos[bi].z * beaconPos[bi].z);
                    float sc = 0.5f + d / 1100f;
                    int baseI = bPos.Count;
                    for (int i = 0; i < bGeo.VertCount; i += 3)
                    {
                        var a = Vector3.Scale(bGeo.pos[i], new Vector3(sc, sc, sc)) + beaconPos[bi];
                        var b2 = Vector3.Scale(bGeo.pos[i + 1], new Vector3(sc, sc, sc)) + beaconPos[bi];
                        var cc = Vector3.Scale(bGeo.pos[i + 2], new Vector3(sc, sc, sc)) + beaconPos[bi];
                        bPos.Add(a); bPos.Add(cc); bPos.Add(b2);
                        bNor.Add(Vector3.up); bNor.Add(Vector3.up); bNor.Add(Vector3.up);
                        bUv.Add(Vector2.zero); bUv.Add(Vector2.zero); bUv.Add(Vector2.zero);
                        for (int k = 0; k < 3; k++) bUv1.Add(new Vector4(beaconPhase[bi], 0, 0, 0));
                    }
                }
                var bm = new Mesh { name = "skyline_beacons" };
                bm.vertices = bPos.ToArray(); bm.normals = bNor.ToArray(); bm.uv = bUv.ToArray();
                bm.SetUVs(1, bUv1);
                var bidx = new int[bPos.Count];
                for (int i = 0; i < bPos.Count; i++) bidx[i] = i;
                bm.triangles = bidx;
                bm.RecalculateBounds();
                var bgo = new GameObject("skyline_beacons");
                bgo.transform.SetParent(root, false);
                bgo.AddComponent<MeshFilter>().sharedMesh = bm;
                bgo.AddComponent<MeshRenderer>().sharedMaterial = bMat;
            }

            // ---- holographic ads ----
            int holoCount = HoloAds(root, S._towers);

            // colliders skipped (player collision separate todo)
            return $"skyline: {S._towers.Count} towers, {meshCount} meshes {vertTotal} verts, {spireCount} spires, {beaconPos.Count} beacons, {holoCount} holos | fb: tries={fbTries} blocked={fbBlocked} first3=" + string.Join(",", S._dbgFirst3);
        }

        static int HoloAds(Transform root, List<Tower> towers)
        {
            var ads = LmMats.Tex("ads");
            var ads2 = LmMats.Tex("ads2");
            var ads3 = LmMats.Tex("ads3");
            if (ads == null) return 0;
            var places = new List<(Texture2D tex, float[] rect, float w, float h, float x, float y, float z, float ry, float intensity, float seed)>();
            float[] Panel(int i) => new[] { i * 0.25f + 0.004f, 0f, (i + 1) * 0.25f - 0.004f, 1f };
            const float faceWest = -Mathf.PI / 2f;
            places.Add((ads, Panel(2), 36, 72, 244.2f, 110, -330, faceWest, 4.0f, 1));
            places.Add((ads, Panel(1), 18, 36, 237.6f, 110, -120, faceWest, 3.5f, 2));
            places.Add((ads3 ?? ads, Panel(3), 22, 44, 280.5f, 140, -40, faceWest, 3.2f, 3));
            places.Add((ads, Panel(3), 26, 52, 352, 180, -230, -Mathf.PI * 0.35f, 3.2f, 4));
            places.Add((ads, Panel(2), 24, 48, 330, 230, -449, 0, 4.0f, 5));
            places.Add((ads3 ?? ads, Panel(0), 30, 60, 60, 300, -596, 0, 3.5f, 6));
            places.Add((ads3 ?? ads, Panel(2), 26, 52, -120, 220, -539, 0.3f, 3.0f, 7));
            // procedural: big vertical ads on south faces of tall unrotated towers
            var cand = towers.FindAll(t => t.rot == 0 && t.h > 170 && t.z < -420 && t.x > -250 && (t.type == "box" || t.type == "stepped" || t.type == "stepped2"));
            var pool = new (Texture2D tex, int panel)[] {
                (ads3, 0), (ads, 3), (ads3, 1), (ads, 0), (ads3, 2), (ads, 1), (ads3, 3), (ads, 2),
            };
            int shown = Mathf.Min(18, cand.Count);
            for (int i = 0; i < shown; i++)
            {
                var t = cand[i];
                float w = Mathf.Min(t.w * 0.7f, 46), h = w * 2;
                float top = t.type == "stepped" ? t.h * 0.56f : t.type == "stepped2" ? t.h * 0.7f : t.h;
                float y = Mathf.Min(top - h * 0.6f, t.h * 0.45f + h * 0.5f);
                float zf = t.z + (t.type == "stepped2" ? t.d * 0.5f : t.d / 2f) + 0.6f;
                if (i % 3 == 2 && ads2 != null)
                    places.Add((ads2, i % 2 == 1 ? new[] { 0f, 0.5f, 1f, 1f } : new[] { 0f, 0f, 1f, 0.5f }, w * 1.6f, w * 0.4f, t.x, y, zf, 0, 3.0f, 20 + i));
                else
                {
                    var pk = pool[i % pool.Length];
                    places.Add((pk.tex, Panel(pk.panel), w, h, t.x, y, zf, 0, 3.4f, 20 + i));
                }
            }
            if (ads2 != null)
            {
                places.Add((ads2, new[] { 0f, 0.5f, 1f, 1f }, 48, 12, 244.5f, 40, -330, faceWest, 3.0f, 8));
                places.Add((ads2, new[] { 0f, 0f, 1f, 0.5f }, 40, 10, 237.6f, 60, -120, faceWest, 3.0f, 9));
                places.Add((ads2, new[] { 0f, 0.5f, 1f, 1f }, 60, 15, 180, 320, -738, 0, 3.0f, 10));
            }

            // group by texture -> one mesh each
            var byTex = new Dictionary<Texture2D, List<int>>();
            for (int i = 0; i < places.Count; i++)
            {
                if (!byTex.TryGetValue(places[i].tex, out var l)) byTex[places[i].tex] = l = new List<int>();
                l.Add(i);
            }
            int count = 0;
            foreach (var (tex, list) in byTex)
            {
                var pos = new List<Vector3>(); var nor = new List<Vector3>();
                var uv0 = new List<Vector2>(); var uv1 = new List<Vector4>();
                foreach (var i in list)
                {
                    var (texi, rect, w, h, x, y, z, ry, intensity, seed) = places[i];
                    // plane facing +z in sim space; mirror to unity (z -> -z, winding flip)
                    var cs = Mathf.Cos(ry); var sn = Mathf.Sin(ry);
                    Vector3 R(Vector3 p)
                    {
                        float sx = x + p.x * cs + p.z * sn, sz = z - p.x * sn + p.z * cs;
                        return new Vector3(sx, y + p.y, -sz);
                    }
                    Vector2 UV(Vector2 c) => new Vector2(Mathf.Lerp(rect[0], rect[2], c.x), Mathf.Lerp(rect[1], rect[3], c.y));
                    (Vector2 c, Vector3 lp)[] q = {
                        (new Vector2(0, 0), new Vector3(-w/2, 0, 0)), (new Vector2(1, 0), new Vector3(w/2, 0, 0)),
                        (new Vector2(1, 1), new Vector3(w/2, h, 0)), (new Vector2(0, 1), new Vector3(-w/2, h, 0)),
                    };
                    var n = new Vector3(0, 0, 1);
                    var nU = new Vector3(n.x * cs + n.z * sn, 0, -(-n.x * sn + n.z * cs));
                    int[] order = { 0, 2, 1, 0, 3, 2 }; // winding flip for the z mirror
                    foreach (var oi in order)
                    {
                        pos.Add(R(q[oi].lp)); nor.Add(nU);
                        uv0.Add(UV(q[oi].c)); uv1.Add(new Vector4(intensity, seed, 0, 0));
                    }
                    count++;
                }
                var m = new Mesh { name = "skyline_holos" };
                m.vertices = pos.ToArray(); m.normals = nor.ToArray(); m.uv = uv0.ToArray();
                m.SetUVs(1, uv1);
                var idx = new int[pos.Count];
                for (int i = 0; i < pos.Count; i++) idx[i] = i;
                m.triangles = idx;
                m.RecalculateBounds();
                var go = new GameObject("skyline_holos_" + tex.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mat = LmMats.ShaderMat("NEON KAIDO/SkylineHolo");
                mat.SetTexture("_MainTex", tex);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return count;
        }
    }
}
