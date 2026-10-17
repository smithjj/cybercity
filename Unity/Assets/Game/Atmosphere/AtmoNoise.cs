using System;

namespace NeonKaido
{
    /// Exact port of src/systems/atmosphere/noiseTex.js createNoiseTexture.
    /// Tileable multi-channel fBm: R=low-freq fbm, G=mid, B=high detail, A=ridged/billow.
    /// Lattice values are float32 (JS Float32Array truncates rnd()); interpolation in double.
    public static class AtmoNoise
    {
        public static byte[] Create(int size = 256, uint seed = 1337)
        {
            var rnd = new Mulberry32(seed);
            const int octs = 6;
            int n = octs + 2;
            var lats = new float[n][];
            var periods = new int[n];
            for (int o = 0; o < n; o++)
            {
                int p = 4 << o;
                periods[o] = p;
                lats[o] = new float[p * p];
                for (int i = 0; i < lats[o].Length; i++) lats[o][i] = (float)rnd.Next();
            }

            var data = new byte[size * size * 4];
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    double x = i / (double)size, y = j / (double)size;
                    int k = (j * size + i) * 4;
                    data[k] = Clamp8(Fbm(lats, periods, x, y, 0, 5, false) * 255);
                    data[k + 1] = Clamp8(Fbm(lats, periods, x, y, 1, 5, false) * 255);
                    data[k + 2] = Clamp8(Fbm(lats, periods, x, y, 3, 4, false) * 255);
                    data[k + 3] = Clamp8(Fbm(lats, periods, x, y, 1, 4, true) * 255);
                }
            }
            return data;
        }

        static byte Clamp8(double v)
        {
            v = Math.Min(255, Math.Max(0, v));
            return (byte)v; // truncation, matching Uint8Array store
        }

        static double Fbm(float[][] lats, int[] periods, double x, double y, int start, int n, bool ridged)
        {
            double s = 0, amp = 0.5, norm = 0;
            for (int o = start; o < start + n; o++)
            {
                double v = ValueNoise(lats[o], periods[o], x * periods[o], y * periods[o]);
                if (ridged) v = 1 - Math.Abs(v * 2 - 1);
                s += v * amp; norm += amp; amp *= 0.5;
            }
            return s / norm;
        }

        static double ValueNoise(float[] lat, int period, double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi;
            double u = xf * xf * xf * (xf * (xf * 6 - 15) + 10);
            double v = yf * yf * yf * (yf * (yf * 6 - 15) + 10);
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            double a = lat[y0 * period + x0], b = lat[y0 * period + x1];
            double c = lat[y1 * period + x0], d = lat[y1 * period + x1];
            return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v;
        }
    }
}
