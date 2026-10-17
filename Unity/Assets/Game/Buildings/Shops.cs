// Port of src/systems/buildings/shops.js — ground-floor shopfronts with real interior geometry.
// Shop atlas slots in pixels (4096x4096); the atlas itself is composed in a later editor step.
using System;
using System.Collections.Generic;

namespace NeonKaido
{
    public static class ShopAtlas
    {
        public const int Size = 4096;
        public static readonly Dictionary<string, double[]> Slots = new Dictionary<string, double[]>
        {
            ["ramen"] = new[] { 0.0, 0, 2048, 1024 }, ["konbini"] = new[] { 2048.0, 0, 2048, 1024 },
            ["izakaya"] = new[] { 0.0, 1024, 2048, 1024 }, ["pachinko"] = new[] { 2048.0, 1024, 2048, 1024 },
            ["shelves"] = new[] { 0.0, 2048, 2048, 1024 }, ["shutter"] = new[] { 2048.0, 2048, 1024, 1024 },
            ["vending"] = new[] { 3072.0, 2048, 512, 1024 }, ["noren"] = new[] { 3584.0, 2048, 512, 256 },
            ["posters"] = new[] { 0.0, 3072, 1024, 1024 },
        };

        // uv for slot-relative (u,v) in [0,1], v up; sub=[uOff,vOff,wScale,hScale] in slot units
        public static double[] SlotUV(string name, double u, double v, double[] sub = null)
        {
            var s = Slots[name];
            double x = s[0], y = s[1], w = s[2], h = s[3];
            if (sub != null) { x += sub[0] * w; y += sub[1] * h; w *= sub[2]; h *= sub[3]; }
            return new[] { (x + u * w) / Size, 1 - (y + (1 - v) * h) / Size };
        }
    }

    public class ShopCtx
    {
        public BuildingsRng Rng;
        public BFrame Fr;
        public double W, H, BaseY, Depth, Ho, S0, S1, FasH;
        public string Type;
        public BLot Lot;
        public BBucket Det, Fac, Glass;
        public BOut Out;
        public double[] P(double s, double y, double d) => new[]
        {
            Fr.Origin[0] + Fr.Right[0] * s - Fr.Normal[0] * d,
            BaseY + y,
            Fr.Origin[2] + Fr.Right[2] * s - Fr.Normal[2] * d,
        };
    }

    public class ShopBuilder
    {
        readonly BChunkedBuckets B;
        readonly BOut Out;

        static readonly double[] Wood = { 0.32, 0.19, 0.1 }, WoodL = { 0.55, 0.37, 0.2 }, Dark = { 0.05, 0.05, 0.055 }, Steel = { 0.55, 0.56, 0.57 };

        static readonly Dictionary<string, (string, double)[]> ShopWeights = new Dictionary<string, (string, double)[]>
        {
            ["avenue"] = new[] { ("ramen", 2.0), ("konbini", 1.2), ("izakaya", 1.6), ("pachinko", 0.7), ("shutter", 0.6), ("bar", 1.1), ("boutique", 1.4), ("lobby", 0.7) },
            ["cross"] = new[] { ("ramen", 1.2), ("konbini", 0.6), ("izakaya", 1.5), ("shutter", 2.2), ("bar", 1.4), ("boutique", 0.8), ("lobby", 1.2) },
            ["alley"] = new[] { ("izakaya", 3.0), ("bar", 2.5), ("ramen", 1.4), ("shutter", 0.7) },
        };
        static readonly Dictionary<string, double[]> LightCol = new Dictionary<string, double[]>
        {
            ["ramen"] = new[] { 1.0, 0.7, 0.42 }, ["konbini"] = new[] { 0.85, 0.93, 1.0 }, ["izakaya"] = new[] { 1.0, 0.62, 0.32 },
            ["pachinko"] = new[] { 1.0, 0.85, 0.7 }, ["bar"] = new[] { 1.0, 0.6, 0.65 }, ["boutique"] = new[] { 1.0, 0.92, 0.82 },
            ["lobby"] = new[] { 0.9, 0.95, 1.0 },
        };

        public ShopBuilder(BChunkedBuckets b, BOut outp) { B = b; Out = outp; }

        // The exact index.js shop-selection loop (hero ramen + anti-closed-run).
        public void BuildAll(List<BLot> lots)
        {
            var OPEN = new[] { "izakaya", "ramen", "bar", "boutique", "konbini", "izakaya", "ramen" };
            bool hasPrev = false; double prevX = 0, prevZ = 0; bool prevClosed = false;
            foreach (var lot in lots)
                foreach (var f in lot.Fronts)
                {
                    if (!(f.Shop && lot.Detail == "hi" && f.BaseY >= 0)) continue;
                    double cx = (lot.X0 + lot.X1) / 2, cz = (lot.Z0 + lot.Z1) / 2;
                    string type = null;
                    if (f.District == "avenue" && lot.X1 <= -9 && lot.Z0 < 26 && lot.Z1 > 18) type = "ramen";
                    else if (hasPrev && prevClosed && GroundGeo.Hypot2(prevX - cx, prevZ - cz) < 16) type = OPEN[lot.Id % OPEN.Length];
                    f.ShopType = Build(lot, f, type);
                    hasPrev = true; prevX = cx; prevZ = cz;
                    prevClosed = f.ShopType == "shutter" || f.ShopType == "lobby";
                }
        }

        public string Build(BLot lot, BFront front, string typeOpt)
        {
            var rng = new BuildingsRng((uint)((long)lot.Id * 104729 + front.Side[1] * 31));
            var v = lot.Volumes[0];
            var fr = BuildArchitecture.SideFrame(v, front.Side, 0);
            double baseY = front.BaseY != null && front.BaseY > 0 ? front.BaseY.Value : 0;
            double W = fr.Width;
            double H = lot.GroundH;
            double lotDepth = (front.Side == "nx" || front.Side == "px") ? v.X1 - v.X0 : v.Z1 - v.Z0;
            double cx = fr.Origin[0] + fr.Right[0] * W / 2, cz = fr.Origin[2] + fr.Right[2] * W / 2;
            var det = B.Get(cx, cz, "detail", BuildArchitecture.DetailAttrs);
            var fac = B.Get(cx, cz, "facade", BuildArchitecture.FacadeAttrs);
            var glass = B.Get(cx, cz, "glass", new Dictionary<string, double[]>());
            string type = typeOpt ?? rng.Weighted(ShopWeights.TryGetValue(front.District, out var w) ? w : ShopWeights["cross"]);
            var c = new ShopCtx { Rng = rng, Fr = fr, W = W, H = H, BaseY = baseY, Lot = lot, Type = type, Det = det, Fac = fac, Glass = glass, Out = Out, Depth = Math.Min(5.5, lotDepth - 0.6) };

            // structure: pillars + fascia (facade material so it matches the building)
            // JS: trimAttr(lot.wallLayer ?? 1, ...) — wallLayer is always set by architecture; 0 is VALID
            var ta = BuildArchitecture.TrimAttr(lot.WallLayer, 3, 0.5, lot.Seed);
            double pw = W > 7 ? 0.45 : 0.3;
            FBox(c, fac, pw / 2, H / 2, -0.04, pw, H, 0.3, ta);
            FBox(c, fac, W - pw / 2, H / 2, -0.04, pw, H, 0.3, ta);
            double fasH = rng.Range(0.75, 1.0);
            double ho = H - fasH; // opening height
            c.Ho = ho; c.S0 = pw; c.S1 = W - pw; c.FasH = fasH;
            var fasCol = rng.Pick(new[]
            {
                new[] { 0.08, 0.08, 0.09 }, new[] { 0.16, 0.05, 0.04 }, new[] { 0.85, 0.85, 0.82 },
                new[] { 0.05, 0.07, 0.1 }, new[] { 0.3, 0.3, 0.3 },
            });
            DBox(c, W / 2, ho + fasH / 2, -0.12, W - 0.02, fasH, 0.26, fasCol, 0, 0.45, 0.3, 0.6);
            // fascia downlights strip
            for (double s = 0.8; s < W - 0.5; s += rng.Range(1.4, 2.2))
                DBox(c, s, ho - 0.01, -0.2, 0.1, 0.025, 0.1, new[] { 1.0, 0.8, 0.6 }, 1.6, 0.5, 0, 0);

            // ShopCtx holds a struct COPY of the rng — sync it before the type methods consume draws,
            // and sync back after, so the whole shop uses ONE continuous stream (JS uses one object).
            c.Rng = rng;
            if (type == "shutter") Shutter(c);
            else
            {
                InteriorShell(c);
                switch (type)
                {
                    case "ramen": Ramen(c); break;
                    case "konbini": Konbini(c); break;
                    case "izakaya": Izakaya(c); break;
                    case "pachinko": Pachinko(c); break;
                    case "bar": Bar(c); break;
                    case "boutique": Boutique(c); break;
                    case "lobby": Lobby(c); break;
                }
                var lc = LightCol[type];
                var pos = c.P(W / 2, ho - 0.35, 0.6);
                Out.Lights.Add(new BLight { Pos = pos, Color = lc, Intensity = type == "pachinko" || type == "konbini" ? 9 : 6, Distance = 9 });
            }
            // utility meter box / gas pipe on one pillar (dead code in JS but consumes one rng draw)
            rng = c.Rng;
            if (rng.Chance(0.0))
            {
                double ms = rng.Chance(0.5) ? 0.2 : W - 0.2;
                DBox(c, ms, 1.5, -0.22, 0.28, 0.4, 0.12, new[] { 0.42, 0.42, 0.39 }, 0, 0.5, 0.2, 0.9);
                DBox(c, ms, 1.56, -0.285, 0.12, 0.06, 0.01, new[] { 0.5, 0.9, 0.6 }, 0.6, 0.3);
                DBox(c, ms, 0.65, -0.2, 0.04, 1.3, 0.04, new[] { 0.6, 0.55, 0.2 }, 0, 0.5, 0.4, 0.7);
            }
            return type;
        }

        // ---------- primitives ----------
        void DBox(ShopCtx c, double s, double y, double d, double sw, double sh, double sd, double[] col, double em, double rough, double metal = 0, double grime = 0.4, double tex = 0)
        {
            var p = c.P(s, y, d);
            double rotY = Math.Atan2(c.Fr.Right[2], c.Fr.Right[0]) * -1;
            c.Det.Box(p[0], p[1], p[2], sw, sh, sd, new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], em }).Set("aPbr", new[] { rough, metal, tex, grime }), rotY);
        }

        void FBox(ShopCtx c, BBucket bucket, double s, double y, double d, double sw, double sh, double sd, BAttrSet a)
        {
            var p = c.P(s, y, d);
            double rotY = -Math.Atan2(c.Fr.Right[2], c.Fr.Right[0]);
            bucket.Box(p[0], p[1], p[2], sw, sh, sd, a, rotY);
        }

        void TQuad(ShopCtx c, double s0, double s1, double y0, double y1, double d, string slot, double[] col, double em, double[] sub = null, double rough = 0.6, bool flipFacing = false)
        {
            var p0 = c.P(s0, y0, d); var p1 = c.P(s1, y0, d); var p2 = c.P(s1, y1, d); var p3 = c.P(s0, y1, d);
            var a = new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], em }).Set("aPbr", new[] { rough, 0, 1, 0 });
            double[] U(double uu, double vv) => ShopAtlas.SlotUV(slot, uu, vv, sub);
            if (!flipFacing) c.Det.Quad(p0, p1, p2, p3, U(0, 0), U(1, 0), U(1, 1), U(0, 1), a);
            else c.Det.Quad(p1, p0, p3, p2, U(1, 0), U(0, 0), U(0, 1), U(1, 1), a);
        }

        void LQuad(ShopCtx c, double[][] pts, double[] col, double em, double rough = 0.7, double metal = 0)
        {
            var w = new double[pts.Length][];
            for (int i = 0; i < pts.Length; i++) w[i] = c.P(pts[i][0], pts[i][1], pts[i][2]);
            var a = new BAttrSet().Set("aCol", new[] { col[0], col[1], col[2], em }).Set("aPbr", new[] { rough, metal, 0, 0.2 });
            c.Det.Quad(w[0], w[1], w[2], w[3], new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 }, a);
        }

        // Textured tiled wall A->B (A/B are local [s,d] points), random square-ish crops of the slot.
        void TileWall(ShopCtx c, double[] A, double[] Bp, double y0, double y1, string slot, double[] tint, double em)
        {
            double len = GroundGeo.Hypot2(Bp[0] - A[0], Bp[1] - A[1]);
            double th = y1 - y0;
            var s = ShopAtlas.Slots[slot];
            double sw = s[2], sh = s[3];
            double n = Math.Max(1, JRound(len / th));
            double tw = len / n;
            double cw = Math.Min(1, (tw / th) * (sh / sw));
            for (int i = 0; i < (int)n; i++)
            {
                double t0 = (double)i / n, t1 = (double)(i + 1) / n;
                double sA = A[0] + (Bp[0] - A[0]) * t0, dA = A[1] + (Bp[1] - A[1]) * t0;
                double sB = A[0] + (Bp[0] - A[0]) * t1, dB = A[1] + (Bp[1] - A[1]) * t1;
                double u0 = c.Rng.Range(0, 1 - cw);
                double[] U(double uu, double vv) => ShopAtlas.SlotUV(slot, u0 + uu * cw, vv);
                var p0 = c.P(sA, y0, dA); var p1 = c.P(sB, y0, dB); var p2 = c.P(sB, y1, dB); var p3 = c.P(sA, y1, dA);
                c.Det.Quad(p0, p1, p2, p3, U(0, 0), U(1, 0), U(1, 1), U(0, 1),
                    new BAttrSet().Set("aCol", new[] { tint[0], tint[1], tint[2], em }).Set("aPbr", new[] { 0.6, 0, 1, 0 }));
            }
        }

        static double JRound(double x) => Math.Floor(x + 0.5);

        double[] Em(ShopCtx c, double[] lc)
        {
            double[] wc = WallCol(c.Type);
            return new[] { wc[0] * lc[0], wc[1] * lc[1], wc[2] * lc[2] };
        }

        static double[] WallCol(string type)
        {
            switch (type)
            {
                case "ramen": return new[] { 0.55, 0.4, 0.26 };
                case "konbini": return new[] { 0.92, 0.93, 0.95 };
                case "izakaya": return new[] { 0.35, 0.22, 0.12 };
                case "pachinko": return new[] { 0.9, 0.75, 0.5 };
                case "bar": return new[] { 0.16, 0.1, 0.07 };
                case "boutique": return new[] { 0.92, 0.9, 0.86 };
                default: return new[] { 0.8, 0.8, 0.78 };
            }
        }
        static double WallEm(string type)
        {
            switch (type)
            {
                case "ramen": return 0.35; case "konbini": return 0.7; case "izakaya": return 0.3;
                case "pachinko": return 0.8; case "bar": return 0.2; case "boutique": return 0.6; default: return 0.45;
            }
        }
        static double[] FloorCol(string type)
        {
            switch (type)
            {
                case "ramen": return new[] { 0.25, 0.16, 0.1 };
                case "konbini": return new[] { 0.75, 0.75, 0.73 };
                case "izakaya": return new[] { 0.2, 0.13, 0.08 };
                case "pachinko": return new[] { 0.5, 0.12, 0.1 };
                case "bar": return new[] { 0.1, 0.08, 0.08 };
                case "boutique": return new[] { 0.55, 0.45, 0.35 };
                default: return new[] { 0.4, 0.4, 0.42 };
            }
        }

        void InteriorShell(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            string type = c.Type;
            var lc = LightCol.TryGetValue(type, out var l2) ? l2 : new[] { 1.0, 0.9, 0.8 };
            var wallCol = WallCol(type);
            double wallEm = WallEm(type);
            var floorCol = FloorCol(type);
            double ceilH = ho + 0.25;
            var emCol = new[] { wallCol[0] * lc[0], wallCol[1] * lc[1], wallCol[2] * lc[2] };
            // floor (faces up)
            c.Det.Quad(c.P(s0, 0.02, 0), c.P(s1, 0.02, 0), c.P(s1, 0.02, D), c.P(s0, 0.02, D),
                new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 },
                new BAttrSet().Set("aCol", new[] { floorCol[0], floorCol[1], floorCol[2], wallEm * 0.4 }).Set("aPbr", new[] { 0.25, 0, 0, 0.2 }),
                new[] { 0.0, 1, 0 });
            // ceiling (faces down)
            c.Det.Quad(c.P(s0, ceilH, 0), c.P(s0, ceilH, D), c.P(s1, ceilH, D), c.P(s1, ceilH, 0),
                new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 },
                new BAttrSet().Set("aCol", new[] { emCol[0], emCol[1], emCol[2], wallEm * 0.6 }).Set("aPbr", new[] { 0.8, 0, 0, 0 }),
                new[] { 0.0, -1, 0 });
            // side walls (face inward)
            string sideSlot = type switch
            {
                "ramen" => "izakaya", "konbini" => "shelves", "izakaya" => "izakaya", "pachinko" => "pachinko",
                "bar" => "izakaya", "boutique" => "shelves", _ => null,
            };
            if (sideSlot != null)
            {
                var k = type == "bar" ? new[] { 0.6, 0.45, 0.5 } : type == "ramen" ? new[] { 0.95, 0.8, 0.65 } : new[] { 0.9, 0.9, 0.9 };
                double emVal = type == "pachinko" ? 1.1 : 0.55;
                double y0 = (type == "konbini" || type == "boutique" || type == "pachinko") ? 0 : 0.9;
                TileWall(c, new[] { s0, 0.15 }, new[] { s0, D }, y0, ceilH, sideSlot, k, emVal);
                TileWall(c, new[] { s1, D }, new[] { s1, 0.15 }, y0, ceilH, sideSlot, k, emVal);
                if (y0 > 0)
                {
                    LQuad(c, new[] { new[] { s0, 0.0, 0 }, new[] { s0, 0.0, D }, new[] { s0, y0, D }, new[] { s0, y0, 0 } }, new[] { 0.18, 0.11, 0.06 }, 0.12, 0.6);
                    LQuad(c, new[] { new[] { s1, 0.0, D }, new[] { s1, 0.0, 0 }, new[] { s1, y0, 0 }, new[] { s1, y0, D } }, new[] { 0.18, 0.11, 0.06 }, 0.12, 0.6);
                }
            }
            else
            {
                var q0 = new[] { new[] { s0, 0.0, D }, new[] { s0, 0.0, 0 }, new[] { s0, ceilH, 0 }, new[] { s0, ceilH, D } };
                Array.Reverse(q0);
                LQuad(c, q0, emCol, wallEm);
                var q1 = new[] { new[] { s1, 0.0, 0 }, new[] { s1, 0.0, D }, new[] { s1, ceilH, D }, new[] { s1, ceilH, 0 } };
                Array.Reverse(q1);
                LQuad(c, q1, emCol, wallEm);
            }
            // back wall textured
            string slot = type switch
            {
                "ramen" => "ramen", "konbini" => "konbini", "izakaya" => "izakaya", "pachinko" => "pachinko",
                "bar" => "izakaya", "boutique" => "shelves", _ => null,
            };
            if (slot != null)
            {
                var tc = type == "bar" ? new[] { 0.9, 0.6, 0.75 } : new[] { 1.0, 1.0, 1.0 };
                double y0 = (type == "konbini" || type == "boutique" || type == "pachinko") ? 0 : 0.9;
                TileWall(c, new[] { s0, D }, new[] { s1, D }, y0, ceilH, slot, tc, type == "pachinko" ? 1.4 : 0.95);
                if (y0 > 0) LQuad(c, new[] { new[] { s0, 0.0, D }, new[] { s1, 0.0, D }, new[] { s1, y0, D }, new[] { s0, y0, D } }, new[] { 0.2, 0.12, 0.07 }, 0.15, 0.6);
            }
            else LQuad(c, new[] { new[] { s0, 0.0, D }, new[] { s1, 0.0, D }, new[] { s1, ceilH, D }, new[] { s0, ceilH, D } }, emCol, wallEm);
            // glass front with mullions, door, kick plate
            if (type != "izakaya") GlassFront(c);
        }

        void GlassFront(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho;
            var rng = c.Rng;
            var g = c.Glass;
            double d = 0.06;
            g.Quad(c.P(s0, 0, d), c.P(s1, 0, d), c.P(s1, ho, d), c.P(s0, ho, d),
                new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 }, null);
            var fcol = rng.Chance(0.6) ? Dark : Steel;
            double n = Math.Max(2, JRound((s1 - s0) / 1.6));
            for (int i = 0; i <= (int)n; i++)
            {
                double s = s0 + (s1 - s0) * i / n;
                DBox(c, s, ho / 2, d, 0.06, ho, 0.08, fcol, 0, 0.35, 0.7, 0.1);
            }
            DBox(c, (s0 + s1) / 2, 0.15, d, s1 - s0, 0.3, 0.1, fcol, 0, 0.4, 0.6, 0.3);
            DBox(c, (s0 + s1) / 2, ho - 0.03, d, s1 - s0, 0.06, 0.1, fcol, 0, 0.4, 0.6, 0.1);
            DBox(c, (s0 + s1) / 2, ho - 0.55, d, s1 - s0, 0.05, 0.08, fcol, 0, 0.4, 0.6, 0.1);
        }

        void Noren(ShopCtx c, double s, double w, double top)
        {
            const int pn = 4; const double gap = 0.03;
            double pw = (w - gap * (pn - 1)) / pn;
            for (int i = 0; i < pn; i++)
            {
                double a = s + i * (pw + gap);
                double u0 = new[] { 0.012, 0.256, 0.502, 0.748 }[i], u1 = u0 + 0.24;
                var sub = new[] { u0, 0.075, u1 - u0, 0.84 };
                double sway = i % 2 == 1 ? 0.02 : -0.015;
                var p0 = c.P(a, top - 0.85, -0.12 - sway); var p1 = c.P(a + pw, top - 0.85, -0.12 - sway);
                var p2 = c.P(a + pw, top, -0.1); var p3 = c.P(a, top, -0.1);
                var at = new BAttrSet().Set("aCol", new[] { 1.0, 1.0, 1.0, 0.06 }).Set("aPbr", new[] { 0.9, 0, 1, 0 });
                double[] U(double uu, double vv) => ShopAtlas.SlotUV("noren", uu, vv, sub);
                c.Det.Quad(p0, p1, p2, p3, U(0, 0), U(1, 0), U(1, 1), U(0, 1), at);
                c.Det.Quad(p1, p0, p3, p2, U(1, 0), U(0, 0), U(0, 1), U(1, 1), at);
            }
            DBox(c, s + w / 2, top + 0.02, -0.1, w + 0.2, 0.03, 0.03, Wood, 0, 0.5);
        }

        void Awning(ShopCtx c, double[] col, bool stripes = false)
        {
            double W = c.W, ho = c.Ho;
            var rng = c.Rng;
            double proj = rng.Range(0.9, 1.5), drop = rng.Range(0.35, 0.6);
            double yTop = ho + 0.12;
            int n = stripes ? (int)Math.Max(4, JRound(W / 0.5)) : 1;
            for (int i = 0; i < n; i++)
            {
                double s0 = 0.1 + (W - 0.2) * i / n, s1 = 0.1 + (W - 0.2) * (i + 1) / n;
                var cc = stripes && i % 2 == 1 ? new[] { 0.85, 0.83, 0.78 } : col;
                var a = new BAttrSet().Set("aCol", new[] { cc[0], cc[1], cc[2], 0.02 }).Set("aPbr", new[] { 0.55, 0, 0, 0.6 });
                var p0 = c.P(s0, yTop - drop, -proj); var p1 = c.P(s1, yTop - drop, -proj);
                var p2 = c.P(s1, yTop, -0.25); var p3 = c.P(s0, yTop, -0.25);
                c.Det.Quad(p0, p1, p2, p3, new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 }, a);
                c.Det.Quad(p1, p0, p3, p2, new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 },
                    new BAttrSet().Set("aCol", new[] { cc[0] * 0.5, cc[1] * 0.5, cc[2] * 0.5, 0.05 }).Set("aPbr", new[] { 0.8, 0, 0, 0.3 }));
                var v0 = c.P(s0, yTop - drop - 0.22, -proj); var v1 = c.P(s1, yTop - drop - 0.22, -proj);
                var v2 = c.P(s1, yTop - drop, -proj); var v3 = c.P(s0, yTop - drop, -proj);
                c.Det.Quad(v0, v1, v2, v3, new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 }, a);
            }
            foreach (double s in new[] { 0.15, W - 0.15 })
            {
                var p0 = c.P(s, yTop - drop, -proj); var p1 = c.P(s, yTop - 0.05, -0.2);
                c.Det.Beam(p0, p1, 0.025, new BAttrSet().Set("aCol", new[] { Dark[0], Dark[1], Dark[2], 0 }).Set("aPbr", new[] { 0.4, 0.7, 0, 0.3 }));
            }
        }

        // ---------- types ----------
        void Ramen(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            var rng = c.Rng;
            double cd = Math.Min(D * 0.45, 2.0);
            DBox(c, (s0 + s1) / 2, 0.5, cd + 0.3, s1 - s0 - 0.6, 1.0, 0.6, Wood, 0.12, 0.5);
            DBox(c, (s0 + s1) / 2, 1.03, cd + 0.25, s1 - s0 - 0.5, 0.06, 0.75, WoodL, 0.15, 0.25);
            for (double s = s0 + 0.6; s < s1 - 0.6; s += 0.65)
            {
                DBox(c, s + rng.Range(-0.1, 0.1), 1.1, cd + 0.12, 0.16, 0.08, 0.16, new[] { 0.9, 0.88, 0.8 }, 0.25, 0.3);
                if (rng.Chance(0.5)) DBox(c, s + 0.25, 1.12, cd + 0.4, 0.06, 0.13, 0.06, new[] { 0.5, 0.1, 0.05 }, 0.1, 0.3);
            }
            for (double s = s0 + 0.55; s < s1 - 0.4; s += 0.62)
            {
                DBox(c, s, 0.36, cd - 0.25, 0.05, 0.7, 0.05, Steel, 0.05, 0.4, 0.8, 0.1);
                DBox(c, s, 0.72, cd - 0.25, 0.34, 0.07, 0.34, new[] { 0.6, 0.06, 0.04 }, 0.12, 0.45);
            }
            for (double s = s0 + 0.8; s < s1 - 0.5; s += 1.25)
            {
                DBox(c, s, ho - 0.2, cd + 0.3, 0.01, 0.5, 0.01, Dark, 0, 0.5);
                DBox(c, s, ho - 0.5, cd + 0.3, 0.28, 0.16, 0.28, new[] { 1.0, 0.72, 0.4 }, 3.5, 0.5);
            }
            for (double s = s0 + 0.3; s < s1 - 0.3; s += 0.16) DBox(c, s, ho - 0.15, D - 0.2, 0.12, 0.35, 0.01, new[] { 0.95, 0.9, 0.75 }, 0.5, 0.8);
            Noren(c, s0 + (s1 - s0) * rng.Range(0.1, 0.45), Math.Min(1.6, (s1 - s0) * 0.45), ho - 0.05);
            if (rng.Chance(0.7)) Awning(c, rng.Pick(new[] { new[] { 0.55, 0.06, 0.05 }, new[] { 0.1, 0.12, 0.2 }, new[] { 0.12, 0.2, 0.1 } }), rng.Chance(0.3));
        }

        void Konbini(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth, W = c.W;
            var rng = c.Rng;
            for (double d = 0.6; d < D - 0.3; d += 1.0) DBox(c, (s0 + s1) / 2, ho + 0.2, d, s1 - s0 - 0.4, 0.04, 0.2, new[] { 0.95, 0.98, 1.0 }, 5.0, 0.5);
            var rows = D > 4 ? new[] { 1.8, 3.2 } : new[] { 2.0 };
            foreach (double d in rows)
            {
                DBox(c, (s0 + s1) / 2, 0.75, d, (s1 - s0) * 0.7, 1.5, 0.6, new[] { 0.9, 0.9, 0.9 }, 0.4, 0.5);
                TQuad(c, s0 + (s1 - s0) * 0.15, s1 - (s1 - s0) * 0.15, 0.02, 1.5, d - 0.31, "shelves", new[] { 1.0, 1.0, 1.0 }, 0.9, new[] { rng.Range(0, 0.5), 0, 0.5, 1 });
            }
            DBox(c, (s0 + s1) / 2, 0.45, 0.35, s1 - s0 - 0.6, 0.9, 0.3, new[] { 0.85, 0.85, 0.85 }, 0.4, 0.5);
            TQuad(c, s0 + 0.3, s1 - 0.3, 0.5, 0.9, 0.19, "shelves", new[] { 1.0, 1.0, 1.0 }, 0.8, new[] { rng.Range(0, 0.5), 0.6, 0.5, 0.4 });
            DBox(c, s1 - 0.8, 0.5, D * 0.5, 0.8, 1.0, 1.6, new[] { 0.85, 0.85, 0.85 }, 0.35, 0.4);
            var stripe = rng.Pick(new[] { new[] { 0.05, 0.55, 0.25 }, new[] { 0.95, 0.45, 0.05 }, new[] { 0.1, 0.3, 0.75 } });
            DBox(c, W / 2, ho + 0.25, -0.27, W - 0.1, 0.18, 0.03, stripe, 1.2, 0.4);
            DBox(c, W / 2, ho + 0.55, -0.27, W - 0.1, 0.12, 0.03, new[] { 0.9, 0.9, 0.9 }, 1.0, 0.4);
        }

        void Izakaya(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            var rng = c.Rng;
            double doorW = Math.Min(1.5, (s1 - s0) * 0.35);
            double doorS = s0 + (s1 - s0 - doorW) * rng.Range(0.2, 0.8);
            var paperCol = new[] { 1.0, 0.72, 0.42 };
            var segs = new[] { new[] { s0, doorS }, new[] { doorS + doorW, s1 } };
            foreach (var seg in segs)
            {
                double a = seg[0], b = seg[1];
                if (b - a < 0.2) continue;
                LQuad(c, new[] { new[] { a, 0.3, 0.12 }, new[] { b, 0.3, 0.12 }, new[] { b, ho, 0.12 }, new[] { a, ho, 0.12 } }, paperCol, 1.4, 0.9);
                for (double s = a + 0.04; s < b; s += 0.09) DBox(c, s, (ho + 0.3) / 2, 0.06, 0.035, ho - 0.3, 0.05, Wood, 0.02, 0.6, 0, 0.3);
                foreach (double y in new[] { 0.9, ho - 0.6 }) DBox(c, (a + b) / 2, y, 0.04, b - a, 0.05, 0.06, Wood, 0, 0.6);
                DBox(c, (a + b) / 2, 0.15, 0.06, b - a, 0.3, 0.1, new[] { 0.15, 0.1, 0.07 }, 0, 0.7, 0, 0.7);
            }
            var g = c.Glass;
            g.Quad(c.P(doorS, 0, 0.08), c.P(doorS + doorW, 0, 0.08), c.P(doorS + doorW, ho, 0.08), c.P(doorS, ho, 0.08),
                new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 }, null);
            DBox(c, doorS, ho / 2, 0.06, 0.08, ho, 0.1, Wood, 0, 0.6);
            DBox(c, doorS + doorW, ho / 2, 0.06, 0.08, ho, 0.1, Wood, 0, 0.6);
            DBox(c, (s0 + s1) / 2, 0.55, D * 0.55, s1 - s0 - 0.4, 1.1, 0.5, Wood, 0.15, 0.5);
            for (double s = s0 + 0.5; s < s1 - 0.4; s += 0.7) DBox(c, s, 0.7, D * 0.55 - 0.5, 0.3, 0.06, 0.3, WoodL, 0.1, 0.5);
            Noren(c, doorS - 0.1, doorW + 0.2, ho - 0.02);
            if (rng.Chance(0.5)) Awning(c, new[] { 0.12, 0.1, 0.09 });
        }

        void Pachinko(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth, W = c.W;
            for (double d = 0.5; d < D; d += 0.9) DBox(c, (s0 + s1) / 2, ho + 0.2, d, s1 - s0 - 0.3, 0.05, 0.3, new[] { 1.0, 0.95, 0.85 }, 6, 0.5);
            double md = Math.Min(2.4, D - 1);
            DBox(c, (s0 + s1) / 2, 0.95, md + 0.3, s1 - s0 - 0.5, 1.9, 0.6, new[] { 0.7, 0.6, 0.3 }, 0.3, 0.3, 0.6);
            TQuad(c, s0 + 0.25, s1 - 0.25, 0.1, 1.9, md - 0.01, "pachinko", new[] { 1.0, 1.0, 1.0 }, 1.5, new[] { 0.0, 0.1, 1, 0.8 });
            for (double s = s0 + 0.5; s < s1 - 0.4; s += 0.55) DBox(c, s, 0.4, md - 0.45, 0.3, 0.08, 0.3, new[] { 0.7, 0.1, 0.1 }, 0.2, 0.4);
            DBox(c, W / 2, ho + 0.1, -0.27, W - 0.1, 0.08, 0.04, new[] { 1.0, 0.75, 0.3 }, 3, 0.3, 0.8);
        }

        void Bar(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            var rng = c.Rng;
            var neon = rng.Pick(new[] { new[] { 1.0, 0.15, 0.6 }, new[] { 0.2, 0.7, 1.0 }, new[] { 0.7, 0.2, 1.0 } });
            DBox(c, (s0 + s1) / 2, ho + 0.15, D - 0.3, s1 - s0 - 0.3, 0.04, 0.04, neon, 8, 0.4);
            DBox(c, (s0 + s1) / 2, 0.55, D * 0.5, s1 - s0 - 0.6, 1.1, 0.5, new[] { 0.12, 0.08, 0.06 }, 0.1, 0.3);
            DBox(c, (s0 + s1) / 2, 1.12, D * 0.5, s1 - s0 - 0.5, 0.04, 0.6, new[] { 0.4, 0.3, 0.25 }, 0.2, 0.15, 0.3);
            for (double s = s0 + 0.5; s < s1 - 0.4; s += 0.6)
            {
                DBox(c, s, 0.4, D * 0.5 - 0.5, 0.04, 0.8, 0.04, Steel, 0.05, 0.3, 0.9);
                DBox(c, s, 0.8, D * 0.5 - 0.5, 0.32, 0.06, 0.32, new[] { 0.1, 0.05, 0.05 }, 0.1, 0.4);
            }
            DBox(c, (s0 + s1) / 2, 1.6, D - 0.25, s1 - s0 - 0.4, 0.03, 0.3, neon, 2.5, 0.4);
        }

        void Boutique(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            var rng = c.Rng;
            for (double d = 0.6; d < D - 0.3; d += 1.2) DBox(c, (s0 + s1) / 2, ho + 0.2, d, s1 - s0 - 0.6, 0.04, 0.16, new[] { 1.0, 0.95, 0.88 }, 4, 0.5);
            for (int i = 0; i < 3; i++)
            {
                double s = s0 + (s1 - s0) * (0.2 + 0.3 * i), d = rng.Range(1.2, D - 1.0);
                DBox(c, s, 0.45, d, 1.0, 0.9, 0.6, new[] { 0.85, 0.82, 0.78 }, 0.35, 0.5);
                DBox(c, s, 0.95, d, 0.8, 0.12, 0.4, rng.Pick(new[] { new[] { 0.6, 0.2, 0.2 }, new[] { 0.2, 0.3, 0.5 }, new[] { 0.9, 0.85, 0.7 } }), 0.3, 0.8);
            }
            foreach (double s in new[] { s0 + 0.6, s1 - 0.6 })
                DBox(c, s, 0.9, 0.6, 0.35, 1.7, 0.25, rng.Pick(new[] { new[] { 0.85, 0.85, 0.85 }, new[] { 0.15, 0.15, 0.15 } }), 0.25, 0.6);
            if (rng.Chance(0.5)) Awning(c, rng.Pick(new[] { new[] { 0.2, 0.2, 0.22 }, new[] { 0.5, 0.1, 0.1 } }), true);
        }

        void Lobby(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, D = c.Depth;
            DBox(c, (s0 + s1) / 2, ho + 0.2, D * 0.5, 1.2, 0.04, 1.2, new[] { 0.95, 0.97, 1.0 }, 5, 0.5);
            DBox(c, s0 + 0.15, 1.3, D * 0.6, 0.2, 1.2, 1.6, new[] { 0.6, 0.6, 0.62 }, 0.3, 0.4, 0.6);
            DBox(c, (s0 + s1) / 2, 1.1, D - 0.05, 1.0, 2.2, 0.05, new[] { 0.7, 0.7, 0.72 }, 0.3, 0.3, 0.8);
        }

        void Shutter(ShopCtx c)
        {
            double s0 = c.S0, s1 = c.S1, ho = c.Ho, W = c.W;
            var rng = c.Rng;
            var sub = new[] { rng.Range(0, 0.4), 0, 0.6, 1 };
            double open = rng.Chance(0.2) ? rng.Range(0.25, 0.5) : 0;
            TQuad(c, s0, s1, open, ho, 0.12, "shutter", new[] { 0.85, 0.85, 0.85 }, 0.0, sub, 0.5);
            DBox(c, W / 2, ho + 0.15, -0.0, s1 - s0, 0.3, 0.3, new[] { 0.45, 0.45, 0.43 }, 0, 0.5, 0.4, 0.8);
            DBox(c, s0 + 0.02, ho / 2, 0.08, 0.05, ho, 0.08, new[] { 0.35, 0.35, 0.34 }, 0, 0.5, 0.6, 0.6);
            DBox(c, s1 - 0.02, ho / 2, 0.08, 0.05, ho, 0.08, new[] { 0.35, 0.35, 0.34 }, 0, 0.5, 0.6, 0.6);
            if (open > 0)
                LQuad(c, new[] { new[] { s0, 0.0, 0.5 }, new[] { s1, 0.0, 0.5 }, new[] { s1, open, 0.5 }, new[] { s0, open, 0.5 } }, new[] { 1.0, 0.85, 0.6 }, 1.5, 0.8);
            LQuad(c, new[] { new[] { s0, 0.0, 0.6 }, new[] { s1, 0.0, 0.6 }, new[] { s1, ho, 0.6 }, new[] { s0, ho, 0.6 } }, new[] { 0.02, 0.02, 0.02 }, 0);
        }
    }
}
