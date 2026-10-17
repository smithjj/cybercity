// Oracle: noise texture byte-exactness (C# AtmoNoise.Create vs real JS createNoiseTexture).
// Dump: tools/dump-noise.mjs. Run: tools/run-oracle-bg.sh NoiseTex
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NeonKaido;

namespace NeonKaido.EditorTools.Oracles
{
    public static class NoiseTex
    {
        public static void Compare(string arg)
        {
            string dump = string.IsNullOrEmpty(arg) ? "Temp/oracle/dump-noise.json" : arg;
            var doc = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(dump)));
            var wantArr = doc.RootElement.GetProperty("data");
            var want = new byte[wantArr.GetArrayLength()];
            for (int i = 0; i < want.Length; i++) want[i] = wantArr[i].GetByte();
            int size = doc.RootElement.GetProperty("size").GetInt32();
            var got = AtmoNoise.Create(size, 4242);
            var sb = new StringBuilder();
            int mismatches = 0;
            int len = Math.Min(want.Length, got.Length);
            for (int i = 0; i < len; i++)
            {
                if (want[i] != got[i])
                {
                    mismatches++;
                    if (mismatches <= 5) sb.AppendLine($"MISMATCH byte {i}: want={want[i]} got={got[i]}");
                }
            }
            sb.AppendLine(want.Length != got.Length
                ? $"FAIL noiseTex: length {want.Length} vs {got.Length}"
                : mismatches == 0
                    ? $"PASS noiseTex: {len} bytes byte-exact"
                    : $"FAIL noiseTex: {mismatches}/{len} mismatched");
            Directory.CreateDirectory("Temp/oracle");
            File.WriteAllText("Temp/oracle/report.txt", sb.ToString());
        }
    }
}
#endif
