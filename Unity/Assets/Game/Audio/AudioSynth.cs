// Procedural sound recipes for NEON KAIDO. Faithful port of src/systems/audio/synth.js.
// Each recipe returns stereo (or mono) float buffers, loops normalized to RMS 0.12.
using System;
using System.Collections.Generic;

namespace NeonKaido
{
    public class SynthBuf
    {
        public float[][] Channels;
        public bool Loop;
    }

    public static class AudioSynth
    {
        const double LOOP_RMS = 0.12;
        static int N(double sec) => (int)Math.Round(sec * AudioDsp.SR);

        static AudioDsp.Rng Rng(int seed) => AudioDsp.Mulberry32(seed);
        static double Rr(AudioDsp.Rng rng, double a, double b) => AudioDsp.Rr(rng, a, b);

        static float[] Filt(float[] b, AudioDsp.FiltSpec[] specs) { AudioDsp.Chain(b, specs); return b; }

        static void Impulse(float[] dst, int pos, double amp, AudioDsp.Rng rng)
        {
            int L = dst.Length;
            for (int k = 0; k < 3; k++) dst[(pos + k) % L] += (float)(amp * (k == 0 ? 1 : (rng() * 2 - 1) * 0.5));
        }

        static SynthBuf Out(float[][] chs, bool loop)
        {
            // sanitize NaN/Inf
            foreach (var c in chs)
                for (int i = 0; i < c.Length; i++) if (!float.IsFinite(c[i])) c[i] = 0;
            return new SynthBuf { Channels = chs, Loop = loop };
        }

        static SynthBuf Mono(float[] c, bool loop) => Out(new[] { c }, loop);
        static SynthBuf Stereo(float[] l, float[] r, bool loop) => Out(new[] { l, r }, loop);

        // ------------------------------------------------------------------ REVERB IR
        public static SynthBuf ReverbIR()
        {
            var rng = Rng(101);
            int n = N(3.2);
            var chs = new float[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                var lo = Filt(AudioDsp.White(n, rng), new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 450, 0.6) });
                var mid = Filt(AudioDsp.White(n, rng), new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 450, 0.6), AudioDsp.F(AudioDsp.FiltType.Lowpass, 3500, 0.6) });
                var hi = Filt(AudioDsp.White(n, rng), new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 3500, 0.6) });
                var o = new float[n];
                int pre = N(0.012), ramp = N(0.06);
                for (int i = pre; i < n; i++)
                {
                    double t = (i - pre) / (double)AudioDsp.SR;
                    double e(double T) => Math.Exp(-6.91 * t / T);
                    double r = Math.Min(1, (i - pre) / (double)ramp);
                    o[i] = (float)((lo[i] * e(2.4) * 1.2 + mid[i] * e(1.7) * 0.9 + hi[i] * e(0.7) * 0.55) * r * r);
                }
                for (int k = 0; k < 18; k++)
                {
                    double t = 0.006 + rng() * 0.11;
                    double a = (0.5 + rng() * 0.5) * Math.Exp(-t / 0.08) * 1.6;
                    var c = Filt(AudioDsp.NoiseClick(rng, 0.004, 0.0008), new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 5000 - t * 20000, 0.7) });
                    AudioDsp.Mix(o, c, a * (rng() < 0.5 ? -1 : 1), N(t));
                }
                for (int k = 1; k < 9; k++)
                {
                    double t = 0.064 * k + (ch != 0 ? 0.003 : 0) + AudioDsp.Rgauss(rng) * 0.0015;
                    var c = Filt(AudioDsp.NoiseClick(rng, 0.006, 0.001), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 1400, 0.8) });
                    AudioDsp.Mix(o, c, 0.5 * Math.Pow(0.62, k), N(t));
                }
                AudioDsp.Fades(o, 0, 0.3);
                chs[ch] = o;
            }
            AudioDsp.NormalizePeak(chs, 0.9);
            return Stereo(chs[0], chs[1], false);
        }

        // ------------------------------------------------------------------ RAIN
        public static SynthBuf RainFar()
        {
            var rng = Rng(201);
            int n = N(14);
            var gust = AudioDsp.SmoothRandomLoop(n, rng, 0.35);
            var chs = new float[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                var baseB = AudioDsp.PinkLoop(n, rng);
                AudioDsp.ChainPeriodic(baseB, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 70, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 350, 0.9, 1.5), AudioDsp.F(AudioDsp.FiltType.Peaking, 3200, 0.7, 3.5), AudioDsp.F(AudioDsp.FiltType.Highshelf, 7000, 0.7, -5), AudioDsp.F(AudioDsp.FiltType.Lowpass, 11000, 0.7) });
                var crack = new float[n];
                int count = (int)Math.Round(n / (double)AudioDsp.SR * 2600);
                for (int k = 0; k < count; k++)
                {
                    int p = (int)(rng() * n);
                    crack[p] += (float)((rng() < 0.5 ? -1 : 1) * AudioDsp.Rpow(rng, 3));
                }
                var crk = crack;
                AudioDsp.ChainPeriodic(crk, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 1200, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 4200, 0.9, 3), AudioDsp.F(AudioDsp.FiltType.Lowpass, 9000, 0.7) });
                var local = AudioDsp.SmoothRandomLoop(n, rng, 1.3);
                var o = new float[n];
                for (int i = 0; i < n; i++)
                {
                    double g = 1 + 0.2 * gust[i] + 0.07 * local[i];
                    o[i] = (float)((baseB[i] + crk[i] * 2.2) * g);
                }
                chs[ch] = o;
            }
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(chs[0], chs[1], true);
        }

        public static SynthBuf RainNear()
        {
            var rng = Rng(202);
            int n = N(11.3);
            var chs = new float[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                var o = new float[n];
                int count = (int)Math.Round(n / (double)AudioDsp.SR * 700);
                for (int k = 0; k < count; k++)
                {
                    double a = AudioDsp.Rpow(rng, 2.6);
                    var c = AudioDsp.NoiseClick(rng, 0.004, Rr(rng, 0.00025, 0.0011));
                    AudioDsp.MixWrap(o, c, a, (int)(rng() * n));
                }
                var bed = AudioDsp.PinkLoop(n, rng);
                AudioDsp.ChainPeriodic(bed, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 1500, 0.7) });
                AudioDsp.Mix(o, bed, 0.35);
                AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 500, 0.6), AudioDsp.F(AudioDsp.FiltType.Peaking, 3000, 0.7, 3), AudioDsp.F(AudioDsp.FiltType.Highshelf, 6500, 0.7, -6), AudioDsp.F(AudioDsp.FiltType.Lowpass, 12000, 0.7) });
                chs[ch] = o;
            }
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(chs[0], chs[1], true);
        }

        public static SynthBuf Droplets()
        {
            var rng = Rng(203);
            int n = N(9.7);
            var L = new float[n]; var R = new float[n];
            int count = (int)Math.Round(n / (double)AudioDsp.SR * 26);
            for (int k = 0; k < count; k++)
            {
                int pos = (int)(rng() * n);
                double a = 0.25 + 0.75 * AudioDsp.Rpow(rng, 2);
                var pg = AudioDsp.PanGains(Rr(rng, -1, 1));
                var ev = new float[N(0.12)];
                var click = Filt(AudioDsp.NoiseClick(rng, 0.005, Rr(rng, 0.0004, 0.0016)), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 1800, 6000), 0.9) });
                AudioDsp.Mix(ev, click, 1.0);
                double kind = rng();
                if (kind < 0.45) AudioDsp.Mix(ev, AudioDsp.Bubble(Rr(rng, 1300, 4200), Rr(rng, 0.003, 0.011), Rr(rng, 0.05, 0.3)), 0.45, N(0.001));
                else if (kind < 0.52)
                {
                    AudioDsp.Mix(ev, AudioDsp.Bubble(Rr(rng, 500, 1100), Rr(rng, 0.012, 0.025), 0.3), 0.6, N(0.002));
                    AudioDsp.Mix(ev, Filt(AudioDsp.NoiseClick(rng, 0.05, 0.012), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 1500, 0.7) }), 0.5);
                }
                AudioDsp.MixWrap(L, ev, a * pg[0], pos); AudioDsp.MixWrap(R, ev, a * pg[1], pos);
            }
            AudioDsp.ChainPeriodic(L, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 200, 0.7), AudioDsp.F(AudioDsp.FiltType.Highshelf, 8000, 0.7, -3) });
            AudioDsp.ChainPeriodic(R, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 200, 0.7), AudioDsp.F(AudioDsp.FiltType.Highshelf, 8000, 0.7, -3) });
            var chs = new[] { L, R };
            AudioDsp.NormalizeRms(chs, LOOP_RMS * 0.8);
            return Stereo(L, R, true);
        }

        public static SynthBuf MetalPatter()
        {
            var rng = Rng(204);
            int n = N(8.9);
            var L = new float[n]; var R = new float[n];
            double[][][] panels = new double[3][][];
            // each panel: [pan, rate, then modes flattened f/decay/g]
            double[][] pm = new double[3][];
            pm[0] = Sheet(700, 7500, 16, 0.006, 0.022, rng);
            pm[1] = Sheet(550, 6500, 16, 0.007, 0.025, rng);
            pm[2] = Sheet(260, 3200, 12, 0.008, 0.03, rng);
            double[] pans = { -0.55, 0.45, 0.0 };
            double[] rates = { 34, 28, 45 };
            for (int pi = 0; pi < 3; pi++)
            {
                var exc = new float[n];
                int count = (int)Math.Round(n / (double)AudioDsp.SR * rates[pi]);
                for (int k = 0; k < count; k++) Impulse(exc, (int)(rng() * n), 0.2 + 0.8 * AudioDsp.Rpow(rng, 2), rng);
                var ring = Periodic(exc, d => AudioDsp.ModalBank(d, pm[pi]));
                var tick = (float[])exc.Clone();
                AudioDsp.ChainPeriodic(tick, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 3000, 0.7) });
                var pg = AudioDsp.PanGains(pans[pi]);
                for (int i = 0; i < n; i++)
                {
                    double v = ring[i] * 0.9 + tick[i] * 0.4;
                    L[i] += (float)(v * pg[0]); R[i] += (float)(v * pg[1]);
                }
            }
            int drips = (int)Math.Round(n / (double)AudioDsp.SR * 3.2);
            for (int k = 0; k < drips; k++)
            {
                int pos = (int)(rng() * n);
                var ev = new float[N(0.12)];
                AudioDsp.Mix(ev, AudioDsp.Bubble(Rr(rng, 600, 1400), Rr(rng, 0.008, 0.02), 0.25), 0.5);
                AudioDsp.Mix(ev, Filt(AudioDsp.NoiseClick(rng, 0.03, 0.005), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 900, 2000), 0.8) }), 0.8);
                var pg = AudioDsp.PanGains(Rr(rng, -0.7, 0.7));
                double a = Rr(rng, 0.4, 1);
                AudioDsp.MixWrap(L, ev, a * pg[0], pos); AudioDsp.MixWrap(R, ev, a * pg[1], pos);
            }
            AudioDsp.ChainPeriodic(L, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 150, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 10000, 0.7) });
            AudioDsp.ChainPeriodic(R, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 150, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 10000, 0.7) });
            var chs = new[] { L, R };
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(L, R, true);
        }

        static double[] Sheet(double fmin, double fmax, int count, double dmin, double dmax, AudioDsp.Rng rng)
        {
            var m = new double[count * 3];
            for (int i = 0; i < count; i++)
            {
                double f = fmin * Math.Pow(fmax / fmin, rng());
                m[i * 3] = f; m[i * 3 + 1] = Rr(rng, dmin, dmax); m[i * 3 + 2] = Math.Pow(fmin / f, 0.35) * Rr(rng, 0.5, 1);
            }
            return m;
        }

        delegate float[] PeriodicFn(float[] d);
        static float[] Periodic(float[] b, PeriodicFn fn)
        {
            int n = b.Length;
            var d = new float[2 * n];
            Array.Copy(b, d, n); Array.Copy(b, 0, d, n, n);
            var o = fn(d);
            var res = new float[n];
            Array.Copy(o, n, res, 0, n);
            return res;
        }

        public static SynthBuf CarRoof()
        {
            var rng = Rng(205);
            int n = N(7.9);
            double[] roofModes = { 95, 0.05, 1, 148, 0.045, 0.9, 213, 0.04, 0.8, 307, 0.035, 0.6, 452, 0.03, 0.45, 690, 0.02, 0.3, 1150, 0.012, 0.2 };
            double[] glassModes = { 1850, 0.012, 1, 2650, 0.01, 0.7, 3900, 0.008, 0.5, 5200, 0.006, 0.3 };
            var wash = new float[n];
            Array.Copy(AudioDsp.White(n, rng), wash, n);
            AudioDsp.ChainPeriodic(wash, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 260, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 260, 0.7) });
            var chs = new float[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                var exc = new float[n];
                for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 170); k < c; k++) Impulse(exc, (int)(rng() * n), 0.15 + 0.85 * AudioDsp.Rpow(rng, 2.5), rng);
                var roof = Periodic(exc, d => AudioDsp.ModalBank(d, roofModes));
                var tick = (float[])exc.Clone();
                AudioDsp.ChainPeriodic(tick, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 2400, 0.8) });
                var gexc = new float[n];
                for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 45); k < c; k++) Impulse(gexc, (int)(rng() * n), AudioDsp.Rpow(rng, 2), rng);
                var glass = Periodic(gexc, d => AudioDsp.ModalBank(d, glassModes));
                var o = new float[n];
                for (int i = 0; i < n; i++) o[i] = (float)(roof[i] * 1.0 + tick[i] * 0.5 + glass[i] * 0.6 + wash[i] * 1.2);
                AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 45, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 1200, 0.8, 4), AudioDsp.F(AudioDsp.FiltType.Lowpass, 6000, 0.7) });
                chs[ch] = o;
            }
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(chs[0], chs[1], true);
        }

        public static SynthBuf Gutter()
        {
            var rng = Rng(206);
            int n = N(6.1);
            var o = new float[n];
            int count = (int)Math.Round(n / (double)AudioDsp.SR * 75);
            for (int k = 0; k < count; k++)
            {
                double f = 350 * Math.Exp(rng() * 1.5);
                AudioDsp.MixWrap(o, AudioDsp.Bubble(f, Rr(rng, 0.004, 0.018), Rr(rng, 0.1, 0.6), AudioDsp.SR, rng() * 6), 0.2 + 0.8 * AudioDsp.Rpow(rng, 1.5), (int)(rng() * n));
            }
            var flow = AudioDsp.PinkLoop(n, rng);
            AudioDsp.ChainPeriodic(flow, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 700, 0.7) });
            var mod = AudioDsp.SmoothRandomLoop(n, rng, 6);
            for (int i = 0; i < n; i++) o[i] += (float)(flow[i] * (0.6 + 0.4 * mod[i]) * 0.9);
            for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 4); k < c; k++)
            {
                AudioDsp.MixWrap(o, Filt(AudioDsp.NoiseClick(rng, 0.03, 0.006), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 2500, 0.8) }), Rr(rng, 0.2, 0.6), (int)(rng() * n));
            }
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 120, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 260, 4, 4), AudioDsp.F(AudioDsp.FiltType.Lowpass, 7000, 0.7) });
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        // ------------------------------------------------------------------ CITY
        public static SynthBuf TrafficBed()
        {
            var rng = Rng(301);
            int n = N(23);
            var shared = AudioDsp.White(n, rng);
            AudioDsp.ChainPeriodic(shared, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 150, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 150, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 22, 0.7) });
            var L = new float[n]; var R = new float[n];
            for (int ch = 0; ch < 2; ch++)
            {
                var own = AudioDsp.White(n, rng);
                AudioDsp.ChainPeriodic(own, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 180, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 180, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 22, 0.7) });
                var dst = ch != 0 ? R : L;
                for (int i = 0; i < n; i++) dst[i] = (float)(shared[i] * 0.75 + own[i] * 0.4);
            }
            AudioDsp.Scale(L, 6); AudioDsp.Scale(R, 6);
            for (int k = 0; k < 15; k++)
            {
                double w = Rr(rng, 1.2, 3.5);
                int len = N(w * 5);
                int tc = len / 2;
                double dir = rng() < 0.5 ? -1 : 1;
                double amp = 0.15 + 0.85 * AudioDsp.Rpow(rng, 2);
                var hiss = AudioDsp.Pink(len, rng);
                AudioDsp.Chain(hiss, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 1300, 2500), 0.55), AudioDsp.F(AudioDsp.FiltType.Lowpass, 6000, 0.7) });
                var eng = new float[len];
                double f0 = Rr(rng, 35, 75);
                double ph = 0;
                for (int i = 0; i < len; i++)
                {
                    double x = (i - tc) / (w * AudioDsp.SR);
                    double f = f0 * (1 - 0.04 * Math.Tanh(x * 1.5));
                    ph += f / AudioDsp.SR; if (ph >= 1) ph -= 1;
                    eng[i] = (float)(2 * ph - 1);
                }
                AudioDsp.Chain(eng, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 220, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 220, 0.7) });
                int start = (int)(rng() * n);
                for (int i = 0; i < len; i++)
                {
                    double x = (i - tc) / (w * AudioDsp.SR);
                    double env = Math.Exp(-x * x * 1.6);
                    var pg = AudioDsp.PanGains(dir * Math.Tanh(x * 1.4) * 0.85);
                    double v = (hiss[i] * 1.6 + eng[i] * 0.5) * env * amp;
                    int o = (start + i) % n;
                    L[o] += (float)(v * pg[0]); R[o] += (float)(v * pg[1]);
                }
            }
            AudioDsp.ChainPeriodic(L, new[] { AudioDsp.F(AudioDsp.FiltType.Highshelf, 2500, 0.7, -6), AudioDsp.F(AudioDsp.FiltType.Lowpass, 7000, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 20, 0.7) });
            AudioDsp.ChainPeriodic(R, new[] { AudioDsp.F(AudioDsp.FiltType.Highshelf, 2500, 0.7, -6), AudioDsp.F(AudioDsp.FiltType.Lowpass, 7000, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 20, 0.7) });
            var chs = new[] { L, R };
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(L, R, true);
        }

        static SynthBuf SirenTone(double sec, Func<double, double> fc, int seed)
        {
            int n = N(sec);
            var f = new double[n];
            double cyc = 0;
            for (int i = 0; i < n; i++) { f[i] = fc(i / (double)AudioDsp.SR); cyc += f[i] / AudioDsp.SR; }
            double s = Math.Round(cyc) / cyc;
            var o = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                ph += f[i] * s / AudioDsp.SR; if (ph >= 1) ph -= 1;
                double w = 2 * Math.PI * ph;
                double v = 0;
                for (int k = 1; k <= 9; k++) v += Math.Sin(w * k) * (k % 2 != 0 ? 1.0 / k : 0.35 / k);
                o[i] = (float)v;
            }
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 380, 0.7), AudioDsp.F(AudioDsp.FiltType.Bandpass, 1300, 0.45), AudioDsp.F(AudioDsp.FiltType.Peaking, 2600, 1.2, 4) });
            AudioDsp.TanhSat(o, 2.2);
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 200, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 6500, 0.7) });
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        public static SynthBuf SirenWail() => SirenTone(6, t =>
        {
            double p = t % 3 / 3;
            double x = p < 0.35 ? Math.Sin(p / 0.35 * Math.PI / 2) : Math.Cos((p - 0.35) / 0.65 * Math.PI / 2);
            return 640 + 520 * x * x;
        }, 311);

        public static SynthBuf SirenHiLo() => SirenTone(2.6, t =>
        {
            double p = t % 1.3 / 1.3;
            double s2 = 0.5 + 0.5 * Math.Tanh(Math.Sin(p * 2 * Math.PI) * 30);
            return 770 + 190 * s2;
        }, 312);

        static SynthBuf Horn(int seed, bool dbl)
        {
            var rng = Rng(seed);
            int n = N(dbl ? 1.0 : 0.75);
            var o = new float[n];
            double f1 = Rr(rng, 395, 430), f2 = f1 * Rr(rng, 1.18, 1.26);
            double[] segs = dbl ? new[] { 0.0, 0.18, 0.3, 0.6 } : new[] { 0.0, Rr(rng, 0.35, 0.6) };
            double p1 = 0, p2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)AudioDsp.SR;
                double env = 0;
                for (int si = 0; si < segs.Length; si += 2)
                {
                    double a = segs[si], b = segs[si + 1];
                    if (t >= a && t < b + 0.05)
                        env = Math.Max(env, Math.Min(1, (t - a) / 0.012) * (t > b ? Math.Max(0, 1 - (t - b) / 0.05) : 1));
                }
                p1 += f1 / AudioDsp.SR; p2 += f2 / AudioDsp.SR; p1 %= 1; p2 %= 1;
                double sq(double p) => Math.Tanh(Math.Sin(2 * Math.PI * p) * 4);
                o[i] = (float)((sq(p1) + sq(p2) * 0.8) * env);
            }
            AudioDsp.Chain(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 300, 0.7), AudioDsp.F(AudioDsp.FiltType.Bandpass, 900, 0.6), AudioDsp.F(AudioDsp.FiltType.Peaking, 2400, 1.5, 5), AudioDsp.F(AudioDsp.FiltType.Lowpass, 5000, 0.7) });
            AudioDsp.NormalizePeak(new[] { o }, 0.9);
            return Mono(o, false);
        }

        public static SynthBuf TrainRumble()
        {
            var rng = Rng(321);
            int n = N(10);
            var o = new float[n];
            var rumble = AudioDsp.White(n, rng);
            AudioDsp.ChainPeriodic(rumble, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 140, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 140, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 25, 0.7) });
            var boom = AudioDsp.White(n, rng);
            AudioDsp.ChainPeriodic(boom, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 68, 4) });
            var roar = AudioDsp.PinkLoop(n, rng);
            AudioDsp.ChainPeriodic(roar, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 650, 0.6) });
            var mod = AudioDsp.SmoothRandomLoop(n, rng, 1);
            double wph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)AudioDsp.SR;
                wph += 820 / (double)AudioDsp.SR; wph %= 1;
                double whine = Math.Sin(2 * Math.PI * wph) + 0.3 * Math.Sin(4 * Math.PI * wph) + 0.12 * Math.Sin(6 * Math.PI * wph);
                o[i] = (float)(rumble[i] * 9 * (1 + 0.2 * mod[i]) + boom[i] * 10 + roar[i] * 0.55 + whine * 0.012 * (1 + 0.3 * Math.Sin(2 * Math.PI * 0.2 * t)));
            }
            double period = 1.25;
            for (double t0 = 0; t0 < 10 - 1e-6; t0 += period)
            {
                foreach (var off in new[] { 0.0, 0.115, 0.82, 0.935 })
                {
                    var ev = new float[N(0.2)];
                    AudioDsp.Mix(ev, AudioDsp.Ping(Rr(rng, 58, 82), 0.05), 1.0);
                    AudioDsp.Mix(ev, Filt(AudioDsp.NoiseClick(rng, 0.02, 0.003), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 1500, 2400), 0.8) }), 0.45);
                    AudioDsp.MixWrap(o, ev, Rr(rng, 0.25, 0.4), N(t0 + off + AudioDsp.Rgauss(rng) * 0.004));
                }
            }
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 22, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 6000, 0.7) });
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        // ------------------------------------------------------------------ VOICE
        static readonly double[][] Vowels = {
            new double[] { 730, 1090, 2440 }, new double[] { 270, 2290, 3010 }, new double[] { 300, 870, 2240 },
            new double[] { 530, 1840, 2480 }, new double[] { 570, 840, 2410 }, new double[] { 440, 1020, 2240 },
            new double[] { 660, 1720, 2410 }, new double[] { 490, 1350, 1690 }, new double[] { 400, 1900, 2600 },
        };

        static float[] Voice(AudioDsp.Rng rng, int n, double f0, double fShift, double talk)
        {
            const int BLK = 128;
            int nb = (int)Math.Ceiling(n / (double)BLK) + 1;
            var env = new double[n]; var pitch = new double[n];
            var F1 = new double[nb]; var F2 = new double[nb]; var F3 = new double[nb];
            for (int b = 0; b < nb; b++) { F1[b] = 500; F2[b] = 1500; F3[b] = 2500; }
            var o = new float[n];
            int i = (int)(rng() * AudioDsp.SR * 1.2);
            while (i < n)
            {
                int uttStart = i;
                int uttLen = (int)(Rr(rng, 0.7, 3.2) * AudioDsp.SR);
                int uttEnd = Math.Min(n, i + uttLen);
                double d0 = Rr(rng, 1.05, 1.3), d1 = Rr(rng, 0.82, 0.95);
                double excite = rng() < 0.12 ? 1.35 : 1;
                while (i < uttEnd)
                {
                    int sl = (int)(Rr(rng, 0.09, 0.26) * AudioDsp.SR);
                    double stress = 0.35 + 0.65 * Math.Pow(rng(), 0.7);
                    var v = Vowels[(int)(rng() * Vowels.Length)];
                    double j1 = 1 + AudioDsp.Rgauss(rng) * 0.06, j2 = 1 + AudioDsp.Rgauss(rng) * 0.06;
                    double pr = Rr(rng, -0.1, 0.14) * excite;
                    for (int b = i / BLK; b < Math.Min(nb, (i + sl) / BLK + 1); b++)
                    {
                        F1[b] = v[0] * j1 * fShift; F2[b] = v[1] * j2 * fShift; F3[b] = v[2] * fShift;
                    }
                    for (int j = i; j < Math.Min(i + sl, n); j++)
                    {
                        double t = (j - i) / (double)sl;
                        env[j] = Math.Pow(Math.Sin(Math.PI * t), 0.6) * stress;
                        double up = (j - uttStart) / (double)uttLen;
                        pitch[j] = f0 * excite * (d0 + (d1 - d0) * up) * (1 + pr * t);
                    }
                    if (rng() < 0.45 && i < n)
                    {
                        var cb = Filt(AudioDsp.NoiseClick(rng, Rr(rng, 0.03, 0.07), Rr(rng, 0.01, 0.025)), new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, Rr(rng, 2500, 4500), 0.7) });
                        AudioDsp.Mix(o, cb, 0.12 * stress, i);
                    }
                    i += sl + (rng() < 0.2 ? (int)(Rr(rng, 0.02, 0.09) * AudioDsp.SR) : 0);
                }
                i = uttEnd + (int)(Rr(rng, 0.25, 2.2) / Math.Max(0.2, talk) * 0.6 * AudioDsp.SR);
            }
            foreach (var F in new[] { F1, F2, F3 })
            {
                double y = F[0];
                for (int b = 0; b < nb; b++) { y += (F[b] - y) * 0.25; F[b] = y; }
            }
            var src = new double[n];
            double ph = 0, lp = 0;
            double a = Math.Exp(-2 * Math.PI * 1400 / AudioDsp.SR);
            for (int j = 0; j < n; j++)
            {
                if (env[j] <= 0) { lp *= a; src[j] = lp; continue; }
                double f = pitch[j] * (1 + (rng() - 0.5) * 0.02);
                ph += f / AudioDsp.SR; if (ph >= 1) ph -= 1;
                double x = (2 * ph - 1) * 0.85 + (rng() * 2 - 1) * 0.12;
                lp = a * lp + (1 - a) * x;
                src[j] = lp * env[j] * 3;
            }
            var b1 = new AudioDsp.Biquad(AudioDsp.FiltType.Bandpass, 500, 6);
            var b2 = new AudioDsp.Biquad(AudioDsp.FiltType.Bandpass, 1500, 12);
            var b3 = new AudioDsp.Biquad(AudioDsp.FiltType.Bandpass, 2500, 16);
            for (int b = 0; b * BLK < n; b++)
            {
                b1.Set(AudioDsp.FiltType.Bandpass, F1[b], F1[b] / 90);
                b2.Set(AudioDsp.FiltType.Bandpass, F2[b], F2[b] / 110);
                b3.Set(AudioDsp.FiltType.Bandpass, F3[b], F3[b] / 160);
                int e = Math.Min(n, (b + 1) * BLK);
                for (int j = b * BLK; j < e; j++)
                {
                    double x = src[j];
                    o[j] += (float)(b1.Tick(x) * 1.0 + b2.Tick(x) * 0.55 + b3.Tick(x) * 0.28);
                }
            }
            return o;
        }

        static void Babble(AudioDsp.Rng rng, double sec, int voices, bool near, out float[] L, out float[] R, out int n, out int F)
        {
            F = (int)(1.0 * AudioDsp.SR);
            n = N(sec + 1.0);
            L = new float[n]; R = new float[n];
            for (int v = 0; v < voices; v++)
            {
                bool female = rng() < 0.5;
                double f0 = female ? Rr(rng, 175, 250) : Rr(rng, 92, 140);
                var vo = Voice(rng, n, f0, female ? 1.12 : 1, Rr(rng, 0.4, 0.8));
                double dist = near ? Rr(rng, 0.5, 1) : Rr(rng, 0.15, 1);
                AudioDsp.Chain(vo, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, near ? 6000 : 1800 + 3000 * dist, 0.7) });
                var pg = AudioDsp.PanGains(Rr(rng, -0.9, 0.9));
                for (int i = 0; i < n; i++) { L[i] += (float)(vo[i] * dist * pg[0]); R[i] += (float)(vo[i] * dist * pg[1]); }
            }
        }

        public static SynthBuf CrowdBabble()
        {
            Babble(Rng(331), 17, 16, false, out var L, out var R, out var n, out var F);
            AudioDsp.Chain(L, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 160, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 4500, 0.7) });
            AudioDsp.Chain(R, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 160, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 4500, 0.7) });
            var lo = AudioDsp.Loopify(L, F); var ro = AudioDsp.Loopify(R, F);
            var chs = new[] { lo, ro };
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(lo, ro, true);
        }

        public static SynthBuf RamenShop()
        {
            var rng = Rng(342);
            Babble(Rng(341), 13, 5, true, out var L, out var R, out var n, out var F);
            AudioDsp.Scale(L, 0.8); AudioDsp.Scale(R, 0.8);
            void Ev(float[] buf, double gain, double pan, int pos)
            {
                var pg = AudioDsp.PanGains(pan);
                AudioDsp.Mix(L, buf, gain * pg[0], pos); AudioDsp.Mix(R, buf, gain * pg[1], pos);
            }
            for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 0.9); k < c; k++)
            {
                double f = Rr(rng, 1700, 2600);
                var exc = new float[N(0.6)]; Impulse(exc, 0, 1, rng);
                var bowl = AudioDsp.ModalBank(exc, new[] { f, 0.22, 1, f * 2.71, 0.13, 0.6, f * 4.93, 0.08, 0.4, f * 7.3, 0.05, 0.25 });
                Ev(bowl, Rr(rng, 0.25, 0.7), Rr(rng, -0.6, 0.6), (int)(rng() * (n - bowl.Length)));
            }
            for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 1.5); k < c; k++)
            {
                Ev(AudioDsp.Ping(Rr(rng, 2200, 3800), 0.004), Rr(rng, 0.1, 0.25), Rr(rng, -0.6, 0.6), (int)(rng() * (n - 2000)));
            }
            var sz = new float[n];
            for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 500); k < c; k++) sz[(int)(rng() * n)] += (float)((rng() < 0.5 ? -1 : 1) * AudioDsp.Rpow(rng, 3));
            AudioDsp.Chain(sz, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 2500, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 6000, 1, 4), AudioDsp.F(AudioDsp.FiltType.Lowpass, 10000, 0.7) });
            var szm = AudioDsp.SmoothRandomLoop(n, rng, 0.5);
            for (int i = 0; i < n; i++)
            {
                double v = sz[i] * (0.7 + 0.3 * szm[i]) * 0.5;
                L[i] += (float)(v * 0.8); R[i] += (float)(v * 0.5);
            }
            for (int k = 0, c = (int)Math.Round(n / (double)AudioDsp.SR * 14); k < c; k++)
            {
                var b = AudioDsp.Bubble(Rr(rng, 150, 420), Rr(rng, 0.01, 0.03), 0.2);
                AudioDsp.Chain(b, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 1500, 0.7) });
                Ev(b, Rr(rng, 0.1, 0.3), 0.35, (int)(rng() * (n - b.Length)));
            }
            AudioDsp.Chain(L, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 120, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 7000, 0.7) });
            AudioDsp.Chain(R, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 120, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 7000, 0.7) });
            var lo = AudioDsp.Loopify(L, F); var ro = AudioDsp.Loopify(R, F);
            var chs = new[] { lo, ro };
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(lo, ro, true);
        }

        public static SynthBuf NeonHum(int seed, bool bright)
        {
            var rng = Rng(seed);
            int n = N(2.0);
            var o = new float[n];
            double baseF = bright ? 100 : 50;
            int K = bright ? 34 : 14;
            for (int k = 1; k <= K; k++)
            {
                double f = baseF * k;
                double a = 1 / Math.Pow(k, bright ? 1.0 : 1.5) * (0.5 + rng());
                if (!bright && (k == 2 || k == 4)) a *= 2.5;
                double ph = rng() * 2 * Math.PI;
                double w = 2 * Math.PI * f / AudioDsp.SR;
                for (int i = 0; i < n; i++) o[i] += (float)(Math.Sin(w * i + ph) * a);
            }
            if (bright)
            {
                var fz = AudioDsp.White(n, rng);
                AudioDsp.ChainPeriodic(fz, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 2500, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 9000, 0.7) });
                for (int i = 0; i < n; i++) o[i] += (float)(fz[i] * Math.Pow(Math.Abs(Math.Sin(2 * Math.PI * 50 * i / (double)AudioDsp.SR)), 6) * 1.2);
            }
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 45, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, bright ? 7000 : 1200, 0.7) });
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        public static SynthBuf NeonZap()
        {
            var rng = Rng(352);
            int n = N(0.45);
            var o = new float[n];
            int clicks = 8 + (int)(rng() * 12);
            for (int k = 0; k < clicks; k++) AudioDsp.Mix(o, AudioDsp.NoiseClick(rng, 0.006, Rr(rng, 0.0003, 0.0015)), Rr(rng, 0.3, 1), (int)(rng() * N(0.28)));
            for (int i = 0; i < N(0.3); i++)
            {
                double gate = Math.Sin(i / (double)AudioDsp.SR * 2 * Math.PI * 7) > -0.2 ? 1 : 0;
                double v = 0; for (int k = 1; k < 12; k++) v += Math.Sin(2 * Math.PI * 100 * k * i / (double)AudioDsp.SR) / k;
                o[i] += (float)(v * 0.15 * gate * Math.Exp(-i / (double)N(0.15)));
            }
            AudioDsp.Chain(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 300, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 9000, 0.7) });
            AudioDsp.Fades(o, 0.001, 0.05);
            AudioDsp.NormalizePeak(new[] { o }, 0.9);
            return Mono(o, false);
        }

        public static SynthBuf Vending()
        {
            var rng = Rng(361);
            int n = N(4.0);
            var o = new float[n];
            double[][] harmonics = {
                new double[] { 50, 0.35 }, new double[] { 100, 1 }, new double[] { 150, 0.45 }, new double[] { 200, 0.5 }, new double[] { 300, 0.22 },
                new double[] { 400, 0.12 }, new double[] { 600, 0.05 }, new double[] { 137, 0.18 }, new double[] { 274, 0.06 }, new double[] { 7200, 0.006 },
            };
            foreach (var ha in harmonics)
            {
                double f = ha[0], a = ha[1];
                double ph = rng() * 6.28, w = 2 * Math.PI * f / AudioDsp.SR;
                for (int i = 0; i < n; i++) o[i] += (float)(Math.Sin(w * i + ph) * a);
            }
            var fan = AudioDsp.PinkLoop(n, rng);
            AudioDsp.ChainPeriodic(fan, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 520, 0.8) });
            AudioDsp.Mix(o, fan, 0.9);
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 40, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 8000, 0.7) });
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        public static SynthBuf Wind()
        {
            var rng = Rng(371);
            int n = N(15);
            var chs = new float[2][];
            var gust = AudioDsp.SmoothRandomLoop(n, rng, 0.4);
            for (int ch = 0; ch < 2; ch++)
            {
                var baseB = AudioDsp.PinkLoop(n, rng);
                AudioDsp.ChainPeriodic(baseB, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 600, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 40, 0.7) });
                var w = AudioDsp.White(n, rng);
                var fm = AudioDsp.SmoothRandomLoop(n, rng, 0.25);
                var bq = new AudioDsp.Biquad(AudioDsp.FiltType.Bandpass, 700, 18);
                var wh = new float[n];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if ((i & 127) == 0) bq.Set(AudioDsp.FiltType.Bandpass, 700 + 250 * fm[i], 18);
                        double y = bq.Tick(w[i]);
                        if (pass != 0) wh[i] = (float)y;
                    }
                }
                var o = new float[n];
                for (int i = 0; i < n; i++)
                {
                    double g = 0.55 + 0.45 * gust[i];
                    o[i] = (float)(baseB[i] * g * 2 + wh[i] * g * g * 0.25);
                }
                chs[ch] = o;
            }
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(chs[0], chs[1], true);
        }

        // ------------------------------------------------------------------ THUNDER
        static SynthBuf Thunder(int seed, bool near, double dur)
        {
            var rng = Rng(seed);
            int n = N(dur);
            var L = new float[n]; var R = new float[n];
            int grains = near ? 95 : 70;
            var rolls = new List<(double t, double w, double a)>();
            double tc = near ? 0.03 : Rr(rng, 0.3, 0.7);
            while (tc < dur - 3)
            {
                rolls.Add((tc, Rr(rng, 0.15, 0.55), Math.Exp(-tc / (dur * 0.35)) * Rr(rng, 0.3, 1) * (rolls.Count == 0 ? 1.4 : 1)));
                tc += Rr(rng, 0.7, 2.2);
            }
            double wsum = 0; foreach (var r in rolls) wsum += r.a;
            for (int g = 0; g < grains; g++)
            {
                double pick = rng() * wsum;
                var roll = rolls[0];
                foreach (var r in rolls) { pick -= r.a; if (pick <= 0) { roll = r; break; } }
                double t = roll.t + Math.Abs(AudioDsp.Rgauss(rng)) * roll.w;
                t = Math.Min(t, dur - 2);
                double gd = Rr(rng, 0.4, 2.0);
                int len = N(gd);
                double cutoff = Rr(rng, 60, 320) * (near ? 1 + 1.6 * Math.Exp(-t / 0.8) : 1) * (1 - 0.4 * t / dur);
                var gr = AudioDsp.White(len, rng);
                AudioDsp.Chain(gr, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, cutoff, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, cutoff, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 22, 0.7) });
                double att = Rr(rng, 0.02, near ? 0.15 : 0.4) * AudioDsp.SR;
                double tau = Rr(rng, 0.1, 0.45) * AudioDsp.SR;
                for (int i = 0; i < len; i++) gr[i] = (float)(gr[i] * Math.Min(1, i / att) * Math.Exp(-Math.Max(0, i - att) / tau));
                double a = (0.3 + 0.7 * rng()) * Math.Sqrt(roll.a);
                var pg = AudioDsp.PanGains(Rr(rng, -0.85, 0.85));
                AudioDsp.Mix(L, gr, a * pg[0], N(t)); AudioDsp.Mix(R, gr, a * pg[1], N(t));
            }
            var sub = new float[n];
            double ph = 0;
            double subT = near ? 0.6 : 1.4;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)AudioDsp.SR;
                ph += (38 - 8 * Math.Min(1, t / 4)) / AudioDsp.SR;
                double e = Math.Exp(-Math.Pow((t - subT) / (near ? 1.2 : 1.8), 2));
                sub[i] = (float)(Math.Sin(2 * Math.PI * ph) * e);
            }
            AudioDsp.Mix(L, sub, near ? 0.09 : 0.06); AudioDsp.Mix(R, sub, near ? 0.09 : 0.06);
            if (near)
            {
                var crack = new float[N(1.2)];
                int subs = 7 + (int)(rng() * 7);
                for (int k = 0; k < subs; k++)
                {
                    double t = k == 0 ? 0 : AudioDsp.Rpow(rng, 1.6) * 0.45;
                    var c = AudioDsp.NoiseClick(rng, Rr(rng, 0.15, 0.5), Rr(rng, 0.02, 0.09));
                    AudioDsp.Mix(crack, c, (k == 0 ? 1 : Rr(rng, 0.3, 0.8)) * Math.Exp(-t / 0.4), N(t));
                }
                for (int k = 0; k < 900; k++)
                {
                    double t = AudioDsp.Rpow(rng, 1.8) * 0.9;
                    crack[N(t)] += (float)((rng() < 0.5 ? -1 : 1) * AudioDsp.Rpow(rng, 2) * Math.Exp(-t / 0.3) * 2.5);
                }
                AudioDsp.Chain(crack, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 120, 0.7), AudioDsp.F(AudioDsp.FiltType.Peaking, 1400, 0.8, 3), AudioDsp.F(AudioDsp.FiltType.Lowpass, 6500, 0.7) });
                for (int i = 0; i < crack.Length; i++) crack[i] = (float)(crack[i] * (Math.Exp(-i / (double)N(0.35)) * 0.6 + 0.4 * Math.Max(0, 1 - i / (double)crack.Length)));
                int pre = N(0.02);
                AudioDsp.Mix(L, crack, 0.4, pre); AudioDsp.Mix(R, crack, 0.4, pre + N(0.004));
            }
            AudioDsp.Chain(L, new[] { AudioDsp.F(AudioDsp.FiltType.Lowshelf, 120, 0.7, 4), AudioDsp.F(AudioDsp.FiltType.Highpass, 20, 0.7) });
            AudioDsp.RemoveDC(L);
            AudioDsp.Chain(R, new[] { AudioDsp.F(AudioDsp.FiltType.Lowshelf, 120, 0.7, 4), AudioDsp.F(AudioDsp.FiltType.Highpass, 20, 0.7) });
            AudioDsp.RemoveDC(R);
            var chs = new[] { L, R };
            AudioDsp.NormalizePeak(chs, 0.6);
            foreach (var c in chs) { AudioDsp.TanhSat(c, 1.6); AudioDsp.Fades(c, 0.002, 2.0); }
            AudioDsp.NormalizePeak(chs, 0.9);
            return Stereo(L, R, false);
        }

        public static SynthBuf ThunderNear() => Thunder(501, true, 10);
        public static SynthBuf ThunderMid() => Thunder(502, true, 12);
        public static SynthBuf ThunderFar() => Thunder(503, false, 13);

        // ------------------------------------------------------------------ FOLEY
        static SynthBuf Footstep(int seed, bool puddle)
        {
            var rng = Rng(seed);
            int n = N(puddle ? 0.5 : 0.35);
            var o = new float[n];
            AudioDsp.Mix(o, AudioDsp.Ping(Rr(rng, 65, 105), 0.02), 0.32);
            AudioDsp.Mix(o, Filt(AudioDsp.NoiseClick(rng, 0.03, 0.0035), new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 1600, 0.7) }), 0.5);
            var slap = Filt(AudioDsp.NoiseClick(rng, 0.16, Rr(rng, 0.018, 0.035)), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 1700, 2900), 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 700, 0.7) });
            for (int i = 0; i < Math.Min(slap.Length, 70); i++) slap[i] = (float)(slap[i] * i / 70);
            AudioDsp.Mix(o, slap, 0.75, N(0.002));
            for (int k = 0, c = 4 + (int)(rng() * 6); k < c; k++)
            {
                AudioDsp.Mix(o, Filt(AudioDsp.NoiseClick(rng, 0.004, 0.0006), new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 3000, 0.7) }), Rr(rng, 0.08, 0.2), N(Rr(rng, 0.005, 0.09)));
            }
            int toe = N(Rr(rng, 0.06, 0.11));
            AudioDsp.Mix(o, Filt(AudioDsp.NoiseClick(rng, 0.06, 0.012), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 2500, 0.8) }), 0.35, toe);
            AudioDsp.Mix(o, AudioDsp.Ping(Rr(rng, 90, 140), 0.012), 0.18, toe);
            if (puddle)
            {
                AudioDsp.Mix(o, Filt(AudioDsp.NoiseClick(rng, 0.3, Rr(rng, 0.045, 0.07)), new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, Rr(rng, 900, 1500), 0.5) }), 0.9, N(0.004));
                for (int k = 0, c = 3 + (int)(rng() * 4); k < c; k++) AudioDsp.Mix(o, AudioDsp.Bubble(Rr(rng, 600, 2000), Rr(rng, 0.005, 0.015), 0.3), Rr(rng, 0.15, 0.35), N(Rr(rng, 0.02, 0.2)));
            }
            AudioDsp.Chain(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 50, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 11000, 0.7) });
            AudioDsp.Fades(o, 0, 0.05);
            AudioDsp.NormalizePeak(new[] { o }, 0.9);
            return Mono(o, false);
        }


        public static SynthBuf Horn0() { return Horn(801, false); }
        public static SynthBuf Horn1() { return Horn(802, true); }
        public static SynthBuf Horn2() { return Horn(803, false); }

        public static SynthBuf FootstepVar(int i) => Footstep(601 + i, false);
        public static SynthBuf PuddleVar(int i) => Footstep(611 + i, true);

        static SynthBuf Engine(int seed, bool high)
        {
            var rng = Rng(seed);
            double cyc = 0.06; int cycles = 20;
            int n = N(cyc * cycles);
            var o = new float[n];
            var cylAmp = new double[6];
            for (int k = 0; k < 6; k++) cylAmp[k] = 1 + AudioDsp.Rgauss(rng) * 0.12;
            double fr = high ? 260 : 160, tau = high ? 0.0028 : 0.0042;
            var pulse = new float[N(0.03)];
            for (int i = 0; i < pulse.Length; i++)
            {
                double t = i / (double)AudioDsp.SR;
                pulse[i] = (float)(Math.Exp(-t / tau) * Math.Sin(2 * Math.PI * fr * t));
            }
            for (int c = 0; c < cycles; c++)
            {
                for (int k = 0; k < 6; k++)
                {
                    double t = c * cyc + k * (cyc / 6) + AudioDsp.Rgauss(rng) * 0.00012;
                    double a = cylAmp[k] * (1 + AudioDsp.Rgauss(rng) * 0.04);
                    AudioDsp.MixWrap(o, pulse, a, N(t));
                    if (high)
                    {
                        var nc = AudioDsp.NoiseClick(rng, 0.004, 0.0009);
                        AudioDsp.Chain(nc, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 3500, 0.7) });
                        AudioDsp.MixWrap(o, nc, a * 0.35, N(t));
                    }
                }
            }
            var intake = AudioDsp.White(n, rng);
            AudioDsp.ChainPeriodic(intake, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, high ? 1800 : 900, 0.7) });
            for (int i = 0; i < n; i++) o[i] += (float)(intake[i] * (high ? 0.06 : 0.03) * (1 + Math.Sin(2 * Math.PI * 100 * i / (double)AudioDsp.SR)));
            AudioDsp.ChainPeriodic(o, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 30, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowshelf, 110, 0.7, high ? 2 : 5), AudioDsp.F(AudioDsp.FiltType.Peaking, high ? 600 : 250, 1, 4), AudioDsp.F(AudioDsp.FiltType.Lowpass, high ? 3800 : 1100, 0.7) });
            if (high) AudioDsp.TanhSat(o, 2.0);
            AudioDsp.RemoveDC(o);
            AudioDsp.NormalizeRms(new[] { o }, LOOP_RMS);
            return Mono(o, true);
        }

        public static SynthBuf EngineLow() => Engine(701, false);
        public static SynthBuf EngineHigh() => Engine(702, true);

        public static SynthBuf TireHiss()
        {
            var rng = Rng(381);
            int n = N(5);
            var chs = new float[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                var h = AudioDsp.PinkLoop(n, rng);
                AudioDsp.ChainPeriodic(h, new[] { AudioDsp.F(AudioDsp.FiltType.Bandpass, 1900, 0.5), AudioDsp.F(AudioDsp.FiltType.Highshelf, 6000, 0.7, -6) });
                var s = AudioDsp.PinkLoop(n, rng);
                AudioDsp.ChainPeriodic(s, new[] { AudioDsp.F(AudioDsp.FiltType.Lowpass, 500, 0.7), AudioDsp.F(AudioDsp.FiltType.Highpass, 60, 0.7) });
                var o = new float[n];
                for (int i = 0; i < n; i++) o[i] = (float)(h[i] * 1.6 + s[i] * 0.8);
                chs[ch] = o;
            }
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(chs[0], chs[1], true);
        }

        // ------------------------------------------------------------------ MUSIC
        static double Mtof(double m) => 440 * Math.Pow(2, (m - 69) / 12);

        public static SynthBuf Music()
        {
            var rng = Rng(401);
            double beat = 0.75, bar = beat * 4, Ls = bar * 16;
            int n = N(Ls);
            int tail = N(4);
            var padL = new float[n + tail]; var padR = new float[n + tail];
            int[][] chords = {
                new[] { 38, 50, 53, 57, 60, 64 },   // Dm9
                new[] { 34, 46, 50, 53, 57, 60 },   // Bbmaj9
                new[] { 31, 43, 46, 50, 53, 57 },   // Gm9
                new[] { 33, 45, 50, 52, 55, 57 },   // A7sus4
                new[] { 38, 50, 53, 57, 62, 64 },
                new[] { 34, 46, 53, 57, 60, 62 },
                new[] { 31, 43, 50, 53, 57, 58 },
                new[] { 33, 45, 49, 52, 55, 57 },   // A7
            };
            double cl = bar * 2;
            for (int ci = 0; ci < chords.Length; ci++)
            {
                int s = N(ci * cl); int len = N(cl + 3);
                int[] notes = new int[5];
                Array.Copy(chords[ci], 1, notes, 0, 5);
                double root = chords[ci][0];
                foreach (var m in notes)
                {
                    double f = Mtof(m);
                    double[] dets = { -7, 5, -4, 8 };
                    for (int d = 0; d < 4; d++)
                    {
                        var tmp = new float[len];
                        double det = dets[d];
                        double phase0 = rng();
                        AudioDsp.AddSaw(tmp, f * Math.Pow(2, det / 1200), 0.05, 0, len, phase0);
                        for (int i = 0; i < len; i++)
                        {
                            double t = i / (double)AudioDsp.SR;
                            double a = Math.Min(1, t / 1.8);
                            double r = t > cl ? Math.Max(0, 1 - (t - cl) / 3) : 1;
                            tmp[i] = (float)(tmp[i] * a * a * r);
                        }
                        AudioDsp.Mix(d >= 2 ? padR : padL, tmp, 1, s);
                    }
                }
            }
            var pL = AudioDsp.FoldTail(padL, n); var pR = AudioDsp.FoldTail(padR, n);
            foreach (var p in new[] { pL, pR })
            {
                var bq = new AudioDsp.Biquad(AudioDsp.FiltType.Lowpass, 800, 1.1);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if ((i & 127) == 0) bq.Set(AudioDsp.FiltType.Lowpass, 650 + 550 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (double)N(24))), 1.1);
                        double y = bq.Tick(p[i]);
                        if (pass != 0) p[i] = (float)y;
                    }
                }
            }
            var L = new float[n + tail]; var R = new float[n + tail];
            AudioDsp.Mix(L, pL, 1); AudioDsp.Mix(R, pR, 1);
            double e8 = beat / 2;
            for (int k = 0; k < Ls / e8; k++)
            {
                double t0 = k * e8;
                var c = chords[(int)(t0 / cl) % chords.Length];
                double f = Mtof(c[0] + 12);
                int len = N(e8 + 0.05);
                var b = new float[len];
                double ph = 0;
                for (int i = 0; i < len; i++)
                {
                    double t = i / (double)AudioDsp.SR;
                    ph += f / AudioDsp.SR;
                    double tri = 1 - 4 * Math.Abs(ph % 1 - 0.5);
                    b[i] = (float)((Math.Sin(2 * Math.PI * ph) * 0.8 + tri * 0.2) * Math.Min(1, t / 0.03) * Math.Exp(-t / 0.22) * (k % 2 != 0 ? 0.75 : 1));
                }
                AudioDsp.Mix(L, b, 0.22, N(t0)); AudioDsp.Mix(R, b, 0.22, N(t0));
            }
            double s16 = beat / 4;
            int[] pat = { 0, 2, 4, 1, 3, 4, 2, 0 };
            for (int k = 0; k < Ls / s16; k++)
            {
                if (rng() < 0.42) continue;
                double t0 = k * s16;
                int ci = (int)(t0 / cl) % chords.Length;
                var c = chords[ci];
                int m = c[1 + pat[k % pat.Length]] + 24;
                double f = Mtof(m);
                int len = N(0.5);
                var b = new float[len];
                for (int i = 0; i < len; i++)
                {
                    double t = i / (double)AudioDsp.SR;
                    b[i] = (float)((Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t + 1) + 0.08 * Math.Sin(6 * Math.PI * f * t)) * Math.Min(1, t / 0.004) * Math.Exp(-t / 0.13));
                }
                double g = 0.05 * (0.6 + 0.4 * rng()) * (ci >= 4 ? 1.0 : 0.6);
                for (int e = 0; e < 6; e++)
                {
                    float[] dst = e % 2 != 0 ? R : L;
                    double t = t0 + e * beat * 0.75;
                    if (e == 0) { AudioDsp.MixWrap(L, b, g * Math.Pow(0.5, e) * 0.7, N(t)); AudioDsp.MixWrap(R, b, g * Math.Pow(0.5, e) * 0.7, N(t)); }
                    else AudioDsp.MixWrap(dst, b, g * Math.Pow(0.5, e), N(t));
                }
            }
            for (int ci = 0; ci < chords.Length; ci++)
            {
                int len = N(cl + 2);
                int[] ms = { chords[ci][3] + 24, chords[ci][5] + 12 };
                foreach (var m in ms)
                {
                    double f = Mtof(m);
                    var b = new float[len];
                    double tr = Rr(rng, 0.15, 0.4);
                    for (int i = 0; i < len; i++)
                    {
                        double t = i / (double)AudioDsp.SR;
                        b[i] = (float)(Math.Sin(2 * Math.PI * f * t) * Math.Sin(Math.PI * Math.Min(1, t / (double)len * AudioDsp.SR)) * (0.6 + 0.4 * Math.Sin(2 * Math.PI * tr * t)));
                    }
                    var pg = AudioDsp.PanGains(Rr(rng, -0.6, 0.6));
                    AudioDsp.Mix(L, b, 0.012 * pg[0], N(ci * cl)); AudioDsp.Mix(R, b, 0.012 * pg[1], N(ci * cl));
                }
            }
            var lo = AudioDsp.FoldTail(L, n); var ro = AudioDsp.FoldTail(R, n);
            AudioDsp.ChainPeriodic(lo, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 30, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 9000, 0.7) });
            AudioDsp.ChainPeriodic(ro, new[] { AudioDsp.F(AudioDsp.FiltType.Highpass, 30, 0.7), AudioDsp.F(AudioDsp.FiltType.Lowpass, 9000, 0.7) });
            var chs = new[] { lo, ro };
            AudioDsp.NormalizeRms(chs, LOOP_RMS);
            return Stereo(lo, ro, true);
        }
    }
}
