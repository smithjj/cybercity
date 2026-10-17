// District / area detection from LAYOUT. Port of src/systems/player/zones.js.
// Tests take SIM coordinates (x, y, zsim) like the JS original.
namespace NeonKaido
{
    public struct ZoneDef
    {
        public string Id, District, Area, Jp, Tint;
        public delegate bool Test(double x, double y, double z);
        public Test Fn;
    }

    public static class Zones
    {
        public static readonly ZoneDef Fallback = new ZoneDef
        {
            Id = "district", District = "KABUKICHO-9", Area = "BACKSTREETS", Jp = "裏通り", Tint = "red",
        };

        static readonly ZoneDef[] ZoneList =
        {
            new ZoneDef
            {
                Id = "overlook", District = "KABUKICHO-9", Area = "SKYLINE OVERLOOK", Jp = "展望台", Tint = "cyan",
                Fn = (x, y, z) => y > 14 && System.Math.Sqrt((x - Layout.OverlookPosition[0]) * (x - Layout.OverlookPosition[0])
                                                           + (z - Layout.OverlookPosition[2]) * (z - Layout.OverlookPosition[2])) < 30,
            },
            new ZoneDef
            {
                Id = "bridge", District = "KAWABATA", Area = "MIRAI BRIDGE", Jp = "未来橋", Tint = "cyan",
                Fn = (x, y, z) => System.Math.Abs(z - Layout.Bridge0Z) < 9
                               && x > Layout.RiverXCenter - Layout.RiverHalfWidth - 6 && x < Layout.RiverXCenter + Layout.RiverHalfWidth + 6,
            },
            new ZoneDef
            {
                Id = "alley", District = "KABUKICHO-9", Area = "LANTERN ALLEY", Jp = "提灯横丁", Tint = "red",
                Fn = (x, y, z) => x < Layout.AlleyXStart + 2 && x > Layout.AlleyXEnd - 6 && System.Math.Abs(z - Layout.AlleyZ) < Layout.AlleyWidth * 0.5 + 4,
            },
            new ZoneDef
            {
                Id = "river", District = "KAWABATA", Area = "SAKURA RIVERWALK", Jp = "桜川遊歩道", Tint = "pink",
                Fn = (x, y, z) => x > Layout.PromenadeXMin - 14 && x < Layout.RiverXCenter + Layout.RiverHalfWidth + 20,
            },
            new ZoneDef
            {
                Id = "cross", District = "SHINSEN", Area = "MARKET CROSSING", Jp = "市場通り", Tint = "yellow",
                Fn = (x, y, z) =>
                {
                    if (System.Math.Abs(x - Layout.AvenueX) <= Layout.AvenueRoadHalfWidth + Layout.AvenueSidewalkWidth + 4) return false;
                    foreach (var cz in Layout.CrossZs)
                        if (System.Math.Abs(z - cz) < Layout.CrossRoadHalfWidth + Layout.CrossSidewalkWidth + 6) return true;
                    return false;
                },
            },
            new ZoneDef
            {
                Id = "avenue", District = "KABUKICHO-9", Area = "NEON AVENUE", Jp = "ネオン通り", Tint = "yellow",
                Fn = (x, y, z) => System.Math.Abs(x - Layout.AvenueX) < Layout.AvenueRoadHalfWidth + Layout.AvenueSidewalkWidth + 14,
            },
        };

        public static ref readonly ZoneDef[] List => ref ZoneList;

        public static ZoneDef At(double x, double y, double zsim)
        {
            foreach (var zn in ZoneList) if (zn.Fn(x, y, zsim)) return zn;
            return Fallback;
        }
    }
}
