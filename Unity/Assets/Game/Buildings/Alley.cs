// Port of src/systems/buildings/alley.js — backstreet alley (rng 0xA11E):
// paving, stone stairs rising per Layout alley stairs, upper level, handrails, clutter.
using System;
using System.Collections.Generic;

namespace NeonKaido
{
    public static class Alley
    {
        static double[][] _leaf = null;
        static double[][] LeafGeo() { if (_leaf == null) _leaf = BGeom.PolyhedronIco(1, 0.3); return _leaf; }

        public static void BuildAlley(BChunkedBuckets B, BOut outp)
        {
            double A_z = Layout.AlleyZ, w = Layout.AlleyWidth;
            double A_xStart = Layout.AlleyXStart, A_xEnd = Layout.AlleyXEnd;
            double S_xStart = Layout.AlleyStairsXStart, S_xEnd = Layout.AlleyStairsXEnd, S_rise = Layout.AlleyStairsRise;
            var rng = new BuildingsRng(0xA11E);
            double zN = A_z - w / 2, zS = A_z + w / 2, zc = A_z;
            BBucket Fac(double x) => B.Get(x, zc, "facade", BuildArchitecture.FacadeAttrs);
            BBucket Det(double x) => B.Get(x, zc, "detail", BuildArchitecture.DetailAttrs);
            var paveWin = new BAttrSet()
                .Set("aWin", new[] { 0.3, 3, 3, 9999 })
                .Set("aWin2", new[] { 0.5, 0.5, 0, 4 })
                .Set("aWin3", new[] { 9999, 1, 2.2, 0.4 });
            void Pave(double x0, double x1, double y)
            {
                Fac((x0 + x1) / 2).Quad(new[] { x0, y, zS }, new[] { x1, y, zS }, new[] { x1, y, zN }, new[] { x0, y, zN },
                    new[] { x0, -zS }, new[] { x1, -zS }, new[] { x1, -zN }, new[] { x0, -zN }, paveWin,
                    new[] { 0.0, 1, 0 }, new[] { 1.0, 0, 0 });
            }
            double xEndWall = A_xEnd; // closing building facade plane
            // lower paving (slightly above ground to avoid z-fight)
            Pave(S_xStart, A_xStart, 0.025);
            // stairs: two flights with a landing
            var stone = BuildArchitecture.TrimAttr(1, 1.6, 0.35, 0.77);
            double midX0 = S_xStart + (S_xEnd - S_xStart) * 0.45, midX1 = S_xStart + (S_xEnd - S_xStart) * 0.55;
            var flights = new (double xa, double xb, double ya, double yb)[]
            {
                (S_xStart, midX0, 0.0, S_rise / 2),
                (midX1, S_xEnd, S_rise / 2, S_rise),
            };
            foreach (var f in flights)
            {
                double run = Math.Abs(f.xb - f.xa), rise = f.yb - f.ya;
                int n = (int)Math.Floor(rise / 0.165 + 0.5); // JS Math.round
                double tread = run / n, riser = rise / n;
                for (int i = 0; i < n; i++)
                {
                    double x0 = f.xa - i * tread, x1 = x0 - tread;
                    double yTop = f.ya + (i + 1) * riser;
                    Fac(x0).Box((x0 + x1) / 2 - 0.02, yTop / 2, zc, tread + 0.04, yTop, w,
                        BuildArchitecture.TrimAttr(1, 1.6, 0.3 + rng.F() * 0.15, 0.77 + rng.F() * 0.2));
                    // worn nosing strip (darker metal edge)
                    Det(x0).Box(x0 - 0.03, yTop - 0.01, zc, 0.05, 0.025, w - 0.1,
                        new BAttrSet().Set("aCol", new[] { 0.12, 0.12, 0.11, 0 }).Set("aPbr", new[] { 0.3, 0.8, 0, 0.6 }));
                }
                // ramp collider along the flight
                double len = GroundGeo.Hypot2(run, rise), ang = Math.Atan2(rise, run);
                double cx = (f.xa + f.xb) / 2, cy = (f.ya + f.yb) / 2 - 0.15;
                // rotate around z so the ramp rises toward -x
                outp.Colliders.Add(new BCollider { X = cx, Y = cy, Z = zc, Hx = len / 2, Hy = 0.15, Hz = w / 2,
                    Rot = new[] { 0.0, 0.0, Math.Sin(-ang / 2), Math.Cos(-ang / 2) } });
            }
            // landing
            Fac(midX0).Box((midX0 + midX1) / 2, S_rise / 4, zc, midX0 - midX1, S_rise / 2, w, stone);
            outp.Colliders.Add(new BCollider { X = (midX0 + midX1) / 2, Y = S_rise / 4, Z = zc, Hx = (midX0 - midX1) / 2, Hy = S_rise / 4, Hz = w / 2 });
            // upper level platform
            double ux0 = xEndWall, ux1 = S_xEnd;
            Fac(ux0).Box((ux0 + ux1) / 2, S_rise / 2 - 0.01, zc, ux1 - ux0, S_rise - 0.02, w, stone, 0, new HashSet<string> { "py" });
            Pave(ux0, ux1, S_rise);
            outp.Colliders.Add(new BCollider { X = (ux0 + ux1) / 2, Y = S_rise / 2, Z = zc, Hx = (ux1 - ux0) / 2, Hy = S_rise / 2, Hz = w / 2 });

            // handrails along both walls + central rail on the stairs
            var metal = new BAttrSet().Set("aCol", new[] { 0.07, 0.07, 0.07, 0 }).Set("aPbr", new[] { 0.3, 0.85, 0, 0.5 });
            double YAt(double x)
            {
                if (x > S_xStart) return 0;
                if (x > midX0) return (S_xStart - x) / (S_xStart - midX0) * S_rise / 2;
                if (x > midX1) return S_rise / 2;
                if (x > S_xEnd) return S_rise / 2 + (midX1 - x) / (midX1 - S_xEnd) * S_rise / 2;
                return S_rise;
            }
            foreach (double z in new[] { zN + 0.18, zS - 0.18, zc })
            {
                var pts = new List<double[]>();
                for (double x = S_xStart + 0.5; x >= S_xEnd - 0.5; x -= 0.5) pts.Add(new[] { x, YAt(x) + 0.9, z });
                for (int i = 0; i < pts.Count - 1; i++) Det(pts[i][0]).Beam(pts[i], pts[i + 1], 0.045, metal);
                for (int i = 0; i < pts.Count; i += 3)
                {
                    var p = pts[i];
                    Det(p[0]).Beam(new[] { p[0], YAt(p[0]), z }, p, 0.04, metal);
                }
                if (z == zc) continue;
                // lower rail
                for (int i = 0; i < pts.Count - 1; i++)
                    Det(pts[i][0]).Beam(new[] { pts[i][0], pts[i][1] - 0.45, z }, new[] { pts[i + 1][0], pts[i + 1][1] - 0.45, z }, 0.025, metal);
            }

            // potted plants on the stairs by the walls (classic Tokyo backstreet)
            for (double x = S_xStart - 1.2; x > S_xEnd + 0.5; x -= rng.Range(1.0, 2.6))
            {
                if (!rng.Chance(0.6)) continue;
                double z = rng.Chance(0.5) ? zN + 0.42 : zS - 0.42;
                double y = YAt(x + 0.25);
                var col = rng.Pick(new[] { new[] { 0.45, 0.25, 0.15 }, new[] { 0.2, 0.2, 0.22 }, new[] { 0.6, 0.6, 0.58 } });
                var pot = new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], 0.0 }).Set("aPbr", new[] { 0.6, 0, 0, 0.6 });
                Det(x).Cyl(new[] { x, y, z }, new[] { x, y + 0.35, z }, 0.16, pot, 8);
                for (int k = 0; k < 3; k++)
                {
                    // JS: compose(vec3(pos draws), quaternion(euler draws), vec3(scale draws)) — left-to-right
                    double px = x + rng.Range(-0.12, 0.12), py = y + 0.55 + k * 0.12, pz = z + rng.Range(-0.1, 0.1);
                    var q = BGeom.SetFromEuler(rng.F() * 3, rng.F() * 3, 0, "XYZ");
                    var m = BGeom.Compose(new[] { px, py, pz }, q, new[] { rng.Range(0.6, 1.0), rng.Range(0.6, 1.1), rng.Range(0.6, 1.0) });
                    Det(x).Geom(LeafGeo(), m,
                        new BAttrSet().Set("aCol", new[] { 0.05, 0.11 + rng.F() * 0.07, 0.035, 0 }).Set("aPbr", new[] { 0.45, 0, 0, 0.3 }));
                }
            }
            // lit doors at the landing (both sides)
            foreach (double z in new[] { zN + 0.02, zS - 0.02 })
            {
                double x = (midX0 + midX1) / 2;
                double nn = z < zc ? 1 : -1;
                Det(x).Box(x, S_rise / 2 + 1.05, z + nn * 0.01, 0.9, 2.1, 0.04,
                    new BAttrSet().Set("aCol", new[] { 0.35, 0.25, 0.15, 0 }).Set("aPbr", new[] { 0.5, 0, 0, 0.6 }));
                Det(x).Box(x, S_rise / 2 + 2.3, z + nn * 0.1, 0.22, 0.12, 0.18,
                    new BAttrSet().Set("aCol", new[] { 1, 0.8, 0.5, 4 }).Set("aPbr", new[] { 0.5, 0, 0, 0 }));
            }
            outp.Lights.Add(new BLight { Pos = new[] { (midX0 + midX1) / 2, S_rise / 2 + 2.6, zc }, Color = new[] { 1.0, 0.75, 0.45 }, Intensity = 4, Distance = 8 });

            // clutter: beer crates, bins, buckets, bikes, pipes along walls
            void Crate(double x, double y, double z, double[] col)
            {
                double r = rng.Range(-0.3, 0.3);
                var a = new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], 0.02 }).Set("aPbr", new[] { 0.35, 0, 0, 0.5 });
                // frame posts + rails (open plastic beer crate) with bottles inside
                foreach (var dxdz in new[] { new[] { -0.21, -0.16 }, new[] { 0.21, -0.16 }, new[] { -0.21, 0.16 }, new[] { 0.21, 0.16 } })
                    Det(x).Box(x + dxdz[0], y + 0.15, z + dxdz[1], 0.03, 0.3, 0.03, a, r);
                foreach (double yy in new[] { 0.02, 0.15, 0.28 })
                {
                    Det(x).Box(x, y + yy, z - 0.16, 0.45, 0.04, 0.03, a, r);
                    Det(x).Box(x, y + yy, z + 0.16, 0.45, 0.04, 0.03, a, r);
                }
                Det(x).Box(x, y + 0.02, z, 0.45, 0.04, 0.35, a, r);
                Det(x).Box(x, y + 0.14, z, 0.4, 0.24, 0.3,
                    new BAttrSet().Set("aCol", new[] { 0.12, 0.06, 0.02, 0 }).Set("aPbr", new[] { 0.15, 0, 0, 0.1 }), r);
            }
            for (double x = A_xStart - 2; x > A_xEnd + 1; x -= rng.Range(1.2, 3.5))
            {
                if (x < S_xStart + 0.5 && x > S_xEnd - 0.5) continue;
                double y = YAt(x);
                double z = rng.Chance(0.5) ? zN + 0.35 : zS - 0.35;
                double r = rng.F();
                if (r < 0.4)
                {
                    var col = rng.Pick(new[] { new[] { 0.7, 0.1, 0.08 }, new[] { 0.1, 0.35, 0.15 }, new[] { 0.75, 0.6, 0.1 }, new[] { 0.15, 0.2, 0.5 } });
                    int n = rng.Int(1, 4);
                    for (int k = 0; k < n; k++) Crate(x, y + k * 0.3, z, col);
                }
                else if (r < 0.6)
                {
                    // wheeled refuse bin with lid, handle and wheels
                    var bc = rng.Pick(new[] { new[] { 0.12, 0.22, 0.14 }, new[] { 0.15, 0.2, 0.35 }, new[] { 0.3, 0.3, 0.3 } });
                    Det(x).Box(x, y + 0.5, z, 0.58, 0.8, 0.62, new BAttrSet().Set("aCol", new[] { bc[0], bc[1], bc[2], 0.0 }).Set("aPbr", new[] { 0.35, 0.1, 0, 0.8 }));
                    Det(x).Box(x, y + 0.93, z + 0.02, 0.64, 0.06, 0.7, new BAttrSet().Set("aCol", new[] { bc[0] * 0.8, bc[1] * 0.8, bc[2] * 0.8, 0.0 }).Set("aPbr", new[] { 0.3, 0.1, 0, 0.6 }));
                    Det(x).Box(x, y + 0.85, z - 0.35, 0.5, 0.04, 0.04, new BAttrSet().Set("aCol", new[] { 0.05, 0.05, 0.05, 0 }).Set("aPbr", new[] { 0.4, 0, 0, 0.3 }));
                    foreach (double dx in new[] { -0.22, 0.22 })
                        Det(x).Cyl(new[] { x + dx, y + 0.08, z - 0.25 }, new[] { x + dx, y + 0.08, z - 0.33 }, 0.08,
                            new BAttrSet().Set("aCol", new[] { 0.03, 0.03, 0.03, 0 }).Set("aPbr", new[] { 0.6, 0, 0, 0.2 }), 8);
                    Det(x).Box(x, y + 0.55, z + (z < zc ? 0.315 : -0.315), 0.3, 0.2, 0.01,
                        new BAttrSet().Set("aCol", new[] { 0.9, 0.85, 0.2, 0.05 }).Set("aPbr", new[] { 0.4, 0, 0, 0.5 }));
                }
                else if (r < 0.75)
                {
                    // bucket + stacked boxes
                    Det(x).Box(x, y + 0.2, z, 0.6, 0.4, 0.4, new BAttrSet().Set("aCol", new[] { 0.33, 0.24, 0.15, 0 }).Set("aPbr", new[] { 0.85, 0, 0, 0.7 }));
                    Det(x).Box(x + 0.1, y + 0.55, z, 0.5, 0.3, 0.35, new BAttrSet().Set("aCol", new[] { 0.36, 0.27, 0.17, 0 }).Set("aPbr", new[] { 0.85, 0, 0, 0.7 }));
                }
                else if (r < 0.88)
                {
                    // bicycle (very simplified: wheels as thin boxes + frame beams)
                    var m2 = new BAttrSet().Set("aCol", new[] { 0.05, 0.05, 0.05, 0 }).Set("aPbr", new[] { 0.4, 0.6, 0, 0.3 });
                    foreach (double dx in new[] { -0.5, 0.5 }) Det(x).Box(x + dx, y + 0.33, z, 0.66, 0.66, 0.04, m2);
                    Det(x).Beam(new[] { x - 0.5, y + 0.33, z }, new[] { x + 0.1, y + 0.8, z }, 0.035,
                        new BAttrSet().Set("aCol", new[] { 0.5, 0.05, 0.05, 0 }).Set("aPbr", new[] { 0.3, 0.5, 0, 0.3 }));
                    Det(x).Beam(new[] { x + 0.1, y + 0.8, z }, new[] { x + 0.5, y + 0.33, z }, 0.035,
                        new BAttrSet().Set("aCol", new[] { 0.5, 0.05, 0.05, 0 }).Set("aPbr", new[] { 0.3, 0.5, 0, 0.3 }));
                }
                else
                {
                    // gas cylinders
                    for (int k = 0; k < 2; k++)
                        Det(x).Box(x + k * 0.35, y + 0.6, z, 0.3, 1.2, 0.3,
                            new BAttrSet().Set("aCol", new[] { 0.6, 0.6, 0.58, 0 }).Set("aPbr", new[] { 0.4, 0.5, 0, 0.6 }));
                }
            }
        }
    }
}
