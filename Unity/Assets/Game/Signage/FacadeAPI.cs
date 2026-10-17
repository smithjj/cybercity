// Facade API — port of buildings/index.js facade records (center/normal/right/size/district...).
// Vectors are mirrored to Unity space pointwise: (x, y, -z). Placement formulas that use
// right/normal offsets stay identical to the JS.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public class SFacade
    {
        public Vector3 Center, Normal, Right;
        public double Width, Height;
        public int Floors;
        public double FloorHeight, BaseY, GroundFloorHeight, FasciaY, TotalHeight, Overhang;
        public string District, ShopType, Style, Detail;
        public bool Shopfront, Lane;
    }

    public static class FacadeAPI
    {
        static List<SFacade> _cache;

        // Port of index.js facade API loop over csLots (real building facades; JS uses these when >= 20).
        // MUST be called after architecture (needs lot.Volumes).
        public static void CacheFromLots(List<BLot> lots)
        {
            var res = new List<SFacade>();
            foreach (var lot in lots)
            {
                foreach (var f in lot.Fronts)
                {
                    var v = (lot.Overhang != 0 && f.Side == lot.OverhangSide) ? lot.Volumes[1] : lot.Volumes[0];
                    var fr = BuildArchitecture.SideFrame(v, f.Side, 0);
                    double baseY = (f.BaseY ?? 0) > 0 ? (f.BaseY ?? 0) : 0;
                    double height = lot.H - baseY;
                    res.Add(new SFacade
                    {
                        Center = new Vector3(
                            (float)(fr.Origin[0] + fr.Right[0] * fr.Width / 2),
                            (float)(baseY + height / 2),
                            (float)(-(fr.Origin[2] + fr.Right[2] * fr.Width / 2))),
                        Normal = new Vector3((float)fr.Normal[0], (float)fr.Normal[1], (float)-fr.Normal[2]),
                        Right = new Vector3((float)fr.Right[0], (float)fr.Right[1], (float)-fr.Right[2]),
                        Width = fr.Width,
                        Height = height,
                        Floors = lot.Floors,
                        FloorHeight = lot.FloorH,
                        District = f.District,
                        Shopfront = !string.IsNullOrEmpty(f.ShopType),
                        BaseY = baseY,
                        GroundFloorHeight = lot.GroundH,
                        ShopType = f.ShopType,
                        Lane = f.Lane,
                        Style = lot.Style,
                        Detail = lot.Detail,
                        Overhang = (lot.Overhang != 0 && f.Side == lot.OverhangSide) ? lot.Overhang : 0,
                        FasciaY = baseY + lot.GroundH - 0.45,
                        TotalHeight = lot.H,
                    });
                }
            }
            _cache = res;
        }

        public static List<SFacade> Collect()
        {
            return _cache ?? new List<SFacade>();
        }

        // sim-space accessors (Unity vector -> sim vector: z flip)
        public static Vector3 CS(this SFacade f) => new Vector3(f.Center.x, f.Center.y, -f.Center.z);
        public static Vector3 NS(this SFacade f) => new Vector3(f.Normal.x, f.Normal.y, -f.Normal.z);
        public static Vector3 RS(this SFacade f) => new Vector3(f.Right.x, f.Right.y, -f.Right.z);
    }
}
