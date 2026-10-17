// Port of src/systems/buildings/overlook.js — cantilevered rooftop deck (panel 2 vantage):
// deck slab + wet deck surface, steel brackets, chunky railing, planters, bench, lit door housing.
using System;
using System.Collections.Generic;

using UnityEngine;

namespace NeonKaido
{
    public static class Overlook
    {
        public static void BuildOverlook(BChunkedBuckets B, List<BLot> lots, BOut outp)
        {
            double[] ov = Layout.OverlookPosition;
            BLot lot = null;
            foreach (var l in lots) if (l.OverlookDeckY.HasValue) { lot = l; break; }
            if (lot == null) return;
            double y = lot.OverlookDeckY.Value;
            double x0 = lot.X0 + 0.5, x1 = lot.X1 - 0.5;
            double zFront = lot.Z0;             // building north face
            double zEdge = ov[2] - 1.35;        // deck edge (railing) in front of the camera
            var fac = B.Get(ov[0], ov[2], "facade", BuildArchitecture.FacadeAttrs);
            var det = B.Get(ov[0], ov[2], "detail", BuildArchitecture.DetailAttrs);
            var slab = BuildArchitecture.TrimAttr(1, 3, 0.45, 0.31);
            // deck slab (cantilever)
            fac.Box((x0 + x1) / 2, y - 0.2, (zFront + zEdge) / 2, x1 - x0, 0.4, zFront - zEdge, slab);
            // walkable deck collider (top at y+0.005)
            {
                var broot = GameObject.Find("/Buildings");
                if (broot != null)
                {
                    var cgo = new GameObject("overlook_deck_col");
                    cgo.transform.SetParent(broot.transform, false);
                    cgo.transform.position = new Vector3((float)((x0 + x1) / 2), (float)(y - 0.15), (float)(-(zFront + zEdge) / 2));
                    var bc = cgo.AddComponent<BoxCollider>();
                    bc.size = new Vector3((float)(x1 - x0), 0.3f, (float)(zFront - zEdge));
                }
            }
            // deck surface: wet, puddled roof material
            var ra = new BAttrSet().Set("aWin", new[] { 0.2, 3, 3, 9999 }).Set("aWin2", new[] { 0.5, 0.5, 0, 4 }).Set("aWin3", new[] { 9999, 7, 3.0, 0.4 });
            fac.Quad(new[] { x0, y + 0.005, zFront }, new[] { x1, y + 0.005, zFront }, new[] { x1, y + 0.005, zEdge }, new[] { x0, y + 0.005, zEdge },
                new[] { x0, -zFront }, new[] { x1, -zFront }, new[] { x1, -zEdge }, new[] { x0, -zEdge }, ra, new[] { 0.0, 1.0, 0.0 }, new[] { 1.0, 0.0, 0.0 });
            // steel brackets under the cantilever
            var steel = new BAttrSet().Set("aCol", new[] { 0.06, 0.06, 0.065, 0 }).Set("aPbr", new[] { 0.4, 0.8, 0, 0.5 });
            for (double x = x0 + 0.3; x <= x1; x += (x1 - x0 - 0.6) / 3)
            {
                det.Beam(new[] { x, y - 0.4, zEdge + 0.3 }, new[] { x, y - 3.8, zFront }, 0.18, steel);
                det.Beam(new[] { x, y - 0.45, zEdge + 0.1 }, new[] { x, y - 0.45, zFront }, 0.25, steel);
            }
            outp.Colliders.Add(new BCollider { X = (x0 + x1) / 2, Y = y - 0.2, Z = (zFront + zEdge) / 2, Hx = (x1 - x0) / 2, Hy = 0.2, Hz = (zFront - zEdge) / 2 });

            // railing (dark metal, chunky like the reference)
            var rail = new BAttrSet().Set("aCol", new[] { 0.035, 0.035, 0.04, 0 }).Set("aPbr", new[] { 0.28, 0.85, 0, 0.35 });
            double H = 1.12;
            double[][] edge = { new[] { x0 + 0.05, zFront - 0.05 }, new[] { x0 + 0.05, zEdge + 0.05 }, new[] { x1 - 0.05, zEdge + 0.05 }, new[] { x1 - 0.05, zFront - 0.05 } };
            for (int i = 0; i < 3; i++)
            {
                double ax = edge[i][0], az = edge[i][1], bx = edge[i + 1][0], bz = edge[i + 1][1];
                det.Beam(new[] { ax, y + H, az }, new[] { bx, y + H, bz }, 0.075, rail, 0.06);
                det.Beam(new[] { ax, y + H - 0.07, az }, new[] { bx, y + H - 0.07, bz }, 0.03, rail);
                foreach (double hh in new[] { 0.35, 0.65 }) det.Beam(new[] { ax, y + hh, az }, new[] { bx, y + hh, bz }, 0.028, rail);
                det.Beam(new[] { ax, y + 0.08, az }, new[] { bx, y + 0.08, bz }, 0.06, rail, 0.12);
                double len = GroundGeo.Hypot2(bx - ax, bz - az);
                int n = (int)Math.Ceiling(len / 1.4);
                for (int k = 0; k <= n; k++)
                {
                    double t = (double)k / n, px = ax + (bx - ax) * t, pz = az + (bz - az) * t;
                    det.Beam(new[] { px, y, pz }, new[] { px, y + H, pz }, 0.055, rail);
                }
                // thin vertical balusters
                int nb = (int)Math.Ceiling(len / 0.14);
                for (int k = 0; k < nb; k++)
                {
                    double t = (k + 0.5) / nb, px = ax + (bx - ax) * t, pz = az + (bz - az) * t;
                    det.Beam(new[] { px, y + 0.08, pz }, new[] { px, y + H - 0.07, pz }, 0.014, rail);
                }
                double cx = (ax + bx) / 2, cz = (az + bz) / 2;
                outp.Colliders.Add(new BCollider { X = cx, Y = y + H / 2, Z = cz, Hx = Math.Max(Math.Abs(bx - ax) / 2, 0.05), Hy = H / 2, Hz = Math.Max(Math.Abs(bz - az) / 2, 0.05) });
            }
            // deck clutter: planters, a bench, a lamp
            var planter = new BAttrSet().Set("aCol", new[] { 0.25, 0.25, 0.24, 0 }).Set("aPbr", new[] { 0.7, 0, 0, 0.8 });
            var leaf = new BAttrSet().Set("aCol", new[] { 0.08, 0.16, 0.06, 0 }).Set("aPbr", new[] { 0.6, 0, 0, 0.3 });
            foreach (double px in new[] { x0 + 0.6, x1 - 0.6 })
            {
                det.Box(px, y + 0.3, zEdge + 0.6, 0.7, 0.6, 0.7, planter);
                det.Box(px, y + 0.8, zEdge + 0.6, 0.6, 0.5, 0.6, leaf);
            }
            det.Box(x1 - 0.5, y + 0.22, (zFront + zEdge) / 2, 0.5, 0.44, 1.8,
                new BAttrSet().Set("aCol", new[] { 0.3, 0.2, 0.12, 0 }).Set("aPbr", new[] { 0.5, 0, 0, 0.5 }));
            // door housing on the roof with a lit door
            double hx = x0 + 2.2, hz = zFront + 3;
            fac.Box(hx, y + 1.4, hz, 3.2, 2.8, 3.0, BuildArchitecture.TrimAttr(lot.WallLayer, 3, 0.5, 0.6));
            det.Box(hx, y + 1.05, hz - 1.51, 0.95, 2.1, 0.04,
                new BAttrSet().Set("aCol", new[] { 0.3, 0.3, 0.32, 0 }).Set("aPbr", new[] { 0.4, 0.7, 0, 0.4 }));
            det.Box(hx, y + 2.3, hz - 1.6, 0.3, 0.12, 0.15,
                new BAttrSet().Set("aCol", new[] { 1.0, 0.85, 0.6, 3 }).Set("aPbr", new[] { 0.5, 0, 0, 0 }));
            outp.Colliders.Add(new BCollider { X = hx, Y = y + 1.4, Z = hz, Hx = 1.6, Hy = 1.4, Hz = 1.5 });
            outp.Lights.Add(new BLight { Pos = new[] { hx, y + 2.1, hz - 2.0 }, Color = new[] { 1.0, 0.8, 0.55 }, Intensity = 3, Distance = 7 });
        }
    }
}
