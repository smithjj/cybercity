// Oracle: mulberry32 bit-exactness (C# vs JS). Run: tools/run-oracle-bg.sh Mulberry32
// Dump:    tools/dump-mulberry32.mjs  (extracts the function from the real JS source)
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NeonKaido;
using Rng = NeonKaido.Mulberry32;

namespace NeonKaido.EditorTools.Oracles
{
    public static class Mulberry32
    {
        public static void Compare(string arg)
        {
            string dump = string.IsNullOrEmpty(arg) ? "Temp/oracle/dump-mulberry32.json" : arg;
            var doc = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(dump)));
            var sb = new StringBuilder();
            int mismatches = 0, total = 0;
            foreach (var seedObj in doc.RootElement.GetProperty("seeds").EnumerateArray())
            {
                uint seed = (uint)seedObj.GetProperty("seed").GetInt64();
                var rnd = new Rng(seed);
                var vals = seedObj.GetProperty("valuesHex");
                for (int i = 0; i < vals.GetArrayLength(); i++)
                {
                    double v = rnd.Next();
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
                    total++;
                    string want = vals[i].GetString();
                    if (!string.Equals(want, bits.ToString("x16"), StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches++;
                        if (mismatches <= 5) sb.AppendLine($"MISMATCH seed={seed} idx={i} want={want} got={bits:x16}");
                    }
                }
            }
            sb.AppendLine(mismatches == 0
                ? $"PASS mulberry32: {total} values bit-exact across all seeds"
                : $"FAIL mulberry32: {mismatches}/{total} mismatched");
            Directory.CreateDirectory("Temp/oracle");
            File.WriteAllText("Temp/oracle/report.txt", sb.ToString());
        }
    }
}
#endif
