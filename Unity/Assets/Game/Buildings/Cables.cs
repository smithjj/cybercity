// Port of src/systems/buildings/cables.js — utility poles + sagging power/telecom cables:
// along the avenue, cross streets, the alley, service drops to facades, building-to-building spans.
using System;
using System.Collections.Generic;

namespace NeonKaido
{
    public static class Cables
    {
        class PoleTop { public double X, Z, H; public double[][][] Arms = new double[3][][]; public double[] Tel, Tel2; }

        static readonly BAttrSet WIRE = new BAttrSet().Set("aCol", new[] { 0.015, 0.015, 0.017, 0 }).Set("aPbr", new[] { 0.35, 0.2, 0, 0 });
        static readonly BAttrSet POLE = new BAttrSet().Set("aCol", new[] { 0.42, 0.41, 0.39, 0 }).Set("aPbr", new[] { 0.75, 0, 0, 0.9 });
        static readonly BAttrSet ARM = new BAttrSet().Set("aCol", new[] { 0.2, 0.2, 0.2, 0 }).Set("aPbr", new[] { 0.45, 0.7, 0, 0.6 });
        static readonly BAttrSet INS = new BAttrSet().Set("aCol", new[] { 0.6, 0.62, 0.6, 0 }).Set("aPbr", new[] { 0.3, 0, 0, 0.3 });
        static readonly BAttrSet TTR = new BAttrSet().Set("aCol", new[] { 0.5, 0.52, 0.5, 0 }).Set("aPbr", new[] { 0.5, 0.3, 0, 0.7 });
        static readonly BAttrSet TBOX = new BAttrSet().Set("aCol", new[] { 0.55, 0.55, 0.52, 0 }).Set("aPbr", new[] { 0.5, 0.2, 0, 0.8 });
        static readonly BAttrSet PLATE = new BAttrSet().Set("aCol", new[] { 0.6, 0.55, 0.15, 0 }).Set("aPbr", new[] { 0.5, 0, 0, 0.7 });

        static BBucket Det(BChunkedBuckets B, double x, double z) => B.Get(x, z, "detail", BuildArchitecture.DetailAttrs);

        static void Wire(BChunkedBuckets B, double[] a, double[] b, double sag, double th = 0.022, int segs = 10)
        {
            double len = GroundGeo.Hypot2(b[0] - a[0], b[2] - a[2]);
            int n = Math.Max(3, Math.Min(segs, (int)Math.Ceiling(len / 2.5)));
            double[] prev = a;
            var bucket = Det(B, (a[0] + b[0]) / 2, (a[2] + b[2]) / 2);
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double[] p = { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t - sag * 4 * t * (1 - t), a[2] + (b[2] - a[2]) * t };
                bucket.Beam(prev, p, th, WIRE);
                prev = p;
            }
        }

        // ---------- pole lines ----------
        // dir: 'z' => line runs along z (crossarms along x); 'x' => runs along x
        static List<PoleTop> PoleLine(BuildingsRng r, BChunkedBuckets B, BOut outp, double fixed_, double from, double to, char dir, Func<double, bool> skip, double step = 24)
        {
            var poles = new List<double[]>();
            double t = from + r.Range(2, 8);
            while ((from < to && t < to) || (from > to && t > to))
            {
                if (!skip(t)) poles.Add(dir == 'z' ? new[] { fixed_, t } : new[] { t, fixed_ });
                t += (from < to ? 1 : -1) * (step + r.Range(-4, 4));
            }
            var tops = new List<PoleTop>();
            foreach (var pz in poles)
            {
                double x = pz[0], z = pz[1];
                double H = r.Range(10.5, 12);
                var b = Det(B, x, z);
                b.Cyl(new[] { x, 0.0, z }, new[] { x, H, z }, 0.17, POLE, 8);
                b.Cyl(new[] { x, H, z }, new[] { x, H + 0.2, z }, 0.12, POLE, 8);
                var arms = new List<double[][]>();
                double[] armYs = { H - 0.4, H - 1.3, H - 2.3 };
                foreach (double y in armYs)
                {
                    double half = 0.95;
                    double[] a0 = dir == 'z' ? new[] { x - half, y, z } : new[] { x, y, z - half };
                    double[] a1 = dir == 'z' ? new[] { x + half, y, z } : new[] { x, y, z + half };
                    b.Beam(a0, a1, 0.09, ARM);
                    var pts = new List<double[]>();
                    foreach (double f in new[] { -0.85, 0.0, 0.85 })
                    {
                        double[] p = dir == 'z' ? new[] { x + f, y + 0.12, z } : new[] { x, y + 0.12, z + f };
                        b.Box(p[0], p[1], p[2], 0.07, 0.14, 0.07, INS);
                        pts.Add(new[] { p[0], p[1] + 0.08, p[2] });
                    }
                    arms.Add(pts.ToArray());
                    // diagonal brace
                    b.Beam(new[] { x, y - 0.5, z }, dir == 'z' ? new[] { x + 0.6, y, z } : new[] { x, y, z + 0.6 }, 0.04, ARM);
                }
                // transformer + telecom box
                if (r.Chance(0.4))
                {
                    double ox = dir == 'z' ? 0 : 0.42, oz = dir == 'z' ? 0.42 : 0;
                    b.Cyl(new[] { x + ox, H - 4.2, z + oz }, new[] { x + ox, H - 3.1, z + oz }, 0.3, TTR, 10);
                    b.Beam(new[] { x, H - 3.6, z }, new[] { x + ox, H - 3.6, z + oz }, 0.05, ARM);
                }
                b.Box(x + (dir == 'z' ? 0.25 : 0), 5.2, z + (dir == 'z' ? 0 : 0.25), 0.3, 0.45, 0.3, TBOX);
                // telecom cable clamp ring
                double[] tel = { x, 6.2, z }, tel2 = { x, 5.8, z };
                // step bolts
                for (double y = 2.4; y < H - 2.5; y += 0.45)
                {
                    double s = ((long)Math.Floor(y / 0.45 + 0.5) % 2 == 1 ? 1 : -1) * 0.22;
                    b.Beam(new[] { x, y, z }, dir == 'z' ? new[] { x, y, z + s } : new[] { x + s, y, z }, 0.025, ARM);
                }
                // address plate
                b.Box(x + (dir == 'z' ? -0.18 : 0), 2.2, z + (dir == 'z' ? 0 : -0.18),
                    dir == 'z' ? 0.02 : 0.22, 0.55, dir == 'z' ? 0.22 : 0.02, PLATE);
                outp.Colliders.Add(new BCollider { X = x, Y = H / 2, Z = z, Hx = 0.18, Hy = H / 2, Hz = 0.18 });
                tops.Add(new PoleTop { X = x, Z = z, Arms = arms.ToArray(), Tel = tel, Tel2 = tel2, H = H });
            }
            // wires between consecutive poles
            for (int i = 0; i < tops.Count - 1; i++)
            {
                var A = tops[i]; var Bp = tops[i + 1];
                double len = GroundGeo.Hypot2(Bp.X - A.X, Bp.Z - A.Z);
                if (len > 45) continue;
                for (int k = 0; k < 3; k++) for (int j = 0; j < 3; j++)
                {
                    if (k == 2 && j == 1) continue;
                    Wire(B, A.Arms[k][j], Bp.Arms[k][j], (0.12 + len * r.Range(0.025, 0.05)) * (k == 0 ? 0.8 : 1.0), 0.016);
                }
                Wire(B, A.Tel, Bp.Tel, (0.12 + len * r.Range(0.025, 0.05)) * 1.3, 0.04);
                Wire(B, A.Tel2, Bp.Tel2, (0.12 + len * r.Range(0.025, 0.05)) * 1.5, 0.03);
                if (r.Chance(0.5)) Wire(B, new[] { A.X, 6.6, A.Z }, new[] { Bp.X, 6.6, Bp.Z }, (0.12 + len * r.Range(0.025, 0.05)) * 1.2, 0.025);
            }
            return tops;
        }

        // ---------- service drops to facades ----------
        static void Drops(BuildingsRng r, BChunkedBuckets B, List<PoleTop> tops, Func<PoleTop, double, double, double[]> facadeAt)
        {
            foreach (var p in tops)
            {
                int n = r.Int(2, 5);
                for (int k = 0; k < n; k++)
                {
                    double off = r.Range(-9, 9), y = r.Range(4.5, 9.5);
                    double[] tgt = facadeAt(p, off, y);
                    if (tgt == null) continue;
                    double[] src = r.Chance(0.5) ? p.Tel : p.Arms[2][r.Int(0, 2)];
                    double len = GroundGeo.Hypot2(tgt[0] - src[0], tgt[2] - src[2]);
                    Wire(B, src, tgt, (0.12 + len * r.Range(0.025, 0.05)) * 0.7, r.Chance(0.5) ? 0.016 : 0.022, 6);
                    // wall anchor
                    Det(B, tgt[0], tgt[2]).Box(tgt[0], tgt[1], tgt[2], 0.1, 0.1, 0.1, ARM);
                }
            }
        }

        public static void BuildCables(BChunkedBuckets B, BOut outp)
        {
            var r = new BuildingsRng(0xCAB1E);
            double X = Layout.AvenueRoadHalfWidth + 0.7;
            double[] cz = Layout.CrossZs;
            Func<double, bool> avSkip = z =>
            {
                foreach (double c in cz) if (Math.Abs(z - c) < 10) return true;
                return Math.Abs(z - Layout.RailZ) < 8 || Math.Abs(z - Layout.ExpresswayZ) < 11 || Math.Abs(z - Layout.AlleyZ) < 3.5;
            };
            double front = Layout.BlocksFrontageOffset;
            var east = PoleLine(r, B, outp, X, Layout.AvenueZStart - 4, Layout.AvenueZEnd + 4, 'z', avSkip, 22);
            var west = PoleLine(r, B, outp, -X, Layout.AvenueZStart - 12, Layout.AvenueZEnd + 4, 'z', avSkip, 22);
            Drops(r, B, east, (p, dz, y) => new[] { front - 0.02, y, p.Z + dz });
            Drops(r, B, west, (p, dz, y) => new[] { -front + 0.02, y, p.Z + dz });
            // spans across the avenue: pole to pole and facade to facade
            for (int i = 0; i < Math.Min(east.Count, west.Count); i++)
            {
                var e = east[i];
                PoleTop w = west[0];
                foreach (var q in west) if (Math.Abs(q.Z - e.Z) < Math.Abs(w.Z - e.Z)) w = q;
                if (Math.Abs(w.Z - e.Z) > 14) continue;
                if (r.Chance(0.65)) Wire(B, e.Arms[1][0], w.Arms[1][2], 0.9, 0.02);
                if (r.Chance(0.5)) Wire(B, e.Tel, w.Tel, 1.2, 0.035);
            }
            for (double z = Layout.AvenueZStart - 6; z > Layout.AvenueZEnd; z -= r.Range(10, 22))
            {
                if (avSkip(z)) continue;
                double y1 = r.Range(7, 13), y2 = y1 + r.Range(-2, 2);
                Wire(B, new[] { front - 0.05, y1, z }, new[] { -front + 0.05, y2, z + r.Range(-6, 6) }, r.Range(0.8, 1.8), 0.02, 12);
            }

            // ---------- cross streets (near the avenue) ----------
            foreach (double c in cz)
            {
                double zN = c - Layout.CrossRoadHalfWidth - 0.6, zS = c + Layout.CrossRoadHalfWidth + 0.6;
                foreach (int sgn in new[] { 1, -1 })
                {
                    double xa = sgn * (front + 3), xb = sgn * 125;
                    var t1 = PoleLine(r, B, outp, zN, xa, xb, 'x', _ => false, 26);
                    var t2 = PoleLine(r, B, outp, zS, xa, xb, 'x', _ => false, 26);
                    Drops(r, B, t1, (p, dx, y) => new[] { p.X + dx, y, c - Layout.CrossRoadHalfWidth - Layout.CrossSidewalkWidth + 0.02 });
                    Drops(r, B, t2, (p, dx, y) => new[] { p.X + dx, y, c + Layout.CrossRoadHalfWidth + Layout.CrossSidewalkWidth - 0.02 });
                    for (int i = 0; i < Math.Min(t1.Count, t2.Count); i++) if (r.Chance(0.5)) Wire(B, t1[i].Tel, t2[i].Tel, 0.9, 0.03);
                }
            }

            // ---------- the alley: dense criss-crossing wires ----------
            double zaN = Layout.AlleyZ - Layout.AlleyWidth / 2 + 0.02, zaS = Layout.AlleyZ + Layout.AlleyWidth / 2 - 0.02;
            Func<double, double> gY = x => x > Layout.AlleyStairsXStart ? 0 : x < Layout.AlleyStairsXEnd ? Layout.AlleyStairsRise
                : (Layout.AlleyStairsXStart - x) / (Layout.AlleyStairsXStart - Layout.AlleyStairsXEnd) * Layout.AlleyStairsRise;
            for (double x = Layout.AlleyXStart - 1; x > Layout.AlleyXEnd + 1; x -= r.Range(0.6, 1.8))
            {
                double y = gY(x) + r.Range(3.4, 10);
                double x2 = x + r.Range(-3, 3);
                Wire(B, new[] { x, y, zaN }, new[] { x2, gY(x2) + r.Range(3.4, 10), zaS }, r.Range(0.15, 0.7), r.Chance(0.3) ? 0.03 : 0.016, 6);
                Det(B, x, zaN).Box(x, y, zaN + 0.04, 0.08, 0.08, 0.08, ARM);
            }
            // long runs along the alley walls & bundles
            for (int k = 0; k < 14; k++)
            {
                double z = r.Chance(0.5) ? zaN + r.Range(0.05, 0.6) : zaS - r.Range(0.05, 0.6);
                double x = Layout.AlleyXStart - r.Range(0, 6);
                while (x > Layout.AlleyXEnd + 3)
                {
                    double x2 = Math.Max(Layout.AlleyXEnd + 1, x - r.Range(4, 9));
                    double y1 = gY(x) + 4 + k * 0.35, y2 = gY(x2) + 4 + k * 0.35;
                    Wire(B, new[] { x, y1, z }, new[] { x2, y2, z + r.Range(-0.3, 0.3) }, r.Range(0.1, 0.5), 0.018, 5);
                    x = x2;
                }
            }
        }
    }
}
