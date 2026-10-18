// Port of signage/signs.js (SignBuilder) + populate.js (populateFacades).
// Facade neon blades, light boxes, fascias, wall neon, tenant stacks, posters, rooftop letters.
// All placement math in SIM space (mirrored to Unity x,y,-z at mesh build); one faces mesh +
// one housing mesh; pool lights capped to the shader light budget, flicker synced in C#.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NeonKaido
{
    public class SDesign
    {
        public float[] UV; public float Aspect; public float NatW, NatH; public int Mode;
    }

    public static class SignLib
    {
        public static List<SDesign> NeonV, NeonH, NeonIcon, ChanH, BoxV, BoxH, BoxSq, Posters, Screens, Tenant;
        public static List<SDesign> Lantern;
        public static Dictionary<string, SDesign> Misc = new Dictionary<string, SDesign>();

        // lantern cell order (JS Set insertion order of lanternTexts in library.js)
        public static readonly string[] LanternTexts = { "ら", "ー", "め", "ん", "居", "酒", "屋", "祭", "焼鳥", "赤提灯", "やきとり", "提灯", "御食事", "串" };
        public static SDesign LanternOf(string t)
        {
            if (Lantern == null) return null;
            for (int i = 0; i < LanternTexts.Length; i++)
                if (LanternTexts[i] == t && i < Lantern.Count) return Lantern[i];
            return Lantern.Count > 7 ? Lantern[7] : null; // fallback 祭
        }

        static List<SDesign> Cat(string name) => name switch
        {
            "neonV" => NeonV, "neonH" => NeonH, "neonIcon" => NeonIcon, "chanH" => ChanH,
            "boxV" => BoxV, "boxH" => BoxH, "boxSq" => BoxSq, "posters" => Posters,
            "screens" => Screens, "tenant" => Tenant, "lantern" => Lantern, _ => null
        };

        public static void Load()
        {
            NeonV = new List<SDesign>(); NeonH = new List<SDesign>(); NeonIcon = new List<SDesign>();
            ChanH = new List<SDesign>(); BoxV = new List<SDesign>(); BoxH = new List<SDesign>();
            BoxSq = new List<SDesign>(); Posters = new List<SDesign>(); Screens = new List<SDesign>();
            Tenant = new List<SDesign>(); Lantern = new List<SDesign>(); Misc.Clear();
            foreach (var line in System.IO.File.ReadAllLines("Assets/Game/Signage/sign_lib.txt"))
            {
                var p = line.Split('|');
                if (p.Length < 9) continue;
                var d = new SDesign
                {
                    UV = new[] { float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture), float.Parse(p[4], CultureInfo.InvariantCulture) },
                    Aspect = float.Parse(p[5], CultureInfo.InvariantCulture),
                    NatW = float.Parse(p[6], CultureInfo.InvariantCulture),
                    NatH = float.Parse(p[7], CultureInfo.InvariantCulture),
                    Mode = int.Parse(p[8]),
                };
                if (p[0].StartsWith("misc_")) Misc[p[0].Substring(5)] = d;
                else Cat(p[0])?.Add(d);
            }
        }
    }

    public static class FacadeSigns
    {
        static readonly Vector3 UP = new Vector3(0, 1, 0);
        static Vector3 Mir(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        // neon palette (util.js)
        static readonly Dictionary<string, Color> NEON = new Dictionary<string, Color>
        {
            { "magenta", new Color(1.0f, 0.08f, 0.55f) }, { "pink", new Color(1.0f, 0.25f, 0.6f) },
            { "hotpink", new Color(1.0f, 0.05f, 0.35f) }, { "cyan", new Color(0.05f, 0.85f, 1.0f) },
            { "ice", new Color(0.45f, 0.85f, 1.0f) }, { "blue", new Color(0.12f, 0.35f, 1.0f) },
            { "violet", new Color(0.55f, 0.15f, 1.0f) }, { "purple", new Color(0.75f, 0.1f, 1.0f) },
            { "red", new Color(1.0f, 0.06f, 0.04f) }, { "amber", new Color(1.0f, 0.45f, 0.08f) },
            { "orange", new Color(1.0f, 0.3f, 0.03f) }, { "warm", new Color(1.0f, 0.7f, 0.35f) },
            { "yellow", new Color(1.0f, 0.8f, 0.15f) }, { "lime", new Color(0.45f, 1.0f, 0.12f) },
            { "green", new Color(0.1f, 1.0f, 0.45f) }, { "white", new Color(0.95f, 0.95f, 1.0f) },
        };
        static readonly (string, float)[] NEON_WEIGHTS = {
            ("magenta", 5), ("pink", 3), ("hotpink", 2), ("cyan", 5), ("ice", 2), ("blue", 2),
            ("violet", 3), ("purple", 2), ("red", 4), ("amber", 3), ("orange", 2), ("warm", 2),
            ("yellow", 1), ("lime", 0.6f), ("green", 0.6f), ("white", 1),
        };
        static readonly (Color col, Vector2 mr)[] HousingCols = {
            (new Color(0.025f, 0.025f, 0.028f), new Vector2(0.5f, 0.45f)), (new Color(0.04f, 0.04f, 0.045f), new Vector2(0.6f, 0.4f)),
            (new Color(0.3f, 0.3f, 0.32f), new Vector2(0.85f, 0.35f)), (new Color(0.5f, 0.5f, 0.5f), new Vector2(0f, 0.5f)),
            (new Color(0.35f, 0.03f, 0.03f), new Vector2(0.2f, 0.45f)), (new Color(0.03f, 0.05f, 0.12f), new Vector2(0.3f, 0.45f)),
            (new Color(0.6f, 0.6f, 0.62f), new Vector2(0.1f, 0.4f)),
        };
        static readonly (Color col, Vector2 mr) Steel = (new Color(0.18f, 0.18f, 0.19f), new Vector2(0.8f, 0.45f));

        // ---- state ----
        static Mulberry32 _rnd;
        static int _seed = 1;
        static float _lightScale = 1;
        static int _signs, _lights;

        // faces batch (UV2=sp, UV3=back, UV4=luv; COLOR=tint)
        static List<Vector3> _FV; static List<int> _FI; static List<Color> _FC; static List<Vector2> _FUV; static List<Vector4> _FSP, _FLUV; static List<Vector3> _FB;
        // housing batch (COLOR=col, UV2=mr)
        static List<Vector3> _HV; static List<int> _HI; static List<Color> _HC; static List<Vector4> _HMR;

        struct PoolL { public Vector3 pos; public Color col; public float inten, dist, seed, ftype, imp; }
        static List<PoolL> _pool;

        /// Lanterns (and future featured set) append their lights to the shared pool here;
        /// called BEFORE the budget in Build() so all lights compete by intensity*importance.
        internal static void AddPoolLight(Vector3 simPos, float r, float g, float b, float inten, float dist, float imp)
        {
            _pool.Add(new PoolL { pos = simPos, col = new Color(r, g, b), inten = inten, dist = dist, seed = 1, ftype = 0, imp = imp });
        }

        /// Real kept sign lights for per-frame flicker sync (PlayerController).
        public static List<Light> FlickerL = new List<Light>();
        public static List<float> FlickerSeed = new List<float>();
        public static List<float> FlickerType = new List<float>();
        public static List<float> FlickerBase = new List<float>();

        // ---- rng helpers (util.js semantics) ----
        static double Range(double a, double b) => a + (b - a) * _rnd.Next();
        static int Int(int a, int b) => Mathf.FloorToInt((float)Range(a, b + 1.0 - 1e-9));
        static SDesign Pick(List<SDesign> l) { int n = l.Count; return n == 0 ? null : l[Mathf.Clamp((int)(_rnd.Next() * n), 0, n - 1)]; }
        static string Weighted()
        {
            double tot = 0; foreach (var w in NEON_WEIGHTS) tot += w.Item2;
            double r = _rnd.Next() * tot, acc = 0;
            foreach (var w in NEON_WEIGHTS) { acc += w.Item2; if (r < acc) return w.Item1; }
            return "cyan";
        }
        static Color NeonColor() => NEON[Weighted()];
        static int NextSeed() { _seed = (_seed % 997) + 1; return _seed; }
        static float Ftype(float broken = 0.12f, float stutter = 0.08f, float breathe = 0.06f)
        {
            float r = (float)_rnd.Next();
            if (r < broken) return 2;
            if (r < broken + stutter) return 1;
            if (r < broken + stutter + breathe) return 4;
            return 0;
        }

        // ---- primitives ----
        static void Face(Vector3 c, Vector3 u, Vector3 v, float hu, float hv, Vector3 n, SDesign d, Color tint, float inten, int seed, float ftype, Vector3 back)
        {
            int b = _FV.Count;
            Vector3[] p = { c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv };
            // sign_lib UV is min/max (x0,y0,x1,y1) into the 4096 atlas — the same convention
            // TrafficSignals.RectOf + AddLens bake. The shader samples _NeonTex / _ColorTex with this
            // uv directly, so it has to be atlas space, not the local 0..1 quad (which showed the
            // whole atlas squeezed onto every sign).
            Vector2[] t = {
                new Vector2(d.UV[0], d.UV[1]), new Vector2(d.UV[2], d.UV[1]),
                new Vector2(d.UV[2], d.UV[3]), new Vector2(d.UV[0], d.UV[3]) };
            float[] su = { -1, 1, 1, -1 }, sv = { -1, -1, 1, 1 };
            var cu = Mir(u); var cv = Mir(v); var cn = Mir(n);
            bool ccw = Vector3.Dot(Vector3.Cross(cu, cv), cn) >= 0;
            for (int i = 0; i < 4; i++)
            {
                _FV.Add(Mir(p[i])); _FC.Add(tint); _FUV.Add(t[i]);
                _FSP.Add(new Vector4(d.Mode, inten, seed, ftype));
                _FB.Add(back);
                _FLUV.Add(new Vector4(su[i] * hu + hu, sv[i] * hv + hv, 2 * hu, 2 * hv));
            }
            if (ccw) { _FI.Add(b); _FI.Add(b + 1); _FI.Add(b + 2); _FI.Add(b); _FI.Add(b + 2); _FI.Add(b + 3); }
            else { _FI.Add(b); _FI.Add(b + 2); _FI.Add(b + 1); _FI.Add(b); _FI.Add(b + 3); _FI.Add(b + 2); }
        }

        static void Strip(Vector3 c, Vector3 u, Vector3 v, float hu, float hv, Vector3 n, Color tint, float inten, int seed, float ftype)
        {
            var d = new SDesign { UV = new[] { 0f, 0f, 0.0001f, 0.0001f }, Mode = 2 };
            Face(c, u, v, hu, hv, n, d, tint, inten, seed, ftype, new Vector3(0, 0, 0));
        }

        static void Hbox(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float hx, float hy, float hz, Color col, Vector2 mr, HashSet<string> skip = null)
        {
            var axes = new[] { ax, ay, az };
            var he = new[] { hx, hy, hz };
            var names = new[] { "+x", "-x", "+y", "-y", "+z", "-z" };
            for (int a = 0; a < 3; a++)
            {
                for (int s = 0; s < 2; s++)
                {
                    if (skip != null && skip.Contains(names[a * 2 + s])) continue;
                    float e = s == 0 ? he[a] : -he[a];
                    Vector3 n = axes[a] * (s == 0 ? 1 : -1);
                    Vector3 fc = c + axes[a] * e;
                    // span axes = the other two
                    Vector3 s1 = axes[(a + 1) % 3] * he[(a + 1) % 3];
                    Vector3 s2 = axes[(a + 2) % 3] * he[(a + 2) % 3];
                    Vector3[] p = { fc - s1 - s2, fc + s1 - s2, fc + s1 + s2, fc - s1 + s2 };
                    var m1 = Mir(s1); var m2 = Mir(s2); var mn = Mir(n);
                    bool ccw = Vector3.Dot(Vector3.Cross(m1, m2), mn) >= 0;
                    int b = _HV.Count;
                    foreach (var q in p) { _HV.Add(Mir(q)); _HC.Add(col); _HMR.Add(new Vector4(mr.x, mr.y, 0, 0)); }
                    if (ccw) { _HI.Add(b); _HI.Add(b + 1); _HI.Add(b + 2); _HI.Add(b); _HI.Add(b + 2); _HI.Add(b + 3); }
                    else { _HI.Add(b); _HI.Add(b + 2); _HI.Add(b + 1); _HI.Add(b); _HI.Add(b + 3); _HI.Add(b + 2); }
                }
            }
        }

        static void Bar(Vector3 a, Vector3 b, float r, Color col, Vector2 mr)
        {
            Vector3 d = b - a; float L = d.magnitude; if (L < 1e-4f) return;
            Vector3 z = d / L;
            Vector3 x = Mathf.Abs(z.y) < 0.9f ? Vector3.Cross(UP, z).normalized : new Vector3(1, 0, 0);
            Vector3 y = Vector3.Cross(z, x).normalized;
            Hbox((a + b) * 0.5f, x, y, z, r, r, L / 2, col, mr);
        }

        static void Light(Vector3 pos, Color col, float inten, float dist, int seed, float ftype, float imp = 1)
        {
            _pool.Add(new PoolL { pos = pos, col = col, inten = inten * _lightScale, dist = dist, seed = seed, ftype = ftype, imp = imp });
            _lights++;
        }

        // ---- sign types ----
        static (Vector3 c, float D, float Hh) Blade(SFacade f, float a, float y0, SDesign design, bool isTall = false,
            float? depth = null, float maxH = 9, float? thick = null, bool? edge = null, float lightMul = 1, float importance = 1,
            Color? tintO = null, float? ftypeO = null, float? intenO = null, (Color, Vector2)? housing = null, float? gapO = null)
        {
            Vector3 n = f.NS(), r = f.RS();
            bool isNeon = design.Mode == 0;
            float D = depth ?? (float)Range(0.75, 1.25);
            float Hh = D / design.Aspect;
            if (Hh > maxH) { Hh = maxH; D = Hh * design.Aspect; }
            float T = thick ?? (isNeon ? (float)Range(0.18, 0.3) : (float)Range(0.25, 0.4));
            float gap = gapO ?? (float)Range(0.25, 0.5);
            Vector3 baseP = f.CS() + r * a;
            baseP.y = y0 + Hh / 2;
            Vector3 c = baseP + n * (gap + D / 2);
            var (hcol, hmr) = housing ?? (isNeon ? HousingCols[Int(0, 2)] : PickHousing());
            float fr = 0.05f;
            Hbox(c, r, UP, n, T / 2, Hh / 2 + fr, D / 2 + fr, hcol, hmr, new HashSet<string> { "+x", "-x" });
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 fc = c + r * (s * (T / 2 + 0.015f));
                Hbox(fc + UP * (Hh / 2 + fr / 2), r, UP, n, 0.02f, fr / 2, D / 2 + fr, hcol, hmr);
                Hbox(fc - UP * (Hh / 2 + fr / 2), r, UP, n, 0.02f, fr / 2, D / 2 + fr, hcol, hmr);
                Hbox(fc + n * (D / 2 + fr / 2), r, UP, n, 0.02f, Hh / 2, fr / 2, hcol, hmr);
                Hbox(fc - n * (D / 2 + fr / 2), r, UP, n, 0.02f, Hh / 2, fr / 2, hcol, hmr);
            }
            Color tint = tintO ?? (isNeon ? NeonColor() : Color.white);
            int seed = NextSeed();
            float ft = ftypeO ?? (isNeon ? Ftype() : Ftype(0f, 0.06f, 0f));
            float inten = intenO ?? (isNeon ? (float)Range(5, 8) : (float)Range(1.6, 2.4));
            Vector3 back = isNeon
                ? new Vector3(hcol.r * 0.6f + 0.008f, hcol.g * 0.6f + 0.008f, hcol.b * 0.6f + 0.01f)
                : new Vector3(0.02f, 0.02f, 0.02f);
            Face(c + r * (T / 2 + 0.003f), -n, UP, D / 2, Hh / 2, r, design, tint, inten, seed, ft, back);
            Face(c - r * (T / 2 + 0.003f), n, UP, D / 2, Hh / 2, -r, design, tint, inten, seed, ft, back);
            if (edge ?? _rnd.Next() < 0.45)
            {
                Color ec = isNeon ? tint : NeonColor();
                Strip(c + n * (D / 2 + fr + 0.004f), r, UP, T * 0.32f, Hh / 2 + fr, n, ec * 3f, (float)Range(2.5, 4.5), seed, ft);
            }
            for (int k = 0; k < 2; k++)
            {
                float yy = k == 0 ? Hh / 2 - 0.12f : -Hh / 2 + 0.12f;
                Vector3 p0 = baseP + UP * yy;
                Bar(p0, p0 + n * (gap + 0.02f), 0.03f, Steel.col, Steel.mr);
                Hbox(p0 + n * 0.01f, r, UP, n, 0.08f, 0.08f, 0.01f, Steel.col, Steel.mr);
            }
            if (Hh > 3) { Vector3 mid = baseP; Bar(mid, mid + n * (gap + 0.02f), 0.025f, Steel.col, Steel.mr); }
            Vector3 lp = c + n * 0.2f;
            lp.y = Mathf.Max(y0 + Hh * 0.35f, y0 + 0.5f);
            Color lc = isNeon ? tint : new Color(1, 0.85f, 0.7f);
            Light(lp, lc, (isNeon ? 9 : 7) * Mathf.Sqrt(D * Hh) * lightMul, Mathf.Min(20, 9 + Hh * 1.3f), seed, ft, importance);
            _signs++;
            return (c, D, Hh);
        }

        static (Color, Vector2) PickHousing() => HousingCols[Mathf.Clamp((int)(_rnd.Next() * HousingCols.Length), 0, HousingCols.Length - 1)];

        static void Flat(SFacade f, float a, float yc, SDesign design, float width = 3, float maxH = 0, float? depth = null,
            float off = 0.03f, bool noHousing = false, float frame = 0.06f, float lightMul = 1, float importance = 1,
            Color? tintO = null, float? ftypeO = null, float? intenO = null, (Color, Vector2)? housing = null,
            bool lightOn = true, Color? lightColor = null)
        {
            Vector3 n = f.NS(), r = f.RS();
            bool isNeon = design.Mode == 0 || design.Mode == 4;
            float W = width; float Hh = W / design.Aspect;
            if (maxH > 0 && Hh > maxH) { Hh = maxH; W = Hh * design.Aspect; }
            float Dp = depth ?? (isNeon ? 0.12f : (float)Range(0.2, 0.35));
            Vector3 c = f.CS() + r * a + n * (Dp / 2 + off);
            c.y = yc;
            var (hcol, hmr) = housing ?? (isNeon ? HousingCols[Int(0, 1)] : PickHousing());
            float fr = frame;
            if (!noHousing)
            {
                Hbox(c, r, UP, n, W / 2 + fr, Hh / 2 + fr, Dp / 2, hcol, hmr, new HashSet<string> { "+z" });
                Vector3 fc = c + n * (Dp / 2 + 0.012f);
                Hbox(fc + UP * (Hh / 2 + fr / 2), r, UP, n, W / 2 + fr, fr / 2, 0.015f, hcol, hmr);
                Hbox(fc - UP * (Hh / 2 + fr / 2), r, UP, n, W / 2 + fr, fr / 2, 0.015f, hcol, hmr);
                Hbox(fc + r * (W / 2 + fr / 2), r, UP, n, fr / 2, Hh / 2, 0.015f, hcol, hmr);
                Hbox(fc - r * (W / 2 + fr / 2), r, UP, n, fr / 2, Hh / 2, 0.015f, hcol, hmr);
            }
            Color tint = tintO ?? (isNeon ? NeonColor() : Color.white);
            int seed = NextSeed();
            float ft = ftypeO ?? (isNeon ? Ftype() : Ftype(0f, 0.05f, 0f));
            float inten = intenO ?? (design.Mode == 0 ? (float)Range(5, 8) : design.Mode == 4 ? (float)Range(4, 6) : design.Mode == 3 ? 0.06f : (float)Range(1.6, 2.4));
            Vector3 back = isNeon
                ? new Vector3(hcol.r * 0.6f + 0.008f, hcol.g * 0.6f + 0.008f, hcol.b * 0.6f + 0.01f)
                : new Vector3(0.02f, 0.02f, 0.02f);
            Face(c + n * (Dp / 2 + 0.003f), r, UP, W / 2, Hh / 2, n, design, tint, inten, seed, ft, back);
            if (lightOn && design.Mode != 3)
            {
                Vector3 lp = c + n * (1.0f + Hh * 0.15f);
                Color lc = isNeon ? tint : (lightColor ?? new Color(1, 0.9f, 0.8f));
                Light(lp, lc, (isNeon ? 5.5f : 4.5f) * Mathf.Sqrt(W * Hh) * lightMul, Mathf.Min(20, 8 + W), seed, ft, importance);
            }
            _signs++;
        }

        static void TenantStack(SFacade f, float a, float y0, int count)
        {
            Vector3 n = f.NS(), r = f.RS();
            float D = 0.95f, cellH = D / 2, T = 0.3f, gap = 0.3f;
            Vector3 baseP = f.CS() + r * a;
            float Htot = count * (cellH + 0.04f);
            baseP.y = y0 + Htot / 2;
            Vector3 c = baseP + n * (gap + D / 2);
            var hcol = new Color(0.55f, 0.55f, 0.56f); var hmr = new Vector2(0.6f, 0.4f);
            Hbox(c, r, UP, n, T / 2, Htot / 2 + 0.05f, D / 2 + 0.05f, hcol, hmr, new HashSet<string> { "+x", "-x" });
            int seed0 = NextSeed();
            for (int i = 0; i < count; i++)
            {
                var d = Pick(SignLib.Tenant);
                float yc = y0 + (i + 0.5f) * (cellH + 0.04f);
                Vector3 cc = c; cc.y = yc;
                int seed = NextSeed();
                float ft = _rnd.Next() < 0.08 ? 1 : 0;
                float inten = _rnd.Next() < 0.12 ? 0.05f : (float)Range(1.5, 2.2);
                Face(cc + r * (T / 2 + 0.003f), -n, UP, D / 2 - 0.02f, cellH / 2, r, d, Color.white, inten, seed, ft, new Vector3(0.02f, 0.02f, 0.025f));
                Face(cc - r * (T / 2 + 0.003f), n, UP, D / 2 - 0.02f, cellH / 2, -r, d, Color.white, inten, seed, ft, new Vector3(0.02f, 0.02f, 0.025f));
                Hbox(cc + UP * (cellH / 2 + 0.02f), r, UP, n, T / 2 + 0.02f, 0.02f, D / 2 + 0.05f, hcol, hmr);
            }
            for (int k = 0; k < 2; k++)
            {
                float yy = k == 0 ? Htot / 2 - 0.1f : -Htot / 2 + 0.1f;
                Vector3 p0 = baseP + UP * yy;
                Bar(p0, p0 + n * (gap + 0.02f), 0.03f, Steel.col, Steel.mr);
            }
            Vector3 lp = c + n * 0.2f; lp.y = y0 + Htot * 0.4f;
            Light(lp, new Color(1, 0.92f, 0.85f), 10 * Mathf.Sqrt(D * Htot), Mathf.Min(18, 8 + Htot), seed0, 0);
            _signs++;
        }

        static void Rooftop(SFacade f, SDesign design, float width = 0)
        {
            Vector3 n = f.NS(), r = f.RS();
            float W = Mathf.Min((float)f.Width * 0.85f, width > 0 ? width : (float)Range(8, 16));
            float Hh = W / design.Aspect;
            float top = f.CS().y + (float)f.Height / 2;
            Vector3 c = f.CS() + n * -0.6f; c.y = top + 0.8f + Hh / 2;
            var steel = (new Color(0.12f, 0.12f, 0.13f), new Vector2(0.8f, 0.5f));
            int nPosts = Mathf.Max(2, Mathf.RoundToInt(W / 3));
            for (int i = 0; i < nPosts; i++)
            {
                float x = -W / 2 + (i / (float)(nPosts - 1)) * W;
                Vector3 p = c + r * x + n * -0.25f;
                Bar(new Vector3(p.x, top, p.z), new Vector3(p.x, c.y + Hh / 2, p.z), 0.05f, steel.Item1, steel.Item2);
                Vector3 p2 = new Vector3(p.x, c.y, p.z) + n * -1.6f;
                Bar(new Vector3(p.x, top, p.z), p2, 0.035f, steel.Item1, steel.Item2);
            }
            for (int k = 0; k < 2; k++)
            {
                float yy = k == 0 ? -Hh / 2 : Hh / 2 - 0.05f;
                Vector3 p = c + n * -0.25f; p.y += yy;
                Bar(p + r * (-W / 2), p + r * (W / 2), 0.04f, steel.Item1, steel.Item2);
            }
            Color tint = NeonColor();
            int seed = NextSeed();
            float ft = _rnd.Next() < 0.15 ? 2 : 0;
            float inten = (float)Range(4, 6);
            Face(c, r, UP, W / 2, Hh / 2, n, new SDesign { UV = design.UV, Mode = design.Mode + 6 }, tint, inten, seed, ft, new Vector3(0.01f, 0.01f, 0.012f));
            Light(c + n * 1.5f, tint, 14 * Mathf.Sqrt(W * Hh), 20, seed, ft, 0.6f);
            _signs++;
        }

        // ---- populate (populate.js) ----
        public static int Populate()
        {
            int used = 0;
            foreach (var f in FacadeAPI.Collect())
            {
                float baseY = f.CS().y - (float)f.Height / 2;
                float top = f.CS().y + (float)f.Height / 2;
                float w = (float)f.Width;
                string d = f.District;
                if (w < 3 || f.Height < 5) continue;
                string detail = string.IsNullOrEmpty(f.Detail) ? "hi" : f.Detail;
                float dense = d == "avenue" ? 1 : d == "alley" ? 0.9f : d == "cross" ? 0.7f : 0.3f;
                if (detail == "mid") dense *= 0.3f;
                if (f.Lane) dense *= 0.5f;
                if (detail == "lo")
                {
                    if (_rnd.Next() < 0.05 && w > 10) Rooftop(f, Pick(SignLib.ChanH), width: Mathf.Min(w * 0.8f, (float)Range(8, 14)));
                    else if (_rnd.Next() < 0.05) Flat(f, 0, baseY + (float)f.Height * (float)Range(0.4, 0.8), Pick(SignLib.NeonH), width: (float)Range(4, 8), maxH: 2.5f, depth: 0.1f, importance: 0.5f);
                    continue;
                }
                if (_rnd.Next() > Mathf.Min(1, dense * 1.6f)) continue;
                _lightScale = d == "alley" ? 0.5f : d == "cross" ? 0.8f : 1;

                if (f.Shopfront && _rnd.Next() < 0.85 * dense + 0.1)
                {
                    float yc = baseY + (d == "alley" ? 2.9f : 3.5f); // fasciaY
                    bool neonFascia = _rnd.Next() < 0.3;
                    if (neonFascia)
                    {
                        var ds = Pick(SignLib.NeonH);
                        Flat(f, (float)Range(-0.1, 0.1) * w, yc, ds, width: Mathf.Min(w * 0.75f, 6), maxH: 1.1f, depth: 0.15f,
                            housing: HousingCols[Int(0, 2)]);
                    }
                    else
                    {
                        var ds = _rnd.Next() < 0.7 ? Pick(SignLib.BoxH) : Pick(SignLib.BoxSq);
                        float maxH = ds.Aspect < 1.5 ? 1.6f : 1.0f;
                        Flat(f, (float)Range(-0.1, 0.1) * w, yc + (maxH > 1.2f ? 0.3f : 0), ds, width: Mathf.Min(w * 0.8f, 6.5f), maxH: maxH);
                    }
                }

                int nBlades = Mathf.Max(0, Mathf.RoundToInt(w / 6.5f * dense * (float)Range(0.7, 1.4)));
                var slots = new List<float>();
                for (int i = 0; i < nBlades; i++) slots.Add(-w / 2 + 0.8f + (w - 1.6f) * ((i + (float)Range(0.2, 0.8)) / Mathf.Max(1, nBlades)));
                if (d == "avenue" && f.Height > 20 && _rnd.Next() < 0.45)
                {
                    float aa = (_rnd.Next() < 0.5 ? -1 : 1) * (w / 2 - 0.9f);
                    float y0 = baseY + (float)Range(8, 13);
                    float room = top - y0 - 1;
                    if (room > 6) Blade(f, aa, y0, _rnd.Next() < 0.7 ? Pick(SignLib.NeonV) : Pick(SignLib.BoxV),
                        depth: (float)Range(1.5, 2.3), maxH: Mathf.Min(room, (float)Range(8, 15)), thick: 0.45f, edge: _rnd.Next() < 0.6, importance: 1.5f);
                }
                foreach (var aa in slots)
                {
                    float y0 = baseY + (d == "alley" ? (float)Range(3.2, 4.5) : (float)Range(4.2, 6.5));
                    float room = top - y0 - 0.8f;
                    if (room < 1.8f) continue;
                    float kind = (float)_rnd.Next();
                    if (kind < 0.16 && room > 3)
                    {
                        TenantStack(f, aa, y0, Mathf.Min(Mathf.FloorToInt(room / 0.55f), Int(4, 12)));
                    }
                    else
                    {
                        var ds = kind < 0.6 ? Pick(SignLib.NeonV) : Pick(SignLib.BoxV);
                        if (ds != null)
                            Blade(f, aa, y0, ds, maxH: Mathf.Min(room, d == "alley" ? 4.5f : (float)Range(3.5, 9)),
                                depth: d == "alley" ? (float)Range(0.55, 0.85) : (float?)null);
                    }
                }

                int floors = Mathf.FloorToInt((float)f.Height / (float)f.FloorHeight);
                int upperBlade = 0;
                for (int k = 2; k < floors - 1; k++)
                {
                    if (_rnd.Next() > 0.32 * dense) continue;
                    float yc = baseY + k * (float)f.FloorHeight + (float)f.FloorHeight * 0.5f;
                    float r = (float)_rnd.Next();
                    float aa = (float)Range(-0.3, 0.3) * w;
                    if (r < 0.25 && upperBlade < 2 && top - (yc - 1) > 4)
                    {
                        upperBlade++;
                        Blade(f, (_rnd.Next() < 0.5 ? -1 : 1) * (float)Range(0.2, 0.45) * w, yc - 1,
                            _rnd.Next() < 0.6 ? Pick(SignLib.NeonV) : Pick(SignLib.BoxV),
                            maxH: Mathf.Min(top - yc, (float)Range(3, 7)), lightMul: 0.7f, importance: 0.8f);
                    }
                    else if (r < 0.5) Flat(f, aa, yc, Pick(SignLib.NeonH), width: (float)Range(2.5, Mathf.Min(7, w * 0.8f)), maxH: 2.0f, depth: 0.1f);
                    else if (r < 0.65) Flat(f, aa, yc, Pick(SignLib.NeonIcon), width: (float)Range(1.4, 2.4), depth: 0.1f);
                    else if (r < 0.85) Flat(f, aa, yc, Pick(SignLib.BoxSq), width: (float)Range(1.6, 2.6));
                    else if (SignLib.Screens.Count > 0 && w > 8) Flat(f, aa, yc + 1.5f, Pick(SignLib.Screens), width: (float)Range(4, 6.5f), depth: 0.3f, intenO: 2.2f,
                        housing: (new Color(0.02f, 0.02f, 0.02f), new Vector2(0.5f, 0.4f)), lightColor: new Color(0.8f, 0.5f, 1f));
                }

                if ((d == "alley" || d == "cross") && SignLib.Posters.Count > 0 && _rnd.Next() < 0.6)
                {
                    int nP = Int(1, 4);
                    for (int i = 0; i < nP; i++)
                    {
                        var p = Pick(SignLib.Posters);
                        float fa = (float)Range(-0.45, 0.45) * w;
                        Flat(f, fa, baseY + (float)Range(1.2, 1.9), p, width: (float)Range(0.5, 0.75), depth: 0.004f, off: 0.005f,
                            noHousing: true, lightOn: false, intenO: 0.04f, tintO: new Color(0.85f, 0.85f, 0.85f));
                    }
                }

                if (d != "alley" && f.Height < 45 && w > 10 && _rnd.Next() < 0.18 * dense + 0.04)
                {
                    Rooftop(f, Pick(SignLib.ChanH), width: Mathf.Min(w * 0.8f, (float)Range(8, 14)));
                }
                used++;
            }
            _lightScale = 1;
            return used;
        }

        // flicker (C# twin of shader NkFlicker) for kept real lights
        public static float PcgHash(float seed)
        {
            uint state = (uint)seed * 747796405u + 2891336453u;
            uint word = ((state >> ((int)(state >> 28) + 4)) ^ state) * 277803737u;
            uint res = (word >> 22) ^ word;
            return res / 4294967296f;
        }

        public static float Flicker(float seed, float type, float seg, float t)
        {
            float Burst() => PcgHash(Mathf.Floor(t * 0.7f) + seed * 7919);
            float Fast() => PcgHash(Mathf.Floor(t * 23f) + seed * 4999 + seg * 1597);
            if (type >= 4.5f) return Mathf.Sin(t * 1.7f + seed * 0.4f) * 0.18f + 0.86f;
            if (type >= 3.5f)
            {
                float ph = Mathf.Repeat(t * 0.45f - seg * 0.11f + seed * 0.137f, 1f);
                float q = Mathf.Clamp01(ph / 0.08f); q = q * q * (3 - 2 * q);
                return Mathf.Max(q * (ph < 0.72f ? 1 : 0), 0.05f);
            }
            if (type >= 2.5f)
            {
                float segH = PcgHash(seg * 613 + seed * 2851);
                float segDead = segH < 0.16f ? 0.025f : (segH < 0.3f ? Mathf.Max(PcgHash(Mathf.Floor(t * 15f) + seg * 977 + seed * 31) < 0.45f ? 0.025f : 1f, 0.025f) : 1f);
                float burstWin = Burst();
                float stutter = burstWin > 0.72f ? (Fast() < 0.38f ? 0.08f : 0.92f) : 1f;
                return segDead * (stutter * 0.3f + 0.7f);
            }
            if (type >= 1.5f)
            {
                float burstWin = Burst();
                return burstWin > 0.72f ? (Fast() < 0.38f ? 0.08f : 0.92f) : 1f;
            }
            return 1f;
        }

        public static string Build(Material faces, Material housing)
        {
            var old = GameObject.Find("/FacadeSigns");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("FacadeSigns").transform;

            SignLib.Load();
            _rnd = new Mulberry32(20771);
            _seed = 1; _signs = 0; _lights = 0; _lightScale = 1;
            _FV = new List<Vector3>(); _FI = new List<int>(); _FC = new List<Color>(); _FUV = new List<Vector2>();
            _FSP = new List<Vector4>(); _FLUV = new List<Vector4>(); _FB = new List<Vector3>();
            _HV = new List<Vector3>(); _HI = new List<int>(); _HC = new List<Color>(); _HMR = new List<Vector4>();
            _pool = new List<PoolL>();
            FlickerL.Clear(); FlickerSeed.Clear(); FlickerType.Clear(); FlickerBase.Clear();

            int used = Populate();

            // lanterns (rows/strings/tall/andon) — append lights to _pool before the budget
            string lan = Lanterns.Build(root);

            // holographic koi swimming above the avenue
            string koi = Koi.Build(root);

            Commit(root, "SignFaces", _FV, _FI, _FC, _FUV, _FSP, _FB, _FLUV, faces);
            Commit(root, "SignHousing", _HV, _HI, _HC, null, _HMR, null, null, housing);

            // light budget: shader arrays hold 384; ~196 already used by buildings+lamps+billboards
            _pool.Sort((a, b) => (b.inten * b.imp).CompareTo(a.inten * a.imp));
            int keep = Mathf.Min(_pool.Count, 188);
            for (int j = 0; j < keep; j++)
            {
                var e = _pool[j];
                var go = new GameObject($"sgnLight_{j}");
                go.transform.SetParent(root, false);
                go.transform.position = Mir(e.pos);
                var pl = go.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = e.col;
                pl.range = e.dist;
                pl.intensity = e.inten;
                pl.shadows = LightShadows.None;
                FlickerL.Add(pl); FlickerSeed.Add(e.seed); FlickerType.Add(e.ftype); FlickerBase.Add(e.inten);
            }

            // re-push full light arrays
            var all = new List<Light>();
            foreach (var rn in new[] { "/Buildings", "/StreetLamps", "/Signage", "/FacadeSigns" })
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
            return $"facade signs: {used} facades, {_signs} signs, {_lights} lights ({keep} real) | {lan} | {koi}";
        }

        static void Commit(Transform root, string name, List<Vector3> V, List<int> Ix, List<Color> C, List<Vector2> uv0, List<Vector4> uv1, List<Vector3> uv2, List<Vector4> uv3, Material mat)
        {
            if (V.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(V);
            mesh.SetColors(C);
            mesh.SetIndices(Ix, MeshTopology.Triangles, 0);
            if (uv0 != null) mesh.SetUVs(0, uv0);
            if (uv1 != null) mesh.SetUVs(1, uv1);
            if (uv2 != null) mesh.SetUVs(2, uv2);
            if (uv3 != null) mesh.SetUVs(3, uv3);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
