// Pure C# DSP toolkit used to synthesize all NEON KAIDO audio buffers at runtime.
// Faithful port of src/systems/audio/dsp.js — same seeded RNG, same math, bit-comparable outputs.
using System;

namespace NeonKaido
{
    public static class AudioDsp
    {
        public const int SR = 48000;

        public static double DbToGain(double db) => Math.Pow(10, db / 20);
        public static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;
        public static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;

        public delegate double Rng();

        // Exact port of JS mulberry32 (with Math.imul semantics)
        public static Rng Mulberry32(int seed)
        {
            uint a = (uint)seed;
            return () =>
            {
                a = a + 0x6d2b79f5u | 0u;
                uint t = (uint)((int)(a ^ (a >> 15)) * (int)(1u | a));
                t = (t + ((t ^ (t >> 7)) | 0u)) ^ t;
                t = t ^ (t >> 14);
                return (double)t / 4294967296.0;
            };
        }

        public static double Rr(Rng rng, double a, double b) => a + (b - a) * rng();
        public static int Rpick(Rng rng, int len) => (int)(rng() * len);
        public static double Rgauss(Rng rng) => (rng() + rng() + rng() - 1.5) / 0.5;
        public static double Rpow(Rng rng, double k) => Math.Pow(rng(), k);

        // ---------- Biquad (RBJ cookbook) ----------
        public enum FiltType { Lowpass, Highpass, Bandpass, Notch, Peaking, Lowshelf, Highshelf }

        public struct Biquad
        {
            public double b0, b1, b2, a1, a2, z1, z2;

            public Biquad(FiltType type, double freq, double q = 0.7071, double gainDb = 0, int sr = SR)
            {
                this = default(Biquad);
                Set(type, freq, q, gainDb, sr);
            }

            public Biquad Set(FiltType type, double freq, double q = 0.7071, double gainDb = 0, int sr = SR)
            {
                double f = Clamp(freq, 5, sr * 0.49);
                double w0 = 2 * Math.PI * f / sr;
                double cw = Math.Cos(w0), sw = Math.Sin(w0);
                double alpha = sw / (2 * q);
                double A = Math.Pow(10, gainDb / 40);
                double b0 = 0, b1 = 0, b2 = 0, a0 = 1, a1 = 0, a2 = 0;
                switch (type)
                {
                    case FiltType.Lowpass:
                        b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = b0; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                    case FiltType.Highpass:
                        b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = b0; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                    case FiltType.Bandpass:
                        b0 = alpha; b1 = 0; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                    case FiltType.Notch:
                        b0 = 1; b1 = -2 * cw; b2 = 1; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                    case FiltType.Peaking:
                        b0 = 1 + alpha * A; b1 = -2 * cw; b2 = 1 - alpha * A; a0 = 1 + alpha / A; a1 = -2 * cw; a2 = 1 - alpha / A; break;
                    case FiltType.Lowshelf:
                    {
                        double s = 2 * Math.Sqrt(A) * alpha;
                        b0 = A * ((A + 1) - (A - 1) * cw + s); b1 = 2 * A * ((A - 1) - (A + 1) * cw); b2 = A * ((A + 1) - (A - 1) * cw - s);
                        a0 = (A + 1) + (A - 1) * cw + s; a1 = -2 * ((A - 1) + (A + 1) * cw); a2 = (A + 1) + (A - 1) * cw - s; break;
                    }
                    case FiltType.Highshelf:
                    {
                        double s = 2 * Math.Sqrt(A) * alpha;
                        b0 = A * ((A + 1) + (A - 1) * cw + s); b1 = -2 * A * ((A - 1) + (A + 1) * cw); b2 = A * ((A + 1) + (A - 1) * cw - s);
                        a0 = (A + 1) - (A - 1) * cw + s; a1 = 2 * ((A - 1) - (A + 1) * cw); a2 = (A + 1) + (A - 1) * cw - s; break;
                    }
                }
                b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
                this.b0 = b0; this.b1 = b1; this.b2 = b2; this.a1 = a1; this.a2 = a2;
                return this;
            }

            public double Tick(double x)
            {
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }

            public void Run(float[] buf)
            {
                double zz1 = z1, zz2 = z2;
                for (int i = 0; i < buf.Length; i++)
                {
                    double x = buf[i];
                    double y = b0 * x + zz1;
                    zz1 = b1 * x - a1 * y + zz2;
                    zz2 = b2 * x - a2 * y;
                    buf[i] = (float)y;
                }
                z1 = Math.Abs(zz1) < 1e-20 ? 0 : zz1;
                z2 = Math.Abs(zz2) < 1e-20 ? 0 : zz2;
            }
        }

        // spec = {type, freq, q, gain}
        public struct FiltSpec { public FiltType t; public double f, q, g; }
        public static FiltSpec F(FiltType t, double f, double q = 0.7071, double g = 0) => new FiltSpec { t = t, f = f, q = q, g = g };

        public static void Chain(float[] buf, FiltSpec[] specs, int sr = SR)
        {
            foreach (var s in specs) new Biquad(s.t, s.f, s.q, s.g, sr).Run(buf);
        }

        public static void ChainPeriodic(float[] buf, FiltSpec[] specs, int sr = SR)
        {
            foreach (var s in specs)
            {
                var f = new Biquad(s.t, s.f, s.q, s.g, sr);
                var tmp = (float[])buf.Clone();
                f.Run(tmp); // warm-up
                f.Run(buf);
            }
        }

        // ---------- Noise ----------
        public static float[] White(int n, Rng rng)
        {
            var b = new float[n];
            for (int i = 0; i < n; i++) b[i] = (float)(rng() * 2 - 1);
            return b;
        }

        public static float[] Pink(int n, Rng rng)
        {
            var b = new float[n];
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (int i = 0; i < n; i++)
            {
                double w = rng() * 2 - 1;
                b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.969 * b2 + w * 0.153852;
                b3 = 0.8665 * b3 + w * 0.3104856; b4 = 0.55 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.016898;
                b[i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11);
                b6 = w * 0.115926;
            }
            return b;
        }

        public static float[] Brown(int n, Rng rng, double leak = 0.996)
        {
            var b = new float[n];
            double y = 0;
            for (int i = 0; i < n; i++) { y = leak * y + (rng() * 2 - 1) * 0.06; b[i] = (float)y; }
            return b;
        }

        public static float[] PinkLoop(int n, Rng rng)
        {
            var w = White(n, rng);
            var o = new float[n];
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    double x = w[i];
                    b0 = 0.99886 * b0 + x * 0.0555179; b1 = 0.99332 * b1 + x * 0.0750759; b2 = 0.969 * b2 + x * 0.153852;
                    b3 = 0.8665 * b3 + x * 0.3104856; b4 = 0.55 * b4 + x * 0.5329522; b5 = -0.7616 * b5 - x * 0.016898;
                    if (pass != 0) o[i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + x * 0.5362) * 0.11);
                    b6 = x * 0.115926;
                }
            }
            return o;
        }

        // Seamless-loop smooth random control in [-1,1]; rate = control points per second.
        public static float[] SmoothRandomLoop(int n, Rng rng, double rate, int sr = SR)
        {
            int pts = Math.Max(2, (int)Math.Round(n / (double)sr * rate));
            var P = new float[pts];
            for (int i = 0; i < pts; i++) P[i] = (float)(rng() * 2 - 1);
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                double pos = i / (double)n * pts;
                int k = (int)pos;
                double t = pos - k;
                double a = P[k % pts], c = P[(k + 1) % pts];
                b[i] = (float)(a + (c - a) * (1 - Math.Cos(Math.PI * t)) / 2);
            }
            return b;
        }

        // ---------- Buffer ops ----------
        public static float[] Buf(double sec, int sr = SR) => new float[(int)Math.Round(sec * sr)];
        public static float[] Buf(int n) => new float[n];

        public static void Mix(float[] dst, float[] src, double gain = 1, int offset = 0)
        {
            int n = Math.Min(src.Length, dst.Length - offset);
            for (int i = 0; i < n; i++) dst[i + offset] += (float)(src[i] * gain);
        }

        public static void MixWrap(float[] dst, float[] src, double gain, int offset)
        {
            int L = dst.Length;
            int o = ((offset % L) + L) % L;
            for (int i = 0; i < src.Length; i++)
            {
                dst[o] += (float)(src[i] * gain);
                if (++o >= L) o = 0;
            }
        }

        public static float[] Scale(float[] b, double g)
        {
            for (int i = 0; i < b.Length; i++) b[i] = (float)(b[i] * g);
            return b;
        }

        public static float[] MulBuf(float[] b, float[] m)
        {
            for (int i = 0; i < b.Length; i++) b[i] *= m[i];
            return b;
        }

        public static double Rms(float[] b)
        {
            double s = 0;
            for (int i = 0; i < b.Length; i++) s += b[i] * b[i];
            return Math.Sqrt(s / b.Length);
        }

        public static double Peak(float[] b)
        {
            double p = 0;
            for (int i = 0; i < b.Length; i++) { double a = Math.Abs(b[i]); if (a > p) p = a; }
            return p;
        }

        public static float[] RemoveDC(float[] b)
        {
            double m = 0;
            for (int i = 0; i < b.Length; i++) m += b[i];
            m /= b.Length;
            for (int i = 0; i < b.Length; i++) b[i] = (float)(b[i] - m);
            return b;
        }

        public static float[] TanhSat(float[] b, double drive = 1)
        {
            double k = Math.Tanh(drive);
            for (int i = 0; i < b.Length; i++) b[i] = (float)(Math.Tanh(b[i] * drive) / k);
            return b;
        }

        public static float[][] NormalizeRms(float[][] chs, double targetRms, double peakCeil = 0.95)
        {
            double r = 0, p = 0;
            foreach (var c in chs) { r = Math.Max(r, Rms(c)); p = Math.Max(p, Peak(c)); }
            if (r < 1e-9) return chs;
            double g = targetRms / r;
            if (p * g > peakCeil) g = peakCeil / p;
            foreach (var c in chs) Scale(c, g);
            return chs;
        }

        public static float[][] NormalizePeak(float[][] chs, double target = 0.9)
        {
            double p = 0;
            foreach (var c in chs) p = Math.Max(p, Peak(c));
            if (p > 1e-9) foreach (var c in chs) Scale(c, target / p);
            return chs;
        }

        public static float[] Loopify(float[] b, int F)
        {
            int N = b.Length - F;
            var o = new float[N];
            Array.Copy(b, o, N);
            for (int i = 0; i < F; i++)
            {
                double t = i / (double)F;
                o[i] = (float)(b[i] * Math.Sin(t * Math.PI / 2) + b[N + i] * Math.Cos(t * Math.PI / 2));
            }
            return o;
        }

        public static float[] FoldTail(float[] b, int N)
        {
            var o = new float[N];
            Array.Copy(b, o, N);
            for (int i = N; i < b.Length; i++) o[(i - N) % N] += b[i];
            return o;
        }

        public static float[] Fades(float[] b, double inSec, double outSec, int sr = SR)
        {
            int fi = Math.Min(b.Length, (int)Math.Round(inSec * sr)), fo = Math.Min(b.Length, (int)Math.Round(outSec * sr));
            for (int i = 0; i < fi; i++) b[i] = (float)(b[i] * (0.5 - 0.5 * Math.Cos(Math.PI * i / fi)));
            for (int i = 0; i < fo; i++) b[b.Length - 1 - i] = (float)(b[b.Length - 1 - i] * (0.5 - 0.5 * Math.Cos(Math.PI * i / fo)));
            return b;
        }

        // ---------- Grains / events ----------
        public static float[] NoiseClick(Rng rng, double durSec, double tauSec, int sr = SR)
        {
            int n = Math.Max(2, (int)Math.Round(durSec * sr));
            var b = new float[n];
            double k = 1 / (tauSec * sr);
            for (int i = 0; i < n; i++) b[i] = (float)((rng() * 2 - 1) * Math.Exp(-i * k));
            return b;
        }

        public static float[] Bubble(double f0, double tauSec, double rise, int sr = SR, double phase = 0)
        {
            double dur = tauSec * 6;
            int n = Math.Max(2, (int)Math.Round(dur * sr));
            var b = new float[n];
            double ph = phase;
            int att = Math.Max(1, (int)Math.Round(0.0006 * sr));
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double f = f0 * (1 + rise * t / tauSec);
                ph += 2 * Math.PI * f / sr;
                double env = Math.Exp(-t / tauSec) * (i < att ? i / (double)att : 1);
                b[i] = (float)(Math.Sin(ph) * env);
            }
            return b;
        }

        public static float[] Ping(double f, double tauSec, int sr = SR, double durMul = 6)
        {
            int n = Math.Max(2, (int)Math.Round(tauSec * durMul * sr));
            var b = new float[n];
            double w = 2 * Math.PI * f / sr;
            int att = Math.Max(1, (int)Math.Round(0.0004 * sr));
            for (int i = 0; i < n; i++) b[i] = (float)(Math.Sin(w * i) * Math.Exp(-i / (tauSec * sr)) * (i < att ? i / (double)att : 1));
            return b;
        }

        // modes: [freq, decaySec, gain] triples (flat array, 3 per mode)
        public static float[] ModalBank(float[] exc, double[] modes, int sr = SR)
        {
            var o = new float[exc.Length];
            for (int m = 0; m + 2 < modes.Length; m += 3)
            {
                double f = modes[m], decay = modes[m + 1], g = modes[m + 2];
                double r = Math.Exp(-1 / (decay * sr));
                double w = 2 * Math.PI * f / sr;
                double a1 = -2 * r * Math.Cos(w), a2 = r * r;
                double norm = (1 - r) * g * 2;
                double y1 = 0, y2 = 0;
                for (int i = 0; i < exc.Length; i++)
                {
                    double y = exc[i] * norm - a1 * y1 - a2 * y2;
                    y2 = y1; y1 = y;
                    o[i] += (float)y;
                }
            }
            return o;
        }

        public static double[] PanGains(double p)
        {
            double a = (Clamp(p, -1, 1) + 1) * Math.PI / 4;
            return new[] { Math.Cos(a), Math.Sin(a) };
        }

        public static double AddSaw(float[] dst, double freq, double amp, int startIdx, int len, double phase0 = 0, int sr = SR)
        {
            double ph = phase0;
            int end = Math.Min(dst.Length, startIdx + len);
            double dt = freq / sr;
            for (int i = Math.Max(0, startIdx); i < end; i++)
            {
                double v = 2 * ph - 1;
                if (ph < dt) { double t = ph / dt; v -= t + t - t * t - 1; }
                else if (ph > 1 - dt) { double t = (ph - 1) / dt; v -= t * t + t + t + 1; }
                dst[i] += (float)(v * amp);
                ph += dt; if (ph >= 1) ph -= 1;
            }
            return ph;
        }

        public static float[] CombDelay(float[] b, double delaySec, double fb, double mixAmt, int sr = SR)
        {
            int d = Math.Max(1, (int)Math.Round(delaySec * sr));
            var line = new float[d];
            int idx = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double y = line[idx];
                line[idx] = (float)(b[i] + y * fb);
                idx = (idx + 1) % d;
                b[i] = (float)(b[i] + y * mixAmt);
            }
            return b;
        }
    }
}
