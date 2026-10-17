// Oracle: compares the C# BuildPlan output against the node dump (dump-buildings.mjs).
// Pure data comparison — safe to run on a worker thread via run-oracle-bg.sh.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NeonKaido.EditorTools.Oracles
{
    public static class BuildingsOracle
    {
        static int _bad;
        static readonly System.Collections.Generic.Dictionary<string, int> _cats = new System.Collections.Generic.Dictionary<string, int>();
        static readonly StringBuilder _sb = new StringBuilder();

        static void Fail(string msg)
        {
            if (_bad < 400) _sb.AppendLine(msg);
            _bad++;
            int sp = msg.IndexOf(':');
            var keyB = new System.Text.StringBuilder();
            bool skipBr = false;
            foreach (var ch in (sp > 0 ? msg.Substring(0, sp) : msg))
            {
                if (ch == '[') { skipBr = true; continue; }
                if (ch == ']') { skipBr = false; continue; }
                if (!skipBr && !char.IsDigit(ch)) keyB.Append(ch);
            }
            string key = keyB.ToString();
            _cats[key] = _cats.TryGetValue(key, out var c2) ? c2 + 1 : 1;
        }

        static void D(string label, double a, string hexB, string ctx)
        {
            long jb = long.Parse(hexB, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            long ab = BitConverter.DoubleToInt64Bits(a);
            if (ab != jb) Fail($"{ctx} {label}: C#={a:R}({ab:X16}) JS={hexB}");
        }
        static void I(string label, long a, long b, string ctx)
        {
            if (a != b) Fail($"{ctx} {label}: C#={a} JS={b}");
        }
        static void S(string label, string a, string b, string ctx)
        {
            if (a != b) Fail($"{ctx} {label}: C#='{a}' JS='{b}'");
        }

        static int Num(JsonElement e, string f)
        {
            foreach (var p in e.EnumerateObject()) if (p.Name == f) return p.Value.GetInt32();
            return -1;
        }
        static string Hex(JsonElement e, string f)
        {
            foreach (var p in e.EnumerateObject()) if (p.Name == f) return p.Value.GetString();
            return null;
        }
        static string Str(JsonElement e, string f)
        {
            foreach (var p in e.EnumerateObject()) if (p.Name == f && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
            return null;
        }
        static bool Bool(JsonElement e, string f)
        {
            foreach (var p in e.EnumerateObject()) if (p.Name == f) return p.Value.ValueKind == JsonValueKind.True;
            return false;
        }

        static long SumList(List<double> l) { long s = 0; foreach (var v in l) s += (long)v; return s; }

        static readonly object _gate = new object();

        public static void Compare(string arg)
        {
            // re-entrancy guard: eval retries can queue concurrent runs sharing static _sb
            if (!System.Threading.Monitor.TryEnter(_gate)) return;
            try
            {
            _bad = 0;
            _sb.Clear();
            string dump = string.IsNullOrEmpty(arg)
                ? (File.Exists(Path.GetFullPath("Temp/oracle/buildings-overlook.json"))
                    ? Path.GetFullPath("Temp/oracle/buildings-overlook.json")
                    : File.Exists(Path.GetFullPath("Temp/oracle/buildings-alley.json"))
                    ? Path.GetFullPath("Temp/oracle/buildings-alley.json")
                    : File.Exists(Path.GetFullPath("Temp/oracle/buildings-props.json"))
                        ? Path.GetFullPath("Temp/oracle/buildings-props.json")
                        : File.Exists(Path.GetFullPath("Temp/oracle/buildings-shops.json"))
                            ? Path.GetFullPath("Temp/oracle/buildings-shops.json")
                            : Path.GetFullPath("Temp/oracle/buildings-arch.json"))
                : Path.GetFullPath(arg);
            if (!File.Exists(dump)) { File.WriteAllText("Temp/oracle/report.txt", $"MISSING DUMP: {dump}"); return; }

            var doc = JsonDocument.Parse(File.ReadAllText(dump));
            var jsLots = doc.RootElement.GetProperty("lots");

            var csLots = BuildPlan.Run();

            // architecture stage (fills volumes/sides, builds buckets) — needed by both compares below
            var B = new BChunkedBuckets(96);
            var outp = new BOut();
            foreach (var l in csLots) BuildArchitecture.BuildLotArchitecture(l, B, outp);
            // shops + props + alley + overlook + cables stages (when the dump includes them)
            bool full = dump.EndsWith("buildings.json");
            bool withOverlook = full || dump.EndsWith("buildings-overlook.json");
            bool withAlley = full || withOverlook || dump.EndsWith("buildings-alley.json");
            bool withProps = full || withAlley || dump.EndsWith("buildings-props.json");
            bool withShops = full || withProps || dump.EndsWith("buildings-shops.json");
            _sb.AppendLine($"dump={Path.GetFileName(dump)} withProps={withProps} withShops={withShops} withAlley={withAlley} withOverlook={withOverlook} full={full}");
            if (withShops) new ShopBuilder(B, outp).BuildAll(csLots);
            if (withProps) Props.BuildProps(B, csLots, outp);
            if (withAlley) Alley.BuildAlley(B, outp);
            if (withOverlook) Overlook.BuildOverlook(B, csLots, outp);
            if (full) Cables.BuildCables(B, outp);

            if (csLots.Count != jsLots.GetArrayLength())
                Fail($"lot count: C#={csLots.Count} JS={jsLots.GetArrayLength()}");

            int n = Math.Min(csLots.Count, jsLots.GetArrayLength());
            for (int i = 0; i < n; i++)
            {
                var j = jsLots[i];
                var l = csLots[i];
                string ctx = $"lot[{i}] id={l.Id}";
                I("id", l.Id, Num(j, "id"), ctx);
                D("x0", l.X0, Hex(j, "x0"), ctx); D("x1", l.X1, Hex(j, "x1"), ctx);
                D("z0", l.Z0, Hex(j, "z0"), ctx); D("z1", l.Z1, Hex(j, "z1"), ctx);
                D("baseY", l.BaseY, Hex(j, "baseY"), ctx);
                D("h", l.H, Hex(j, "h"), ctx);
                I("floors", l.Floors, (int)Num(j, "floors"), ctx);
                D("floorH", l.FloorH, Hex(j, "floorH"), ctx);
                D("groundH", l.GroundH, Hex(j, "groundH"), ctx);
                S("style", l.Style, Str(j, "style"), ctx);
                S("detail", l.Detail, Str(j, "detail"), ctx);
                D("seed", l.Seed, Hex(j, "seed"), ctx);
                D("roofKind", l.RoofKind, Hex(j, "roofKind"), ctx);
                if (l.Tower != Bool(j, "tower")) Fail($"{ctx} tower: C#={l.Tower} JS={Bool(j, 't' + "ower")}");
                var jsSetback = j.GetProperty("setback");
                if (l.Setback == null)
                {
                    if (jsSetback.ValueKind != JsonValueKind.Null) Fail($"{ctx} setback: C#=null JS=present");
                }
                else
                {
                    if (jsSetback.ValueKind == JsonValueKind.Null) Fail($"{ctx} setback: C#=present JS=null");
                    else
                    {
                        I("setback.atFloor", l.Setback.AtFloor, (int)Num(jsSetback, "atFloor"), ctx);
                        D("setback.inset", l.Setback.Inset, Hex(jsSetback, "inset"), ctx);
                    }
                }
                // cover
                var jc = j.GetProperty("cover");
                D("cover.nx", l.CoverNx, Hex(jc, "nx"), ctx);
                D("cover.px", l.CoverPx, Hex(jc, "px"), ctx);
                D("cover.nz", l.CoverNz, Hex(jc, "nz"), ctx);
                D("cover.pz", l.CoverPz, Hex(jc, "pz"), ctx);
            // fronts
            var jf = j.GetProperty("fronts");
                if (l.Fronts.Count != jf.GetArrayLength())
                    Fail($"{ctx} fronts count: C#={l.Fronts.Count} JS={jf.GetArrayLength()}");
                int fn = Math.Min(l.Fronts.Count, jf.GetArrayLength());
                for (int k = 0; k < fn; k++)
                {
                    var f = l.Fronts[k];
                    var jfi = jf[k];
                    string fctx = $"{ctx} front[{k}]";
                    S("side", f.Side, Str(jfi, "side"), fctx);
                    S("district", f.District, Str(jfi, "district"), fctx);
                    if (f.Shop != Bool(jfi, "shop")) Fail($"{fctx} shop: C#={f.Shop} JS={Bool(jfi, 's' + "hop")}");
                    if (f.Lane != Bool(jfi, "lane")) Fail($"{fctx} lane: C#={f.Lane} JS={Bool(jfi, 'l' + "ane")}");
                    D("baseY", f.BaseY ?? 0, Hex(jfi, "baseY"), fctx);
                    S("shopType", f.ShopType ?? "", Str(jfi, "shopType") ?? "", fctx);
                }
                // volumes (architecture stage)
                var jv = j.GetProperty("volumes");
                var cv = l.Volumes ?? new List<BVol>();
                if (cv.Count != jv.GetArrayLength()) Fail($"{ctx} volumes count: C#={cv.Count} JS={jv.GetArrayLength()}");
                int vn = Math.Min(cv.Count, jv.GetArrayLength());
                for (int k = 0; k < vn; k++)
                {
                    var vi2 = jv[k];
                    D($"vol[{k}].x0", cv[k].X0, vi2[0].GetString(), ctx);
                    D($"vol[{k}].x1", cv[k].X1, vi2[1].GetString(), ctx);
                    D($"vol[{k}].z0", cv[k].Z0, vi2[2].GetString(), ctx);
                    D($"vol[{k}].z1", cv[k].Z1, vi2[3].GetString(), ctx);
                    D($"vol[{k}].y0", cv[k].Y0, vi2[4].GetString(), ctx);
                    D($"vol[{k}].y1", cv[k].Y1, vi2[5].GetString(), ctx);
                }
            }

            // ---- bucket comparison (architecture stage) ----
            var jsBuckets = doc.RootElement.GetProperty("buckets");
            if (B.Map.Count != jsBuckets.GetArrayLength())
                Fail($"bucket count: C#={B.Map.Count} JS={jsBuckets.GetArrayLength()}");
            double JHex(JsonElement e) => BitConverter.Int64BitsToDouble(long.Parse(e.GetString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
            void T(string label, double a, double b, string bctx)
            {
                if (Math.Abs(a - b) > 3e-7 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)))) // f32-scale: V8 vs Mono libm noise in f32-stored shape data
                    Fail($"{bctx} {label}: C#={a:R}({BitConverter.DoubleToInt64Bits(a):X16}) JS={b:R}({BitConverter.DoubleToInt64Bits(b):X16})");
            }
            int bn = Math.Min(B.Map.Count, jsBuckets.GetArrayLength());
            for (int i = 0; i < bn; i++)
            {
                var jb = jsBuckets[i];
                string key = Str(jb, "key");
                string bctx = $"bucket[{i}] '{key}'";
                if (!B.Map.TryGetValue(key, out var b)) { Fail($"{bctx}: MISSING in C#"); continue; }
                I("verts", b.Pos.Count / 3, (int)Num(jb, "verts"), bctx);
                I("tris", b.Idx.Count / 3, (int)Num(jb, "tris"), bctx);
                I("idxsum", SumList(b.Idx), long.Parse(jb.GetProperty("idxsum").GetRawText(), System.Globalization.CultureInfo.InvariantCulture), bctx);
                var bb = jb.GetProperty("bbox");
                double mnx = 1e30, mny = 1e30, mnz = 1e30, mxx = -1e30, mxy = -1e30, mxz = -1e30;
                double sx = 0, sy = 0, sz = 0;
                for (int p = 0; p < b.Pos.Count; p += 3)
                {
                    double x = b.Pos[p], y = b.Pos[p + 1], z = b.Pos[p + 2];
                    if (x < mnx) mnx = x; if (x > mxx) mxx = x;
                    if (y < mny) mny = y; if (y > mxy) mxy = y;
                    if (z < mnz) mnz = z; if (z > mxz) mxz = z;
                    sx += x; sy += y; sz += z;
                }
                T("bbox.x0", mnx, JHex(bb[0]), bctx); T("bbox.y0", mny, JHex(bb[1]), bctx); T("bbox.z0", mnz, JHex(bb[2]), bctx);
                T("bbox.x1", mxx, JHex(bb[3]), bctx); T("bbox.y1", mxy, JHex(bb[4]), bctx); T("bbox.z1", mxz, JHex(bb[5]), bctx);
                T("possum.x", sx, JHex(jb.GetProperty("sums")[0]), bctx);
                T("possum.y", sy, JHex(jb.GetProperty("sums")[1]), bctx);
                T("possum.z", sz, JHex(jb.GetProperty("sums")[2]), bctx);
                double ns = 0; foreach (var v in b.Nrm) ns += v;
                T("nrmsum", ns, JHex(jb.GetProperty("nsum")), bctx);
                double us = 0; foreach (var v in b.Uv) us += v;
                T("uvsum", us, JHex(jb.GetProperty("uvsum")), bctx);
                if (jb.TryGetProperty("ys", out var jys))
                {
                    var ys = new List<double>();
                    for (int p = 1; p < b.Pos.Count; p += 3) ys.Add(b.Pos[p]);
                    ys.Sort();
                    if (ys.Count != jys.GetArrayLength()) Fail($"{bctx} ys count: C#={ys.Count} JS={jys.GetArrayLength()}");
                    else
                    {
                        int yn = ys.Count, yd = 0;
                        for (int p = 0; p < yn && yd < 5; p++)
                        {
                            long jb2 = long.Parse(jys[p].GetString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                            long ab2 = BitConverter.DoubleToInt64Bits(ys[p]);
                            if (ab2 != jb2 && Math.Abs(BitConverter.Int64BitsToDouble(ab2) - BitConverter.Int64BitsToDouble(jb2)) > 2.4e-7 * Math.Max(Math.Abs(BitConverter.Int64BitsToDouble(ab2)), Math.Abs(BitConverter.Int64BitsToDouble(jb2))) + 1e-9) // f32 noise + V8 hypot gap
                            {
                                yd++;
                                Fail($"{bctx} ys[{p}]: C#={ys[p]:R}({ab2:X16}) JS={BitConverter.Int64BitsToDouble(jb2):R} ({jys[p].GetString()})");
                            }
                        }
                    }
                }
                var ja = jb.GetProperty("attrSums");
                foreach (var k in b.Attrs.Keys)
                {
                    double avs = 0; foreach (var v in b.Attrs[k]) avs += v;
                    T($"attr.{k}", avs, JHex(ja.GetProperty(k)), bctx);
                }
            }

            // ---- colliders / lights / instanced prop matrices (props stage) ----
            if (doc.RootElement.TryGetProperty("props", out var jprops))
            {
                var jcol2 = doc.RootElement.GetProperty("colliders");
                if (outp.Colliders.Count != jcol2.GetArrayLength())
                    Fail($"collider count: C#={outp.Colliders.Count} JS={jcol2.GetArrayLength()}");
                int cn = Math.Min(outp.Colliders.Count, jcol2.GetArrayLength());
                for (int i = 0; i < cn; i++)
                {
                    var c = outp.Colliders[i]; var jc3 = jcol2[i];
                    string cctx = $"collider[{i}]";
                    D("x", c.X, jc3[0].GetString(), cctx); D("y", c.Y, jc3[1].GetString(), cctx); D("z", c.Z, jc3[2].GetString(), cctx);
                    D("hx", c.Hx, jc3[3].GetString(), cctx); D("hy", c.Hy, jc3[4].GetString(), cctx); D("hz", c.Hz, jc3[5].GetString(), cctx);
                    D("rotY", c.RotY, jc3[6].GetString(), cctx);
                    if (jc3.GetArrayLength() > 10)
                    {
                        var rot = c.Rot ?? new[] { 0.0, 0.0, 0.0, 0.0 };
                        D("rot.x", rot[0], jc3[7].GetString(), cctx); D("rot.y", rot[1], jc3[8].GetString(), cctx);
                        D("rot.z", rot[2], jc3[9].GetString(), cctx); D("rot.w", rot[3], jc3[10].GetString(), cctx);
                    }
                }
                // kept lights: greedy filter (exact index.js) — stable sort to match V8
                var kept = new List<BLight>();
                var cand = outp.Lights.Where(l => !l.Minor).OrderByDescending(l => l.Intensity).ToList();
                foreach (var l in cand)
                {
                    bool close = false;
                    foreach (var k in kept)
                    {
                        double dx = k.Pos[0] - l.Pos[0], dy = k.Pos[1] - l.Pos[1], dz = k.Pos[2] - l.Pos[2];
                        if (dx * dx + dy * dy + dz * dz < 121) { close = true; break; }
                    }
                    if (!close) kept.Add(l);
                }
                var jl = doc.RootElement.GetProperty("lights");
                I("lights.raw", outp.Lights.Count, (int)Num(jl, "raw"), "lights");
                I("lights.kept", kept.Count, (int)Num(jl, "kept"), "lights");
                var jlist = jl.GetProperty("list");
                int ln = Math.Min(kept.Count, jlist.GetArrayLength());
                for (int i = 0; i < ln; i++)
                {
                    var l = kept[i]; var jli = jlist[i];
                    string lctx = $"light[{i}]";
                    D("px", l.Pos[0], jli.GetProperty("p")[0].GetString(), lctx);
                    D("py", l.Pos[1], jli.GetProperty("p")[1].GetString(), lctx);
                    D("pz", l.Pos[2], jli.GetProperty("p")[2].GetString(), lctx);
                    D("cr", l.Color[0], jli.GetProperty("c")[0].GetString(), lctx);
                    D("cg", l.Color[1], jli.GetProperty("c")[1].GetString(), lctx);
                    D("cb", l.Color[2], jli.GetProperty("c")[2].GetString(), lctx);
                    D("i", l.Intensity, jli.GetProperty("i").GetString(), lctx);
                    D("d", l.Distance, jli.GetProperty("d").GetString(), lctx);
                }
                foreach (var (name, arr) in new[] { ("ac", outp.AcM), ("tank", outp.TankM), ("ptank", outp.PtankM) })
                {
                    var jp = jprops.GetProperty(name);
                    int elems = jp.GetArrayLength();
                    if (arr == null) { if (elems > 0) Fail($"props.{name}: C#=null JS={elems} elems"); continue; }
                    if (arr.Count * 16 != elems) Fail($"props.{name}: C#={arr.Count * 16} elems JS={elems}");
                    int mn2 = Math.Min(arr.Count * 16, elems);
                    for (int i = 0; i < mn2; i++)
                    {
                        double v = (double)(float)arr[i / 16][i % 16]; // InstancedMesh stores Float32Array
                        string jhex = jp[i].GetString();
                        long jv = long.Parse(jhex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                        long av = BitConverter.DoubleToInt64Bits(v);
                        if (av != jv)
                        {
                            double jval = BitConverter.Int64BitsToDouble(jv);
                            if (Math.Abs(v - jval) > Math.Max(1.2e-7 * Math.Max(Math.Abs(jval), Math.Abs(v)), 1e-9)) // ±1 f32 ulp (V8 vs Mono sin/cos/atan2), abs floor for near-zero elements
                                Fail($"props.{name}[{i / 16}].e{i % 16}: C#={v:R}({av:X16}) JS={jhex}");
                        }
                    }
                }
            }

            string verdict = _bad == 0 ? $"ORACLE PASS — {n} lots, {bn} buckets identical" : $"ORACLE FAIL — {_bad} mismatches";
            if (_bad > 0) verdict += "\n" + string.Join("\n", _cats.OrderByDescending(kv => kv.Value).Select(kv => $"  {kv.Key} x{kv.Value}"));
            _sb.Insert(0, verdict + "\n");
            File.WriteAllText("Temp/oracle/report.txt", _sb.ToString());
            }
            finally { System.Threading.Monitor.Exit(_gate); }
        }
    }
}

// recompile-bust v2
