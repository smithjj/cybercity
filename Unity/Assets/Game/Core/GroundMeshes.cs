using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    /// Sim -> Unity mesh conversion. sim (x,y,z) -> Unity (x,y,-z). The z flip is a mirror,
    /// which flips winding: every triangle has index 1<->2 swapped and normals get z negated.
    /// Quads reproduce the JS PlaneGeometry().rotate().translate() layout EXACTLY:
    /// local corners [(-w/2,+h/2),(+w/2,+h/2),(-w/2,-h/2),(+w/2,-h/2)],
    /// uv [(0,1),(1,1),(0,0),(1,0)], index tris (0,2,1),(2,3,1) in sim space.
    public static class GroundMeshes
    {
        // Sim-space quad via the JS layout: base plane in XY, w along +x, h along +y (pre-rotation).
        // rotX: h axis maps to -z (so +h/2 end lands at z0); rotY(±90): variants for walls.
        static void AddPlaneQuad(List<double[]> pos, List<double[]> nor, List<double[]> uv, List<int> idx,
            double cx, double cy, double cz, double w, double h, double[] nrmSim)
        {
            // corners in sim space for rotateX(-pi/2) plane (ground quads), normal +Y
            int i0 = pos.Count;
            pos.Add(new[] { cx - w / 2, cy, cz - h / 2 });              // v0 (x0, z0) uv(0,1)
            pos.Add(new[] { cx + w / 2, cy, cz - h / 2 });             // v1 (x1, z0) uv(1,1)
            pos.Add(new[] { cx - w / 2, cy, cz + h / 2 });             // v2 (x0, z1) uv(0,0)
            pos.Add(new[] { cx + w / 2, cy, cz + h / 2 });             // v3 (x1, z1) uv(1,0)
            var n = new[] { nrmSim[0], nrmSim[1], nrmSim[2] };
            for (int k = 0; k < 4; k++) { nor.Add(n); }
            uv.Add(new[] { 0.0, 1.0 }); uv.Add(new[] { 1.0, 1.0 }); uv.Add(new[] { 0.0, 0.0 }); uv.Add(new[] { 1.0, 0.0 });
            // JS tris (0,2,1),(2,3,1); Unity flip => (0,1,2),(2,1,3)
            idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2);
            idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0 + 3);
        }

        public static Mesh BuildBase(List<GroundGeo.Rect2> rects, out List<BoxCollider> colliders)
        {
            var pos = new List<double[]>();
            var nor = new List<double[]>();
            var uv = new List<double[]>();
            var idx = new List<int>();
            colliders = new List<BoxCollider>();
            foreach (var r in rects)
                AddPlaneQuad(pos, nor, uv, idx, (r.X0 + r.X1) / 2, 0, (r.Z0 + r.Z1) / 2,
                    r.X1 - r.X0, r.Z1 - r.Z0, new[] { 0.0, 1.0, 0.0 });
            return MakeMesh(pos, nor, uv, idx, "GroundBase");
        }

        public static Mesh BuildQuads(IList<Tuple<GroundGeo.Rect2, double>> quads)
        {
            var pos = new List<double[]>();
            var nor = new List<double[]>();
            var uv = new List<double[]>();
            var idx = new List<int>();
            foreach (var q in quads)
            {
                var r = q.Item1; double y = q.Item2;
                AddPlaneQuad(pos, nor, uv, idx, (r.X0 + r.X1) / 2, y, (r.Z0 + r.Z1) / 2,
                    r.X1 - r.X0, r.Z1 - r.Z0, new[] { 0.0, 1.0, 0.0 });
            }
            return MakeMesh(pos, nor, uv, idx, "GroundQuads");
        }

        // Walls: JS PlaneGeometry(zB-zA, wallH).rotateY(±pi/2).translate(bankX, -wallH/2, midZ).
        // west (+1): verts (x, 0, zB)uv(0,1) (x, 0, zA)uv(1,1) (x,-H,zB)uv(0,0) (x,-H,zA)uv(1,0), normal +X
        // east (-1): verts (x, 0, zA)uv(0,1) (x, 0, zB)uv(1,1) (x,-H,zA)uv(0,0) (x,-H,zB)uv(1,0), normal -X
        static void AddWall(List<double[]> pos, List<double[]> nor, List<double[]> uv, List<int> idx,
            double bankX, double zA, double zB, double wallH, double faceX)
        {
            int i0 = pos.Count;
            if (faceX > 0)
            {
                pos.Add(new[] { bankX, 0.0, zB });
                pos.Add(new[] { bankX, 0.0, zA });
                pos.Add(new[] { bankX, -wallH, zB });
                pos.Add(new[] { bankX, -wallH, zA });
                nor.Add(new[] { 1.0, 0.0, 0.0 }); nor.Add(new[] { 1.0, 0.0, 0.0 });
                nor.Add(new[] { 1.0, 0.0, 0.0 }); nor.Add(new[] { 1.0, 0.0, 0.0 });
            }
            else
            {
                pos.Add(new[] { bankX, 0.0, zA });
                pos.Add(new[] { bankX, 0.0, zB });
                pos.Add(new[] { bankX, -wallH, zA });
                pos.Add(new[] { bankX, -wallH, zB });
                nor.Add(new[] { -1.0, 0.0, 0.0 }); nor.Add(new[] { -1.0, 0.0, 0.0 });
                nor.Add(new[] { -1.0, 0.0, 0.0 }); nor.Add(new[] { -1.0, 0.0, 0.0 });
            }
            uv.Add(new[] { 0.0, 1.0 }); uv.Add(new[] { 1.0, 1.0 }); uv.Add(new[] { 0.0, 0.0 }); uv.Add(new[] { 1.0, 0.0 });
            idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2);
            idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0 + 3);
        }

        public static Mesh BuildSidewalks(List<GroundGeo.SidewalkPoly> polys)
        {
            var pos = new List<double[]>();
            var nor = new List<double[]>();
            var uv = new List<double[]>();
            var idx = new List<int>();
            foreach (var poly in polys)
            {
                int i0 = pos.Count;
                foreach (var p in poly.Inner)
                {
                    pos.Add(new[] { p[0], GroundPlan.CurbH, p[1] });
                    nor.Add(new[] { 0.0, 1.0, 0.0 });
                    uv.Add(new[] { p[0], -p[1] }); // shape-space uv, as ShapeGeometry
                }
                foreach (int t in poly.Indices) idx.Add(i0 + t);
            }
            return MakeMesh(pos, nor, uv, idx, "GroundSidewalk");
        }

        public static Mesh BuildCurb(GroundGeo.CurbData curb)
        {
            var pos = new List<double[]>(curb.Pos);
            var nor = new List<double[]>(curb.Nor);
            var uv = new List<double[]>(curb.Uv);
            // triangle-major vertices; swap within each 3-vertex group (sim -> unity flip)
            var idx = new List<int>(curb.Pos.Count);
            for (int t = 0; t < pos.Count; t += 3)
            {
                idx.Add(t); idx.Add(t + 2); idx.Add(t + 1);
            }
            return MakeMesh(pos, nor, uv, idx, "GroundCurb");
        }

        public static Mesh BuildWalls(GroundGeo.GroundData data)
        {
            var pos = new List<double[]>(); var nor = new List<double[]>(); var uv = new List<double[]>(); var idx = new List<int>();
            AddWall(pos, nor, uv, idx, data.WallWest.X0, data.WallWest.Z0, data.WallWest.Z1, data.WallH, +1);
            AddWall(pos, nor, uv, idx, data.WallEast.X0, data.WallEast.Z0, data.WallEast.Z1, data.WallH, -1);
            return MakeMesh(pos, nor, uv, idx, "GroundWalls");
        }

        static Mesh MakeMesh(List<double[]> pos, List<double[]> nor, List<double[]> uv, List<int> idx, string name)
        {
            var vpos = new List<Vector3>(pos.Count);
            var vnor = new List<Vector3>(nor.Count);
            var vuv = new List<Vector2>(uv.Count);
            foreach (var p in pos) vpos.Add(SimCoord.ToUnity(p[0], p[1], p[2]));
            foreach (var n in nor) vnor.Add(new Vector3((float)n[0], (float)n[1], (float)-n[2]));
            foreach (var u in uv) vuv.Add(new Vector2((float)u[0], (float)u[1]));
            var m = new Mesh { name = name };
            m.SetVertices(vpos);
            m.SetNormals(vnor);
            m.SetUVs(0, vuv);
            m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
