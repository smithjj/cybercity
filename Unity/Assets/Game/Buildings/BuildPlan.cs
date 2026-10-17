// Port of src/systems/buildings/plan.js — district plan: LAYOUT -> deterministic building lots.
// Draw-order fidelity with the JS mulberry32 stream is critical (oracle-compared against
// unity/tools/dump-buildings.mjs). All math in sim space (doubles).
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeonKaido
{
    /// <summary>mulberry32 + the helpers from src/systems/buildings/rng.js, in stream order.
    /// CLASS (not struct) on purpose: JS passes one rng object around; a struct copy would fork the stream.</summary>
    public class BuildingsRng
    {
        Mulberry32 _r;
        public BuildingsRng(uint seed) => _r = new Mulberry32(seed);
        public double F() => _r.Next();
        // two-step mul-then-add with a noinline boundary: prevents the JIT from contracting a + (b-a)*f
        // into a single-rounding FMA (JS never fuses; bit-exactness requires the same two roundings).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public double Range(double a, double b) { double m = (b - a) * F(); return a + m; }
        public int Int(int a, int b) => (int)Math.Floor(a + (b - a + 1) * F());
        /// JS: f() &lt; p — always draws.
        public bool Chance(double p) => F() < p;
        public T Pick<T>(IReadOnlyList<T> arr) => arr[(int)(F() * arr.Count) % arr.Count];
        /// JS weighted(): r=f()*s; walk pairs subtracting w; first r&lt;=0 wins; fallback last.
        public T Weighted<T>(IReadOnlyList<(T val, double w)> pairs)
        {
            double s = 0; for (int i = 0; i < pairs.Count; i++) s += pairs[i].w;
            double r = F() * s;
            for (int i = 0; i < pairs.Count; i++) { r -= pairs[i].w; if (r <= 0) return pairs[i].val; }
            return pairs[pairs.Count - 1].val;
        }
    }

    public class BFront
    {
        public string Side, District;
        public bool Shop, Lane;
        public double? BaseY; // null = JS undefined (the overlook lot's pushed front)
        public string ShopType; // filled by ShopBuilder later
    }

    public class BSetback { public int AtFloor; public double Inset; }

    public class BLot
    {
        public int Id;
        public double X0, X1, Z0, Z1, BaseY, H, FloorH, GroundH;
        public int Floors;
        public string Style, Detail;
        public List<BFront> Fronts = new List<BFront>();
        public bool Tower;
        public double Seed, RoofKind;
        public BSetback Setback;
        public double CoverNx, CoverPx, CoverNz, CoverPz;
        public double? OverlookDeckY; // overlook building only
        public double WallLayer;
        public List<BVol> Volumes;
        public double Overhang;
        public string OverhangSide;
        public Dictionary<string, BSide> Sides;
        public double Cx => (X0 + X1) / 2;
        public double Cz => (Z0 + Z1) / 2;
    }

    public class BStreet
    {
        public string Side, District;
        public bool Shop, Lane;
        public double[] Depth, Width;
        public int[] Floors;
        public string Detail;
        public double BaseY;
        public double? GroundH;
        public string Style;
    }

    public class BlockCfg
    {
        public int[] TowerFloors;
        public string TowerDetail;
        public double[] TowerSize;
    }

    class Rct
    {
        public double X0, X1, Z0, Z1;
        public Rct(double x0, double x1, double z0, double z1) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; }
        public Rct Clone() => new Rct(X0, X1, Z0, Z1);
        public double Cx => (X0 + X1) / 2;
        public double Cz => (Z0 + Z1) / 2;
    }

    public static class BuildPlan
    {
        const double EPS = 0.05;

        public static readonly (string, double)[] FrontStyles =
            { ("tile", 3.0), ("whitetile", 2.5), ("concrete", 2.0), ("render", 2.5), ("brick", 1.5), ("metal", 1.2), ("ribbon", 1.2), ("corrugated", 0.8), ("glass", 0.5) };
        public static readonly (string, double)[] TowerStyles =
            { ("glass", 3.0), ("metal", 2.0), ("ribbon", 2.0), ("concrete", 1.5), ("whitetile", 1.5), ("tile", 1.0) };

        class Opts
        {
            public int? Floors;
            public double? FloorH, GroundH;
            public double BaseY;
            public string Style, Detail;
            public bool Tower;
        }

        class Seg { public double Z0, Z1; public string NKind, SKind; }

        static readonly List<BLot> _lots = new List<BLot>();
        static BuildingsRng _rng;
        static int _nextId;

        static double Hyp(double a, double b) => GroundGeo.Hypot2(a, b);

        /// Cached lots from the last Run() (weather heightfield bake reads these).
        public static List<BLot> Lots => _lots;

        public static List<BLot> Run()
        {
            _rng = new BuildingsRng(0xC17E);
            _lots.Clear();
            _nextId = 0;
            var front = Layout.BlocksFrontageOffset;               // 10
            var crossHalf = Layout.CrossRoadHalfWidth + Layout.CrossSidewalkWidth; // 8
            var alleyHalf = Layout.AlleyWidth / 2;
            var railHalf = Layout.RailWidth / 2 + 1.5;
            var expHalf = Layout.ExpresswayWidth / 2 + 2;
            var riverBankW = Layout.PromenadeXMin - 4;             // 124
            var riverBankE = Layout.RiverXCenter + Layout.RiverHalfWidth + 6; // 216
            var ov = Layout.OverlookPosition;

            // --------- height caps ---------
            var ovLook = Layout.OverlookLookAt;
            double ovl = Hyp(ovLook[0] - ov[0], ovLook[2] - ov[2]);
            double ovDirX = (ovLook[0] - ov[0]) / ovl, ovDirZ = (ovLook[2] - ov[2]) / ovl;

            double HeightCap(double x0, double x1, double z0, double z1)
            {
                double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
                double cap = 999;
                double dx = cx - ov[0], dz = cz - ov[2];
                double d = Hyp(dx, dz);
                if (d > 8 && d < 260)
                {
                    double c = (dx * ovDirX + dz * ovDirZ) / d;
                    double side = dx * -ovDirZ + dz * ovDirX;
                    if (c > Math.Cos(0.62) && !(side < -0 && c < Math.Cos(0.42))) cap = Math.Min(cap, 12 + d * 0.035);
                }
                if (cz > 45) cap = Math.Min(cap, 14 + Math.Max(0, cz - 45) * 0.12);
                if (cz > -70 && cz < 45 && cx < -6 && cx > -140) cap = Math.Min(cap, 30);
                if (z0 < Layout.RailZ + (railHalf + 2) && z1 > Layout.RailZ - (railHalf + 2)) cap = Math.Min(cap, Layout.RailHeight - 3);
                if (z0 < Layout.ExpresswayZ + (expHalf + 2) && z1 > Layout.ExpresswayZ - (expHalf + 2)) cap = Math.Min(cap, Layout.ExpresswayHeight - 4);
                return cap;
            }

            string PickStyle((string, double)[] table, double w, int floors)
            {
                string s = _rng.Weighted(table);
                if (s == "corrugated" && floors > 5) s = "render";
                if (s == "glass" && (w < 9 || floors < 6)) s = "metal";
                return s;
            }

            BLot AddLot(Rct r, Opts o)
            {
                double w = Math.Min(r.X1 - r.X0, r.Z1 - r.Z0);
                if (w < 3.5) return null;
                double floorH = o.FloorH ?? _rng.Range(3.0, 3.45);
                int floors = o.Floors ?? 0;
                double groundH = o.GroundH ?? _rng.Range(3.9, 4.6);
                double fh2 = (floors - 1) * floorH;
                double h = groundH + fh2;
                double cap = HeightCap(r.X0, r.X1, r.Z0, r.Z1);
                if (h > cap)
                {
                    floors = Math.Max(1, (int)Math.Floor((cap - groundH) / floorH) + 1);
                    double fh3 = (floors - 1) * floorH;
                    h = groundH + fh3;
                }
                string style = o.Style ?? PickStyle(o.Tower ? TowerStyles : FrontStyles, w, floors);
                var lot = new BLot
                {
                    Id = _nextId++, X0 = r.X0, X1 = r.X1, Z0 = r.Z0, Z1 = r.Z1, BaseY = o.BaseY,
                    Floors = floors, FloorH = floorH, GroundH = groundH, H = h, Style = style,
                    Detail = o.Detail ?? "lo", Tower = o.Tower,
                    Seed = _rng.F(), Setback = null, RoofKind = _rng.F(),
                };
                if (floors >= 9 && _rng.Chance(o.Tower ? 0.55 : 0.35))
                {
                    int sf = (int)Math.Floor(floors * _rng.Range(0.45, 0.75));
                    lot.Setback = new BSetback { AtFloor = sf, Inset = _rng.Range(1.5, 3.5) };
                }
                _lots.Add(lot);
                return lot;
            }

            void Bsp(Rct r, double[] size, Action<Rct> cb)
            {
                double w = r.X1 - r.X0, d = r.Z1 - r.Z0;
                if (w < 4 || d < 4) return;
                if (w <= size[1] && d <= size[1] && (_rng.F() < 0.4 || (w < size[0] * 2 && d < size[0] * 2))) { cb(r); return; }
                if (w >= d)
                {
                    if (w < size[0] * 2) { cb(r); return; }
                    double s = r.X0 + w * _rng.Range(0.35, 0.65);
                    Bsp(new Rct(r.X0, s, r.Z0, r.Z1), size, cb);
                    Bsp(new Rct(s, r.X1, r.Z0, r.Z1), size, cb);
                }
                else
                {
                    if (d < size[0] * 2) { cb(r); return; }
                    double s = r.Z0 + d * _rng.Range(0.35, 0.65);
                    Bsp(new Rct(r.X0, r.X1, r.Z0, s), size, cb);
                    Bsp(new Rct(r.X0, r.X1, s, r.Z1), size, cb);
                }
            }

            void CarveBlock(Rct rect, List<BStreet> streetsIn, BlockCfg cfg = null)
            {
                cfg ??= new BlockCfg();
                var seen = new HashSet<string>();
                var streets = streetsIn.Where(s => seen.Add(s.Side)).ToList();
                Rct rem = rect.Clone();
                var made = new List<BLot>();
                foreach (var st in streets)
                {
                    bool xSide = st.Side == "nx" || st.Side == "px";
                    double len = xSide ? rem.Z1 - rem.Z0 : rem.X1 - rem.X0;
                    double maxD = xSide ? rem.X1 - rem.X0 : rem.Z1 - rem.Z0;
                    if (len < 4 || maxD < 4) continue;
                    double d = Math.Min(_rng.Range(st.Depth[0], st.Depth[1]), maxD);
                    if (maxD - d < 8) d = maxD;
                    double t = 0;
                    var widths = new List<double>();
                    while (t < len - 0.01)
                    {
                        double w = _rng.Range(st.Width[0], st.Width[1]);
                        if (len - t - w < st.Width[0]) w = len - t;
                        widths.Add(w); t += w;
                    }
                    t = 0;
                    foreach (double w in widths)
                    {
                        double dj = d >= maxD ? d : Math.Max(4, Math.Min(maxD, d + _rng.Range(-2.5, 2.5)));
                        Rct r;
                        if (st.Side == "nx") r = new Rct(rem.X0, rem.X0 + dj, rem.Z0 + t, rem.Z0 + t + w);
                        else if (st.Side == "px") r = new Rct(rem.X1 - dj, rem.X1, rem.Z0 + t, rem.Z0 + t + w);
                        else if (st.Side == "nz") r = new Rct(rem.X0 + t, rem.X0 + t + w, rem.Z0, rem.Z0 + dj);
                        else r = new Rct(rem.X0 + t, rem.X0 + t + w, rem.Z1 - dj, rem.Z1);
                        t += w;
                        var lot = AddLot(r, new Opts
                        {
                            Floors = _rng.Int(st.Floors[0], st.Floors[1]), Detail = st.Detail, BaseY = st.BaseY,
                            GroundH = st.GroundH, Style = st.Style,
                        });
                        if (lot != null) made.Add(lot);
                    }
                    double used = d >= maxD ? d : d + 2.5;
                    if (st.Side == "nx") rem.X0 += used; else if (st.Side == "px") rem.X1 -= used;
                    else if (st.Side == "nz") rem.Z0 += used; else rem.Z1 -= used;
                    if (rem.X1 - rem.X0 < 4 || rem.Z1 - rem.Z0 < 4) { rem = null; break; }
                }
                if (rem != null)
                {
                    Bsp(rem, cfg.TowerSize ?? new[] { 12.0, 26.0 }, r =>
                    {
                        var tf = cfg.TowerFloors ?? new[] { 8, 20 };
                        var lot = AddLot(r, new Opts { Floors = _rng.Int(tf[0], tf[1]), Detail = cfg.TowerDetail ?? "lo", Tower = true });
                        if (lot != null) made.Add(lot);
                    });
                }
                foreach (var lot in made)
                {
                    foreach (var st in streets)
                    {
                        bool touch = st.Side == "nx" ? Math.Abs(lot.X0 - rect.X0) < EPS
                            : st.Side == "px" ? Math.Abs(lot.X1 - rect.X1) < EPS
                            : st.Side == "nz" ? Math.Abs(lot.Z0 - rect.Z0) < EPS
                            : Math.Abs(lot.Z1 - rect.Z1) < EPS;
                        if (touch) lot.Fronts.Add(new BFront { Side = st.Side, District = st.District, Shop = st.Shop, Lane = st.Lane, BaseY = st.BaseY });
                    }
                    if (lot.Fronts.Count > 0 && lot.Tower) lot.Tower = false;
                }
            }

            // ---------------- Z segmentation along the avenue ----------------
            var zCuts = new List<(double, double, string)>();
            foreach (double z in Layout.CrossZs) zCuts.Add((z - crossHalf, z + crossHalf, "cross"));
            zCuts.Add((Layout.RailZ - railHalf, Layout.RailZ + railHalf, "rail"));
            zCuts.Add((Layout.ExpresswayZ - expHalf, Layout.ExpresswayZ + expHalf, "exp"));

            List<Seg> Segments(double zA, double zB, List<(double, double, string)> extraCuts = null)
            {
                var cuts = new List<(double c0, double c1, string k)>(zCuts);
                if (extraCuts != null) cuts.AddRange(extraCuts);
                cuts.Sort((a, b) => b.c1.CompareTo(a.c1));
                var segs = new List<Seg>();
                double cur = zA; string curKind = "end";
                foreach (var c in cuts)
                {
                    if (c.c1 > cur || c.c0 < zB) continue;
                    if (cur - c.c1 > 4) segs.Add(new Seg { Z0 = c.c1, Z1 = cur, NKind = c.k, SKind = curKind });
                    cur = c.c0; curKind = c.k;
                }
                if (cur - zB > 4) segs.Add(new Seg { Z0 = zB, Z1 = cur, NKind = "end", SKind = curKind });
                return segs;
            }

            bool IsStreet(string k) => k == "cross" || k == "alleyS" || k == "alleyN" || k == "lane" || k == "end";
            string KindDistrict(string k) => (k == "alleyS" || k == "alleyN") ? "alley" : "cross";

            // ---------------- Avenue blocks (both sides) ----------------
            double backLaneX = 38, backLaneW = 5;
            foreach (int sideSign in new[] { 1, -1 })
            {
                var extra = sideSign < 0
                    ? new List<(double, double, string)> { (Layout.AlleyZ - alleyHalf, Layout.AlleyZ + alleyHalf, "alley") }
                    : null;
                foreach (var seg in Segments(Layout.AvenueZStart, Layout.AvenueZEnd, extra))
                {
                    double x0 = sideSign > 0 ? front : -backLaneX, x1 = sideSign > 0 ? backLaneX : -front;
                    var rect = new Rct(x0, x1, seg.Z0, seg.Z1);
                    bool isAlleySeg = sideSign < 0 && (seg.NKind == "alley" || seg.SKind == "alley");
                    var streets = new List<BStreet>();
                    string avSide = sideSign > 0 ? "nx" : "px";
                    streets.Add(new BStreet { Side = avSide, District = "avenue", Shop = true, Depth = new[] { 11.0, 16.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 4, 13 }, Detail = "hi" });
                    string kN = seg.NKind == "alley" ? "alleyS" : seg.NKind;
                    string kS = seg.SKind == "alley" ? "alleyN" : seg.SKind;
                    if (IsStreet(kN) && kN != "end") streets.Add(new BStreet { Side = "nz", District = KindDistrict(kN), Shop = true, Depth = new[] { 8.0, 14.0 }, Width = isAlleySeg ? new[] { 4.0, 7.0 } : new[] { 6.0, 12.0 }, Floors = isAlleySeg ? new[] { 2, 6 } : new[] { 3, 10 }, Detail = "hi" });
                    if (IsStreet(kS) && kS != "end") streets.Add(new BStreet { Side = "pz", District = KindDistrict(kS), Shop = true, Depth = new[] { 8.0, 14.0 }, Width = isAlleySeg ? new[] { 4.0, 7.0 } : new[] { 6.0, 12.0 }, Floors = isAlleySeg ? new[] { 2, 6 } : new[] { 3, 10 }, Detail = "hi" });
                    if (seg.NKind == "rail" || seg.NKind == "exp") streets.Add(new BStreet { Side = "nz", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 12.0 }, Width = new[] { 6.0, 12.0 }, Floors = new[] { 2, 4 }, Detail = "mid" });
                    if (seg.SKind == "rail" || seg.SKind == "exp") streets.Add(new BStreet { Side = "pz", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 12.0 }, Width = new[] { 6.0, 12.0 }, Floors = new[] { 2, 4 }, Detail = "mid" });
                    streets.Add(new BStreet { Side = sideSign > 0 ? "px" : "nx", District = "cross", Shop = false, Lane = true, Depth = new[] { 6.0, 9.0 }, Width = new[] { 7.0, 14.0 }, Floors = new[] { 3, 8 }, Detail = "mid" });
                    CarveBlock(rect, streets, new BlockCfg { TowerFloors = new[] { 10, 22 }, TowerDetail = "mid", TowerSize = new[] { 12.0, 22.0 } });
                }
            }

            // ---------------- Alley district (west, beyond the back lane) ----------------
            double alleyEndX = Layout.AlleyXEnd - 7; // -97
            {
                double zs = Layout.AlleyZ + alleyHalf, zn = Layout.AlleyZ - alleyHalf;
                var south = new Rct(Layout.AlleyXEnd, -backLaneX - backLaneW, zs, Layout.CrossZs[0] - crossHalf);
                var north = new Rct(Layout.AlleyXEnd, -backLaneX - backLaneW, Layout.RailZ + railHalf, zn);
                BStreet St(string side) => new BStreet { Side = side, District = "alley", Shop = true, Depth = new[] { 7.0, 11.0 }, Width = new[] { 3.6, 6.5 }, Floors = new[] { 2, 5 }, Detail = "hi" };
                CarveBlock(south, new List<BStreet>
                {
                    St("nz"),
                    new BStreet { Side = "pz", District = "cross", Shop = true, Depth = new[] { 8.0, 12.0 }, Width = new[] { 6.0, 11.0 }, Floors = new[] { 3, 9 }, Detail = "hi" },
                    new BStreet { Side = "px", District = "cross", Shop = false, Lane = true, Depth = new[] { 6.0, 9.0 }, Width = new[] { 6.0, 11.0 }, Floors = new[] { 3, 6 }, Detail = "mid" },
                }, new BlockCfg { TowerFloors = new[] { 6, 14 }, TowerDetail = "mid", TowerSize = new[] { 10.0, 18.0 } });
                CarveBlock(north, new List<BStreet>
                {
                    St("pz"),
                    new BStreet { Side = "nz", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 10.0 }, Width = new[] { 6.0, 11.0 }, Floors = new[] { 2, 4 }, Detail = "mid" },
                    new BStreet { Side = "px", District = "cross", Shop = false, Lane = true, Depth = new[] { 6.0, 9.0 }, Width = new[] { 6.0, 11.0 }, Floors = new[] { 3, 6 }, Detail = "mid" },
                }, new BlockCfg { TowerFloors = new[] { 6, 14 }, TowerDetail = "mid", TowerSize = new[] { 10.0, 18.0 } });
                var endLot = AddLot(new Rct(alleyEndX, Layout.AlleyXEnd, north.Z1 - 8, south.Z0 + 8),
                    new Opts { Floors = 6, Detail = "hi", Style = "render", GroundH = 4.2 });
                if (endLot != null) endLot.Fronts.Add(new BFront { Side = "px", District = "alley", Shop = true, BaseY = Layout.AlleyStairsRise });
            }
            foreach (var lot in _lots)
                foreach (var f in lot.Fronts)
                {
                    if (f.District != "alley") continue;
                    if (lot.Cx < Layout.AlleyStairsXEnd) f.BaseY = Layout.AlleyStairsRise;
                    else if (lot.Cx < Layout.AlleyStairsXStart) f.BaseY = -1;
                }

            // ---------------- Back regions ----------------
            void FillRegion(Rct R, double[] laneXs, List<(double, double)> laneZs, double laneW, Func<Rct, (List<BStreet> streets, BlockCfg cfg)?> cfgFn)
            {
                var xs = new List<double> { R.X0 };
                foreach (double x in laneXs) { xs.Add(x - laneW / 2); xs.Add(x + laneW / 2); }
                xs.Add(R.X1);
                xs.Sort();
                var zsAll = new List<double> { R.Z0 };
                foreach (var g in laneZs) { zsAll.Add(g.Item1); zsAll.Add(g.Item2); }
                zsAll.Add(R.Z1);
                zsAll.Sort();
                for (int i = 0; i < xs.Count - 1; i += 2)
                    for (int j = 0; j < zsAll.Count - 1; j += 2)
                    {
                        var r = new Rct(xs[i], xs[i + 1], zsAll[j], zsAll[j + 1]);
                        if (r.X1 - r.X0 < 6 || r.Z1 - r.Z0 < 6) continue;
                        var c = cfgFn(r);
                        if (c.HasValue) CarveBlock(r, c.Value.streets, c.Value.cfg);
                    }
            }

            var crossZ = Layout.CrossZs;
            (double, double) ZGap(double zc, double half) => (zc - half, zc + half);
            bool NearCross(double z, string side) => crossZ.Any(cz => Math.Abs(side == "nz" ? z - (cz + crossHalf) : (cz - crossHalf) - z) < 0.5);

            (List<BStreet> streets, BlockCfg cfg) BackCfg(Rct r, double detailNear, Func<Rct, double> distFn)
            {
                double dist = distFn(r);
                string detail = dist < detailNear ? "mid" : "lo";
                var streets = new List<BStreet>();
                bool crossShop = dist < 90;
                bool wN = NearCross(r.Z0, "nz"), wS = NearCross(r.Z1, "pz");
                if (wN) streets.Add(new BStreet { Side = "nz", District = "cross", Shop = crossShop, Depth = new[] { 9.0, 14.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 3, 10 }, Detail = crossShop ? "hi" : detail });
                if (wS) streets.Add(new BStreet { Side = "pz", District = "cross", Shop = crossShop, Depth = new[] { 9.0, 14.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 3, 10 }, Detail = crossShop ? "hi" : detail });
                if (!wN) streets.Add(new BStreet { Side = "nz", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 13.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 2, 8 }, Detail = detail });
                if (!wS) streets.Add(new BStreet { Side = "pz", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 13.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 2, 8 }, Detail = detail });
                streets.Add(new BStreet { Side = "nx", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 13.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 2, 9 }, Detail = detail });
                streets.Add(new BStreet { Side = "px", District = "cross", Shop = false, Lane = true, Depth = new[] { 8.0, 13.0 }, Width = new[] { 6.0, 13.0 }, Floors = new[] { 2, 9 }, Detail = detail });
                return (streets, new BlockCfg { TowerFloors = dist < 120 ? new[] { 6, 18 } : new[] { 8, 26 }, TowerDetail = detail, TowerSize = new[] { 12.0, 24.0 } });
            }

            var backZCuts = new List<(double, double)>();
            foreach (double z in crossZ) backZCuts.Add(ZGap(z, crossHalf));
            backZCuts.Add(ZGap(Layout.RailZ, railHalf));
            backZCuts.Add(ZGap(Layout.ExpresswayZ, expHalf));
            backZCuts.Add(ZGap(Layout.Bridge0Z, 6));
            backZCuts.Add(ZGap(-5, 3)); backZCuts.Add(ZGap(-110, 3)); backZCuts.Add(ZGap(-215, 3));
            backZCuts.Add(ZGap(-355, 3)); backZCuts.Add(ZGap(-395, 3)); backZCuts.Add(ZGap(25, 3));

            // East back region: x from back lane to the riverbank
            {
                var R = new Rct(backLaneX + backLaneW, riverBankW, Layout.AvenueZEnd, Layout.AvenueZStart);
                var gaps = backZCuts.Where(g => g.Item1 > R.Z0 && g.Item2 < R.Z1).Select(g => (g.Item1, g.Item2)).ToList();
                FillRegion(R, new[] { 84.0 }, gaps, 6, r =>
                {
                    var c = BackCfg(r, 160, q => Math.Abs((q.X0 + q.X1) / 2));
                    if (Math.Abs(r.X1 - riverBankW) < EPS)
                        c.streets.Insert(0, new BStreet { Side = "px", District = "river", Shop = false, Depth = new[] { 9.0, 14.0 }, Width = new[] { 7.0, 14.0 }, Floors = new[] { 4, 12 }, Detail = "mid" });
                    return (c.streets, c.cfg);
                });
            }
            // West back region (excluding alley district)
            {
                var R = new Rct(-200, -backLaneX - backLaneW, Layout.AvenueZEnd, Layout.AvenueZStart);
                var gaps = backZCuts.Where(g => g.Item1 > R.Z0 && g.Item2 < R.Z1).Select(g => (g.Item1, g.Item2)).ToList();
                FillRegion(R, new[] { -96.0, -150.0 }, gaps, 6, r =>
                {
                    double cz = r.Cz, cx = r.Cx;
                    if (cz > Layout.RailZ && cz < crossZ[0] && cx > alleyEndX - 1) return null;
                    var c = BackCfg(r, 140, q => Math.Abs((q.X0 + q.X1) / 2));
                    return (c.streets, c.cfg);
                });
            }
            // South fill beyond the avenue start and north beyond the avenue end
            {
                var Rs = new Rct(-200, riverBankW, Layout.AvenueZStart + 6, 170);
                FillRegion(Rs, new[] { -96.0, -40.0, 40.0, 84.0 }, new List<(double, double)> { ZGap(110, 3) }, 6, r => { var c = BackCfg(r, 0, q => 999); return (c.streets, c.cfg); });
                var Rn = new Rct(-200, riverBankW, -560, Layout.AvenueZEnd - 6);
                FillRegion(Rn, new[] { -96.0, -40.0, 0.0, 40.0, 84.0 }, new List<(double, double)> { ZGap(-470, 3) }, 6, r => { var c = BackCfg(r, 0, q => 999); return (c.streets, c.cfg); });
            }
            // East bank of the river (far, low detail) facing the river
            {
                var R = new Rct(riverBankE, 400, -700, 170);
                FillRegion(R, new[] { 270.0, 330.0 },
                    new List<(double, double)> { ZGap(-480, 4), ZGap(-330, 4), ZGap(-200, 4), ZGap(Layout.RailZ, railHalf + 2), ZGap(Layout.ExpresswayZ, Layout.ExpresswayWidth / 2 + 2), ZGap(-60, 4), ZGap(60, 4) }, 8, r =>
                    {
                        var c = BackCfg(r, 0, q => 999);
                        c.cfg.TowerFloors = new[] { 10, 34 };
                        if (Math.Abs(r.X0 - riverBankE) < EPS)
                            c.streets.Insert(0, new BStreet { Side = "nx", District = "river", Shop = false, Depth = new[] { 10.0, 16.0 }, Width = new[] { 8.0, 16.0 }, Floors = new[] { 5, 16 }, Detail = "lo" });
                        return (c.streets, c.cfg);
                    });
            }

            // ---------------- Overlook building ----------------
            var ovFoot = new Rct(ov[0] - 8, ov[0] + 9, ov[2] + crossHalf, ov[2] + crossHalf + 22);
            for (int i = _lots.Count - 1; i >= 0; i--)
            {
                var l = _lots[i];
                if (l.X0 < ovFoot.X1 && l.X1 > ovFoot.X0 && l.Z0 < ovFoot.Z1 && l.Z1 > ovFoot.Z0) _lots.RemoveAt(i);
            }
            double deckY = ov[1] - 1.6;
            var ovLot = AddLot(ovFoot, new Opts { Floors = 7, FloorH = (deckY - 4.4) / 6, GroundH = 4.4, Style = "tile", Detail = "hi" });
            ovLot.Fronts.Add(new BFront { Side = "nz", District = "cross", Shop = true });
            ovLot.OverlookDeckY = deckY;
            ovLot.Setback = null;

            // ---------------- Side exposure cover ----------------
            const double G = 32;
            var grid = new Dictionary<long, List<BLot>>();
            long Key(int i, int j) => i * 100003L + j;
            foreach (var l in _lots)
                for (int i = (int)Math.Floor(l.X0 / G); i <= (int)Math.Floor(l.X1 / G); i++)
                    for (int j = (int)Math.Floor(l.Z0 / G); j <= (int)Math.Floor(l.Z1 / G); j++)
                    {
                        long k = Key(i, j);
                        if (!grid.TryGetValue(k, out var list)) { list = new List<BLot>(); grid[k] = list; }
                        list.Add(l);
                    }
            foreach (var l in _lots)
            {
                var near = new HashSet<BLot>();
                for (int i = (int)Math.Floor((l.X0 - 1) / G); i <= (int)Math.Floor((l.X1 + 1) / G); i++)
                    for (int j = (int)Math.Floor((l.Z0 - 1) / G); j <= (int)Math.Floor((l.Z1 + 1) / G); j++)
                        if (grid.TryGetValue(Key(i, j), out var list))
                            foreach (var o in list) if (o != l) near.Add(o);
                double covNx = 0, covPx = 0, covNz = 0, covPz = 0;
                foreach (var side in new[] { "nx", "px", "nz", "pz" })
                {
                    double cov = 0;
                    double len = (side == "nx" || side == "px") ? l.Z1 - l.Z0 : l.X1 - l.X0;
                    foreach (var o in near)
                    {
                        bool touch; double a0, a1;
                        if (side == "nx") { touch = Math.Abs(o.X1 - l.X0) < 1.0; a0 = Math.Max(o.Z0, l.Z0); a1 = Math.Min(o.Z1, l.Z1); }
                        else if (side == "px") { touch = Math.Abs(o.X0 - l.X1) < 1.0; a0 = Math.Max(o.Z0, l.Z0); a1 = Math.Min(o.Z1, l.Z1); }
                        else if (side == "nz") { touch = Math.Abs(o.Z1 - l.Z0) < 1.0; a0 = Math.Max(o.X0, l.X0); a1 = Math.Min(o.X1, l.X1); }
                        else { touch = Math.Abs(o.Z0 - l.Z1) < 1.0; a0 = Math.Max(o.X0, l.X0); a1 = Math.Min(o.X1, l.X1); }
                        if (touch && a1 > a0 && o.H > l.H * 0.6) cov += a1 - a0;
                    }
                    double v = Math.Min(1, cov / Math.Max(len, 0.1));
                    if (side == "nx") covNx = v; else if (side == "px") covPx = v; else if (side == "nz") covNz = v; else covPz = v;
                }
                l.CoverNx = covNx; l.CoverPx = covPx; l.CoverNz = covNz; l.CoverPz = covPz;
            }
            return _lots;
        }
    }
}
