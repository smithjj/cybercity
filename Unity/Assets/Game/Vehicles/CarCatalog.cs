// NEON KAIDO — player-facing ground-vehicle catalog. One source of truth shared by the
// showroom (CarGallery) and the NX-9 delivery app (Delivery): names, classes, blurbs,
// curb weight / top speed, dispatch fees and the paint palettes plus human colour names.
//
// Traffic paints stay in VehicleTraffic (scenery rolls its own palette); the catalogue
// is what the player can actually order, so the hero gets house colours instead of its
// single near-black dump colour.
using UnityEngine;

namespace NeonKaido
{
    public static class CarCatalog
    {
        public class Entry
        {
            public string Type, Name, Klass, Blurb;
            public float Mass, Top;
            public int DispatchFee, Msrp;
            public Color[] Palette;
            public string[] PaintNames;
            public int DefaultPaint;

            public string PaintName(int i)
            {
                if (PaintNames != null && i >= 0 && i < PaintNames.Length) return PaintNames[i];
                return "PAINT " + (i + 1);
            }
        }

        public static readonly Entry[] Ground =
        {
            new Entry
            {
                Type = "kei", Name = "MIZUCHI KEI", Klass = "CITY KEI",
                Blurb = "Tiny urban runabout. Cheap and easy to park.",
                Mass = 850, Top = 40, DispatchFee = 120, Msrp = 18_400, DefaultPaint = 0,
                Palette = VehicleTraffic.PaletteFor("kei"),
                PaintNames = new[] { "CHALK", "NAVY", "VERMILION", "BLACK", "SILVER", "OLIVE", "SAND", "TEAL" },
            },
            new Entry
            {
                Type = "taxi", Name = "KAIDO CAB", Klass = "TAXI SEDAN",
                Blurb = "The city's workhorse. Seats four.",
                Mass = 1350, Top = 50, DispatchFee = 180, Msrp = 26_500, DefaultPaint = 0,
                Palette = VehicleTraffic.PaletteFor("taxi"),
                PaintNames = new[] { "YELLOW", "BLACK", "GREEN", "CREAM" },
            },
            new Entry
            {
                Type = "hero", Name = "KUROGANE GT-R", Klass = "SPORTS COUPE",
                Blurb = "Twin-turbo flagship. Black on black, when you want it.",
                Mass = 1550, Top = 68, DispatchFee = 450, Msrp = 148_000, DefaultPaint = 0,
                Palette = new[] { Hex(0x5c0f16), Hex(0x0a0a0c), Hex(0x2a2e36), Hex(0xd6d2c8), Hex(0x14324a) },
                PaintNames = new[] { "CRIMSON", "BLACK", "GUNMETAL", "PEARL", "MIDNIGHT" },
            },
            new Entry
            {
                Type = "van", Name = "ISUZU-ELF FREIGHT", Klass = "FREIGHT VAN",
                Blurb = "Box body, long wheelbase, big payload.",
                Mass = 3200, Top = 36, DispatchFee = 320, Msrp = 42_000, DefaultPaint = 3,
                Palette = VehicleTraffic.PaletteFor("van"),
                PaintNames = new[] { "STEEL", "ASH", "SLATE", "COBALT", "BONE" },
            },
        };

        public static int Count => Ground.Length;

        public static Entry Get(string type)
        {
            foreach (var e in Ground) if (e.Type == type) return e;
            return null;
        }

        public static int IndexOf(string type)
        {
            for (int i = 0; i < Ground.Length; i++) if (Ground[i].Type == type) return i;
            return -1;
        }

        public static Color Hex(uint hex) =>
            new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        public static string Money(int n) => "€$" + n.ToString("N0");
    }
}
