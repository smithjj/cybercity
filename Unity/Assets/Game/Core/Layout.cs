namespace NeonKaido
{
    /// Exact port of src/world/layout.js LAYOUT. All values in SIM space (JS meters, +Y up,
    /// avenue along -Z "north"). Convert with SimCoord when writing Unity transforms/meshes.
    public static class Layout
    {
        // Main avenue (reference panel 1): road centered on x=0
        public const double AvenueX = 0;
        public const double AvenueRoadHalfWidth = 6;
        public const double AvenueSidewalkWidth = 4;
        public const double AvenueCurbHeight = 0.15;
        public const double AvenueZStart = 60;
        public const double AvenueZEnd = -420;

        // Cross streets (perpendicular, along X)
        public static readonly double[] CrossZs = { -60, -180, -300 };
        public const double CrossRoadHalfWidth = 5;
        public const double CrossSidewalkWidth = 3;
        public const double CrossXStart = -200;
        public const double CrossXEnd = 140;

        // City blocks lining the avenue
        public const double BlocksFrontageOffset = 10;
        public const double BlocksDepth = 28;
        public const double BlocksMinHeight = 12;
        public const double BlocksMaxHeight = 60;

        // Elevated rail + expressway crossing the avenue
        public const double RailZ = -140;
        public const double RailHeight = 14;
        public const double RailWidth = 9;
        public const double ExpresswayZ = -240;
        public const double ExpresswayHeight = 22;
        public const double ExpresswayWidth = 16;
        public const double ElevatedXStart = -400;
        public const double ElevatedXEnd = 400;

        // River to the east running along Z (panel 3)
        public const double RiverXCenter = 175;
        public const double RiverHalfWidth = 35;
        public const double RiverWaterLevel = -3;
        public const double RiverZStart = 200;
        public const double RiverZEnd = -700;

        // Riverside promenade with cherry trees, west bank
        public const double PromenadeXMin = 128;
        public const double PromenadeXMax = 140;
        public const double PromenadeY = 0;
        public const double PromenadeZStart = 40;
        public const double PromenadeZEnd = -500;

        // Bridges over the river
        public const double Bridge0Z = -260;
        public const double Bridge0DeckY = 8;

        // Backstreet alley with stairs and lanterns (panel 4)
        public const double AlleyZ = -100;
        public const double AlleyWidth = 4.5;
        public const double AlleyXStart = -11;
        public const double AlleyXEnd = -90;
        public const double AlleyStairsXStart = -40;
        public const double AlleyStairsXEnd = -62;
        public const double AlleyStairsRise = 7;

        // Rooftop overlook balcony (panel 2 vantage)
        public static readonly double[] OverlookPosition = { 42, 26, -60 };
        public static readonly double[] OverlookLookAt = { 90, 20, -200 };

        // Skyline ring
        public const double SkylineInnerRadius = 450;
        public const double SkylineOuterRadius = 1500;
        public const int SkylineCount = 140;
        public static readonly double[] SkylineMegaTowerHeight = { 150f, 520f };

        // Player spawn
        public static readonly double[] SpawnPosition = { 3, 1.7, 20 };
        public const double SpawnYaw = 0;
    }
}
