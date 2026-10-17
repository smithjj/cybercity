// Oracle: ground geometry — C# GroundGeo vs the real JS buildGroundGeometry (node dump).
// Triangle MULTISETS (canonicalized) so triangulation order may differ; positions/normals/uvs
// must be bit-exact float32. The JS builds quads via PlaneGeometry().rotateX(-PI/2).translate(),
// which leaves a cos(-PI/2)*(±h/2) artifact in y (and cos in normal z) wherever the translate
// doesn't absorb it (cy == 0) — emulated here exactly (QuadVerts/NUpZ).
// Rects and sidewalk colliders compared as sorted exact double multisets.
// Dump: tools/dump-ground.mjs. Run: tools/run-oracle-bg.sh GroundGeo
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NeonKaido;
using Geo = NeonKaido.GroundGeo;

namespace NeonKaido.EditorTools.Oracles
{
    public static class GroundGeo
    {
        const double COS = 6.123233995736766e-17; // Math.cos(-Math.PI/2), same value in JS
        static double F32r(double v) => (double)(float)v;

        // JS quad(r, y): PlaneGeometry(w,h) local verts [(-w/2,+h/2),(+w/2,+h/2),(-w/2,-h/2),(+w/2,-h/2)]
        // at z=0 -> rotateX(-PI/2): y'=COS*y, z'=-y -> translate(cx, y, cz). Returns 4 sim-space verts.
        static List<double[]> QuadVerts(double x0, double x1, double z0, double z1, double cy)
        {
            double w = x1 - x0, h = z1 - z0;
            double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            var lx = new[] { F32r(-w / 2), F32r(w / 2), F32r(-w / 2), F32r(w / 2) };
            var ly = new[] { F32r(h / 2), F32r(h / 2), F32r(-h / 2), F32r(-h / 2) };
            var verts = new List<double[]>();
            for (int i = 0; i < 4; i++)
            {
                double yR = F32r(COS * ly[i]);          // z_local = 0
                double zR = F32r(-ly[i]);               // sin = -1
                verts.Add(new[] { F32r(lx[i] + cx), F32r(yR + cy), F32r(zR + cz) });
            }
            return verts;
        }

        static double[] NUpZ() => new[] { 0.0, 1.0, F32r(COS) }; // rotateX(-PI/2) normal (0,0,1)

        // canonical triangle key: 3 tuples of (pos,nor,uv) hex sorted lexicographically
        static string TriKey(List<string[]> pos, List<string[]> nor, List<string[]> uv, int a, int b, int c)
        {
            var t = new[] {
                string.Join("|", pos[a]) + "#" + string.Join("|", nor[a]) + "#" + string.Join("|", uv[a]),
                string.Join("|", pos[b]) + "#" + string.Join("|", nor[b]) + "#" + string.Join("|", uv[b]),
                string.Join("|", pos[c]) + "#" + string.Join("|", nor[c]) + "#" + string.Join("|", uv[c]),
            };
            Array.Sort(t, StringComparer.Ordinal);
            return t[0] + "@" + t[1] + "@" + t[2];
        }

        static List<string> TrianglesOf(JsonElement geo)
        {
            var pos = HexArr(geo.GetProperty("pos"), 3);
            var nor = HexArr(geo.GetProperty("nor"), 3);
            var uv = HexArr(geo.GetProperty("uv"), 2);
            var idxEl = geo.GetProperty("idx");
            int verts = pos.Count;
            var tris = new List<string>();
            if (idxEl.ValueKind == JsonValueKind.Null)
            {
                for (int t = 0; t < verts; t += 3)
                    tris.Add(TriKey(pos, nor, uv, t, t + 1, t + 2));
            }
            else
            {
                var idx = new int[idxEl.GetArrayLength()];
                for (int i = 0; i < idx.Length; i++) idx[i] = idxEl[i].GetInt32();
                for (int t = 0; t < idx.Length; t += 3)
                    tris.Add(TriKey(pos, nor, uv, idx[t], idx[t + 1], idx[t + 2]));
            }
            tris.Sort(StringComparer.Ordinal);
            return tris;
        }

        static List<string[]> HexArr(JsonElement arr, int stride)
        {
            var list = new List<string[]>();
            for (int i = 0; i < arr.GetArrayLength(); i += stride)
            {
                var tuple = new string[stride];
                for (int k = 0; k < stride; k++) tuple[k] = arr[i + k].GetString();
                list.Add(tuple);
            }
            return list;
        }

        // C# side: convert doubles to float32 hex
        static string F32(double v) => BitConverter.SingleToInt32Bits((float)v).ToString("x8");

        static List<string> CsTriangles(List<double[]> pos, List<double[]> nor, List<double[]> uv, int[] idx)
        {
            string[] p = pos.Select(v => string.Join("|", v.Select(F32))).ToArray();
            string[] n = nor.Select(v => string.Join("|", v.Select(F32))).ToArray();
            string[] u = uv.Select(v => string.Join("|", v.Select(F32))).ToArray();
            var tris = new List<string>();
            if (idx == null)
            {
                for (int t = 0; t < pos.Count; t += 3) tris.Add(TriKeyCs(p, n, u, t, t + 1, t + 2));
            }
            else
            {
                for (int t = 0; t < idx.Length; t += 3) tris.Add(TriKeyCs(p, n, u, idx[t], idx[t + 1], idx[t + 2]));
            }
            tris.Sort(StringComparer.Ordinal);
            return tris;
        }

        static string TriKeyCs(string[] p, string[] n, string[] u, int a, int b, int c)
        {
            var t = new[] { p[a] + "#" + n[a] + "#" + u[a], p[b] + "#" + n[b] + "#" + u[b], p[c] + "#" + n[c] + "#" + u[c] };
            Array.Sort(t, StringComparer.Ordinal);
            return t[0] + "@" + t[1] + "@" + t[2];
        }

        static string Hex64(double v) => BitConverter.DoubleToInt64Bits(v).ToString("x16");

        // Unity's System.Text.Json has no JsonElement indexer — read arrays via EnumerateArray.
        static string[] A(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()).ToArray();

        // quad mesh assembly shared by base/alley/promenade (JS quad layout)
        static void AddQuadMesh(List<double[]> pos, List<double[]> nor, List<double[]> uv, List<int> idx,
            double x0, double x1, double z0, double z1, double cy)
        {
            int i0 = pos.Count;
            pos.AddRange(QuadVerts(x0, x1, z0, z1, cy));
            var n = NUpZ();
            for (int k = 0; k < 4; k++) nor.Add(n);
            uv.Add(new[] { 0.0, 1.0 }); uv.Add(new[] { 1.0, 1.0 }); uv.Add(new[] { 0.0, 0.0 }); uv.Add(new[] { 1.0, 0.0 });
            idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1);
            idx.Add(i0 + 2); idx.Add(i0 + 3); idx.Add(i0 + 1);
        }

        static void AddWallMesh(List<double[]> pos, List<double[]> nor, List<double[]> uv, List<int> idx,
            double bankX, double zA, double zB, double wallH, double faceX)
        {
            // rotateY(±PI/2) artifacts are absorbed by the large translate — analytic verts are exact
            int i0 = pos.Count;
            if (faceX > 0)
            {
                pos.Add(new[] { bankX, 0.0, zB }); pos.Add(new[] { bankX, 0.0, zA });
                pos.Add(new[] { bankX, -wallH, zB }); pos.Add(new[] { bankX, -wallH, zA });
                for (int k = 0; k < 4; k++) nor.Add(new[] { 1.0, 0.0, F32r(COS) }); // rotateY(pi/2) artifact in z
            }
            else
            {
                pos.Add(new[] { bankX, 0.0, zA }); pos.Add(new[] { bankX, 0.0, zB });
                pos.Add(new[] { bankX, -wallH, zA }); pos.Add(new[] { bankX, -wallH, zB });
                for (int k = 0; k < 4; k++) nor.Add(new[] { -1.0, 0.0, F32r(COS) });
            }
            uv.Add(new[] { 0.0, 1.0 }); uv.Add(new[] { 1.0, 1.0 }); uv.Add(new[] { 0.0, 0.0 }); uv.Add(new[] { 1.0, 0.0 });
            idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1);
            idx.Add(i0 + 2); idx.Add(i0 + 3); idx.Add(i0 + 1);
        }

        public static void Compare(string arg)
        {
            string dump = string.IsNullOrEmpty(arg) ? "Temp/oracle/dump-ground.json" : arg;
            var doc = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(dump)));
            var root = doc.RootElement;
            var data = Geo.BuildGroundGeometry();
            var sb = new StringBuilder();
            int fails = 0;

            // --- all six meshes as triangle multisets ---
            var pos = new List<double[]>(); var nor = new List<double[]>(); var uv = new List<double[]>(); var idx = new List<int>();
            foreach (var r in data.BaseRects) AddQuadMesh(pos, nor, uv, idx, r.X0, r.X1, r.Z0, r.Z1, 0);
            var csBase = CsTriangles(pos, nor, uv, idx.ToArray());

            pos = new List<double[]>(); nor = new List<double[]>(); uv = new List<double[]>(); idx = new List<int>();
            AddQuadMesh(pos, nor, uv, idx, data.AlleyRects[0].X0, data.AlleyRects[0].X1, data.AlleyRects[0].Z0, data.AlleyRects[0].Z1, 0);
            AddQuadMesh(pos, nor, uv, idx, data.AlleyRects[1].X0, data.AlleyRects[1].X1, data.AlleyRects[1].Z0, data.AlleyRects[1].Z1, GroundPlan.StairsRise);
            var csAlley = CsTriangles(pos, nor, uv, idx.ToArray());

            pos = new List<double[]>(); nor = new List<double[]>(); uv = new List<double[]>(); idx = new List<int>();
            AddQuadMesh(pos, nor, uv, idx, data.Promenade.X0, data.Promenade.X1, data.Promenade.Z0, data.Promenade.Z1, GroundPlan.PromY);
            var csProm = CsTriangles(pos, nor, uv, idx.ToArray());

            pos = new List<double[]>(); nor = new List<double[]>(); uv = new List<double[]>(); idx = new List<int>();
            AddWallMesh(pos, nor, uv, idx, data.WallWest.X0, data.WallWest.Z0, data.WallWest.Z1, data.WallH, 1);
            AddWallMesh(pos, nor, uv, idx, data.WallEast.X0, data.WallEast.Z0, data.WallEast.Z1, data.WallH, -1);
            var csWall = CsTriangles(pos, nor, uv, idx.ToArray());

            var csCurb = CsTriangles(data.Curb.Pos, data.Curb.Nor, data.Curb.Uv, null);

            // sidewalk: concatenated per-poly Inner verts (y = curbH exactly — artifact absorbed),
            // normals (0,1,COSz) from rotateX, shape-space uv, ear-clip indices
            pos = new List<double[]>(); nor = new List<double[]>(); uv = new List<double[]>(); idx = new List<int>();
            foreach (var poly in data.Sidewalks)
            {
                int i0 = pos.Count;
                foreach (var p in poly.Inner)
                {
                    pos.Add(new[] { p[0], GroundPlan.CurbH, p[1] });
                    nor.Add(NUpZ());
                    uv.Add(new[] { p[0], -p[1] });
                }
                foreach (int t in poly.Indices) idx.Add(i0 + t);
            }
            var csSw = CsTriangles(pos, nor, uv, idx.ToArray());

            var csByName = new Dictionary<string, List<string>>
            {
                ["base"] = csBase, ["curb"] = csCurb, ["sidewalk"] = csSw,
                ["alley"] = csAlley, ["promenade"] = csProm, ["wall"] = csWall,
            };
            foreach (var geoEl in root.GetProperty("geoms").EnumerateArray())
            {
                string name = geoEl.GetProperty("name").GetString();
                if (!csByName.TryGetValue(name, out var cs)) continue;
                var want = TrianglesOf(geoEl);
                var (same, msg) = Diff(want, cs);
                if (!same) { fails++; sb.AppendLine($"FAIL {name}: {msg}"); }
                else sb.AppendLine($"PASS {name}: {cs.Count} tris bit-exact (multiset)");
            }

            // --- baseRects as sorted multiset of hex tuples (JS dumps objects {x0,x1,z0,z1}) ---
            var wantRects = root.GetProperty("baseRects").EnumerateArray()
                .Select(r => r.GetProperty("x0").GetString() + "," + r.GetProperty("x1").GetString() + "," +
                             r.GetProperty("z0").GetString() + "," + r.GetProperty("z1").GetString())
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            var csRects = data.BaseRects
                .Select(r => Hex64(r.X0) + "," + Hex64(r.X1) + "," + Hex64(r.Z0) + "," + Hex64(r.Z1))
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (!wantRects.SequenceEqual(csRects))
            {
                fails++;
                sb.AppendLine($"FAIL baseRects: {wantRects.Count} vs {csRects.Count}");
                for (int i = 0; i < Math.Min(3, Math.Min(wantRects.Count, csRects.Count)); i++)
                    if (wantRects[i] != csRects[i]) { sb.AppendLine($"  [{i}] want {wantRects[i]} got {csRects[i]}"); break; }
            }
            else sb.AppendLine($"PASS baseRects: {csRects.Count} rects exact");

            // --- swColliders ---
            var wantCols = root.GetProperty("swColliders").EnumerateArray()
                .Select(p => string.Join(";", p.EnumerateArray().Select(pt => string.Join(",", A(pt)))))
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            var csCols = data.Sidewalks
                .Select(p => string.Join(";", p.Collider.Select(pt => Hex64(pt[0]) + "," + Hex64(pt[1]))))
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (!wantCols.SequenceEqual(csCols))
            {
                fails++;
                sb.AppendLine($"FAIL swColliders: {wantCols.Count} vs {csCols.Count}");
                for (int i = 0; i < Math.Min(wantCols.Count, csCols.Count); i++)
                    if (wantCols[i] != csCols[i])
                    {
                        sb.AppendLine($"  poly {i}\n  want {wantCols[i].Substring(0, Math.Min(200, wantCols[i].Length))}\n  got  {csCols[i].Substring(0, Math.Min(200, csCols[i].Length))}");
                        break;
                    }
            }
            else sb.AppendLine($"PASS swColliders: {csCols.Count} polys exact");

            sb.AppendLine(fails == 0 ? "PASS ground geometry: all meshes + rects + colliders bit-exact"
                                     : $"FAIL ground geometry: {fails} section(s) differ");
            Directory.CreateDirectory("Temp/oracle");
            File.WriteAllText("Temp/oracle/report.txt", sb.ToString());
        }

        static (bool, string) Diff(List<string> want, List<string> got)
        {
            if (want.Count != got.Count) return (false, $"count {got.Count} vs {want.Count}");
            for (int i = 0; i < want.Count; i++)
                if (want[i] != got[i])
                {
                    string W = want[i].Length > 240 ? want[i].Substring(0, 240) + "…" : want[i];
                    string G = got[i].Length > 240 ? got[i].Substring(0, 240) + "…" : got[i];
                    return (false, $"tri {i}\n  want {W}\n  got  {G}");
                }
            return (true, "");
        }
    }
}
#endif
