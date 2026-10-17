// Port of src/systems/buildings/props.js — facade & roof props.
// Instanced GLB matrices (AC condensers / water tanks / panel tanks) are stored as raw
// Matrix4 element arrays (column-major, like THREE), f32-rounded at oracle time.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeonKaido
{
    public static class Props
    {
        // D(col, em, rough, metal, grime) — matches DETAIL attr layout aCol=[r,g,b,em], aPbr=[rough,metal,tex,grime]
        static BAttrSet D(double[] col, double em = 0, double rough = 0.6, double metal = 0, double grime = 0.5) =>
            new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], em }).Set("aPbr", new[] { rough, metal, 0, grime });

        static readonly double[] PIPE_GREY = { 0.55, 0.55, 0.52 }, PIPE_DARK = { 0.12, 0.12, 0.12 }, STEEL = { 0.3, 0.3, 0.3 }, RUST = { 0.32, 0.18, 0.1 };
        static readonly double[][] LAUNDRY = {
            new[]{ 0.85, 0.85, 0.82 }, new[]{ 0.2, 0.3, 0.55 }, new[]{ 0.7, 0.2, 0.2 }, new[]{ 0.9, 0.75, 0.4 },
            new[]{ 0.3, 0.5, 0.35 }, new[]{ 0.15, 0.15, 0.15 }, new[]{ 0.85, 0.55, 0.65 } };

        public static void BuildProps(BChunkedBuckets B, List<BLot> lots, BOut outp)
        {
            var rng = new BuildingsRng(0x5EED);
            var acM = new List<double[]>(); var tankM = new List<double[]>(); var ptankM = new List<double[]>();

            for (int li = 0; li < lots.Count; li++)
            {
                var lot = lots[li];
                if (lot.Sides == null) continue;
                var st = BuildArchitecture.Styles[lot.Style];
                var r = new BuildingsRng((uint)(lot.Id * 2654435761.0 % 4294967296.0));
                double cx = (lot.X0 + lot.X1) / 2, cz = (lot.Z0 + lot.Z1) / 2;
                var det = B.Get(cx, cz, "detail", BuildArchitecture.DetailAttrs);
                var fac = B.Get(cx, cz, "facade", BuildArchitecture.FacadeAttrs);
                bool near = lot.Detail != "lo";
                bool hi = lot.Detail == "hi";
                var trim = BuildArchitecture.TrimAttr(lot.WallLayer, 3, 0.5, lot.Seed);
                double[] pipeCol = r.Chance(0.6) ? PIPE_GREY : PIPE_DARK;

                foreach (var kv in lot.Sides)
                {
                    var sd = kv.Value;
                    var fr = sd.Fr;
                    double bayW = sd.BayW, startY = sd.StartY;
                    int n = (int)sd.N, vi = (int)sd.Vi;
                    var front = sd.Front;
                    double rot = RotFor(fr.Normal);
                    int floorsHere = (int)Math.Floor((sd.Y1 - 0.6 - Math.Max(startY, sd.Y0)) / lot.FloorH);
                    int iy0 = (int)Math.Max(0, Math.Ceiling((sd.Y0 - startY) / lot.FloorH));
                    if (!near && vi == 0) continue; // far: silhouettes only
                    bool isFrontish = front != null || lot.Tower || vi > 0;
                    bool balcony = hi && front != null && !front.Lane && st.Balcony > 0 && (lot.Seed * 97 % 1) < st.Balcony && fr.Width > 5;
                    bool shopBelow = front != null && front.Shop && hi;

                    // ---------- balconies ----------
                    if (balcony)
                    {
                        double bd = r.Range(0.9, 1.3);
                        string ptype = r.Weighted(new[] { ("solid", 2.0), ("rail", 2.0), ("frost", 1.0) });
                        int partEvery = r.Chance(0.5) ? 2 : 1;
                        for (int iy = shopBelow ? 1 : iy0; iy < iy0 + floorsHere; iy++)
                        {
                            double y = startY + iy * lot.FloorH;
                            if (y < sd.Y0 + 0.5) continue;
                            double sMid = fr.Width / 2;
                            var slabC = WallPt(fr, sMid, y - 0.08, bd / 2);
                            FacBox(fac, fr, slabC[0], slabC[1], slabC[2], fr.Width - 0.3, 0.16, bd, trim);
                            // parapet
                            var pc = WallPt(fr, sMid, y + 0.5, bd - 0.05);
                            if (ptype == "solid")
                                FacBox(fac, fr, pc[0], pc[1], pc[2], fr.Width - 0.3, 1.0, 0.1, trim);
                            else if (ptype == "frost")
                            {
                                var gq = B.Get(cx, cz, "detail", BuildArchitecture.DetailAttrs);
                                FacBox(gq, fr, pc[0], pc[1] - 0.05, pc[2], fr.Width - 0.4, 0.85, 0.03,
                                    D(new[] { 0.55, 0.6, 0.62 }, 0.0, 0.15, 0.0, 0.2));
                                var tp = WallPt(fr, sMid, y + 1.0, bd - 0.05);
                                FacBox(det, fr, tp[0], tp[1], tp[2], fr.Width - 0.3, 0.05, 0.07, D(STEEL, 0, 0.35, 0.8));
                            }
                            else
                            {
                                foreach (double hh in new[] { 0.98, 0.6, 0.25 })
                                {
                                    var a = WallPt(fr, 0.2, y + hh, bd - 0.05); var b = WallPt(fr, fr.Width - 0.2, y + hh, bd - 0.05);
                                    det.Beam(a, b, hh > 0.9 ? 0.05 : 0.025, D(STEEL, 0, 0.35, 0.8, 0.4));
                                }
                                for (double s = 0.2; s <= fr.Width - 0.1; s += 1.1)
                                    det.Beam(WallPt(fr, s, y, bd - 0.05), WallPt(fr, s, y + 1.0, bd - 0.05), 0.035, D(STEEL, 0, 0.35, 0.8, 0.4));
                            }
                            // partitions
                            for (int k = partEvery; k < n; k += partEvery)
                            {
                                var pp = WallPt(fr, k * bayW, y + 1.2, bd / 2);
                                FacBox(fac, fr, pp[0], pp[1], pp[2], 0.06, 2.4, bd, trim);
                            }
                            // clutter per unit
                            for (int k = 0; k < n; k += partEvery)
                            {
                                double s0 = k * bayW, s1 = Math.Min(n, k + partEvery) * bayW;
                                double u = r.F();
                                if (u < 0.45)
                                {
                                    double s = r.Chance(0.5) ? s0 + 0.55 : s1 - 0.55;
                                    var p = WallPt(fr, s, y, 0.05);
                                    Place(acM, p[0], p[1], p[2], rot);
                                }
                                if (r.Chance(0.35)) // laundry
                                {
                                    var pa = WallPt(fr, s0 + 0.3, y + 2.1, bd * 0.6); var pb = WallPt(fr, s1 - 0.3, y + 2.1, bd * 0.6);
                                    det.Beam(pa, pb, 0.02, D(STEEL, 0, 0.4, 0.8));
                                    double s = s0 + 0.4;
                                    while (s < s1 - 0.6)
                                    {
                                        double w = r.Range(0.3, 0.7), h = r.Range(0.4, 0.9);
                                        var c = r.Pick(LAUNDRY);
                                        var q0 = WallPt(fr, s, y + 2.08 - h, bd * 0.6 + 0.01); var q1 = WallPt(fr, s + w, y + 2.08 - h, bd * 0.6 + 0.01);
                                        var q2 = WallPt(fr, s + w, y + 2.08, bd * 0.6 + 0.01); var q3 = WallPt(fr, s, y + 2.08, bd * 0.6 + 0.01);
                                        var ua = new[] { 0.0, 0.0 }; var ub = new[] { 1.0, 0.0 }; var uc = new[] { 1.0, 1.0 }; var ud = new[] { 0.0, 1.0 };
                                        det.Quad(q0, q1, q2, q3, ua, ub, uc, ud, D(c, 0.0, 0.9, 0, 0.3));
                                        det.Quad(q1, q0, q3, q2, ua, ub, uc, ud, D(c, 0.0, 0.9, 0, 0.3));
                                        s += w + r.Range(0.05, 0.3);
                                    }
                                }
                                if (r.Chance(0.3)) // plants / storage box
                                {
                                    double s = r.Range(s0 + 0.4, s1 - 0.4);
                                    var p = WallPt(fr, s, y + 0.3, bd - 0.35);
                                    FacBox(det, fr, p[0], p[1], p[2], 0.45, 0.6, 0.35,
                                        r.Chance(0.5) ? D(new[] { 0.1, 0.2, 0.07 }, 0, 0.6) : D(new[] { 0.6, 0.55, 0.45 }, 0, 0.7));
                                }
                                if (r.Chance(0.12)) // balcony ceiling light
                                {
                                    var p = WallPt(fr, (s0 + s1) / 2, y + lot.FloorH - 0.25, 0.4);
                                    FacBox(det, fr, p[0], p[1], p[2], 0.25, 0.06, 0.12, D(new[] { 1.0, 0.8, 0.55 }, 3, 0.5));
                                }
                            }
                        }
                    }
                    else if (near && isFrontish)
                    {
                        // ---------- AC units hung below windows between bays ----------
                        double pAC = (st.Ac > 0 ? st.Ac : 0.3) * (sd.Kind == 3 ? 0.0 : (front != null && front.District == "alley" ? 0.6 : 0.24));
                        for (int iy = iy0; iy < iy0 + floorsHere; iy++)
                        {
                            double y = startY + iy * lot.FloorH + 0.08;
                            if (y < sd.Y0 + 0.2) continue;
                            for (int ix = 0; ix < n; ix++)
                            {
                                if (!r.Chance(pAC)) continue;
                                double s = (ix + 0.5) * bayW + (r.Chance(0.5) ? -1 : 1) * Math.Max(0, bayW * 0.5 - 0.45);
                                if (s < 0.5 || s > fr.Width - 0.5) continue;
                                var p = WallPt(fr, s, y, 0.02);
                                Place(acM, p[0], p[1], p[2], rot);
                                // bracket
                                foreach (double o in new[] { -0.3, 0.3 })
                                    det.Beam(WallPt(fr, s + o, y - 0.02, 0.0), WallPt(fr, s + o, y - 0.02, 0.34), 0.03, D(RUST, 0, 0.6, 0.5));
                                // drain hose down the wall
                                if (r.Chance(0.5)) det.Beam(WallPt(fr, s + 0.42, y + 0.15, 0.03), WallPt(fr, s + 0.42, y - r.Range(0.5, 2.0), 0.03), 0.018, D(PIPE_DARK, 0, 0.5));
                            }
                        }
                    }

                    // ---------- side walls (party walls): AC stacks + pipes ----------
                    if (near && sd.Kind == 3 && !balcony)
                    {
                        int cols = r.Int(0, 2);
                        for (int c = 0; c < cols; c++)
                        {
                            double s = r.Range(0.8, fr.Width - 0.8);
                            for (int iy = iy0; iy < iy0 + floorsHere; iy++)
                            {
                                if (!r.Chance(0.55)) continue;
                                double y = startY + iy * lot.FloorH + 0.1;
                                if (y < sd.Y0 + 0.2) continue;
                                var p = WallPt(fr, s, y, 0.02);
                                Place(acM, p[0], p[1], p[2], rot);
                                foreach (double o in new[] { -0.3, 0.3 })
                                    det.Beam(WallPt(fr, s + o, y - 0.02, 0.0), WallPt(fr, s + o, y - 0.02, 0.34), 0.03, D(RUST, 0, 0.6, 0.5));
                            }
                            // refrigerant pipe bundle going up beside the stack
                            det.Cyl(WallPt(fr, s + 0.55, 0.5 + sd.Y0, 0.08), WallPt(fr, s + 0.55, sd.Y1 - 0.3, 0.08), 0.05, D(new[] { 0.75, 0.73, 0.66 }, 0, 0.6, 0, 0.7));
                        }
                        if (r.Chance(0.6))
                        {
                            double s = r.Range(0.4, fr.Width - 0.4);
                            det.Cyl(WallPt(fr, s, sd.Y0, 0.1), WallPt(fr, s, sd.Y1, 0.1), 0.06, D(pipeCol, 0, 0.45, 0.2, 0.6));
                            if (r.Chance(0.5)) det.Cyl(WallPt(fr, s + 0.18, sd.Y0, 0.08), WallPt(fr, s + 0.18, sd.Y1 * 0.7, 0.08), 0.035, D(RUST, 0, 0.5, 0.5, 0.6));
                        }
                    }

                    // ---------- drainpipe on fronts ----------
                    if (near && front != null && r.Chance(0.75))
                    {
                        double s = r.Chance(0.5) ? 0.22 : fr.Width - 0.22;
                        double yb = (front.BaseY ?? 0) > 0 ? front.BaseY.Value : 0;
                        double po = front.ShopType != null ? 0.28 : 0.12;
                        det.Cyl(WallPt(fr, s, yb + 0.05, po), WallPt(fr, s, sd.Y1 + 0.3, po), 0.055, D(pipeCol, 0, 0.4, 0.15, 0.6), 8);
                        // brackets
                        for (double y = yb + 1.5; y < sd.Y1; y += 2.2)
                        { var wp = WallPt(fr, s, y, 0.07); FacBox(det, fr, wp[0], wp[1], wp[2], 0.14, 0.04, 0.12, D(STEEL, 0, 0.5, 0.6)); }
                        // gutter head
                        { var wp = WallPt(fr, s, sd.Y1 + 0.35, 0.14); FacBox(det, fr, wp[0], wp[1], wp[2], 0.22, 0.18, 0.2, D(pipeCol, 0, 0.4, 0.15, 0.6)); }
                        // gas meter + pipes at street level
                        if (hi && !front.Shop && r.Chance(0.5))
                        {
                            double s2 = s < 1 ? 0.7 : fr.Width - 0.7;
                            var wp = WallPt(fr, s2, yb + 1.6, 0.1);
                            FacBox(det, fr, wp[0], wp[1], wp[2], 0.32, 0.4, 0.18, D(new[] { 0.68, 0.66, 0.6 }, 0, 0.5, 0.3, 0.8));
                            det.Cyl(WallPt(fr, s2, yb + 0.1, 0.07), WallPt(fr, s2, yb + 1.4, 0.07), 0.025, D(new[] { 0.75, 0.65, 0.2 }, 0, 0.5, 0.4, 0.7));
                        }
                    }

                    // ---------- ground floor of non-shop fronts: shutters / doors ----------
                    if (near && front != null && !(front.Shop && hi) && vi == 0 && front.BaseY >= 0 && fr.Width > 3)
                    {
                        double yb = (front.BaseY ?? 0) > 0 ? front.BaseY.Value : 0;
                        double s = r.Range(0.3, 1.0);
                        while (s < fr.Width - 1.2)
                        {
                            string kind = r.Weighted(new[] { ("shutter", 3.0), ("door", 2.0), ("wall", 2.0) });
                            double w = kind == "door" ? 1.0 : Math.Min(r.Range(2.2, 4.0), fr.Width - s - 0.3);
                            if (w < 0.9) break;
                            if (kind == "shutter")
                            {
                                double h = Math.Min(lot.GroundH - 0.6, 3.0);
                                var sub = new[] { r.Range(0, 0.5), 0.0, 0.5, 1.0 };
                                var p0 = WallPt(fr, s, yb, 0.03); var p1 = WallPt(fr, s + w, yb, 0.03);
                                var p2 = WallPt(fr, s + w, yb + h, 0.03); var p3 = WallPt(fr, s, yb + h, 0.03);
                                det.Quad(p0, p1, p2, p3, ShopAtlas.SlotUV("shutter", 0, 0, sub), ShopAtlas.SlotUV("shutter", 1, 0, sub), ShopAtlas.SlotUV("shutter", 1, 1, sub), ShopAtlas.SlotUV("shutter", 0, 1, sub),
                                    new BAttrSet().Set("aCol", new[] { 0.8, 0.8, 0.8, 0.0 }).Set("aPbr", new[] { 0.5, 0.2, 1, 0.3 }));
                                var wp = WallPt(fr, s + w / 2, yb + h + 0.15, 0.15);
                                FacBox(det, fr, wp[0], wp[1], wp[2], w + 0.1, 0.3, 0.3, D(new[] { 0.4, 0.4, 0.38 }, 0, 0.5, 0.4, 0.8));
                            }
                            else if (kind == "door")
                            {
                                var p = WallPt(fr, s + w / 2, yb + 1.05, 0.04);
                                FacBox(det, fr, p[0], p[1], p[2], w, 2.1, 0.06,
                                    D(r.Pick(new double[][] { new[] { 0.25, 0.25, 0.27 }, new[] { 0.3, 0.18, 0.1 }, new[] { 0.5, 0.5, 0.48 } }), 0, 0.4, 0.5, 0.6));
                                var w2 = WallPt(fr, s + w / 2, yb + 2.15, 0.08);
                                FacBox(det, fr, w2[0], w2[1], w2[2], w + 0.15, 0.08, 0.1, D(STEEL, 0, 0.4, 0.6));
                                // door light
                                var w3 = WallPt(fr, s + w / 2, yb + 2.45, 0.1);
                                FacBox(det, fr, w3[0], w3[1], w3[2], 0.2, 0.12, 0.12, D(new[] { 1.0, 0.85, 0.6 }, r.Chance(0.6) ? 2.5 : 0.05, 0.5));
                                // intercom / mailbox
                                var w4 = WallPt(fr, s + w + 0.25, yb + 1.3, 0.05);
                                FacBox(det, fr, w4[0], w4[1], w4[2], 0.25, 0.35, 0.06, D(new[] { 0.6, 0.6, 0.6 }, 0, 0.4, 0.5));
                            }
                            // posters
                            if (r.Chance(0.35))
                            {
                                double ps = s + w + 0.15;
                                if (ps < fr.Width - 0.8) Poster(det, fr, ps, yb + r.Range(1.0, 1.4), r);
                            }
                            s += w + r.Range(0.3, 1.2);
                        }
                    }
                    // posters on shop pillars / alley walls
                    if (hi && front != null && r.Chance(0.5))
                    {
                        double yb = (front.BaseY ?? 0) > 0 ? front.BaseY.Value : 0;
                        bool sh = front.Shop && F0Shop(front);
                        double sPos = r.Chance(0.5) ? 0.02 : fr.Width - (sh ? 0.32 : 0.62);
                        Poster(det, fr, sPos, yb + r.Range(0.9, 1.5), r, sh ? 0.3 : 0.6, sh ? 0.2 : 0);
                    }
                }

                // ---------- roof ----------
                var top = lot.Volumes[lot.Volumes.Count - 1];
                if (lot.OverlookDeckY != null) continue;
                double rx0 = top.X0 + 0.8, rx1 = top.X1 - 0.8, rz0 = top.Z0 + 0.8, rz1 = top.Z1 - 0.8;
                if (rx1 - rx0 < 1.5 || rz1 - rz0 < 1.5) continue;
                double ry = top.Y1;
                double[] RPx() => new[] { r.Range(rx0, rx1), r.Range(rz0, rz1) };
                int nAC = near ? r.Int(1, 5) : r.Int(0, 2);
                for (int i = 0; i < nAC; i++) { var xz = RPx(); Place(acM, xz[0], ry, xz[1], r.Int(0, 3) * Math.PI / 2); }
                if (r.Chance(near ? 0.4 : 0.2))
                {
                    var xz = RPx();
                    if (r.Chance(0.5)) Place(tankM, xz[0], ry, xz[1], r.F() * 6.28, r.Range(0.8, 1.15));
                    else if (rx1 - rx0 > 4 && rz1 - rz0 > 3) Place(ptankM, (rx0 + rx1) / 2, ry, (rz0 + rz1) / 2, r.Int(0, 3) * Math.PI / 2, 1);
                }
                if (r.Chance(near ? 0.45 : 0.25) && rx1 - rx0 > 3.5 && rz1 - rz0 > 3.5)
                {
                    // stair / elevator house
                    double w = 2.4, d = 2.8, h = r.Range(2.6, 3.4);
                    double x = r.Chance(0.5) ? rx0 + w / 2 : rx1 - w / 2, z = r.Chance(0.5) ? rz0 + d / 2 : rz1 - d / 2;
                    fac.Box(x, ry + h / 2, z, w, h, d, trim);
                    fac.Box(x, ry + h + 0.05, z, w + 0.2, 0.1, d + 0.2, trim);
                }
                if (r.Chance(near ? 0.5 : 0.25))
                {
                    // antenna mast with yagi crossbars
                    var xz = RPx(); double x = xz[0], z = xz[1];
                    double h = r.Range(2.5, 6);
                    det.Cyl(new[] { x, ry, z }, new[] { x, ry + h, z }, 0.035, D(STEEL, 0, 0.4, 0.8), 5);
                    for (int k = 0; k < r.Int(1, 3); k++) // NOTE: JS re-rolls r.int(1,3) every iteration — condition re-evaluates each pass
                    {
                        double yy = ry + h - 0.3 - k * 0.7, a = r.F() * 3.14, l = r.Range(0.8, 1.6);
                        det.Beam(new[] { x - Math.Cos(a) * l / 2, yy, z - Math.Sin(a) * l / 2 }, new[] { x + Math.Cos(a) * l / 2, yy, z + Math.Sin(a) * l / 2 }, 0.02, D(STEEL, 0, 0.4, 0.8));
                    }
                    if (r.Chance(0.5)) det.Box(x, ry + h + 0.05, z, 0.08, 0.1, 0.08, D(new[] { 1.0, 0.1, 0.05 }, 4, 0.5)); // aviation light
                }
                if (near && r.Chance(0.3))
                {
                    // satellite dish
                    var xz = RPx(); double x = xz[0], z = xz[1]; double a = r.F() * 6.28;
                    var m = BGeom.MakeRotationFromEuler(0.9, a, 0, "YXZ");
                    BGeom.SetPosition(ref m, x, ry + 0.9, z);
                    det.Geom(DishGeo(), m, D(new[] { 0.8, 0.8, 0.78 }, 0, 0.5, 0.2, 0.6));
                    det.Cyl(new[] { x, ry, z }, new[] { x, ry + 0.85, z }, 0.03, D(STEEL, 0, 0.4, 0.8));
                }
                if (near && r.Chance(0.25))
                {
                    // roof safety fence
                    double fh = 1.6;
                    var ptsF = new[] {
                        new[] { top.X0 + 0.3, top.Z0 + 0.3 }, new[] { top.X1 - 0.3, top.Z0 + 0.3 },
                        new[] { top.X1 - 0.3, top.Z1 - 0.3 }, new[] { top.X0 + 0.3, top.Z1 - 0.3 }, new[] { top.X0 + 0.3, top.Z0 + 0.3 } };
                    for (int i = 0; i < 4; i++)
                    {
                        var A2 = ptsF[i]; var B2 = ptsF[i + 1];
                        double ax = A2[0], az = A2[1], bx = B2[0], bz = B2[1];
                        foreach (double hh in new[] { fh, fh * 0.5 })
                            det.Beam(new[] { ax, ry + hh, az }, new[] { bx, ry + hh, bz }, 0.03, D(new[] { 0.25, 0.27, 0.25 }, 0, 0.4, 0.7));
                        double len = GroundGeo.Hypot2(bx - ax, bz - az); int nn = (int)Math.Ceiling(len / 2);
                        for (int k = 0; k < nn; k++)
                        {
                            double t = (double)k / nn;
                            det.Beam(new[] { ax + (bx - ax) * t, ry, az + (bz - az) * t }, new[] { ax + (bx - ax) * t, ry + fh, az + (bz - az) * t }, 0.04, D(new[] { 0.25, 0.27, 0.25 }, 0, 0.4, 0.7));
                        }
                    }
                }
                // billboard frame on avenue-facing roofs
                BFront avFront = null;
                foreach (var f in lot.Fronts) if (f.District == "avenue" || (f.District == "cross" && !f.Lane)) { avFront = f; break; }
                if (avFront != null && hi && r.Chance(0.28) && lot.H < 40)
                {
                    var v = lot.Volumes[lot.Volumes.Count - 1];
                    var fr = SideFrameOf(v, avFront.Side);
                    double bw = Math.Min(fr.Width - 1, r.Range(5, 9)), bh = bw * r.Range(0.4, 0.55);
                    double y0 = ry + 1.5;
                    double sMid = fr.Width / 2;
                    var steel = D(new[] { 0.12, 0.12, 0.12 }, 0, 0.45, 0.8, 0.6);
                    foreach (double s in new[] { sMid - bw / 2 + 0.3, sMid + bw / 2 - 0.3 })
                    {
                        det.Beam(WallPt(fr, s, ry, -1.2), WallPt(fr, s, y0 + bh, -0.5), 0.12, steel);
                        det.Beam(WallPt(fr, s, ry, -3.0), WallPt(fr, s, y0 + bh * 0.6, -0.6), 0.08, steel);
                    }
                    for (int k = 0; k <= 3; k++) det.Beam(WallPt(fr, sMid - bw / 2, y0 + bh * k / 3, -0.6), WallPt(fr, sMid + bw / 2, y0 + bh * k / 3, -0.6), 0.06, steel);
                    // panel (poster art, lit by small lamps)
                    int cell = r.Int(0, 15);
                    var sub = new[] { cell % 4 * 0.25, Math.Floor(cell / 4.0) * 0.25, 0.25, 0.25 };
                    var p0 = WallPt(fr, sMid - bw / 2, y0, -0.45); var p1 = WallPt(fr, sMid + bw / 2, y0, -0.45);
                    var p2 = WallPt(fr, sMid + bw / 2, y0 + bh, -0.45); var p3 = WallPt(fr, sMid - bw / 2, y0 + bh, -0.45);
                    det.Quad(p0, p1, p2, p3, ShopAtlas.SlotUV("posters", 0, 0, sub), ShopAtlas.SlotUV("posters", 1, 0, sub), ShopAtlas.SlotUV("posters", 1, 1, sub), ShopAtlas.SlotUV("posters", 0, 1, sub),
                        new BAttrSet().Set("aCol", new[] { 1.0, 1.0, 1.0, r.Range(0.5, 1.0) }).Set("aPbr", new[] { 0.5, 0, 1, 0 }));
                    for (double s = sMid - bw / 2 + 0.6; s < sMid + bw / 2; s += 1.6)
                    {
                        det.Beam(WallPt(fr, s, y0 - 0.1, -0.45), WallPt(fr, s, y0 - 0.1, 0.4), 0.03, steel);
                        var wp = WallPt(fr, s, y0 - 0.12, 0.45);
                        det.Box(wp[0], wp[1], wp[2], 0.18, 0.08, 0.18, D(new[] { 1.0, 0.95, 0.85 }, 4, 0.5));
                    }
                }
            }

            // ---------- vending machines at shop fronts ----------
            for (int li = 0; li < lots.Count; li++)
            {
                var lot = lots[li];
                if (lot.Detail != "hi") continue;
                foreach (var f in lot.Fronts)
                {
                    if (f.ShopType == null || f.ShopType == "pachinko") continue;
                    var r = new BuildingsRng((uint)(((long)lot.Id * 7 + f.Side.Length * 131 + 99) % 4294967296L));
                    if (!r.Chance(f.District == "alley" ? 0.3 : 0.22)) continue;
                    BSide sd;
                    if (!lot.Sides.TryGetValue("0:" + f.Side, out sd)) continue;
                    var fr = sd.Fr;
                    double yb = (f.BaseY ?? 0) > 0 ? f.BaseY.Value : 0;
                    int n = r.Chance(0.3) ? 2 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        double s = (r.Chance(0.5) ? 0.55 : fr.Width - 0.55) + (k != 0 ? (r.Chance(0.5) ? 1 : -1) * 0.98 : 0);
                        if (s < 0.4 || s > fr.Width - 0.4) continue;
                        var det = B.Get((lot.X0 + lot.X1) / 2, (lot.Z0 + lot.Z1) / 2, "detail", BuildArchitecture.DetailAttrs);
                        var body = r.Pick(new double[][] { new[] { 0.85, 0.85, 0.83 }, new[] { 0.7, 0.08, 0.06 }, new[] { 0.08, 0.2, 0.55 }, new[] { 0.15, 0.15, 0.16 } });
                        var c = WallPt(fr, s, yb + 0.93, 0.4);
                        FacBox(det, fr, c[0], c[1], c[2], 0.95, 1.86, 0.75, D(body, 0.02, 0.35, 0.3, 0.5));
                        var p0 = WallPt(fr, s - 0.42, yb + 0.12, 0.78); var p1 = WallPt(fr, s + 0.42, yb + 0.12, 0.78);
                        var p2 = WallPt(fr, s + 0.42, yb + 1.78, 0.78); var p3 = WallPt(fr, s - 0.42, yb + 1.78, 0.78);
                        det.Quad(p0, p1, p2, p3, ShopAtlas.SlotUV("vending", 0, 0), ShopAtlas.SlotUV("vending", 1, 0), ShopAtlas.SlotUV("vending", 1, 1), ShopAtlas.SlotUV("vending", 0, 1),
                            new BAttrSet().Set("aCol", new[] { 1.0, 1.0, 1.0, 1.15 }).Set("aPbr", new[] { 0.2, 0, 1, 0 }));
                        // side panels: ad wrap / stickers
                        foreach (double sgnD in new[] { -1.0, 1.0 })
                        {
                            int sgn = (int)sgnD;
                            int cell = r.Int(0, 15);
                            var sub = new[] { cell % 4 * 0.25 + 0.02, Math.Floor(cell / 4.0) * 0.25 + 0.02, 0.21, 0.21 };
                            double ss = s + sgn * 0.48;
                            var a0 = WallPt(fr, ss, yb + 0.25, sgn > 0 ? 0.72 : 0.08); var a1 = WallPt(fr, ss, yb + 0.25, sgn > 0 ? 0.08 : 0.72);
                            var a2 = new[] { a1[0], yb + 1.7, a1[2] }; var a3 = new[] { a0[0], yb + 1.7, a0[2] };
                            det.Quad(a0, a1, a2, a3, ShopAtlas.SlotUV("posters", 0, 0, sub), ShopAtlas.SlotUV("posters", 1, 0, sub), ShopAtlas.SlotUV("posters", 1, 1, sub), ShopAtlas.SlotUV("posters", 0, 1, sub),
                                new BAttrSet().Set("aCol", new[] { 0.9, 0.9, 0.9, 0.12 }).Set("aPbr", new[] { 0.3, 0, 1, 0.3 }));
                        }
                        var wp = WallPt(fr, s, yb + 1.83, 0.77);
                        FacBox(det, fr, wp[0], wp[1], wp[2], 0.9, 0.08, 0.02, D(new[] { 1.0, 1.0, 1.0 }, 1.5, 0.3));
                        outp.Colliders.Add(new BCollider
                        {
                            X = c[0], Y = c[1], Z = c[2],
                            Hx = Math.Abs(fr.Right[0]) * 0.47 + Math.Abs(fr.Normal[0]) * 0.37,
                            Hy = 0.93,
                            Hz = Math.Abs(fr.Right[2]) * 0.47 + Math.Abs(fr.Normal[2]) * 0.37,
                        });
                        // recycle bin
                        if (k == 0 && r.Chance(0.7))
                        {
                            var b = WallPt(fr, s + (s < 1 ? 0.75 : -0.75), yb + 0.4, 0.3);
                            FacBox(det, fr, b[0], b[1], b[2], 0.4, 0.8, 0.4, D(new[] { 0.15, 0.3, 0.6 }, 0, 0.4, 0.1, 0.6));
                        }
                    }
                    outp.Lights.Add(new BLight { Pos = WallPt(fr, fr.Width / 2, yb + 1.2, 1.4), Color = new[] { 0.85, 0.92, 1.0 }, Intensity = 1.5, Distance = 5, Minor = true });
                }
            }

            // ---------- sidewalk clutter ----------
            for (int li = 0; li < lots.Count; li++)
            {
                var lot = lots[li];
                if (lot.Detail != "hi") continue;
                foreach (var f in lot.Fronts)
                {
                    if ((f.BaseY ?? 0) < 0) continue;
                    BSide sd;
                    if (!lot.Sides.TryGetValue("0:" + f.Side, out sd)) continue;
                    var fr = sd.Fr;
                    var r = new BuildingsRng((uint)(((long)lot.Id * 31337 + f.Side[1]) % 4294967296L));
                    double yb = (f.BaseY ?? 0) > 0 ? f.BaseY.Value : 0;
                    var det = B.Get((lot.X0 + lot.X1) / 2, (lot.Z0 + lot.Z1) / 2, "detail", BuildArchitecture.DetailAttrs);
                    double maxOut = f.District == "alley" ? 0.55 : 1.1;
                    int items = r.Int(0, f.District == "alley" ? 3 : 2);
                    for (int i = 0; i < items; i++)
                    {
                        double s = r.Range(0.6, fr.Width - 0.6);
                        string kind = r.Weighted(new[] {
                            ("bags", 3.0), ("board", f.ShopType != null && f.ShopType != "shutter" ? 3.0 : 0.2),
                            ("bike", 1.5), ("plant", 1.5), ("crates", f.District == "alley" ? 2.0 : 0.6), ("cone", 0.5) });
                        if (kind == "bags")
                        {
                            int n = r.Int(2, 6);
                            for (int k = 0; k < n; k++)
                            {
                                var p = WallPt(fr, s + r.Range(-0.5, 0.5), yb + 0.22, r.Range(0.25, 0.7));
                                var q = BGeom.SetFromEuler(r.F() * 0.5, r.F() * 6.28, r.F() * 0.5, "XYZ");
                                var m = BGeom.Compose(p, q, new[] { r.Range(0.8, 1.2), r.Range(0.7, 1.0), r.Range(0.8, 1.1) });
                                det.Geom(BagGeo(), m, D(r.Chance(0.6) ? new[] { 0.03, 0.03, 0.035 } : new[] { 0.55, 0.6, 0.65 }, 0, 0.18, 0, 0.1));
                            }
                        }
                        else if (kind == "board")
                        {
                            // A-frame menu board with poster art on both faces
                            int cell = r.Int(0, 15);
                            var sub = new[] { cell % 4 * 0.25, Math.Floor(cell / 4.0) * 0.25, 0.25, 0.25 };
                            double d0 = Math.Min(maxOut, 0.9);
                            var top = WallPt(fr, s, yb + 1.0, d0); var b0 = WallPt(fr, s, yb, d0 - 0.3); var b1 = WallPt(fr, s, yb, d0 + 0.3);
                            double hw = 0.3;
                            double[] Off(double[] p, double k) => new[] { p[0] + fr.Right[0] * k, p[1], p[2] + fr.Right[2] * k };
                            double em = r.Chance(0.5) ? 0.5 : 0.05;
                            var aq = new BAttrSet().Set("aCol", new[] { 1.0, 1.0, 1.0, em }).Set("aPbr", new[] { 0.4, 0, 1, 0.2 });
                            var ua = ShopAtlas.SlotUV("posters", 0, 0, sub); var ub = ShopAtlas.SlotUV("posters", 1, 0, sub); var uc = ShopAtlas.SlotUV("posters", 1, 1, sub); var ud = ShopAtlas.SlotUV("posters", 0, 1, sub);
                            // outward-facing face
                            det.Quad(Off(b1, -hw), Off(b1, hw), Off(top, hw), Off(top, -hw), ua, ub, uc, ud, aq);
                            det.Quad(Off(b0, hw), Off(b0, -hw), Off(top, -hw), Off(top, hw), ua, ub, uc, ud, aq);
                            if (em > 0.3) outp.Lights.Add(new BLight { Pos = WallPt(fr, s, yb + 0.6, d0 + 0.6), Color = new[] { 1.0, 0.9, 0.8 }, Intensity = 0.8, Distance = 3, Minor = true });
                        }
                        else if (kind == "bike")
                        {
                            var p = WallPt(fr, s, yb, Math.Min(maxOut, 0.6));
                            double rot = Math.Atan2(-fr.Right[2], fr.Right[0]);
                            var q = BGeom.SetFromAxisAngle(new[] { 0.0, 1.0, 0.0 }, rot + r.Range(-0.2, 0.2));
                            var m = BGeom.Compose(p, q, new[] { 1.0, 1.0, 1.0 });
                            Bike(det, m, r.Pick(new double[][] { new[] { 0.6, 0.05, 0.05 }, new[] { 0.1, 0.1, 0.1 }, new[] { 0.75, 0.75, 0.75 }, new[] { 0.1, 0.3, 0.5 } }));
                        }
                        else if (kind == "plant")
                        {
                            var p = WallPt(fr, s, yb, 0.35);
                            det.Cyl(new[] { p[0], yb, p[2] }, new[] { p[0], yb + 0.45, p[2] }, 0.22, D(new[] { 0.35, 0.22, 0.15 }, 0, 0.6, 0, 0.6), 8);
                            var m = BGeom.Compose(new[] { p[0], yb + 0.8, p[2] }, (0.0, 0.0, 0.0, 1.0), new[] { 0.9, 1.1, 0.9 });
                            det.Geom(BagGeo(), m, D(new[] { 0.06, 0.13, 0.04 }, 0, 0.5, 0, 0.3));
                        }
                        else if (kind == "crates")
                        {
                            var p = WallPt(fr, s, yb, 0.3);
                            var col = r.Pick(new double[][] { new[] { 0.7, 0.1, 0.08 }, new[] { 0.1, 0.35, 0.15 }, new[] { 0.75, 0.6, 0.1 } });
                            int n = r.Int(1, 4);
                            for (int k = 0; k < n; k++)
                            {
                                det.Box(p[0], yb + 0.15 + k * 0.3, p[2], 0.45, 0.28, 0.45, D(col, 0.0, 0.35, 0, 0.5));
                                det.Box(p[0], yb + 0.27 + k * 0.3, p[2], 0.38, 0.06, 0.38, D(new[] { 0.1, 0.05, 0.02 }, 0, 0.2, 0, 0.1));
                            }
                        }
                        else
                        {
                            var p = WallPt(fr, s, yb, Math.Min(maxOut, 0.8));
                            det.Cyl(new[] { p[0], yb, p[2] }, new[] { p[0], yb + 0.7, p[2] }, 0.12, D(new[] { 1.0, 0.35, 0.05 }, 0.05, 0.4, 0, 0.3), 8, 0.02);
                        }
                    }
                }
            }

            outp.AcM = acM; outp.TankM = tankM; outp.PtankM = ptankM;
        }

        static void Place(List<double[]> arr, double x, double y, double z, double rotY, double s = 1)
        {
            var q = BGeom.SetFromAxisAngle(new[] { 0.0, 1.0, 0.0 }, rotY);
            arr.Add(BGeom.Compose(new[] { x, y, z }, q, new[] { s, s, s }));
        }

        static double RotFor(double[] nn) => Math.Atan2(nn[0], nn[2]);

        static double[] WallPt(BFrame fr, double s, double y, double off = 0) =>
            new[] { fr.Origin[0] + fr.Right[0] * s + fr.Normal[0] * off, y, fr.Origin[2] + fr.Right[2] * s + fr.Normal[2] * off };

        // box dims oriented to a facade frame: w along facade, h up, d along normal → world sx, sy, sz
        static double[] Dims(BFrame fr, double w, double h, double d) =>
            Math.Abs(fr.Right[0]) > 0.5 ? new[] { w, h, d } : new[] { d, h, w };

        static void FacBox(BBucket b, BFrame fr, double cx, double cy, double cz, double w, double h, double d, BAttrSet a)
        { var dd = Dims(fr, w, h, d); b.Box(cx, cy, cz, dd[0], dd[1], dd[2], a); }

        static BFrame SideFrameOf(BVol v, string side)
        {
            switch (side)
            {
                case "nx": return new BFrame { Origin = new[] { v.X0, 0.0, v.Z0 }, Right = new[] { 0.0, 0.0, 1.0 }, Normal = new[] { -1.0, 0.0, 0.0 }, Width = v.Z1 - v.Z0 };
                case "px": return new BFrame { Origin = new[] { v.X1, 0.0, v.Z1 }, Right = new[] { 0.0, 0.0, -1.0 }, Normal = new[] { 1.0, 0.0, 0.0 }, Width = v.Z1 - v.Z0 };
                case "nz": return new BFrame { Origin = new[] { v.X1, 0.0, v.Z0 }, Right = new[] { -1.0, 0.0, 0.0 }, Normal = new[] { 0.0, 0.0, -1.0 }, Width = v.X1 - v.X0 };
                default: return new BFrame { Origin = new[] { v.X0, 0.0, v.Z1 }, Right = new[] { 1.0, 0.0, 0.0 }, Normal = new[] { 0.0, 0.0, 1.0 }, Width = v.X1 - v.X0 };
            }
        }

        static bool F0Shop(BFront front) => front.ShopType != null;

        static void Poster(BBucket det, BFrame fr, double s, double y, BuildingsRng r, double w = 0.6, double extra = 0)
        {
            int cell = r.Int(0, 15);
            var sub = new[] { cell % 4 * 0.25, Math.Floor(cell / 4.0) * 0.25, 0.25, 0.25 };
            double h = w * 1.35;
            double off = 0.035 + extra + r.F() * 0.01;
            var p0 = WallPt(fr, s, y, off); var p1 = WallPt(fr, s + w, y, off); var p2 = WallPt(fr, s + w, y + h, off); var p3 = WallPt(fr, s, y + h, off);
            det.Quad(p0, p1, p2, p3, ShopAtlas.SlotUV("posters", 0, 0, sub), ShopAtlas.SlotUV("posters", 1, 0, sub), ShopAtlas.SlotUV("posters", 1, 1, sub), ShopAtlas.SlotUV("posters", 0, 1, sub),
                new BAttrSet().Set("aCol", new[] { 0.8, 0.8, 0.8, 0.0 }).Set("aPbr", new[] { 0.45, 0, 1, 0.3 }));
        }

        static double[][] _bag = null;
        static double[][] BagGeo()
        {
            if (_bag == null) { _bag = BGeom.PolyhedronIco(1, 0.28); BGeom.GeoScale(_bag, 1, 0.85, 1); }
            return _bag;
        }

        static double[][] _wheel = null;
        static void Bike(BBucket det, double[] m, double[] col)
        {
            if (_wheel == null) _wheel = BGeom.TorusGeo(0.32, 0.025, 4, 14);
            var frame = D(col, 0, 0.35, 0.6, 0.3); var black = D(new[] { 0.03, 0.03, 0.03 }, 0, 0.5, 0, 0.1);
            double[] Tp(double x, double y)
            {
                var v = new[] { x, y, 0.0 };
                BGeom.ApplyMatrix4(v, m);
                return v;
            }
            foreach (double x in new[] { -0.52, 0.52 })
            {
                var wm = BGeom.MulMatrices(m, BGeom.MakeTranslation(x, 0.34, 0)); // makeTranslation().premultiply(m) = m * T
                det.Geom(_wheel, wm, black);
            }
            det.Beam(Tp(-0.52, 0.34), Tp(-0.05, 0.36), 0.03, frame);
            det.Beam(Tp(-0.05, 0.36), Tp(0.38, 0.78), 0.03, frame);
            det.Beam(Tp(-0.05, 0.36), Tp(-0.15, 0.82), 0.03, frame);
            det.Beam(Tp(-0.15, 0.78), Tp(0.36, 0.78), 0.03, frame);
            det.Beam(Tp(0.52, 0.34), Tp(0.4, 0.95), 0.03, frame);
            det.Beam(Tp(-0.52, 0.34), Tp(-0.15, 0.78), 0.025, frame);
            det.Beam(Tp(0.3, 0.98), Tp(0.5, 0.98), 0.03, black);
            { var p = Tp(-0.17, 0.88); det.Box(p[0], p[1], p[2], 0.22, 0.05, 0.1, black); }
            // front basket (mamachari)
            { var p = Tp(0.6, 0.85); det.Box(p[0], p[1], p[2], 0.3, 0.22, 0.32, D(new[] { 0.5, 0.5, 0.5 }, 0, 0.4, 0.7, 0.3)); }
        }

        static double[][] _dish = null;
        static double[][] DishGeo()
        {
            if (_dish == null) { _dish = BGeom.SphereGeo(0.45, 10, 4, 0, Math.PI * 2, 0, 0.7); BGeom.GeoScale(_dish, 1, 0.5, 1); }
            return _dish;
        }
    }
}
