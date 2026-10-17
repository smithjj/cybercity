// Port of src/systems/buildings/geom.js + the exact three.js primitives it leans on
// (BoxGeometry(1,1,1), CylinderGeometry(1,1,1,seg,1,false), Quaternion.setFromUnitVectors,
// Matrix4.compose, Matrix3.getNormalMatrix). All math in doubles; sim space.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeonKaido
{
    public static class BGeom
    {
        // V8 Math.hypot(a,b,c) — max-scaled direct sum (within 1 ulp of V8's algorithm;
        // the bucket oracle uses 1e-12 relative tolerance, which absorbs this).
        public static double Hyp3(double a, double b, double c)
        {
            double m = Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c)));
            if (m == 0) return 0;
            return m * Math.Sqrt((a / m) * (a / m) + (b / m) * (b / m) + (c / m) * (c / m));
        }

        public static void Norm(double[] a)
        {
            double l = Hyp3(a[0], a[1], a[2]);
            if (l == 0) l = 1;
            a[0] /= l; a[1] /= l; a[2] /= l;
        }

        public static double[] NormArr(double[] a)
        {
            double l = Hyp3(a[0], a[1], a[2]);
            if (l == 0) l = 1;
            return new[] { a[0] / l, a[1] / l, a[2] / l };
        }

        public static double[] Cross(double[] a, double[] b) => new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        };

        // THREE.Quaternion.setFromUnitVectors(vFrom, vTo) then normalize() — exact port.
        public static void SetFromUnitVectors(double[] vFrom, double[] vTo, out double qx, out double qy, out double qz, out double qw)
        {
            double r = vFrom[0] * vTo[0] + vFrom[1] * vTo[1] + vFrom[2] * vTo[2] + 1;
            if (r < 1e-8)
            {
                r = 0;
                if (Math.Abs(vFrom[0]) > Math.Abs(vFrom[2])) { qx = -vFrom[1]; qy = vFrom[0]; qz = 0; qw = r; }
                else { qx = 0; qy = -vFrom[2]; qz = vFrom[1]; qw = r; }
            }
            else
            {
                qx = vFrom[1] * vTo[2] - vFrom[2] * vTo[1];
                qy = vFrom[2] * vTo[0] - vFrom[0] * vTo[2];
                qz = vFrom[0] * vTo[1] - vFrom[1] * vTo[0];
                qw = r;
            }
            // THREE.Quaternion.normalize(): divideScalar(l || 1) → multiplyScalar(1/l) — reciprocal multiply, not divide
            double len = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (len == 0) len = 1;
            double qinv = 1.0 / len;
            qx *= qinv; qy *= qinv; qz *= qinv; qw *= qinv;
        }

        // THREE.Matrix4.compose(pos, quat, scale) — column-major 16 elements, exact op order.
        public static double[] Compose(double[] pos, (double x, double y, double z, double w) q, double[] scale)
        {
            double x = q.x, y = q.y, z = q.z, w = q.w;
            double x2 = x + x, y2 = y + y, z2 = z + z;
            double xx = x * x2, xy = x * y2, xz = x * z2;
            double yy = y * y2, yz = y * z2, zz = z * z2;
            double wx = w * x2, wy = w * y2, wz = w * z2;
            double sx = scale[0], sy = scale[1], sz = scale[2];
            var te = new double[16];
            te[0] = (1 - (yy + zz)) * sx; te[4] = (xy - wz) * sy; te[8] = (xz + wy) * sz;
            te[1] = (xy + wz) * sx; te[5] = (1 - (xx + zz)) * sy; te[9] = (yz - wx) * sz;
            te[2] = (xz - wy) * sx; te[6] = (yz + wx) * sy; te[10] = (1 - (xx + yy)) * sz;
            te[12] = pos[0]; te[13] = pos[1]; te[14] = pos[2];
            te[15] = 1;
            return te;
        }

        // THREE.Vector3.applyMatrix4 — affine w term = 1 exactly.
        public static void ApplyMatrix4(double[] v, double[] e)
        {
            double x = v[0], y = v[1], z = v[2];
            v[0] = e[0] * x + e[4] * y + e[8] * z + e[12];
            v[1] = e[1] * x + e[5] * y + e[9] * z + e[13];
            v[2] = e[2] * x + e[6] * y + e[10] * z + e[14];
        }

        // THREE.Matrix3.getNormalMatrix: setFromMatrix4().invert().transpose(), then applyMatrix3.
        public static double[] GetNormalMatrix(double[] te)
        {
            double[] m = { te[0], te[1], te[2], te[4], te[5], te[6], te[8], te[9], te[10] };
            m = Matrix3Invert(m);
            return new[] { m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8] };
        }

        // THREE.Matrix3.invert — column-major element naming (n_ij = row i col j), exact cofactor op order.
        static double[] Matrix3Invert(double[] te)
        {
            double n11 = te[0], n21 = te[1], n31 = te[2];
            double n12 = te[3], n22 = te[4], n32 = te[5];
            double n13 = te[6], n23 = te[7], n33 = te[8];
            double t11 = n33 * n22 - n32 * n23;
            double t12 = n32 * n13 - n33 * n12;
            double t13 = n23 * n12 - n22 * n13;
            double det = n11 * t11 + n21 * t12 + n31 * t13;
            if (det == 0) return new double[9];
            double detInv = 1.0 / det;
            return new[]
            {
                t11 * detInv,
                (n31 * n23 - n33 * n21) * detInv,
                (n32 * n21 - n31 * n22) * detInv,
                t12 * detInv,
                (n33 * n11 - n31 * n13) * detInv,
                (n31 * n12 - n32 * n11) * detInv,
                t13 * detInv,
                (n21 * n13 - n23 * n11) * detInv,
                (n22 * n11 - n21 * n12) * detInv,
            };
        }

        public static void ApplyMatrix3(double[] v, double[] e)
        {
            double x = v[0], y = v[1], z = v[2];
            v[0] = e[0] * x + e[3] * y + e[6] * z;
            v[1] = e[1] * x + e[4] * y + e[7] * z;
            v[2] = e[2] * x + e[5] * y + e[8] * z;
        }

        // ---- THREE.BoxGeometry(1,1,1) exact replica ----
        static void BuildPlane(List<double> pos, List<double> nrm, List<double> uv, List<int> idx,
            int uA, int vA, int wA, double udir, double vdir, double ua, double va, double da)
        {
            int gridX = 1, gridY = 1;
            double segW = ua / gridX, segH = va / gridY;
            int baseIdx = pos.Count / 3;
            for (int iy = 0; iy <= gridY; iy++)
            {
                double y = iy * segH - va / 2;
                for (int ix = 0; ix <= gridX; ix++)
                {
                    double x = ix * segW - ua / 2;
                    var v = new double[3];
                    v[uA] = x * udir;
                    v[vA] = y * vdir;
                    v[wA] = da / 2;
                    pos.Add(v[0]); pos.Add(v[1]); pos.Add(v[2]);
                    var n = new double[3];
                    n[uA] = 0; n[vA] = 0; n[wA] = da > 0 ? 1 : -1;
                    nrm.Add(n[0]); nrm.Add(n[1]); nrm.Add(n[2]);
                    uv.Add((double)ix / gridX); uv.Add(1 - (double)iy / gridY);
                }
            }
            for (int iy = 0; iy < gridY; iy++)
            {
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = baseIdx + ix + (gridX + 1) * iy;
                    int b = baseIdx + ix + (gridX + 1) * (iy + 1);
                    int c = baseIdx + ix + 1 + (gridX + 1) * (iy + 1);
                    int d = baseIdx + ix + 1 + (gridX + 1) * iy;
                    idx.Add(a); idx.Add(b); idx.Add(d);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
            }
        }

        static double[][] _unitBox; // [0]=pos,[1]=nrm,[2]=uv,[3]=idx
        public static double[][] UnitBox()
        {
            if (_unitBox != null) return _unitBox;
            var pos = new List<double>(); var nrm = new List<double>();
            var uv = new List<double>(); var idx = new List<int>();
            BuildPlane(pos, nrm, uv, idx, 2, 1, 0, -1, -1, 1, 1, 1);   // px: z,y,x
            BuildPlane(pos, nrm, uv, idx, 2, 1, 0, 1, -1, 1, 1, -1);   // nx
            BuildPlane(pos, nrm, uv, idx, 0, 2, 1, 1, 1, 1, 1, 1);     // py: x,z,y
            BuildPlane(pos, nrm, uv, idx, 0, 2, 1, 1, -1, 1, 1, -1);   // ny
            BuildPlane(pos, nrm, uv, idx, 0, 1, 2, 1, -1, 1, 1, 1);    // pz: x,y,z
            BuildPlane(pos, nrm, uv, idx, 0, 1, 2, -1, -1, 1, 1, -1);  // nz
            _unitBox = new[] { pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.Select(i => (double)i).ToArray() };
            return _unitBox;
        }

        // ---- THREE.CylinderGeometry(1,1,1,seg,1,false) exact replica ----
        static readonly Dictionary<int, double[][]> _unitCyl = new Dictionary<int, double[][]>();
        public static double[][] UnitCyl(int seg)
        {
            if (_unitCyl.TryGetValue(seg, out var cached)) return cached;
            var pos = new List<double>(); var nrm = new List<double>();
            var uv = new List<double>(); var idx = new List<int>();
            var grid = new int[2][];
            for (int y = 0; y <= 1; y++)
            {
                double v = y;
                var row = new int[seg + 1];
                for (int x = 0; x <= seg; x++)
                {
                    double u = (double)x / seg;
                    double theta = u * Math.PI * 2;
                    double s = Math.Sin(theta), c = Math.Cos(theta);
                    pos.Add(F32(s)); pos.Add(F32(0.5 - v)); pos.Add(F32(c));
                    // THREE: normal = (sinTheta, 0, cosTheta).normalize() — Vector3.normalize (plain sqrt)
                    double nl = Math.Sqrt(s * s + c * c);
                    if (nl == 0) nl = 1;
                    nrm.Add(F32(s / nl)); nrm.Add(0); nrm.Add(F32(c / nl));
                    uv.Add(F32(u)); uv.Add(F32(1 - v));
                    row[x] = pos.Count / 3 - 1;
                }
                grid[y] = row;
            }
            for (int x = 0; x < seg; x++)
            {
                int a = grid[0][x], b = grid[1][x], c = grid[1][x + 1], d = grid[0][x + 1];
                idx.Add(a); idx.Add(b); idx.Add(d);
                idx.Add(b); idx.Add(c); idx.Add(d);
            }
            // caps: top then bottom; center vertex repeated radialSegments times (three quirk)
            for (int cap = 0; cap < 2; cap++)
            {
                bool top = cap == 0;
                int centerIndexStart = pos.Count / 3;
                double sign = top ? 1 : -1;
                for (int x = 1; x <= seg; x++)
                {
                    pos.Add(0); pos.Add(0.5 * sign); pos.Add(0);
                    nrm.Add(0); nrm.Add(sign); nrm.Add(0);
                    uv.Add(0.5); uv.Add(0.5);
                }
                int centerIndexEnd = pos.Count / 3;
                for (int x = 0; x <= seg; x++)
                {
                    double u = (double)x / seg;
                    double theta = u * Math.PI * 2;
                    double s = Math.Sin(theta), c = Math.Cos(theta);
                    pos.Add(F32(s)); pos.Add(F32(0.5 * sign)); pos.Add(F32(c));
                    nrm.Add(0); nrm.Add(sign); nrm.Add(0);
                    uv.Add(F32(c * 0.5 + 0.5)); uv.Add(F32(s * 0.5 * sign + 0.5));
                }
                for (int x = 0; x < seg; x++)
                {
                    int c0 = centerIndexStart + x;
                    int i0 = centerIndexEnd + x;
                    if (top) { idx.Add(c0); idx.Add(i0 + 1); idx.Add(i0); }
                    else { idx.Add(c0); idx.Add(i0); idx.Add(i0 + 1); }
                }
            }
            var built = new[] { pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.Select(i => (double)i).ToArray() };
            _unitCyl[seg] = built;
            return built;
        }
        // ---- f32 round-trip: THREE stores geometry data in Float32Array ----
        public static double F32(double v) => (double)(float)v;

        // THREE.Quaternion.setFromAxisAngle(up, angle) (unit axis)
        public static (double, double, double, double) SetFromAxisAngle(double[] axis, double angle)
        {
            double half = angle / 2, s = Math.Sin(half);
            return (axis[0] * s, axis[1] * s, axis[2] * s, Math.Cos(half));
        }

        // THREE.Quaternion.setFromEuler (XYZ / YXZ branches only)
        public static (double, double, double, double) SetFromEuler(double x, double y, double z, string order)
        {
            double c1 = Math.Cos(x / 2), c2 = Math.Cos(y / 2), c3 = Math.Cos(z / 2);
            double s1 = Math.Sin(x / 2), s2 = Math.Sin(y / 2), s3 = Math.Sin(z / 2);
            if (order == "XYZ")
                return (s1 * c2 * c3 + c1 * s2 * s3, c1 * s2 * c3 - s1 * c2 * s3, c1 * c2 * s3 + s1 * s2 * c3, c1 * c2 * c3 - s1 * s2 * s3);
            // YXZ
            return (s1 * c2 * c3 + c1 * s2 * s3, c1 * s2 * c3 - s1 * c2 * s3, c1 * c2 * s3 - s1 * s2 * c3, c1 * c2 * c3 + s1 * s2 * s3);
        }

        public static double[] MakeTranslation(double x, double y, double z) =>
            new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1 };

        // THREE.Matrix4.makeRotationFromEuler (XYZ / YXZ)
        public static double[] MakeRotationFromEuler(double x, double y, double z, string order)
        {
            double a = Math.Cos(x), b = Math.Sin(x);
            double c = Math.Cos(y), d = Math.Sin(y);
            double e = Math.Cos(z), f = Math.Sin(z);
            var te = new double[16];
            if (order == "XYZ")
            {
                double ae = a * e, af = a * f, be = b * e, bf = b * f;
                te[0] = c * e; te[4] = -c * f; te[8] = d;
                te[1] = af + be; te[5] = ae - bf; te[9] = -b * c;
                te[2] = bf - ae; te[6] = be + af; te[10] = a * c;
            }
            else // YXZ
            {
                double ce = c * e, cf = c * f, de = d * e, df = d * f;
                te[0] = ce + df * b; te[4] = de * b - cf; te[8] = a * d;
                te[1] = a * f; te[5] = a * e; te[9] = -b;
                te[2] = cf * b - de; te[6] = df + ce * b; te[10] = a * c;
            }
            te[3] = 0; te[7] = 0; te[11] = 0; te[12] = 0; te[13] = 0; te[14] = 0; te[15] = 1;
            return te;
        }

        // THREE.Matrix4.multiplyMatrices(a, b) → a * b, exact op order
        public static double[] MulMatrices(double[] ae, double[] be)
        {
            double a11 = ae[0], a12 = ae[4], a13 = ae[8], a14 = ae[12];
            double a21 = ae[1], a22 = ae[5], a23 = ae[9], a24 = ae[13];
            double a31 = ae[2], a32 = ae[6], a33 = ae[10], a34 = ae[14];
            double a41 = ae[3], a42 = ae[7], a43 = ae[11], a44 = ae[15];
            double b11 = be[0], b12 = be[4], b13 = be[8], b14 = be[12];
            double b21 = be[1], b22 = be[5], b23 = be[9], b24 = be[13];
            double b31 = be[2], b32 = be[6], b33 = be[10], b34 = be[14];
            double b41 = be[3], b42 = be[7], b43 = be[11], b44 = be[15];
            var te = new double[16];
            te[0] = a11 * b11 + a12 * b21 + a13 * b31 + a14 * b41;
            te[4] = a11 * b12 + a12 * b22 + a13 * b32 + a14 * b42;
            te[8] = a11 * b13 + a12 * b23 + a13 * b33 + a14 * b43;
            te[12] = a11 * b14 + a12 * b24 + a13 * b34 + a14 * b44;
            te[1] = a21 * b11 + a22 * b21 + a23 * b31 + a24 * b41;
            te[5] = a21 * b12 + a22 * b22 + a23 * b32 + a24 * b42;
            te[9] = a21 * b13 + a22 * b23 + a23 * b33 + a24 * b43;
            te[13] = a21 * b14 + a22 * b24 + a23 * b34 + a24 * b44;
            te[2] = a31 * b11 + a32 * b21 + a33 * b31 + a34 * b41;
            te[6] = a31 * b12 + a32 * b22 + a33 * b32 + a34 * b42;
            te[10] = a31 * b13 + a32 * b23 + a33 * b33 + a34 * b43;
            te[14] = a31 * b14 + a32 * b24 + a33 * b34 + a34 * b44;
            te[3] = a41 * b11 + a42 * b21 + a43 * b31 + a44 * b41;
            te[7] = a41 * b12 + a42 * b22 + a43 * b32 + a44 * b42;
            te[11] = a41 * b13 + a42 * b23 + a43 * b33 + a44 * b43;
            te[15] = a41 * b14 + a42 * b24 + a43 * b34 + a44 * b44;
            return te;
        }

        public static void SetPosition(ref double[] m, double x, double y, double z)
        { m[12] = x; m[13] = y; m[14] = z; }

        // ---- PolyhedronGeometry (icosahedron) exact replica with Float32 round-trips ----
        static readonly double PHI = (1 + Math.Sqrt(5)) / 2;

        static double[] Lerp(double[] a, double[] b, double al) =>
            new[] { a[0] + (b[0] - a[0]) * al, a[1] + (b[1] - a[1]) * al, a[2] + (b[2] - a[2]) * al };

        static void SubdivideFace(List<double[]> vertOut, double[] a, double[] b, double[] c, int detail)
        {
            int cols = detail + 1;
            var v = new double[cols + 1][][];
            for (int i = 0; i <= cols; i++)
            {
                v[i] = new double[cols - i + 1][];
                var aj = Lerp(a, c, (double)i / cols);
                var bj = Lerp(b, c, (double)i / cols);
                int rows = cols - i;
                for (int j = 0; j <= rows; j++)
                {
                    if (j == 0 && i == cols) v[i][j] = aj;
                    else v[i][j] = Lerp(aj, bj, (double)j / rows);
                }
            }
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < 2 * (cols - i) - 1; j++)
                {
                    int j1 = j / 2;
                    if (j % 2 == 0)
                    {
                        vertOut.Add(v[i][j1 + 1]); vertOut.Add(v[i + 1][j1]); vertOut.Add(v[i][j1]);
                    }
                    else
                    {
                        vertOut.Add(v[i][j1 + 1]); vertOut.Add(v[i + 1][j1 + 1]); vertOut.Add(v[i + 1][j1]);
                    }
                }
        }

        static void ApplyRadius(List<double[]> verts, double radius)
        {
            // Vector3.normalize(): multiplyScalar(1 / (length || 1))
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i];
                double len = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                double inv = 1.0 / (len != 0 ? len : 1);
                verts[i] = new[] { p[0] * inv * radius, p[1] * inv * radius, p[2] * inv * radius };
            }
        }

        // Angle around the Y axis, counter-clockwise when looking from above.
        static double Azimuth(double[] v) => Math.Atan2(v[2], -v[0]);

        // Angle above the XZ plane (note the -y).
        static double Inclination(double[] v)
        {
            double len = Math.Sqrt(v[0] * v[0] + v[2] * v[2]);
            return Math.Atan2(-v[1], len);
        }

        // THREE.PolyhedronGeometry pipeline → src[4][] { pos, nrm, uv, idx } (f32-rounded)
        public static double[][] PolyhedronIco(int detail, double radius)
        {
            double t = PHI;
            int[] icoIdx = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            double[,] V = {
                { -1, t, 0 }, { 1, t, 0 }, { -1, -t, 0 }, { 1, -t, 0 },
                { 0, -1, t }, { 0, 1, t }, { 0, -1, -t }, { 0, 1, -t },
                { t, 0, -1 }, { t, 0, 1 }, { -t, 0, -1 }, { -t, 0, 1 } };
            double[] A(int vi) => new[] { V[vi, 0], V[vi, 1], V[vi, 2] };
            var vertsRaw = new List<double[]>();
            for (int i = 0; i < icoIdx.Length; i += 3)
                SubdivideFace(vertsRaw, A(icoIdx[i]), A(icoIdx[i + 1]), A(icoIdx[i + 2]), detail);
            ApplyRadius(vertsRaw, radius);

            int n = vertsRaw.Count;
            var uvBuf = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                uvBuf[i * 2] = Azimuth(vertsRaw[i]) / 2 / Math.PI + 0.5;
                uvBuf[i * 2 + 1] = Inclination(vertsRaw[i]) / Math.PI + 0.5;
            }
            // correctUVs — per face (3 verts), centroid-based azimuth fix
            for (int i = 0; i < n; i += 3)
            {
                double inv3 = 1.0 / 3;
                double[] cen = {
                    (vertsRaw[i][0] + vertsRaw[i + 1][0] + vertsRaw[i + 2][0]) * inv3,
                    (vertsRaw[i][1] + vertsRaw[i + 1][1] + vertsRaw[i + 2][1]) * inv3,
                    (vertsRaw[i][2] + vertsRaw[i + 1][2] + vertsRaw[i + 2][2]) * inv3 };
                double azi = Azimuth(cen);
                for (int k = 0; k < 3; k++)
                {
                    int vi = i + k;
                    var v = vertsRaw[vi];
                    if (azi < 0 && uvBuf[vi * 2] == 1) uvBuf[vi * 2] -= 1;
                    if (v[0] == 0 && v[2] == 0) uvBuf[vi * 2] = azi / 2 / Math.PI + 0.5;
                }
            }
            // correctSeam
            for (int i = 0; i < uvBuf.Length; i += 6)
            {
                double x0 = uvBuf[i], x1 = uvBuf[i + 2], x2 = uvBuf[i + 4];
                double mx = Math.Max(x0, Math.Max(x1, x2)), mn = Math.Min(x0, Math.Min(x1, x2));
                if (mx > 0.9 && mn < 0.1)
                {
                    if (x0 < 0.2) uvBuf[i] += 1;
                    if (x1 < 0.2) uvBuf[i + 2] += 1;
                    if (x2 < 0.2) uvBuf[i + 4] += 1;
                }
            }

            // setAttribute Float32 round-trip: position, normal(=position copy), uv
            var pos = new double[n * 3]; var nrm = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                pos[i * 3] = F32(vertsRaw[i][0]); pos[i * 3 + 1] = F32(vertsRaw[i][1]); pos[i * 3 + 2] = F32(vertsRaw[i][2]);
                nrm[i * 3] = pos[i * 3]; nrm[i * 3 + 1] = pos[i * 3 + 1]; nrm[i * 3 + 2] = pos[i * 3 + 2];
            }
            var uv = new double[n * 2];
            for (int i = 0; i < uvBuf.Length; i++) uv[i] = F32(uvBuf[i]);
            // normalizeNormals: f32 read → normalize (multiplyScalar(1/len)) → f32 write
            for (int i = 0; i < n; i++)
            {
                double x = nrm[i * 3], y = nrm[i * 3 + 1], z = nrm[i * 3 + 2];
                double len = Math.Sqrt(x * x + y * y + z * z);
                double inv = 1.0 / (len != 0 ? len : 1);
                nrm[i * 3] = F32(x * inv); nrm[i * 3 + 1] = F32(y * inv); nrm[i * 3 + 2] = F32(z * inv);
            }
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            return new[] { pos, nrm, uv, idx.Select(x => (double)x).ToArray() };
        }

        // BufferGeometry.scale(sx, sy, sz) — applyMatrix4(makeScale) on f32 data, normal via normalMatrix + normalize
        public static void GeoScale(double[][] src, double sx, double sy, double sz)
        {
            var m = new double[] { sx, 0, 0, 0, 0, sy, 0, 0, 0, 0, sz, 0, 0, 0, 0, 1 };
            var nm = GetNormalMatrix(m);
            var pos = src[0]; var nrm = src[1];
            int n = pos.Length / 3;
            for (int i = 0; i < n; i++)
            {
                double[] v = { F32(pos[i * 3]), F32(pos[i * 3 + 1]), F32(pos[i * 3 + 2]) };
                ApplyMatrix4(v, m);
                pos[i * 3] = F32(v[0]); pos[i * 3 + 1] = F32(v[1]); pos[i * 3 + 2] = F32(v[2]);
                double[] w = { F32(nrm[i * 3]), F32(nrm[i * 3 + 1]), F32(nrm[i * 3 + 2]) };
                ApplyMatrix3(w, nm);
                double len = Math.Sqrt(w[0] * w[0] + w[1] * w[1] + w[2] * w[2]);
                double inv = 1.0 / (len != 0 ? len : 1);
                nrm[i * 3] = F32(w[0] * inv); nrm[i * 3 + 1] = F32(w[1] * inv); nrm[i * 3 + 2] = F32(w[2] * inv);
            }
        }

        // THREE.TorusGeometry(radius, tube, radialSegments, tubularSegments) exact replica (f32)
        public static double[][] TorusGeo(double radius, double tube, int radialSegments, int tubularSegments)
        {
            var pos = new List<double>(); var nrm = new List<double>(); var uv = new List<double>(); var idx = new List<double>();
            var center = new double[3]; var vertex = new double[3]; var normal = new double[3];
            for (int j = 0; j <= radialSegments; j++)
                for (int i = 0; i <= tubularSegments; i++)
                {
                    double u = (double)i / tubularSegments * Math.PI * 2;
                    double v = (double)j / radialSegments * Math.PI * 2;
                    vertex[0] = (radius + tube * Math.Cos(v)) * Math.Cos(u);
                    vertex[1] = (radius + tube * Math.Cos(v)) * Math.Sin(u);
                    vertex[2] = tube * Math.Sin(v);
                    center[0] = radius * Math.Cos(u); center[1] = radius * Math.Sin(u);
                    normal[0] = vertex[0] - center[0]; normal[1] = vertex[1] - center[1]; normal[2] = vertex[2] - center[2];
                    double len = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
                    double inv = 1.0 / (len != 0 ? len : 1);
                    pos.Add(F32(vertex[0])); pos.Add(F32(vertex[1])); pos.Add(F32(vertex[2]));
                    nrm.Add(F32(normal[0] * inv)); nrm.Add(F32(normal[1] * inv)); nrm.Add(F32(normal[2] * inv));
                    uv.Add(F32((double)i / tubularSegments)); uv.Add(F32((double)j / radialSegments));
                }
            for (int j = 1; j <= radialSegments; j++)
                for (int i = 1; i <= tubularSegments; i++)
                {
                    int a = (tubularSegments + 1) * (j - 1) + (i - 1);
                    int b = (tubularSegments + 1) * j + (i - 1);
                    int c = (tubularSegments + 1) * j + i;
                    int d = (tubularSegments + 1) * (j - 1) + i;
                    idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
                }
            return new[] { pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.ToArray() };
        }

        // THREE.SphereGeometry(radius, widthSegments, heightSegments, phiStart, phiLength, thetaStart, thetaLength) (f32)
        public static double[][] SphereGeo(double radius, int widthSegments, int heightSegments,
            double phiStart, double phiLength, double thetaStart, double thetaLength)
        {
            var pos = new List<double>(); var nrm = new List<double>(); var uv = new List<double>(); var idx = new List<double>();
            double thetaEnd = thetaStart + thetaLength;
            var grid = new List<int[]>();
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var row = new int[widthSegments + 1];
                double v = (double)iy / heightSegments;
                double theta = thetaStart + v * thetaLength;
                double y = radius * Math.Cos(theta);
                double ringRadius = Math.Sqrt(radius * radius - y * y);
                double uOffset = 0;
                if (iy == 0 && thetaStart == 0) uOffset = 0.5 / widthSegments;
                else if (iy == heightSegments && thetaEnd == Math.PI) uOffset = -0.5 / widthSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    double u = (double)ix / widthSegments;
                    double phi = phiStart + u * phiLength;
                    double x = -ringRadius * Math.Cos(phi);
                    double z = ringRadius * Math.Sin(phi);
                    pos.Add(F32(x)); pos.Add(F32(y)); pos.Add(F32(z));
                    double len = Math.Sqrt(x * x + y * y + z * z);
                    double inv = 1.0 / (len != 0 ? len : 1);
                    nrm.Add(F32(x * inv)); nrm.Add(F32(y * inv)); nrm.Add(F32(z * inv));
                    uv.Add(F32(u + uOffset)); uv.Add(F32(1 - v));
                    row[ix] = pos.Count / 3 - 1;
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < heightSegments; iy++)
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = grid[iy][ix + 1], b = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                    if (iy != 0 || thetaStart > 0) { idx.Add(a); idx.Add(b); idx.Add(d); }
                    if (iy != heightSegments - 1 || thetaEnd < Math.PI) { idx.Add(b); idx.Add(c); idx.Add(d); }
                }
            return new[] { pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.ToArray() };
        }
    }
    public class BAttrSet
    {
        public readonly Dictionary<string, double[]> Vals = new Dictionary<string, double[]>();
        public BAttrSet Set(string k, double[] v) { Vals[k] = v; return this; }
    }

    public class BBucket
    {
        public readonly string MatKey;
        public readonly List<double> Pos = new List<double>(), Nrm = new List<double>();
        public readonly List<double> Tan = new List<double>(), Uv = new List<double>();
        public readonly List<double> Idx = new List<double>();
        public readonly Dictionary<string, List<double>> Attrs;
        readonly Dictionary<string, double[]> _defs;

        public BBucket(string matKey, Dictionary<string, double[]> attrDefs)
        {
            MatKey = matKey;
            _defs = attrDefs;
            Attrs = new Dictionary<string, List<double>>();
            foreach (var k in attrDefs.Keys) Attrs[k] = new List<double>();
        }

        int PushVert(double[] p, double[] n, double[] t, double[] uv2, BAttrSet a)
        {
            Pos.Add(p[0]); Pos.Add(p[1]); Pos.Add(p[2]);
            Nrm.Add(n[0]); Nrm.Add(n[1]); Nrm.Add(n[2]);
            Tan.Add(t[0]); Tan.Add(t[1]); Tan.Add(t[2]); Tan.Add(1);
            Uv.Add(uv2[0]); Uv.Add(uv2[1]);
            foreach (var k in _defs.Keys)
            {
                var v = a != null && a.Vals.TryGetValue(k, out var val) ? val : _defs[k];
                Attrs[k].Add(v[0]); Attrs[k].Add(v[1]); Attrs[k].Add(v[2]); Attrs[k].Add(v[3]);
            }
            return Pos.Count / 3 - 1;
        }

        public void Quad(double[] p0, double[] p1, double[] p2, double[] p3, double[] uv0, double[] uv1, double[] uv2, double[] uv3, BAttrSet a, double[] n = null, double[] tan = null)
        {
            if (n == null)
            {
                var e1 = new[] { p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2] };
                var e2 = new[] { p3[0] - p0[0], p3[1] - p0[1], p3[2] - p0[2] };
                n = BGeom.NormArr(BGeom.Cross(e1, e2));
            }
            if (tan == null)
            {
                tan = BGeom.NormArr(new[] { p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2] });
            }
            int i0 = PushVert(p0, n, tan, uv0, a);
            PushVert(p1, n, tan, uv1, a);
            PushVert(p2, n, tan, uv2, a);
            PushVert(p3, n, tan, uv3, a);
            Idx.Add(i0); Idx.Add(i0 + 1); Idx.Add(i0 + 2);
            Idx.Add(i0); Idx.Add(i0 + 2); Idx.Add(i0 + 3);
        }

        public void VRect(double[] origin, double[] right, double w, double h, BAttrSet a, double u0 = 0, double? v0 = null)
        {
            double vv0 = v0 ?? origin[1];
            double nx = origin[0] + right[0] * w;
            double nz = origin[2] + right[2] * w;
            double y1 = origin[1] + h;
            Quad(
                new[] { origin[0], origin[1], origin[2] },
                new[] { nx, origin[1], nz },
                new[] { nx, y1, nz },
                new[] { origin[0], y1, origin[2] },
                new[] { u0, vv0 }, new[] { u0 + w, vv0 }, new[] { u0 + w, vv0 + h }, new[] { u0, vv0 + h },
                a);
        }

        // Axis-aligned box (optionally rotated around Y). UVs in meters per face — exact geom.js formulas.
        public void Box(double cx, double cy, double cz, double sx, double sy, double sz, BAttrSet a, double rotY = 0, HashSet<string> skip = null, double uvScale = 1)
        {
            double hx = sx / 2, hy = sy / 2, hz = sz / 2;
            double c = Math.Cos(rotY), s = Math.Sin(rotY);
            double[] Tr(double[] p) => new[] { cx + p[0] * c + p[2] * s, cy + p[1], cz - p[0] * s + p[2] * c };
            double[] Rd(double[] p) => new[] { p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c };
            double k = uvScale;
            double ox = cx + cz * 0.37, oy = cy, oz = cz + cx * 0.21;
            void F(string name, double[][] pts, double[] n, double[] t, double[][] uvs)
            {
                if (skip != null && skip.Contains(name)) return;
                Quad(Tr(pts[0]), Tr(pts[1]), Tr(pts[2]), Tr(pts[3]), uvs[0], uvs[1], uvs[2], uvs[3], a, Rd(n), Rd(t));
            }
            double[] UvZ() => new[] { (ox - hx) * k, (oy - hy) * k, (ox + hx) * k, (oy - hy) * k, (ox + hx) * k, (oy + hy) * k, (ox - hx) * k, (oy + hy) * k };
            double[] UvX() => new[] { (oz - hz) * k, (oy - hy) * k, (oz + hz) * k, (oy - hy) * k, (oz + hz) * k, (oy + hy) * k, (oz - hz) * k, (oy + hy) * k };
            double[] Slice(double[] u, int i) => new[] { u[i * 2], u[i * 2 + 1] };
            F("pz", new[] { new[] { -hx, -hy, hz }, new[] { hx, -hy, hz }, new[] { hx, hy, hz }, new[] { -hx, hy, hz } }, new[] { 0.0, 0, 1 }, new[] { 1.0, 0, 0 }, new[] { Slice(UvZ(), 0), Slice(UvZ(), 1), Slice(UvZ(), 2), Slice(UvZ(), 3) });
            F("nz", new[] { new[] { hx, -hy, -hz }, new[] { -hx, -hy, -hz }, new[] { -hx, hy, -hz }, new[] { hx, hy, -hz } }, new[] { 0.0, 0, -1 }, new[] { -1.0, 0, 0 }, new[] { Slice(UvZ(), 0), Slice(UvZ(), 1), Slice(UvZ(), 2), Slice(UvZ(), 3) });
            F("px", new[] { new[] { hx, -hy, hz }, new[] { hx, -hy, -hz }, new[] { hx, hy, -hz }, new[] { hx, hy, hz } }, new[] { 1.0, 0, 0 }, new[] { 0.0, 0, -1 }, new[] { Slice(UvX(), 0), Slice(UvX(), 1), Slice(UvX(), 2), Slice(UvX(), 3) });
            F("nx", new[] { new[] { -hx, -hy, -hz }, new[] { -hx, -hy, hz }, new[] { -hx, hy, hz }, new[] { -hx, hy, -hz } }, new[] { -1.0, 0, 0 }, new[] { 0.0, 0, 1 }, new[] { Slice(UvX(), 0), Slice(UvX(), 1), Slice(UvX(), 2), Slice(UvX(), 3) });
            F("py", new[] { new[] { -hx, hy, hz }, new[] { hx, hy, hz }, new[] { hx, hy, -hz }, new[] { -hx, hy, -hz } }, new[] { 0.0, 1, 0 }, new[] { 1.0, 0, 0 }, new[] { new[] { (ox - hx) * k, (oz + hz) * k }, new[] { (ox + hx) * k, (oz + hz) * k }, new[] { (ox + hx) * k, (oz - hz) * k }, new[] { (ox - hx) * k, (oz - hz) * k } });
            F("ny", new[] { new[] { -hx, -hy, -hz }, new[] { hx, -hy, -hz }, new[] { hx, -hy, hz }, new[] { -hx, -hy, hz } }, new[] { 0.0, -1, 0 }, new[] { 1.0, 0, 0 }, new[] { new[] { (ox - hx) * k, (oz - hz) * k }, new[] { (ox + hx) * k, (oz - hz) * k }, new[] { (ox + hx) * k, (oz + hz) * k }, new[] { (ox - hx) * k, (oz + hz) * k } });
        }

        public void Beam(double[] p0, double[] p1, double th, BAttrSet a, double? th2 = null)
        {
            double dx = p1[0] - p0[0], dy = p1[1] - p0[1], dz = p1[2] - p0[2];
            double len = BGeom.Hyp3(dx, dy, dz);
            if (len < 1e-4) return;
            var dir = new[] { dx / len, dy / len, dz / len };
            BGeom.SetFromUnitVectors(new[] { 0.0, 1.0, 0.0 }, dir, out var qx, out var qy, out var qz, out var qw);
            var mid = new[] { (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2 };
            var m = BGeom.Compose(mid, (qx, qy, qz, qw), new[] { 1.0, 1.0, 1.0 });
            Geom(BGeom.UnitBox(), m, a, new[] { th, len, th2 ?? th });
        }

        public void Cyl(double[] p0, double[] p1, double r, BAttrSet a, int seg = 6, double? r1 = null)
        {
            double dx = p1[0] - p0[0], dy = p1[1] - p0[1], dz = p1[2] - p0[2];
            double len = BGeom.Hyp3(dx, dy, dz);
            if (len < 1e-4) return;
            var dir = new[] { dx / len, dy / len, dz / len };
            BGeom.SetFromUnitVectors(new[] { 0.0, 1.0, 0.0 }, dir, out var qx, out var qy, out var qz, out var qw);
            var mid = new[] { (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2 };
            var m = BGeom.Compose(mid, (qx, qy, qz, qw), new[] { 1.0, 1.0, 1.0 });
            Geom(BGeom.UnitCyl(seg), m, a, new[] { r, len, r }); // r1 accepted but unused, faithful to JS
        }

        // THREE geometry.applyMatrix4(m) with optional pre-scale, exact geom.js semantics.
        public void Geom(double[][] src, double[] m, BAttrSet a, double[] scale = null)
        {
            var pos = src[0]; var nrm = src[1]; var uv = src[2]; var idx = src[3];
            var nm = BGeom.GetNormalMatrix(m);
            int baseIdx = Pos.Count / 3;
            int nv = pos.Length / 3;
            for (int i = 0; i < nv; i++)
            {
                double x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
                if (scale != null) { x *= scale[0]; y *= scale[1]; z *= scale[2]; }
                var v = new[] { x, y, z };
                BGeom.ApplyMatrix4(v, m);
                var n = new[] { nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2] };
                if (scale != null) { n[0] /= scale[0]; n[1] /= scale[1]; n[2] /= scale[2]; }
                BGeom.ApplyMatrix3(n, nm);
                // THREE.Vector3.normalize(): plain sqrt, divide (length || 1)
                // THREE.Vector3.normalize(): divideScalar(l || 1) → multiplyScalar(1/l)
                double nl = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                if (nl == 0) nl = 1;
                double ninv = 1.0 / nl;
                n[0] *= ninv; n[1] *= ninv; n[2] *= ninv;
                Pos.Add(v[0]); Pos.Add(v[1]); Pos.Add(v[2]);
                Nrm.Add(n[0]); Nrm.Add(n[1]); Nrm.Add(n[2]);
                // tangent: any perpendicular (geom.js norm() = 3-arg hypot rule)
                double[] t = Math.Abs(n[1]) < 0.9
                    ? BGeom.NormArr(BGeom.Cross(new[] { 0.0, 1.0, 0.0 }, n))
                    : new[] { 1.0, 0.0, 0.0 };
                Tan.Add(t[0]); Tan.Add(t[1]); Tan.Add(t[2]); Tan.Add(1);
                Uv.Add(uv[i * 2]); Uv.Add(uv[i * 2 + 1]);
                foreach (var k in _defs.Keys)
                {
                    var val = a != null && a.Vals.TryGetValue(k, out var av) ? av : _defs[k];
                    Attrs[k].Add(val[0]); Attrs[k].Add(val[1]); Attrs[k].Add(val[2]); Attrs[k].Add(val[3]);
                }
            }
            foreach (double ii in idx) Idx.Add(baseIdx + ii);
        }
    }

    public class BChunkedBuckets
    {
        public readonly Dictionary<string, BBucket> Map = new Dictionary<string, BBucket>();
        readonly double _chunkSize;
        public BChunkedBuckets(double chunkSize = 96) { _chunkSize = chunkSize; }

        public BBucket Get(double x, double z, string matKey, Dictionary<string, double[]> attrDefs)
        {
            string key = $"{Math.Floor(x / _chunkSize)},{Math.Floor(z / _chunkSize)}|{matKey}";
            if (!Map.TryGetValue(key, out var b))
            {
                b = new BBucket(matKey, attrDefs);
                Map[key] = b;
            }
            return b;
        }

    }
}
