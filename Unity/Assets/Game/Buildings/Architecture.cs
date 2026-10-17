// Port of src/systems/buildings/architecture.js — lot -> volumes -> facade quads, roofs, parapets, trims.
using System;
using System.Collections.Generic;

namespace NeonKaido
{
    public class BVol
    {
        public double X0, X1, Z0, Z1, Y0, Y1;
        public bool IsSetback;
    }

    public class BFrame
    {
        public double[] Origin, Right, Normal;
        public double Width;
    }

    public class BSide
    {
        public double BayW, N, Kind, StartY, WwF, WhF, Y0, Y1, Vi;
        public BFront Front;
        public BFrame Fr;
    }

    public class BCollider { public double X, Y, Z, Hx, Hy, Hz, RotY; public double[] Rot; // optional quaternion (x,y,z,w), alley ramp colliders
    }
    public class BLight { public double[] Pos, Color; public double Intensity, Distance; public bool Minor; }
    public class BOut
    {
        public List<BCollider> Colliders = new List<BCollider>();
        public List<BLight> Lights = new List<BLight>();
        // instanced prop matrices (raw Matrix4 elements, column-major like THREE)
        public List<double[]> AcM, TankM, PtankM;
    }

    public class BStyle
    {
        public int Layer, Kind;
        public double TexScale, Balcony, Ac;
        public double[] Bay, WW, WH;
    }

    public static class BuildArchitecture
    {
        public static readonly Dictionary<string, double[]> FacadeAttrs = new Dictionary<string, double[]>
        {
            ["aWin"] = new[] { 0.0, 3.2, 3.0, 4.0 },
            ["aWin2"] = new[] { 0.5, 0.5, 0.3, 5.0 },
            ["aWin3"] = new[] { 100.0, 1.0, 4.0, 0.0 },
        };
        public static readonly Dictionary<string, double[]> DetailAttrs = new Dictionary<string, double[]>
        {
            ["aCol"] = new[] { 1.0, 1.0, 1.0, 0.0 },
            ["aPbr"] = new[] { 0.6, 0.0, 0.0, 0.5 },
        };

        public static readonly Dictionary<string, BStyle> Styles = new Dictionary<string, BStyle>
        {
            ["tile"] = new BStyle { Layer = 0, TexScale = 0.9, Kind = 0, Bay = new[] { 2.4, 3.4 }, WW = new[] { 0.5, 0.72 }, WH = new[] { 0.48, 0.6 }, Balcony = 0.5, Ac = 0.7 },
            ["whitetile"] = new BStyle { Layer = 6, TexScale = 1.2, Kind = 0, Bay = new[] { 2.6, 3.6 }, WW = new[] { 0.55, 0.8 }, WH = new[] { 0.5, 0.62 }, Balcony = 0.75, Ac = 0.8 },
            ["concrete"] = new BStyle { Layer = 1, TexScale = 4.0, Kind = 0, Bay = new[] { 2.8, 4.2 }, WW = new[] { 0.45, 0.7 }, WH = new[] { 0.45, 0.58 }, Balcony = 0.3, Ac = 0.6 },
            ["ribbon"] = new BStyle { Layer = 1, TexScale = 4.0, Kind = 1, Bay = new[] { 1.6, 2.4 }, WW = new[] { 1.0, 1.0 }, WH = new[] { 0.5, 0.62 }, Balcony = 0.0, Ac = 0.3 },
            ["render"] = new BStyle { Layer = 3, TexScale = 3.0, Kind = 0, Bay = new[] { 2.2, 3.2 }, WW = new[] { 0.4, 0.6 }, WH = new[] { 0.42, 0.55 }, Balcony = 0.25, Ac = 0.9 },
            ["corrugated"] = new BStyle { Layer = 2, TexScale = 2.5, Kind = 0, Bay = new[] { 2.4, 3.4 }, WW = new[] { 0.35, 0.55 }, WH = new[] { 0.4, 0.5 }, Balcony = 0.0, Ac = 0.8 },
            ["metal"] = new BStyle { Layer = 4, TexScale = 3.6, Kind = 1, Bay = new[] { 1.8, 2.6 }, WW = new[] { 1.0, 1.0 }, WH = new[] { 0.55, 0.7 }, Balcony = 0.0, Ac = 0.2 },
            ["brick"] = new BStyle { Layer = 5, TexScale = 1.6, Kind = 0, Bay = new[] { 2.4, 3.2 }, WW = new[] { 0.45, 0.62 }, WH = new[] { 0.48, 0.58 }, Balcony = 0.35, Ac = 0.7 },
            ["glass"] = new BStyle { Layer = 4, TexScale = 3.6, Kind = 2, Bay = new[] { 1.5, 1.8 }, WW = new[] { 1.0, 1.0 }, WH = new[] { 1.0, 1.0 }, Balcony = 0.0, Ac = 0.0 },
        };

        static readonly string[] Sides = { "nx", "px", "nz", "pz" };
        static readonly int[] SideIdx = { 0, 1, 2, 3 };

        // JS Math.round: half toward +Infinity (C# Math.Round is banker's — must not use).
        static double JRound(double x) => Math.Floor(x + 0.5);

        public static BFrame SideFrame(BVol v, string side, double y = 0)
        {
            switch (side)
            {
                case "nx": return new BFrame { Origin = new[] { v.X0, y, v.Z0 }, Right = new[] { 0.0, 0, 1 }, Normal = new[] { -1.0, 0, 0 }, Width = v.Z1 - v.Z0 };
                case "px": return new BFrame { Origin = new[] { v.X1, y, v.Z1 }, Right = new[] { 0.0, 0, -1 }, Normal = new[] { 1.0, 0, 0 }, Width = v.Z1 - v.Z0 };
                case "nz": return new BFrame { Origin = new[] { v.X1, y, v.Z0 }, Right = new[] { -1.0, 0, 0 }, Normal = new[] { 0.0, 0, -1 }, Width = v.X1 - v.X0 };
                default: return new BFrame { Origin = new[] { v.X0, y, v.Z1 }, Right = new[] { 1.0, 0, 0 }, Normal = new[] { 0.0, 0, 1 }, Width = v.X1 - v.X0 };
            }
        }

        public static BAttrSet TrimAttr(double layer, double texScale = 3, double tint = 0.5, double seed = 0.5)
        {
            return new BAttrSet()
                .Set("aWin", new[] { seed, 3.0, 3.0, 9999.0 })
                .Set("aWin2", new[] { 0.5, 0.5, 0.0, 5.0 })
                .Set("aWin3", new[] { 9999.0, layer, texScale, tint });
        }

        public static void BuildLotArchitecture(BLot lot, BChunkedBuckets B, BOut outp)
        {
            var st = Styles[lot.Style];
            var rng = new BuildingsRng((uint)((long)lot.Id * 7919 + 13));
            double tint = rng.F();
            double litP = lot.Detail == "hi" ? rng.Range(0.2, 0.4) : rng.Range(0.28, 0.5);
            double bayT = rng.Range(st.Bay[0], st.Bay[1]);
            double wwF = rng.Range(st.WW[0], st.WW[1]);
            double whF = rng.Range(st.WH[0], st.WH[1]);
            int kindFront = st.Kind;
            double seed = lot.Seed;
            int layer = (st.Layer == 0 && rng.Chance(0.3)) ? 6 : st.Layer;
            int trimLayer = rng.Chance(0.6) ? 1 : layer;
            lot.WallLayer = layer;
            double roofLayer = rng.Weighted(new List<(double, double)> { (7.0, 5.0), (1.0, 3.0), (lot.Style == "corrugated" ? 2.0 : 3.0, 1.0) });
            double roofTint = rng.F();

            // volumes
            var vols = new List<BVol>();
            var baseV = new BVol { X0 = lot.X0, X1 = lot.X1, Z0 = lot.Z0, Z1 = lot.Z1, Y0 = 0, Y1 = lot.H };
            if (lot.Setback != null)
            {
                double yS = lot.GroundH + (lot.Setback.AtFloor - 1) * lot.FloorH;
                baseV.Y1 = yS;
                vols.Add(baseV);
                double ins = lot.Setback.Inset;
                var frontSides = new HashSet<string>();
                foreach (var f in lot.Fronts) frontSides.Add(f.Side);
                var up = new BVol { X0 = lot.X0, X1 = lot.X1, Z0 = lot.Z0, Z1 = lot.Z1, Y0 = yS, Y1 = lot.H, IsSetback = true };
                foreach (var s in Sides)
                {
                    if (!(frontSides.Contains(s) || lot.Tower || rng.Chance(0.3))) continue;
                    if (s == "nx") up.X0 += ins; else if (s == "px") up.X1 -= ins; else if (s == "nz") up.Z0 += ins; else up.Z1 -= ins;
                }
                if (up.X1 - up.X0 > 5 && up.Z1 - up.Z0 > 5) vols.Add(up); else baseV.Y1 = lot.H;
            }
            else vols.Add(baseV);
            var ovFront = (lot.Detail == "hi" && lot.OverlookDeckY == null)
                ? lot.Fronts.Find(f => !f.Lane && f.BaseY == 0) : null;
            lot.Overhang = 0;
            if (ovFront != null && rng.Chance(0.45) && vols[0].Y1 > lot.GroundH + 3)
            {
                double ov = ovFront.District == "alley" ? rng.Range(0.25, 0.5) : rng.Range(0.3, 0.65);
                var b0 = vols[0];
                var g = new BVol { X0 = b0.X0, X1 = b0.X1, Z0 = b0.Z0, Z1 = b0.Z1, Y0 = b0.Y0, Y1 = lot.GroundH, IsSetback = b0.IsSetback };
                var u = new BVol { X0 = b0.X0, X1 = b0.X1, Z0 = b0.Z0, Z1 = b0.Z1, Y0 = lot.GroundH, Y1 = b0.Y1, IsSetback = b0.IsSetback };
                if (ovFront.Side == "nx") u.X0 -= ov; else if (ovFront.Side == "px") u.X1 += ov; else if (ovFront.Side == "nz") u.Z0 -= ov; else u.Z1 += ov;
                vols.RemoveAt(0); vols.Insert(0, g); vols.Insert(1, u);
                lot.Overhang = ov; lot.OverhangSide = ovFront.Side;
            }
            lot.Volumes = vols;

            double cxL = (lot.X0 + lot.X1) / 2, czL = (lot.Z0 + lot.Z1) / 2;
            var fac = B.Get(cxL, czL, "facade", FacadeAttrs);
            var frontMap = new Dictionary<string, BFront>();
            foreach (var f in lot.Fronts) frontMap[f.Side] = f;

            double CoverOf(string s) => s == "nx" ? lot.CoverNx : s == "px" ? lot.CoverPx : s == "nz" ? lot.CoverNz : lot.CoverPz;

            for (int vi = 0; vi < vols.Count; vi++)
            {
                var v = vols[vi];
                foreach (var s in Sides)
                {
                    var fr = SideFrame(v, s, v.Y0);
                    if (fr.Width < 0.5) continue;
                    frontMap.TryGetValue(s, out var front);
                    bool isFront = front != null || lot.Tower || v.IsSetback || CoverOf(s) < 0.45;
                    double kind = isFront ? kindFront : 3;
                    double n = Math.Max(1, JRound(fr.Width / (kind == 3 ? bayT * 1.3 : bayT)));
                    double bayW = fr.Width / n;
                    double startY = lot.GroundH;
                    double y0 = v.Y0;
                    if (vi == 0 && front != null && front.Shop && lot.Detail == "hi" && front.BaseY >= 0) y0 = (front.BaseY ?? 0) + lot.GroundH;
                    if (vi == 0 && front != null && front.BaseY > 0 && !front.Shop) y0 = 0;
                    var a = new BAttrSet()
                        .Set("aWin", new[] { seed + vi * 0.13 + Array.IndexOf(Sides, s) * 0.031, lot.FloorH, bayW, startY })
                        .Set("aWin2", new[] { wwF, whF, kind == 2 ? litP * 1.3 : litP, kind })
                        .Set("aWin3", new[] { v.Y1 - 0.2, (double)layer, st.TexScale, tint });
                    if (lot.Sides == null) lot.Sides = new Dictionary<string, BSide>();
                    lot.Sides[$"{vi}:{s}"] = new BSide { BayW = bayW, N = n, Kind = kind, StartY = startY, WwF = wwF, WhF = whF, Y0 = y0, Y1 = v.Y1, Front = front, Fr = fr, Vi = vi };
                    if (v.Y1 - y0 > 0.1) fac.VRect(new[] { fr.Origin[0], y0, fr.Origin[2] }, fr.Right, fr.Width, v.Y1 - y0, a, 0, y0);
                }
                if (lot.Overhang != 0 && vi == 0)
                {
                    outp.Colliders.Add(new BCollider { X = (v.X0 + v.X1) / 2, Y = (v.Y0 + v.Y1) / 2, Z = (v.Z0 + v.Z1) / 2, Hx = (v.X1 - v.X0) / 2, Hy = (v.Y1 - v.Y0) / 2, Hz = (v.Z1 - v.Z0) / 2 });
                    continue;
                }
                if (lot.Overhang != 0 && vi == 1)
                {
                    var g0 = vols[0];
                    double yy = v.Y0 - 0.005;
                    double sx0 = v.X0, sx1 = v.X1, sz0 = v.Z0, sz1 = v.Z1;
                    var ta2 = TrimAttr(trimLayer, 3, tint, seed);
                    var sv = lot.OverhangSide;
                    var r0 = sv == "nx" ? new[] { sx0, g0.X0, sz0, sz1 } : sv == "px" ? new[] { g0.X1, sx1, sz0, sz1 }
                        : sv == "nz" ? new[] { sx0, sx1, sz0, g0.Z0 } : new[] { sx0, sx1, g0.Z1, sz1 };
                    fac.Quad(new[] { r0[0], yy, r0[2] }, new[] { r0[1], yy, r0[2] }, new[] { r0[1], yy, r0[3] }, new[] { r0[0], yy, r0[3] },
                        new[] { r0[0], r0[2] }, new[] { r0[1], r0[2] }, new[] { r0[1], r0[3] }, new[] { r0[0], r0[3] },
                        ta2, new[] { 0.0, -1, 0 }, new[] { 1.0, 0, 0 });
                }
                var ra = new BAttrSet()
                    .Set("aWin", new[] { seed, 3.0, 3.0, 9999.0 })
                    .Set("aWin2", new[] { 0.5, 0.5, 0.0, 4.0 })
                    .Set("aWin3", new[] { 9999.0, roofLayer, roofLayer == 7 ? 4.5 : 3.5, roofTint });
                double y = v.Y1;
                fac.Quad(new[] { v.X0, y, v.Z1 }, new[] { v.X1, y, v.Z1 }, new[] { v.X1, y, v.Z0 }, new[] { v.X0, y, v.Z0 },
                    new[] { v.X0, -v.Z1 }, new[] { v.X1, -v.Z1 }, new[] { v.X1, -v.Z0 }, new[] { v.X0, -v.Z0 }, ra, new[] { 0.0, 1, 0 }, new[] { 1.0, 0, 0 });
                if (!(lot.OverlookDeckY != null && vi == vols.Count - 1))
                {
                    double ph = rng.Range(0.7, 1.2), pt = 0.22;
                    var ta = TrimAttr(trimLayer, 3, tint, seed);
                    fac.Box((v.X0 + v.X1) / 2, y + ph / 2, v.Z0 + pt / 2, v.X1 - v.X0, ph, pt, ta);
                    fac.Box((v.X0 + v.X1) / 2, y + ph / 2, v.Z1 - pt / 2, v.X1 - v.X0, ph, pt, ta);
                    fac.Box(v.X0 + pt / 2, y + ph / 2, (v.Z0 + v.Z1) / 2, pt, ph, v.Z1 - v.Z0 - pt * 2, ta);
                    fac.Box(v.X1 - pt / 2, y + ph / 2, (v.Z0 + v.Z1) / 2, pt, ph, v.Z1 - v.Z0 - pt * 2, ta);
                    foreach (var f in lot.Fronts)
                    {
                        if (vi != vols.Count - 1 && lot.Setback == null) continue;
                        var fr = SideFrame(v, f.Side, 0);
                        double cx = fr.Origin[0] + fr.Right[0] * fr.Width / 2 + fr.Normal[0] * 0.08;
                        double cz = fr.Origin[2] + fr.Right[2] * fr.Width / 2 + fr.Normal[2] * 0.08;
                        bool along = Math.Abs(fr.Right[0]) > 0.5;
                        fac.Box(cx, y + ph + 0.04, cz, along ? fr.Width + 0.2 : 0.45, 0.08, along ? 0.45 : fr.Width + 0.2, ta);
                    }
                }
                outp.Colliders.Add(new BCollider { X = (v.X0 + v.X1) / 2, Y = (v.Y0 + v.Y1) / 2, Z = (v.Z0 + v.Z1) / 2, Hx = (v.X1 - v.X0) / 2, Hy = (v.Y1 - v.Y0) / 2, Hz = (v.Z1 - v.Z0) / 2 });
            }

            // tower crowns
            if (lot.H > 30 && lot.OverlookDeckY == null)
            {
                var v = vols[vols.Count - 1];
                var det = B.Get(cxL, czL, "detail", DetailAttrs);
                double w = v.X1 - v.X0, d = v.Z1 - v.Z0;
                if (rng.Chance(0.65) && w > 8 && d > 8)
                {
                    double ix = w * rng.Range(0.18, 0.3), iz = d * rng.Range(0.18, 0.3), mh = rng.Range(3.2, 5.5);
                    var ta = TrimAttr(trimLayer, 3, tint, seed);
                    double mx0 = v.X0 + ix, mx1 = v.X1 - ix, mz0 = v.Z0 + iz, mz1 = v.Z1 - iz;
                    fac.Box((mx0 + mx1) / 2, v.Y1 + mh / 2, (mz0 + mz1) / 2, mx1 - mx0, mh, mz1 - mz0, new BAttrSet()
                        .Set("aWin", new[] { seed + 0.5, mh, 4.0, v.Y1 })
                        .Set("aWin2", new[] { 0.3, 0.3, 0.1, 3.0 })
                        .Set("aWin3", new[] { v.Y1 + mh - 0.3, (double)trimLayer, 3.0, tint }));
                    fac.Box((mx0 + mx1) / 2, v.Y1 + mh + 0.1, (mz0 + mz1) / 2, mx1 - mx0 + 0.3, 0.2, mz1 - mz0 + 0.3, ta);
                    outp.Colliders.Add(new BCollider { X = (mx0 + mx1) / 2, Y = v.Y1 + mh / 2, Z = (mz0 + mz1) / 2, Hx = (mx1 - mx0) / 2, Hy = mh / 2, Hz = (mz1 - mz0) / 2 });
                }
                if (rng.Chance(0.35))
                {
                    var col = rng.Pick(new[] { new[] { 0.6, 0.85, 1.0 }, new[] { 1.0, 0.3, 0.7 }, new[] { 0.3, 1.0, 0.9 }, new[] { 1.0, 0.8, 0.5 } });
                    var a = new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], rng.Range(1.5, 3.0) }).Set("aPbr", new[] { 0.5, 0.0, 0, 0 });
                    double y = v.Y1 - rng.Range(0.4, 1.2), t = 0.08;
                    det.Box((v.X0 + v.X1) / 2, y, v.Z0 - 0.03, w, t, 0.06, a);
                    det.Box((v.X0 + v.X1) / 2, y, v.Z1 + 0.03, w, t, 0.06, a);
                    det.Box(v.X0 - 0.03, y, (v.Z0 + v.Z1) / 2, 0.06, t, d, a);
                    det.Box(v.X1 + 0.03, y, (v.Z0 + v.Z1) / 2, 0.06, t, d, a);
                }
                if (lot.H > 45)
                    foreach (var c in new[] { new[] { v.X0 + 0.3, v.Z0 + 0.3 }, new[] { v.X1 - 0.3, v.Z1 - 0.3 }, new[] { v.X0 + 0.3, v.Z1 - 0.3 }, new[] { v.X1 - 0.3, v.Z0 + 0.3 } })
                        det.Box(c[0], v.Y1 + 1.3, c[1], 0.15, 0.15, 0.15, new BAttrSet().Set("aCol", new[] { 1.0, 0.08, 0.04, 6.0 }).Set("aPbr", new[] { 0.5, 0.0, 0, 0 }));
            }
            // floor slab bands
            if (lot.Detail == "hi" && kindFront == 0 && rng.Chance(0.6))
            {
                var ta = TrimAttr(trimLayer, 3, tint, seed);
                foreach (var f in lot.Fronts)
                {
                    var v = lot.Overhang != 0 ? vols[1] : vols[0];
                    var fr = SideFrame(v, f.Side, 0);
                    bool along = Math.Abs(fr.Right[0]) > 0.5;
                    for (int i = 1; i < lot.Floors; i++)
                    {
                        double y = lot.GroundH + (i - 1) * lot.FloorH;
                        if (y > v.Y1 - 0.5) break;
                        double cx = fr.Origin[0] + fr.Right[0] * fr.Width / 2 + fr.Normal[0] * 0.06;
                        double cz = fr.Origin[2] + fr.Right[2] * fr.Width / 2 + fr.Normal[2] * 0.06;
                        fac.Box(cx, y - 0.06, cz, along ? fr.Width : 0.12, 0.14, along ? 0.12 : fr.Width, ta);
                    }
                }
            }
            // corner pilasters
            if (lot.Detail != "lo" && rng.Chance(0.5))
            {
                var ta = TrimAttr(trimLayer, 3, tint, seed);
                foreach (var f in lot.Fronts)
                {
                    var v = lot.Overhang != 0 ? vols[1] : vols[0];
                    var fr = SideFrame(v, f.Side, 0);
                    double y0 = lot.GroundH, h = v.Y1 - y0;
                    if (h < 2) continue;
                    foreach (var t in new[] { 0.18, fr.Width - 0.18 })
                    {
                        double px = fr.Origin[0] + fr.Right[0] * t + fr.Normal[0] * 0.1;
                        double pz = fr.Origin[2] + fr.Right[2] * t + fr.Normal[2] * 0.1;
                        fac.Box(px, y0 + h / 2, pz, 0.36, h, 0.36, ta);
                    }
                }
            }
        }
    }
}
