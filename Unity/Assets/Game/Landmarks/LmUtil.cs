// Port of src/systems/landmarks/util.js — Batch (per-material merged chunk meshes),
// unitBox, profileGeometry (extrude along X from a [z,y] polygon), boxProjectUV,
// beamBetween, groundY. All landmark geometry is built in SIM space and mirrored
// (z -> -z, winding flip) at mesh creation, same as the other systems.
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    /// Simple non-indexed triangle soup geometry (position/normal/uv).
    public class LmGeo
    {
        public Vector3[] pos;
        public Vector3[] nor;   // may be null
        public Vector2[] uv;    // may be null

        public int VertCount => pos.Length;

        public LmGeo Clone()
        {
            var g = new LmGeo { pos = (Vector3[])pos.Clone() };
            if (nor != null) g.nor = (Vector3[])nor.Clone();
            if (uv != null) g.uv = (Vector2[])uv.Clone();
            return g;
        }

        public void ApplyMatrix(Matrix4x4 m)
        {
            bool needNor = nor != null;
            var nmat = new Matrix4x4();
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) nmat[r, c] = m[r, c]; // rotation/scale only
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = m.MultiplyPoint3x4(pos[i]);
                if (needNor) nor[i] = nmat.MultiplyVector(nor[i]).normalized;
            }
        }

        public void DeleteNormals() => nor = null;

        public void EnsureUv()
        {
            if (uv == null) uv = new Vector2[pos.Length];
        }

        /// Area-weighted vertex normals (three.js computeVertexNormals).
        public void ComputeVertexNormals()
        {
            var n = new Vector3[pos.Length];
            for (int i = 0; i + 2 < pos.Length; i += 3)
            {
                Vector3 a = pos[i], b = pos[i + 1], c = pos[i + 2];
                Vector3 fn = Vector3.Cross(b - a, c - a); // not normalized — weights by area, as three.js
                n[i] += fn; n[i + 1] += fn; n[i + 2] += fn;
            }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].sqrMagnitude > 1e-20f ? n[i].normalized : Vector3.up;
            nor = n;
        }

        public void FlipWinding()
        {
            for (int i = 0; i + 2 < pos.Length; i += 3)
            {
                (pos[i + 1], pos[i + 2]) = (pos[i + 2], pos[i + 1]);
                if (nor != null) (nor[i + 1], nor[i + 2]) = (nor[i + 2], nor[i + 1]);
            }
        }

        /// Sim-space geometry -> Unity mesh (mirror z, flip winding, world-space verts).
        public Mesh ToUnityMesh(string name)
        {
            int n = pos.Length;
            var v = new Vector3[n];
            var nn = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(pos[i].x, pos[i].y, -pos[i].z);
                nn[i] = nor != null ? new Vector3(nor[i].x, nor[i].y, -nor[i].z) : Vector3.up;
            }
            var idx = new int[n];
            for (int i = 0; i + 2 < n; i += 3) { idx[i] = i; idx[i + 1] = i + 2; idx[i + 2] = i + 1; }
            var m = new Mesh { name = name };
            m.vertices = v;
            m.normals = nn;
            if (uv != null) m.uv = uv; else m.uv = new Vector2[n];
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }

    /// three.js BoxGeometry(1,1,1) — 24 verts, 36 idx, standard per-face normals/uvs.
    public static class UnitBox
    {
        public static LmGeo Make()
        {
            // three.js buildPlane order: +z, -z, +y, -y, +x, -x with u/v axes as in BoxGeometry
            var faces = new (Vector3 u, Vector3 v, Vector3 w, int wdir, int su, int sv)[]
            {
                (new Vector3(1,0,0), new Vector3(0,-1,0), new Vector3(0,0,1),  1, 1, 1), // +z
                (new Vector3(-1,0,0), new Vector3(0,-1,0), new Vector3(0,0,-1), 1, 1, 1), // -z
                (new Vector3(1,0,0), new Vector3(0,0,1),  new Vector3(0,1,0),  1, 1, 1), // +y
                (new Vector3(1,0,0), new Vector3(0,0,-1), new Vector3(0,-1,0), 1, 1, 1), // -y
                (new Vector3(0,0,-1), new Vector3(0,-1,0), new Vector3(1,0,0), 1, 1, 1), // +x
                (new Vector3(0,0,1),  new Vector3(0,-1,0), new Vector3(-1,0,0), 1, 1, 1), // -x
            };
            var pos = new Vector3[24]; var nor = new Vector3[24]; var uv = new Vector2[24];
            int vi = 0;
            foreach (var f in faces)
            {
                for (int iy = 0; iy < 2; iy++)
                {
                    for (int ix = 0; ix < 2; ix++)
                    {
                        float ux = ix / 1f - 0.5f, uy = iy / 1f - 0.5f;
                        pos[vi] = f.u * ux + f.v * uy + f.w * (f.wdir * 0.5f);
                        nor[vi] = f.w * f.wdir;
                        uv[vi] = new Vector2(ix, 1 - iy);
                        vi++;
                    }
                }
            }
            var idx = new int[36];
            int ii = 0;
            for (int f = 0; f < 6; f++)
            {
                int b = f * 4;
                int[] q = { b, b + 2, b + 1, b + 2, b + 3, b + 1 };
                foreach (var t in q) idx[ii++] = t;
            }
            return new LmGeo { pos = pos, nor = nor, uv = uv };
        }
    }

    /// Collects transformed geometries per material key, then merges into chunked meshes.
    public class LmBatch
    {
        readonly Dictionary<string, List<LmGeo>> _parts = new();
        public static readonly LmGeo BoxGeo = UnitBox.Make();

        /// three.js Euler 'XYZ' order -> Unity quaternion (q = qx * qy * qz).
        public static Quaternion EulToQuat(float rx, float ry, float rz)
        {
            float cx = Mathf.Cos(rx * 0.5f), sx = Mathf.Sin(rx * 0.5f);
            float cy = Mathf.Cos(ry * 0.5f), sy = Mathf.Sin(ry * 0.5f);
            float cz = Mathf.Cos(rz * 0.5f), sz = Mathf.Sin(rz * 0.5f);
            return new Quaternion(
                sx * cy * cz - cx * sy * sz,
                cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz,
                cx * cy * cz + sx * sy * sz);
        }

        public void Add(string key, LmGeo geo, float x = 0, float y = 0, float z = 0,
            float rx = 0, float ry = 0, float rz = 0, float sx = 1, float sy = 1, float sz = 1)
        {
            var m = Matrix4x4.TRS(new Vector3(x, y, z), EulToQuat(rx, ry, rz), new Vector3(sx, sy, sz));
            var g = geo.Clone();
            g.ApplyMatrix(m);
            Push(key, g);
        }

        public void AddMatrix(string key, LmGeo geo, Matrix4x4 m)
        {
            var g = geo.Clone();
            g.ApplyMatrix(m);
            Push(key, g);
        }

        public void Box(string key, float w, float h, float d, float x, float y, float z, float ry = 0, float rx = 0, float rz = 0)
            => Add(key, BoxGeo, x, y, z, rx, ry, rz, w, h, d);

        void Push(string key, LmGeo g)
        {
            if (!_parts.TryGetValue(key, out var list)) { list = new List<LmGeo>(); _parts[key] = list; }
            list.Add(g);
        }

        public IEnumerable<KeyValuePair<string, List<LmGeo>>> Parts => _parts;

        /// Build meshes under a Unity parent. Mirrors to unity space. Chunk buckets by
        /// floor(cx/chunk)+":"+floor(cz/chunk) when chunk > 0. Keys in worldUvKeys get
        /// box-projected meter UVs after merge (landmarks worldUV).
        public List<GameObject> Build(Transform parent, string prefix = "lm_", float chunk = 0, HashSet<string> worldUvKeys = null)
        {
            var result = new List<GameObject>();
            foreach (var kv in _parts)
            {
                var buckets = new Dictionary<string, List<LmGeo>>();
                if (chunk > 0)
                {
                    foreach (var g in kv.Value)
                    {
                        Vector3 c = g.pos[0];
                        var b = new Bounds(g.pos[0], Vector3.zero);
                        for (int i = 1; i < g.pos.Length; i++) b.Encapsulate(g.pos[i]);
                        c = b.center;
                        string k = Mathf.FloorToInt(c.x / chunk) + ":" + Mathf.FloorToInt(c.z / chunk);
                        if (!buckets.TryGetValue(k, out var bl)) { bl = new List<LmGeo>(); buckets[k] = bl; }
                        bl.Add(g);
                    }
                }
                else buckets["all"] = kv.Value;

                foreach (var bk in buckets)
                {
                    int total = 0;
                    foreach (var g in bk.Value) total += g.pos.Length;
                    if (total == 0) continue;
                    var merged = new LmGeo
                    {
                        pos = new Vector3[total],
                        nor = new Vector3[total],
                        uv = new Vector2[total],
                    };
                    int o = 0;
                    foreach (var g in bk.Value)
                    {
                        for (int i = 0; i < g.pos.Length; i++)
                        {
                            merged.pos[o + i] = g.pos[i];
                            merged.nor[o + i] = g.nor != null ? g.nor[i] : Vector3.up;
                            merged.uv[o + i] = g.uv != null ? g.uv[i] : Vector2.zero;
                        }
                        o += g.pos.Length;
                    }
                    if (worldUvKeys != null && worldUvKeys.Contains(kv.Key)) LmUtil.BoxProjectUv(merged, 1);
                    var mesh = merged.ToUnityMesh(prefix + kv.Key);
                    var go = new GameObject(prefix + kv.Key);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>();
                    result.Add(go);
                }
            }
            return result;
        }
    }

    public static class LmUtil
    {
        /// Box-projected world UVs: u/v in meters / scale, chosen by dominant normal axis (sim space, before mirror).
        public static void BoxProjectUv(LmGeo geo, float scale = 1)
        {
            var uv = new Vector2[geo.pos.Length];
            for (int i = 0; i < geo.pos.Length; i++)
            {
                var p = geo.pos[i]; var n = geo.nor != null ? geo.nor[i] : Vector3.up;
                float nx = Mathf.Abs(n.x), ny = Mathf.Abs(n.y), nz = Mathf.Abs(n.z);
                float u, v;
                if (ny >= nx && ny >= nz) { u = p.x; v = p.z; }
                else if (nx >= nz) { u = p.z * Mathf.Sign(n.x == 0 ? 1 : n.x); v = p.y; }
                else { u = -p.x * Mathf.Sign(n.z == 0 ? 1 : n.z); v = p.y; }
                uv[i] = new Vector2(u / scale, v / scale);
            }
            geo.uv = uv;
        }

        /// Trapezoid / arbitrary profile ([z,y] points, CCW in shape space) extruded along X from x0 to x1.
        /// Port of profileGeometry: extrude depth (x1-x0), basis swap, winding flip.
        public static LmGeo ProfileGeometry(Vector2[] points, float x0, float x1)
        {
            int np = points.Length;
            float depth = x1 - x0;
            // triangulate polygon in shape space (sx = z, sy = y)
            double[] data = new double[np * 2];
            for (int i = 0; i < np; i++) { data[i * 2] = points[i].x; data[i * 2 + 1] = points[i].y; }
            int[] tris = Earcut.Triangulate(data, new int[0], 2);

            // vertex layout: front cap np verts (x0), back cap np verts (x1), side walls 4 per edge
            var pos = new List<Vector3>(np * 2 + np * 4);
            // shape space (sx, sy, e) -> basis (e, sy, sx); front cap e=0, back e=depth
            // front cap: reversed winding (faces -X)
            for (int t = tris.Length - 3; t >= 0; t -= 3)
            {
                for (int k = 2; k >= 0; k--)
                {
                    var p = points[tris[t + k]];
                    pos.Add(new Vector3(x0, p.y, p.x));
                }
            }
            // back cap: normal winding (faces +X)
            for (int t = 0; t < tris.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var p = points[tris[t + k]];
                    pos.Add(new Vector3(x1, p.y, p.x));
                }
            }
            // side walls: quad per edge; polygon CCW in shape space -> outward normal
            for (int i = 0; i < np; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % np];
                // two triangles: (a0, b0, b1) and (a0, b1, a1) with e0=x0 e1=x1
                pos.Add(new Vector3(x0, a.y, a.x));
                pos.Add(new Vector3(x0, b.y, b.x));
                pos.Add(new Vector3(x1, b.y, b.x));
                pos.Add(new Vector3(x0, a.y, a.x));
                pos.Add(new Vector3(x1, b.y, b.x));
                pos.Add(new Vector3(x1, a.y, a.x));
            }
            var g = new LmGeo { pos = pos.ToArray() };
            g.ComputeVertexNormals();
            // three.js ExtrudeGeometry generates UVs from vertex positions; box-project instead
            BoxProjectUv(g, 1);
            return g;
        }

        /// Thin box between two points (for wires, rails, beams).
        public static void BeamBetween(LmBatch batch, string key, Vector3 a, Vector3 b, float w, float h)
        {
            var d = b - a;
            float len = d.magnitude;
            var mid = (a + b) * 0.5f;
            var q = Quaternion.FromToRotation(Vector3.right, d.normalized);
            var m = Matrix4x4.TRS(mid, q, new Vector3(len, h, w));
            batch.AddMatrix(key, LmBatch.BoxGeo, m);
        }

        /// Ground height at sim (x, z).
        public static float GroundY(double x, double z)
        {
            return (float)GroundPlan.HeightAt(x, z);
        }

        // ---------- primitive generators (three.js ports, sim space, non-indexed) ----------

        /// CylinderGeometry(rTop, rBottom, height, radialSegments) — sides + caps.
        public static LmGeo GenCylinder(float rTop, float rBottom, float height, int segs)
        {
            var pos = new List<Vector3>(); var nor = new List<Vector3>();
            var ring = new Vector3[segs + 1]; var rn = new Vector3[segs + 1];
            for (int x = 0; x <= segs; x++)
            {
                float th = x / (float)segs * Mathf.PI * 2f;
                ring[x] = new Vector3(Mathf.Sin(th), 0, Mathf.Cos(th));
                rn[x] = ring[x];
            }
            var top = new Vector3(0, height * 0.5f, 0);
            var bot = new Vector3(0, -height * 0.5f, 0);
            // side quads: y rows top->bottom
            for (int x = 0; x < segs; x++)
            {
                Vector3 a = ring[x] * rTop + top, b = ring[x + 1] * rTop + top;
                Vector3 c = ring[x + 1] * rBottom + bot, d = ring[x] * rBottom + bot;
                pos.AddRange(new[] { a, b, d, b, c, d });
                nor.AddRange(new[] { rn[x], rn[x + 1], rn[x], rn[x + 1], rn[x + 1], rn[x] });
            }
            // top cap (fan, +Y): tris (center, k+1, k) — CCW from above matches three.js
            var rim = ring; // at rTop
            for (int x = 0; x < segs; x++)
            {
                pos.AddRange(new[] { top, rim[x + 1] * rTop + top, rim[x] * rTop + top });
                nor.AddRange(new[] { Vector3.up, Vector3.up, Vector3.up });
                pos.AddRange(new[] { bot, rim[x] * rBottom + bot, rim[x + 1] * rBottom + bot });
                nor.AddRange(new[] { Vector3.down, Vector3.down, Vector3.down });
            }
            var g = new LmGeo { pos = pos.ToArray(), nor = nor.ToArray() };
            BoxProjectUv(g, 1);
            return g;
        }

        public static LmGeo GenCone(float r, float height, int segs) => GenCylinder(0.001f, r, height, segs);

        /// SphereGeometry(radius, widthSegments, heightSegments).
        public static LmGeo GenSphere(float r, int wSegs, int hSegs)
        {
            var pos = new List<Vector3>(); var nor = new Vector3[0];
            var v = new Vector3[(wSegs + 1) * (hSegs + 1)];
            for (int iy = 0; iy <= hSegs; iy++)
            {
                float v01 = iy / (float)hSegs;
                float phi = v01 * Mathf.PI;
                for (int ix = 0; ix <= wSegs; ix++)
                {
                    float u01 = ix / (float)wSegs;
                    float th = u01 * Mathf.PI * 2f;
                    v[iy * (wSegs + 1) + ix] = new Vector3(
                        -r * Mathf.Cos(th) * Mathf.Sin(phi),
                        r * Mathf.Cos(phi),
                        r * Mathf.Sin(th) * Mathf.Sin(phi));
                }
            }
            for (int iy = 0; iy < hSegs; iy++)
            {
                for (int ix = 0; ix < wSegs; ix++)
                {
                    int a = iy * (wSegs + 1) + ix + 1;
                    int b = (iy + 1) * (wSegs + 1) + ix;
                    int c = iy * (wSegs + 1) + ix;
                    int d = (iy + 1) * (wSegs + 1) + ix + 1;
                    if (iy != 0) { pos.AddRange(new[] { v[a], v[b], v[c] }); }
                    if (iy != hSegs - 1) { pos.AddRange(new[] { v[a], v[d], v[b] }); }
                }
            }
            var g = new LmGeo { pos = pos.ToArray() };
            g.ComputeVertexNormals();
            BoxProjectUv(g, 1);
            return g;
        }

        /// TorusGeometry(radius, tube, radialSegments, tubularSegments) — lies in XZ plane.
        public static LmGeo GenTorus(float radius, float tube, int radSegs, int tubSegs)
        {
            var pos = new List<Vector3>();
            var ring = new Vector3[radSegs + 1];
            for (int j = 0; j <= radSegs; j++)
            {
                float v = j / (float)radSegs * Mathf.PI * 2f;
                ring[j] = new Vector3(Mathf.Cos(v) * tube, Mathf.Sin(v) * tube, 0);
            }
            for (int i = 0; i < tubSegs; i++)
            {
                float u0 = i / (float)tubSegs * Mathf.PI * 2f;
                float u1 = (i + 1) / (float)tubSegs * Mathf.PI * 2f;
                var c0 = new Vector3(Mathf.Cos(u0) * radius, 0, Mathf.Sin(u0) * radius);
                var c1 = new Vector3(Mathf.Cos(u1) * radius, 0, Mathf.Sin(u1) * radius);
                for (int j = 0; j < radSegs; j++)
                {
                    pos.AddRange(new[] { c0 + ring[j], c1 + ring[j], c1 + ring[j + 1] });
                    pos.AddRange(new[] { c0 + ring[j], c1 + ring[j + 1], c0 + ring[j + 1] });
                }
            }
            var g = new LmGeo { pos = pos.ToArray() };
            g.ComputeVertexNormals();
            BoxProjectUv(g, 1);
            return g;
        }
    }
}

// nudge 2

// nudge3
