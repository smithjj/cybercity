// Audio mix graph + runtime buffer generation. Port of src/systems/audio/core.js + index.js.
// Buses become AudioSource groups; the master limiter runs via OnAudioFilterRead on the listener.
// Reverb uses an AudioReverbZone (the JS convolver's custom IR can't be a Unity send without a mixer;
// the generated IR recipe is still verified by RMS/peak checks).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    // Master soft limiter: threshold -6 dB, ~12:1, smoothed — applied to the whole mix.
    public class AudioLimiterFX : MonoBehaviour
    {
        double _env;
        double _gain = 1;

        void OnAudioFilterRead(float[] data, int channels)
        {
            double thresh = Math.Pow(10, -6.0 / 20); // 0.5012
            int n = data.Length;
            for (int i = 0; i < n; i += channels)
            {
                double m = 0;
                for (int c = 0; c < channels; c++) m = Math.Max(m, Math.Abs(data[i + c]));
                // envelope follower (fast attack 5ms, release 120ms)
                double aAtt = Math.Exp(-1.0 / (0.005 * AudioDsp.SR));
                double aRel = Math.Exp(-1.0 / (0.120 * AudioDsp.SR));
                double a = m > _env ? aAtt : aRel;
                _env = a * _env + (1 - a) * m;
                double target = _env > thresh ? thresh / _env : 1.0;
                double g = Math.Pow(target, 1.0 / 12.0); // ~12:1 soft knee via power law
                _gain += (g - _gain) * 0.0008;
                for (int c = 0; c < channels; c++) data[i + c] = (float)(data[i + c] * _gain);
            }
        }
    }

    public static class AudioBus
    {
        public static bool Ready { get; private set; }
        public static Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        // prefs (localStorage nk.audio.v1 -> PlayerPrefs)
        public const string PrefKey = "nk.audio.v1";
        public static float Volume = 0.8f;
        public static bool Muted, MusicOn = true;

        public static GameObject Root;
        public static AudioSource Music;      // music bus (2D)
        static AudioListener _listener;

        // generation order (most important first so the soundscape comes alive quickly)
        public static readonly (string name, Func<SynthBuf> gen)[] Order = {
            ("rainFar", AudioSynth.RainFar),
            ("trafficBed", AudioSynth.TrafficBed),
            ("crowdBabble", AudioSynth.CrowdBabble),
            ("neonA", () => AudioSynth.NeonHum(351, true)),
            ("neonB", () => AudioSynth.NeonHum(353, false)),
            ("wind", AudioSynth.Wind),
            ("rainNear", AudioSynth.RainNear),
            ("droplets", AudioSynth.Droplets),
            ("sirenWail", AudioSynth.SirenWail),
            ("sirenHiLo", AudioSynth.SirenHiLo),
            ("trainRumble", AudioSynth.TrainRumble),
            ("vending", AudioSynth.Vending),
            ("ramenShop", AudioSynth.RamenShop),
            ("gutter", AudioSynth.Gutter),
            ("metalPatter", AudioSynth.MetalPatter),
            ("carRoof", AudioSynth.CarRoof),
            ("music", AudioSynth.Music),
            ("tireHiss", AudioSynth.TireHiss),
            ("engineLow", AudioSynth.EngineLow),
            ("engineHigh", AudioSynth.EngineHigh),
            ("horn0", () => HornVar(801, false)),
            ("horn1", () => HornVar(802, true)),
            ("horn2", () => HornVar(803, false)),
            ("neonZap", AudioSynth.NeonZap),
            ("thunderNear", AudioSynth.ThunderNear),
            ("thunderMid", AudioSynth.ThunderMid),
            ("thunderFar", AudioSynth.ThunderFar),
            ("step0", () => AudioSynth.FootstepVar(0)),
            ("step1", () => AudioSynth.FootstepVar(1)),
            ("step2", () => AudioSynth.FootstepVar(2)),
            ("step3", () => AudioSynth.FootstepVar(3)),
            ("step4", () => AudioSynth.FootstepVar(4)),
            ("step5", () => AudioSynth.FootstepVar(5)),
            ("step6", () => AudioSynth.FootstepVar(6)),
            ("step7", () => AudioSynth.FootstepVar(7)),
            ("puddle0", () => AudioSynth.PuddleVar(0)),
            ("puddle1", () => AudioSynth.PuddleVar(1)),
            ("puddle2", () => AudioSynth.PuddleVar(2)),
            ("puddle3", () => AudioSynth.PuddleVar(3)),
        };

        static SynthBuf HornVar(int seed, bool dbl)
        {
            // Horn(seed, double) is private; recreate via public wrappers
            return seed == 801 ? AudioSynth.Horn0() : seed == 802 ? AudioSynth.Horn1() : AudioSynth.Horn2();
        }

        public static void ForceReady(bool v) { Ready = v; }

        public static void LoadPrefs()
        {
            Volume = PlayerPrefs.GetFloat(PrefKey + ".volume", 0.8f);
            Muted = PlayerPrefs.GetInt(PrefKey + ".muted", 0) != 0;
            MusicOn = PlayerPrefs.GetInt(PrefKey + ".music", 1) != 0;
        }

        public static void SavePrefs()
        {
            PlayerPrefs.SetFloat(PrefKey + ".volume", Volume);
            PlayerPrefs.SetInt(PrefKey + ".muted", Muted ? 1 : 0);
            PlayerPrefs.SetInt(PrefKey + ".music", MusicOn ? 1 : 0);
            PlayerPrefs.Save();
        }

        // background generation (like synth.worker.js): one recipe at a time on a worker thread,
        // main thread pumps finished buffers into AudioClips
        static System.Threading.Thread _genThread;
        static readonly object _genLock = new object();
        static readonly Queue<(string name, SynthBuf buf)> _done = new Queue<(string, SynthBuf)>();
        static bool _genRunning;

        public static bool GenerateInBackground()
        {
            EnsureRoot();
            if (Clips.Count >= Order.Length) { Ready = true; return false; }
            PumpFinished();
            lock (_genLock)
            {
                if (_genRunning) return true;
                string next = null;
                foreach (var o in Order) if (!Clips.ContainsKey(o.name)) { next = o.name; break; }
                if (next == null) { Ready = true; return false; }
                Func<SynthBuf> gen = null;
                foreach (var o in Order) if (o.name == next) { gen = o.gen; break; }
                _genRunning = true;
                var name = next;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    SynthBuf sb = null;
                    try { sb = gen(); } catch (Exception) { sb = null; }
                    lock (_genLock) { _done.Enqueue((name, sb)); _genRunning = false; }
                });
            }
            return true;
        }

        static void PumpFinished()
        {
            while (true)
            {
                (string, SynthBuf) item;
                lock (_genLock)
                {
                    if (_done.Count == 0) return;
                    item = _done.Dequeue();
                }
                if (item.Item2 == null || Clips.ContainsKey(item.Item1)) continue;
                var sb = item.Item2;
                int chCount = sb.Channels.Length;
                int len = sb.Channels[0].Length;
                var clip = AudioClip.Create("nk." + item.Item1, len, chCount, AudioDsp.SR, false);
                if (chCount == 1) clip.SetData(sb.Channels[0], 0);
                else
                {
                    var inter = new float[len * chCount];
                    for (int c = 0; c < chCount; c++)
                        for (int j = 0; j < len; j++) inter[j * chCount + c] = sb.Channels[c][j];
                    clip.SetData(inter, 0);
                }
                Clips[item.Item1] = clip;
            }
        }

        // Generate one recipe synchronously into an AudioClip (editor/batch use)
        public static float GenerateNext()
        {
            EnsureRoot();
            if (Clips.Count >= Order.Length) { Ready = true; return 1f; }
            for (int i = 0; i < Order.Length; i++)
            {
                var o = Order[i];
                if (Clips.ContainsKey(o.name)) continue;
                var sb = o.gen();
                int chCount = sb.Channels.Length;
                int len = sb.Channels[0].Length;
                var clip = AudioClip.Create("nk." + o.name, len, chCount, AudioDsp.SR, false);
                if (chCount == 1) clip.SetData(sb.Channels[0], 0);
                else
                {
                    var inter = new float[len * chCount];
                    for (int c = 0; c < chCount; c++)
                        for (int j = 0; j < len; j++) inter[j * chCount + c] = sb.Channels[c][j];
                    clip.SetData(inter, 0);
                }
                Clips[o.name] = clip;
                return (i + 1) / (float)Order.Length;
            }
            Ready = true;
            return 1f;
        }

        static void EnsureRoot()
        {
            if (Root != null) return;
            Root = new GameObject("AudioRoot");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(Root);
            _listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (_listener == null)
            {
                var cam = Camera.main;
                if (cam != null) _listener = cam.gameObject.AddComponent<AudioListener>();
            }
            if (_listener != null && _listener.GetComponent<AudioLimiterFX>() == null)
                _listener.gameObject.AddComponent<AudioLimiterFX>();
            LoadPrefs();
        }

        public static AudioSource MakeSource(string clip, bool spatial, float gain, float maxDist = 90f, Transform parent = null)
        {
            EnsureRoot();
            var go = new GameObject("src." + clip + (spatial ? ".3d" : ".2d"));
            if (parent != null) go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = Clips[clip];
            src.loop = Clips[clip].length > 1.5f; // loops are long; one-shots short
            src.volume = gain;
            src.spatialBlend = spatial ? 1f : 0f;
            src.playOnAwake = false;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Custom;
                src.maxDistance = maxDist;
                src.minDistance = 1f;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                    AnimationCurve.Linear(0f, 1f, 1f, 0.05f));
                src.spread = 60f;
                src.dopplerLevel = 0.7f;
            }
            return src;
        }

        public static void SetVol(AudioSource src, float target, float tau, float dt)
        {
            if (src == null) return;
            float k = 1 - Mathf.Exp(-dt / Mathf.Max(0.01f, tau));
            float v = Mathf.Lerp(src.volume, target, k);
            if (Mathf.Abs(v - src.volume) < 1e-4f) v = target;
            src.volume = Mathf.Clamp01(v);
        }

        public static void PlayOneShot(string clip, Vector3 pos, float gain = 1f, float rate = 1f, float maxDist = 220f)
        {
            if (!Ready || !Clips.ContainsKey(clip)) return;
            EnsureRoot();
            var src = MakeSource(clip, true, gain, maxDist, Root.transform);
            src.transform.position = pos;
            src.pitch = rate;
            src.Play();
            var h = src.gameObject.AddComponent<OneShotKiller>();
            h.life = Clips[clip].length / Mathf.Max(0.05f, rate) + 0.3f;
        }

        public static void ApplyMaster()
        {
            AudioListener.volume = Muted ? 0f : Volume;
        }

        public class OneShotKiller : MonoBehaviour
        {
            public float life = 2f;
            float _t;
            AudioSource _s;
            void Awake() { _s = GetComponent<AudioSource>(); }
            void Update()
            {
                _t += Time.deltaTime;
                if (_t >= life || (_s != null && !_s.isPlaying && _t > 0.1f)) Destroy(gameObject);
            }
        }
    }
}
