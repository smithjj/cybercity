using System.Collections.Generic;

namespace NeonKaido
{
    /// Exact port of src/systems/ground/plan.js. SIM space (x, z), doubles for bit-exactness.
    public static class GroundPlan
    {
        // --- G constants (derived from LAYOUT) ---
        public const double CurbH = Layout.AvenueCurbHeight;   // 0.15 sidewalk top
        public const double CurbW = 0.2;                       // granite curb top width
        public const double CornerR = 3.0;                     // rounded curb radius at intersections
        public const double AvHalf = Layout.AvenueRoadHalfWidth; // 6
        public const double AvSide = Layout.AvenueSidewalkWidth + 0.6; // 4.6 (side strip only; xi = avHalf + avSide)
        public const double CrHalf = Layout.CrossRoadHalfWidth; // 5
        public const double CrSide = Layout.CrossSidewalkWidth + 0.6; // 3.6
        public static readonly double[] CrossZ = { Layout.CrossZs[0], Layout.CrossZs[1], Layout.CrossZs[2] };
        public const double ZS = Layout.AvenueZStart + 6;      // 66
        public const double ZN = Layout.AvenueZEnd - 6;        // -426
        public const double XW = Layout.CrossXStart;           // -200
        public const double XE = Layout.PromenadeXMin;         // 128: cross streets end at the promenade
        // alley
        public const double AlleyZ = Layout.AlleyZ;
        public const double AlleyHalf = Layout.AlleyWidth / 2;
        public const double AlleyX0 = Layout.AlleyXEnd - 6;    // -96
        public const double AlleyX1 = -(Layout.AvenueRoadHalfWidth + Layout.AvenueSidewalkWidth + 0.6); // -10.6
        public const double StairsXStart = Layout.AlleyStairsXStart;
        public const double StairsXEnd = Layout.AlleyStairsXEnd;
        public const double StairsRise = Layout.AlleyStairsRise;
        // promenade
        public const double PromX0 = Layout.PromenadeXMin;
        public const double PromX1 = Layout.PromenadeXMax;
        public const double PromZ0 = Layout.PromenadeZEnd;     // -500
        public const double PromZ1 = Layout.PromenadeZStart;   // 40
        public const double PromY = Layout.PromenadeY;
        // river
        public const double RiverX0 = Layout.RiverXCenter - Layout.RiverHalfWidth;
        public const double RiverX1 = Layout.RiverXCenter + Layout.RiverHalfWidth;
        public const double RiverWater = Layout.RiverWaterLevel;
        // world bounds
        public const double WorldX0 = -460;
        public const double WorldX1 = 460;
        public const double WorldZ0 = -780;
        public const double WorldZ1 = 240;

        // --- region tests ---

        static double NearestCross(double z)
        {
            double b = CrossZ[0];
            foreach (double c in CrossZ)
                if (System.Math.Abs(z - c) < System.Math.Abs(z - b)) b = c;
            return b;
        }

        // Signed distance to nearest curb line, positive inside roads (rounded corners).
        public static double CurbDist(double x, double z)
        {
            double zc = NearestCross(z);
            double qx = System.Math.Abs(x) - AvHalf;
            double qz = System.Math.Abs(z - zc) - CrHalf;
            double r = CornerR;
            double wx = r - qx, wz = r - qz;
            double outside = System.Math.Sqrt(
                (System.Math.Max(wx, 0)) * (System.Math.Max(wx, 0)) +
                (System.Math.Max(wz, 0)) * (System.Math.Max(wz, 0))) + System.Math.Min(System.Math.Max(wx, wz), 0);
            double d = outside - r;
            if (x < XW || x > XE) d = AvHalf - System.Math.Abs(x);
            return d;
        }

        public static bool InSidewalk(double x, double z)
        {
            if (z > ZS || z < ZN) return false;
            if (CurbDist(x, z) > 0) return false;
            double ax = System.Math.Abs(x);
            double zc = NearestCross(z);
            double dz = System.Math.Abs(z - zc);
            if (ax <= AvHalf + AvSide) return true;
            if (x < XW || x > XE) return false;
            return dz <= CrHalf + CrSide;
        }

        public static bool InAlleyLower(double x, double z)
        {
            return System.Math.Abs(z - AlleyZ) <= AlleyHalf && x <= AlleyX1 && x >= StairsXStart;
        }

        public static double HeightAt(double x, double z)
        {
            if (System.Math.Abs(z - AlleyZ) <= AlleyHalf && x <= AlleyX1 && x >= AlleyX0)
            {
                if (x >= StairsXStart) return 0;
                if (x <= StairsXEnd) return StairsRise;
                return ((StairsXStart - x) / (StairsXStart - StairsXEnd)) * StairsRise;
            }
            if (x > RiverX0 && x < RiverX1) return RiverWater;
            if (InSidewalk(x, z)) return CurbH;
            return 0;
        }

        // --- sidewalk "C" polygons (CCW in x/z), one per avenue block side ---

        public struct Poly
        {
            public List<double[]> Pts;      // [x, z]
            public List<int> Round;         // indices to round
            public int Side;
        }

        public static List<Poly> SidewalkPolys()
        {
            var polys = new List<Poly>();
            var zs = new List<double> { ZS };
            foreach (double z in CrossZ) { zs.Add(z + CrHalf); zs.Add(z - CrHalf); }
            zs.Add(ZN);
            var blocks = new List<double[]>();
            for (int i = 0; i < zs.Count; i += 2) blocks.Add(new[] { zs[i], zs[i + 1] });

            foreach (int side in new[] { 1, -1 })
            {
                double xr = side * AvHalf;
                double xi = side * (AvHalf + AvSide);
                double xEnd = side > 0 ? XE : XW;
                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    double zHi = blocks[bi][0], zLo = blocks[bi][1];
                    bool southIsCross = bi > 0;
                    bool northIsCross = bi < blocks.Count - 1;
                    var pts = new List<double[]>();
                    var round = new List<int>();

                    pts.Add(new[] { xr, zHi }); if (southIsCross) round.Add(0);
                    pts.Add(new[] { xr, zLo }); if (northIsCross) round.Add(1);
                    if (northIsCross)
                    {
                        pts.Add(new[] { xEnd, zLo });
                        pts.Add(new[] { xEnd, zLo - CrSide });
                        pts.Add(new[] { xi, zLo - CrSide });
                    }
                    else
                    {
                        pts.Add(new[] { xi, zLo });
                    }
                    if (southIsCross)
                    {
                        pts.Add(new[] { xi, zHi + CrSide });
                        pts.Add(new[] { xEnd, zHi + CrSide });
                        pts.Add(new[] { xEnd, zHi });
                    }
                    else
                    {
                        pts.Add(new[] { xi, zHi });
                    }
                    polys.Add(new Poly { Pts = pts, Round = round, Side = side });
                }
            }
            return polys;
        }
    }
}
