using System;
using System.Collections.Generic;
using System.Linq;

namespace NeonKaido
{
    /// Exact port of src/systems/ground/geometry.js — pure sim-space math (oracle-comparable).
    /// 2D points are [x, z] in sim space; "shape space" is (x, -z) where CCW is positive.
    public static class GroundGeo
    {
        public struct Rect2 { public double X0, X1, Z0, Z1; }

        public struct CurbData
        {
            public List<double[]> Pos;   // sim (x, y, z), triangle-major (a0,b0,b1,a0,b1,a1 per quad)
            public List<double[]> Nor;   // sim
            public List<double[]> Uv;    // (u, v)
        }

        public struct SidewalkPoly
        {
            public List<double[]> Rounded;  // sim [x, z]
            public List<double[]> Inner;    // inset by curbW
            public int[] Indices;           // ear-clipped in shape space (CCW)
            public List<double[]> Collider; // original poly.pts
        }

        public class GroundData
        {
            public List<Rect2> BaseRects;
            public List<SidewalkPoly> Sidewalks;
            public CurbData Curb;
            public List<Rect2> AlleyRects;
            public Rect2 Promenade;
            public Rect2 WallWest, WallEast; // X0/X1 = bank x, Z0/Z1 = z span
            public double WallH = 5.0;
        }

        // ---- polygon helpers (2D points [x, z]) ----

        public static double SignedArea(IList<double[]> pts)
        {
            double a = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p1 = pts[i]; var p2 = pts[(i + 1) % pts.Count];
                a += p1[0] * -p2[1] - p2[0] * -p1[1];
            }
            return a / 2;
        }

        // JS Math.hypot == fdlibm: a=max(|x|,|y|); b=min; return a*sqrt(1+(b/a)^2)
        // (.NET Math.Hypot is correctly-rounded and differs by 1 ulp from V8 on some inputs!)
        public static double Hypot2(double x, double y)
        {
            double a = Math.Abs(x), b = Math.Abs(y);
            if (a < b) { double t = a; a = b; b = t; }
            if (b == 0) return a + b;
            double t2 = b / a;
            return a * Math.Sqrt(1 + t2 * t2);
        }

        public static double[] Norm2(double[] v)
        {
            // JS: const norm = v => { const l = Math.hypot(v[0], v[1]) || 1; return [v[0]/l, v[1]/l]; }
            double l = Hypot2(v[0], v[1]);
            if (l == 0) l = 1;
            return new[] { v[0] / l, v[1] / l };
        }

        // Scaled 3-arg hypot (ECMA Math.hypot semantics: divide by max, sum squares, sqrt, scale back)
        static double Hypot3(double a, double b, double c)
        {
            double max = Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c)));
            if (max == 0) return 0;
            a /= max; b /= max; c /= max;
            return max * Math.Sqrt(a * a + b * b + c * c);
        }

        public static List<double[]> RoundPoly(IList<double[]> pts, IList<int> roundIdx, double r, int seg = 14)
        {
            var outp = new List<double[]>();
            var set = new HashSet<int>(roundIdx);
            for (int i = 0; i < pts.Count; i++)
            {
                var V = pts[i];
                if (!set.Contains(i)) { outp.Add(V); continue; }
                var Pp = pts[(i - 1 + pts.Count) % pts.Count];
                var N = pts[(i + 1) % pts.Count];
                var dp = Norm2(new[] { Pp[0] - V[0], Pp[1] - V[1] });
                var dn = Norm2(new[] { N[0] - V[0], N[1] - V[1] });
                double cx = V[0] + (dp[0] + dn[0]) * r, cz = V[1] + (dp[1] + dn[1]) * r;
                double t1x = V[0] + dp[0] * r, t1z = V[1] + dp[1] * r;
                double t2x = V[0] + dn[0] * r, t2z = V[1] + dn[1] * r;
                double a1 = Math.Atan2(t1z - cz, t1x - cx), a2 = Math.Atan2(t2z - cz, t2x - cx);
                double d = a2 - a1;
                while (d > Math.PI) d -= 2 * Math.PI;
                while (d < -Math.PI) d += 2 * Math.PI;
                for (int k = 0; k <= seg; k++)
                {
                    double a = a1 + (d * k) / seg;
                    outp.Add(new[] { cx + Math.Cos(a) * r, cz + Math.Sin(a) * r });
                }
            }
            return outp;
        }

        // Inward normals for a CCW (shape space) polygon given in [x, z]
        public static List<double[]> EdgeInward(IList<double[]> pts)
        {
            var outp = new List<double[]>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                double sx = q[0] - p[0], sy = -(q[1] - p[1]);
                var n = Norm2(new[] { -sy, sx });   // shape space
                outp.Add(new[] { n[0], -n[1] });   // back to [x, z]
            }
            return outp;
        }

        public static List<double[]> InsetPoly(IList<double[]> pts, double d)
        {
            var en = EdgeInward(pts);
            var outp = new List<double[]>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var n1 = en[(i - 1 + pts.Count) % pts.Count];
                var n2 = en[i];
                double s = 1 + n1[0] * n2[0] + n1[1] * n2[1];
                double m0 = (n1[0] + n2[0]) / Math.Max(s, 0.2);
                double m1 = (n1[1] + n2[1]) / Math.Max(s, 0.2);
                outp.Add(new[] { p[0] + m0 * d, p[1] + m1 * d });
            }
            return outp;
        }

        struct RibbonVert { public double[] Pos; public double[] Nor; public double[] Uv; }

        // Curb ribbon along a closed polygon. Profile (inward offset, y): road bottom -> face -> chamfer -> top strip.
        public static CurbData CurbRibbon(IList<double[]> pts, double curbW, double curbH)
        {
            int n = pts.Count;
            var en = EdgeInward(pts);
            var vIn = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                var n1 = en[(i - 1 + n) % n];
                var n2 = en[i];
                double s = 1 + n1[0] * n2[0] + n1[1] * n2[1];
                vIn.Add(new[] { (n1[0] + n2[0]) / Math.Max(s, 0.2), (n1[1] + n2[1]) / Math.Max(s, 0.2) });
            }
            var smooth = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var n1 = en[(i - 1 + n) % n];
                var n2 = en[i];
                smooth[i] = n1[0] * n2[0] + n1[1] * n2[1] > 0.85;
            }
            var prof = new[] { new[] { 0d, -0.06 }, new[] { 0d, curbH - 0.022 }, new[] { 0.028, curbH }, new[] { curbW, curbH } };
            var profV = new double[prof.Length];
            for (int k = 1; k < prof.Length; k++)
                profV[k] = profV[k - 1] + Hypot2(prof[k][0] - prof[k - 1][0], prof[k][1] - prof[k - 1][1]);

            var pos = new List<double[]>();
            var nor = new List<double[]>();
            var uvs = new List<double[]>();
            double u = 0;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var A = pts[i]; var Bp = pts[j];
                double len = Hypot2(Bp[0] - A[0], Bp[1] - A[1]);
                var nA = smooth[i] ? Norm2(vIn[i]) : en[i];
                var nB = smooth[j] ? Norm2(vIn[j]) : en[i];
                for (int k = 0; k < prof.Length - 1; k++)
                {
                    var p0 = prof[k]; var p1 = prof[k + 1];
                    double dIn = p1[0] - p0[0], dY = p1[1] - p0[1];
                    double pl = Hypot2(dIn, dY);
                    double nOut = dY / pl, nUp = dIn / pl; // perpendicular (rotated) components
                    var a0 = Vertex(A[0], A[1], vIn[i], nA, p0[0], p0[1], u, profV[k], nOut, nUp);
                    var a1 = Vertex(A[0], A[1], vIn[i], nA, p1[0], p1[1], u, profV[k + 1], nOut, nUp);
                    var b0 = Vertex(Bp[0], Bp[1], vIn[j], nB, p0[0], p0[1], u + len, profV[k], nOut, nUp);
                    var b1 = Vertex(Bp[0], Bp[1], vIn[j], nB, p1[0], p1[1], u + len, profV[k + 1], nOut, nUp);
                    // winding: front faces outward/up — JS emit order a0,b0,b1,a0,b1,a1
                    foreach (var v in new[] { a0, b0, b1, a0, b1, a1 })
                    {
                        pos.Add(v.Pos); nor.Add(v.Nor); uvs.Add(v.Uv);
                    }
                }
                u += len;
            }
            var data = new CurbData { Pos = pos, Nor = nor, Uv = uvs };
            FixWinding(data);
            return data;
        }

        static RibbonVert Vertex(double px, double pz, double[] vin, double[] nrm, double off, double y,
            double uu, double vv, double nOut, double nUp)
        {
            double X = px + vin[0] * off, Z = pz + vin[1] * off;
            double nx = -nrm[0] * nOut, nz = -nrm[1] * nOut, ny = nUp;
            double l = Hypot3(nx, ny, nz);
            return new RibbonVert
            {
                Pos = new[] { X, y, Z },
                Nor = new[] { nx / l, ny / l, nz / l },
                Uv = new[] { uu, vv },
            };
        }

        static void FixWinding(CurbData d)
        {
            // JS: test first triangle's geometric normal against stored normal; swap v1<->v2 of all tris if flipped.
            // Vertex layout is triangle-major; first tri = vertices 0,1,2.
            var p = d.Pos;
            var nn = d.Nor;
            var e1 = Sub(p[1], p[0]);
            var e2 = Sub(p[2], p[0]);
            var fn = Cross(e1, e2);
            if (fn[0] * nn[0][0] + fn[1] * nn[0][1] + fn[2] * nn[0][2] < 0)
            {
                for (int t = 0; t < p.Count; t += 3)
                {
                    Swap3(p, t); Swap3(nn, t); Swap3(d.Uv, t);
                }
            }
        }

        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double[] Cross(double[] a, double[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        static void Swap3(List<double[]> arr, int t)
        {
            int s = arr[0].Length;
            for (int c = 0; c < s; c++)
            {
                double a = arr[t + 1][c];
                arr[t + 1][c] = arr[t + 2][c];
                arr[t + 2][c] = a;
            }
        }

        public static List<Rect2> RectsMinus(Rect2 world, IEnumerable<Rect2> excl)
        {
            var ex = excl.ToList();
            var xsSet = new List<double> { world.X0, world.X1 };
            foreach (var r in ex) { xsSet.Add(r.X0); xsSet.Add(r.X1); }
            var xs = xsSet.Where(v => v >= world.X0 && v <= world.X1).Distinct().OrderBy(v => v).ToList();
            var zsSet = new List<double> { world.Z0, world.Z1 };
            foreach (var r in ex) { zsSet.Add(r.Z0); zsSet.Add(r.Z1); }
            var zs = zsSet.Where(v => v >= world.Z0 && v <= world.Z1).Distinct().OrderBy(v => v).ToList();

            var outp = new List<Rect2>();
            for (int i = 0; i < xs.Count - 1; i++)
            {
                for (int j = 0; j < zs.Count - 1; j++)
                {
                    double cx = (xs[i] + xs[i + 1]) / 2, cz = (zs[j] + zs[j + 1]) / 2;
                    bool hit = false;
                    foreach (var r in ex)
                        if (cx > r.X0 && cx < r.X1 && cz > r.Z0 && cz < r.Z1) { hit = true; break; }
                    if (hit) continue;
                    outp.Add(new Rect2 { X0 = xs[i], X1 = xs[i + 1], Z0 = zs[j], Z1 = zs[j + 1] });
                }
            }
            outp.Sort((a, b) => a.X0 != b.X0 ? a.X0.CompareTo(b.X0) : a.Z0.CompareTo(b.Z0));
            var merged = new List<Rect2>();
            foreach (var r in outp)
            {
                int last = merged.Count - 1;
                if (last >= 0 && merged[last].X0 == r.X0 && merged[last].X1 == r.X1 &&
                    Math.Abs(merged[last].Z1 - r.Z0) < 1e-6)
                {
                    var m2 = merged[last];
                    m2.Z1 = r.Z1;
                    merged[last] = m2;
                }
                else merged.Add(r);
            }
            return merged;
        }

        public static GroundData BuildGroundGeometry()
        {
            var al = new Rect2
            {
                X0 = GroundPlan.AlleyX0, X1 = GroundPlan.AlleyX1,
                Z0 = GroundPlan.AlleyZ - GroundPlan.AlleyHalf, Z1 = GroundPlan.AlleyZ + GroundPlan.AlleyHalf
            };
            var alleyLower = new Rect2 { X0 = GroundPlan.StairsXStart, X1 = GroundPlan.AlleyX1, Z0 = al.Z0, Z1 = al.Z1 };
            var alleyUpper = new Rect2 { X0 = GroundPlan.AlleyX0, X1 = GroundPlan.StairsXEnd, Z0 = al.Z0, Z1 = al.Z1 };
            var prom = new Rect2 { X0 = GroundPlan.PromX0, X1 = GroundPlan.PromX1, Z0 = GroundPlan.PromZ0, Z1 = GroundPlan.PromZ1 };
            var river = new Rect2 { X0 = GroundPlan.RiverX0, X1 = GroundPlan.RiverX1, Z0 = GroundPlan.WorldZ0 - 1, Z1 = GroundPlan.WorldZ1 + 1 };
            var world = new Rect2 { X0 = GroundPlan.WorldX0, X1 = GroundPlan.WorldX1, Z0 = GroundPlan.WorldZ0, Z1 = GroundPlan.WorldZ1 };

            var data = new GroundData
            {
                BaseRects = RectsMinus(world, new[] { alleyLower, prom, river }),
                AlleyRects = new List<Rect2> { alleyLower, alleyUpper },
                Promenade = prom,
                WallWest = new Rect2 { X0 = GroundPlan.RiverX0, X1 = GroundPlan.RiverX0, Z0 = GroundPlan.WorldZ0, Z1 = GroundPlan.WorldZ1 },
                WallEast = new Rect2 { X0 = GroundPlan.RiverX1, X1 = GroundPlan.RiverX1, Z0 = GroundPlan.WorldZ0, Z1 = GroundPlan.WorldZ1 },
                Sidewalks = new List<SidewalkPoly>(),
                Curb = new CurbData { Pos = new List<double[]>(), Nor = new List<double[]>(), Uv = new List<double[]>() },
            };

            // sidewalks + curbs (JS mergeGeometries = plain concatenation, order preserved)
            foreach (var poly in GroundPlan.SidewalkPolys())
            {
                var pts = RoundPoly(poly.Pts, poly.Round, GroundPlan.CornerR);
                if (SignedArea(pts) < 0) pts.Reverse();
                var curb = CurbRibbon(pts, GroundPlan.CurbW, GroundPlan.CurbH);
                data.Curb.Pos.AddRange(curb.Pos);
                data.Curb.Nor.AddRange(curb.Nor);
                data.Curb.Uv.AddRange(curb.Uv);
                var inner = InsetPoly(pts, GroundPlan.CurbW);
                // ShapeGeometry triangulates the shape-space polygon (sx, sy) = (x, -z) via earcut
                var flat = new double[inner.Count * 2];
                for (int i = 0; i < inner.Count; i++) { flat[i * 2] = inner[i][0]; flat[i * 2 + 1] = -inner[i][1]; }
                data.Sidewalks.Add(new SidewalkPoly
                {
                    Rounded = pts,
                    Inner = inner,
                    Indices = Earcut.Triangulate(flat, null, 2),
                    Collider = poly.Pts,
                });
            }
            return data;
        }
    }
}
