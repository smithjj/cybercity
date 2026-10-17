// Runtime audio mix: rain layers, city bed, positional emitters, trains, sirens, horns,
// neon zaps, thunder, music. Port of src/systems/audio/soundscape.js.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public class EmitterSlot
    {
        public AudioSource Src;
        public float Mul = 1f, Dip = 1f;
        public Vector3 Pos;
        public double FreeAt;
        public bool Active;
    }

    public class EmitterPool
    {
        readonly List<EmitterSlot> _slots = new List<EmitterSlot>();
        readonly float _maxD;
        readonly string _clip;
        readonly float _mul;
        readonly bool _alternate;
        readonly string _clipB;

        public EmitterPool(string clip, int count, float maxD, float mul, bool alternate = false, string clipB = null)
        {
            _clip = clip; _maxD = maxD; _mul = mul; _alternate = alternate; _clipB = clipB;
            for (int i = 0; i < count; i++)
            {
                var use = alternate && i % 2 != 0 && clipB != null ? clipB : clip;
                var s = AudioBus.MakeSource(use, true, 0f, maxD * 2.2f, AudioBus.Root.transform);
                s.Play();
                _slots.Add(new EmitterSlot { Src = s });
            }
        }

        // candidates: world positions; stagger - assigns nearest free slots
        public void Assign(List<Vector3> candidates, double t)
        {
            var free = new List<EmitterSlot>();
            foreach (var s in _slots)
                if (!s.Active && t >= s.FreeAt) free.Add(s);
            if (free.Count == 0 || candidates.Count == 0) return;
            var camPos = Camera.main ? Camera.main.transform.position : Vector3.zero;
            foreach (var c in candidates)
            {
                if (free.Count == 0) break;
                float d2 = (c - camPos).sqrMagnitude;
                if (d2 > _maxD * _maxD) continue;
                // skip if an active slot already owns this point
                bool owned = false;
                foreach (var s in _slots)
                    if (s.Active && (s.Pos - c).sqrMagnitude < 1f) { owned = true; break; }
                if (owned) continue;
                EmitterSlot best = null; float bd = float.MaxValue;
                foreach (var f in free)
                {
                    float fd = (f.Src.transform.position - c).sqrMagnitude;
                    if (fd < bd) { bd = fd; best = f; }
                }
                if (best == null || (best.Src.transform.position - c).sqrMagnitude < 1f && best.Active) continue;
                best.Src.transform.position = c;
                best.Pos = c;
                best.Active = true;
                best.Mul = _mul * (_alternate && (_slots.IndexOf(best) % 2) != 0 ? 0.72f : 1f);
                free.Remove(best);
            }
        }

        // force-assign a specific moving point (tire hiss follows cars)
        public void AssignOne(Vector3 pos, double t)
        {
            var camPos = Camera.main ? Camera.main.transform.position : Vector3.zero;
            EmitterSlot best = null; float bd = float.MaxValue;
            foreach (var s in _slots)
            {
                if (!s.Active || t < s.FreeAt) continue;
                float d = (s.Pos - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            if (best == null || bd > 36f) return; // already tracking, or stale slot too far off
            best.Src.transform.position = pos;
            best.Pos = pos;
        }

        public void Update(float dt, float level, double t)
        {
            foreach (var s in _slots)
            {
                if (!s.Active)
                {
                    AudioBus.SetVol(s.Src, 0f, 0.15f, dt);
                    continue;
                }
                float target = level * s.Mul * s.Dip;
                AudioBus.SetVol(s.Src, target, 0.15f, dt);
                if (s.Dip < 1f) s.Dip = Mathf.Min(1f, s.Dip + dt / 0.2f);
                var camPos = Camera.main ? Camera.main.transform.position : Vector3.zero;
                if ((s.Pos - camPos).sqrMagnitude > _maxD * _maxD * 2.6f)
                {
                    s.Active = false;
                    s.FreeAt = t + 0.4;
                }
            }
        }

        public void ZapNearest(Vector3 pos)
        {
            EmitterSlot best = null; float bd = 4f;
            foreach (var s in _slots)
            {
                if (!s.Active) continue;
                float d = (s.Pos - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            if (best != null) best.Dip = 0.25f;
        }

        public List<EmitterSlot> ActiveSlots()
        {
            var o = new List<EmitterSlot>();
            foreach (var s in _slots) if (s.Active) o.Add(s);
            return o;
        }
    }

    public static class Soundscape
    {
        public static bool Built;

        // 2D beds
        static AudioSource _rainFar, _rainNear, _drops, _traffic, _wind, _babble, _babbleLP;
        static AudioSource _metalPatter, _carRoof;
        // pools
        static EmitterPool _neon, _vending, _shops, _gutters, _tires;
        // moving
        static AudioSource[] _trains;
        static AudioSource[] _sirens;
        static double[] _sirenSpawn;
        // music
        static AudioSource _music;
        // candidates
        static List<Vector3> _neonPts = new List<Vector3>(), _vendPts = new List<Vector3>(),
            _shopPts = new List<Vector3>(), _gutterPts = new List<Vector3>();
        static int _slowPhase; static double _slowT;

        // events
        static double _hornT = 14, _zapT = 8, _thunderT = -1;
        static float _lastLightning;

        public static float Smooth(double a, double b, double x)
        {
            double t = AudioDsp.Clamp((x - a) / (b - a), 0, 1);
            return (float)(t * t * (3 - 2 * t));
        }

        public static void Build()
        {
            if (!AudioBus.Ready) return;
            var root = AudioBus.Root.transform;

            _rainFar = AudioBus.MakeSource("rainFar", false, 0f, parent: root); _rainFar.Play();
            _rainNear = AudioBus.MakeSource("rainNear", false, 0f, parent: root); _rainNear.Play();
            _drops = AudioBus.MakeSource("droplets", false, 0f, parent: root); _drops.Play();
            _traffic = AudioBus.MakeSource("trafficBed", false, 0f, parent: root); _traffic.Play();
            _wind = AudioBus.MakeSource("wind", false, 0f, parent: root); _wind.Play();
            _babble = AudioBus.MakeSource("crowdBabble", false, 0f, parent: root);
            _babbleLP = _babble; // lowpass via AudioLowPassFilter below
            var lpf = _babble.gameObject.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = 3500f;
            _babble.Play();
            _metalPatter = AudioBus.MakeSource("metalPatter", true, 0f, 120f, root); _metalPatter.Play();
            _carRoof = AudioBus.MakeSource("carRoof", false, 0f, parent: root); _carRoof.Play(); // no drivable car yet; stays at 0

            _neon = new EmitterPool("neonA", 4, 45f, 0.5f, true, "neonB");
            _vending = new EmitterPool("vending", 2, 30f, 0.5f);
            _shops = new EmitterPool("ramenShop", 2, 40f, 0.6f);
            _gutters = new EmitterPool("gutter", 2, 25f, 0.5f);
            _tires = new EmitterPool("tireHiss", 3, 45f, 0.6f);

            _trains = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                _trains[i] = AudioBus.MakeSource("trainRumble", true, 0f, 1100f, root);
                _trains[i].Play();
            }
            _sirens = new AudioSource[3];
            _sirenSpawn = new double[3];
            for (int i = 0; i < 3; i++)
            {
                _sirens[i] = null;
                _sirenSpawn[i] = 20 + i * 35;
            }

            _music = AudioBus.MakeSource("music", false, 0.2f, parent: root);
            _music.Play();

            BuildCandidates();
            Built = true;
        }

        static void BuildCandidates()
        {
            _neonPts.Clear(); _vendPts.Clear(); _shopPts.Clear(); _gutterPts.Clear();
            var facades = FacadeAPI.Collect();
            int i = 0;
            foreach (var f in facades)
            {
                var p = f.CS(); p.y += 3.2f;
                if (i % 3 == 0) _neonPts.Add(p);
                if (i % 7 == 2) _vendPts.Add(p);
                if (i % 11 == 5) _gutterPts.Add(p + Vector3.down * 2.2f);
                i++;
            }
            // ramen / shop fronts along the avenue (frontage x = ±10.2, like the JS shop lots)
            for (int k = 0; k < 24; k++)
            {
                float z = 50f - k * 22f;
                _shopPts.Add(new Vector3(10.4f, 1.8f, z));
                _shopPts.Add(new Vector3(-10.4f, 1.8f, z + 9f));
            }
        }

        // ---------------- events ----------------
        struct SirenVoice { public int Slot; public string Clip; public Vector3 From, To; public double T0, Dur; public float Speed; public int Path; }
        static SirenVoice[] _sv = new SirenVoice[3];

        static Vector3 SirenPos(int path, double p)
        {
            // path 0: avenue (x=0, z +420..-420) ; 1/2: cross streets z -60/-180/-300 ; 3: expressway deck y 22.45
            switch (path)
            {
                case 0: return new Vector3((float)Rr2(p, -8, 8), 1.7f, (float)(420 - 840 * p));
                case 1: return new Vector3((float)(-200 + 340 * p), 1.7f, -60f);
                case 2: return new Vector3((float)(-200 + 340 * p), 1.7f, -180f);
                case 3: return new Vector3((float)(-200 + 340 * p), 23.2f, -300f);
                case 4: return new Vector3(0f, 23.2f, (float)(420 - 840 * p));
                default: return new Vector3((float)(-200 + 340 * p), 1.7f, -300f);
            }
        }

        static double Rr2(double p, double a, double b) => a + (b - a) * p;

        static void SpawnSiren(int slot, double t)
        {
            int path = UnityEngine.Random.Range(0, 6);
            if (path >= 3 && UnityEngine.Random.value < 0.5f) path -= 3;
            bool wail = UnityEngine.Random.value < 0.7f;
            double dur = Rr2(UnityEngine.Random.value, 12, 30);
            _sv[slot] = new SirenVoice
            {
                Slot = slot,
                Clip = wail ? "sirenWail" : "sirenHiLo",
                From = SirenPos(path, 0), To = SirenPos(path, 1),
                T0 = t, Dur = dur, Speed = (float)Rr2(UnityEngine.Random.value, 12, 22), Path = path,
            };
            var src = AudioBus.MakeSource(_sv[slot].Clip, true, 0f, 700f, AudioBus.Root.transform);
            src.Play();
            _sirens[slot] = src;
        }

        // ---------------- update ----------------
        public static void Update(float dt, double t)
        {
            if (AudioBus.Root == null) { Built = false; AudioBus.ForceReady(AudioBus.Clips.Count >= 39); }
            if (!Built)
            {
                if (AudioBus.Ready) Build();
                else { AudioBus.GenerateInBackground(); return; } // still synthesizing; come back when done
            }
            if (!Built || AudioBus.Root == null) return;
            AudioBus.ApplyMaster();
            var cam = Camera.main;
            var camPos = cam ? cam.transform.position : Vector3.zero;
            float rain = WeatherDriver.Built ? WeatherDriver.RainNow : 0f;
            float windMag = WeatherDriver.Built ? WeatherDriver.GustNow : 1f;
            float h = Mathf.Max(0f, camPos.y);

            // ---- 2D beds
            AudioBus.SetVol(_rainFar, 0.5f * Mathf.Pow(Mathf.Clamp01(rain), 1.2f), 0.25f, dt);
            AudioBus.SetVol(_rainNear, 0.42f * Mathf.Pow(Mathf.Clamp01(rain), 1.5f) * (1 - 0.4f * Smooth(6, 30, h)), 0.2f, dt);
            AudioBus.SetVol(_drops, 0.4f * Smooth(0.02f, 0.5f, rain), 0.3f, dt);
            AudioBus.SetVol(_traffic, 0.2f * (1 + 0.7f * Smooth(4, 40, h)), 0.35f, dt);
            AudioBus.SetVol(_wind, (0.05f + 0.4f * Smooth(8, 40, h)) * Mathf.Clamp(windMag, 0.4f, 1.6f), 0.45f, dt);

            // crowd density heuristic (district based, reduced by height + rain)
            var u = SimCoord.FromUnity(camPos);
            float dens = 0.15f;
            if (Math.Abs(u.x) < 10.5f) dens = 0.6f;
            else if (u.x < -9f && u.x > -96f && Math.Abs(u.z + 100) < 7) dens = 0.45f;
            else if (u.x > 114f) dens = 0.35f;
            dens *= (float)(1 - 0.4 * Smooth(6, 30, h)) * (1 - 0.3f * Mathf.Clamp01(rain));
            var blp = _babble.GetComponent<AudioLowPassFilter>();
            if (blp != null) blp.cutoffFrequency = Mathf.Lerp(1500, 4500, Smooth(0, 0.6f, dens));
            AudioBus.SetVol(_babble, 0.3f * dens, 0.4f, dt);

            // ---- metal patter: nearest facade point
            if (rain > 0.02f && _neonPts.Count > 0)
            {
                Vector3 best = _neonPts[0]; float bd = float.MaxValue;
                foreach (var p in _gutterPts)
                {
                    float d = (p - camPos).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
                _metalPatter.transform.position = best;
                AudioBus.SetVol(_metalPatter, 0.7f * rain * Mathf.Clamp01(1 - bd / 900f), 0.25f, dt);
            }
            else AudioBus.SetVol(_metalPatter, 0f, 0.25f, dt);

            // ---- pools (assignment at 5Hz staggered)
            _slowT += dt;
            if (_slowT > 0.2f)
            {
                _slowT = 0;
                _slowPhase = (_slowPhase + 1) % 3;
                if (_slowPhase == 0)
                {
                    _neon.Assign(_neonPts, t);
                    _vending.Assign(_vendPts, t);
                    _shops.Assign(_shopPts, t);
                    if (rain > 0.05f) _gutters.Assign(_gutterPts, t);
                }
                else if (_slowPhase == 2)
                {
                    // tire hiss on moving traffic near the player
                    var cars = VehicleTraffic.GetTrafficSample(12, 45f, camPos);
                    int si = 0;
                    foreach (var (pos, sp) in cars)
                    {
                        if (si >= 3) break;
                        if (sp < 2f) continue;
                        _tires.AssignOne(pos, t);
                        si++;
                    }
                }
            }
            _neon.Update(dt, rain > 0.05f ? 0.85f : 1f, t);
            _vending.Update(dt, 1f, t);
            _shops.Update(dt, 1f, t);
            _gutters.Update(dt, 0.5f * Mathf.Pow(Mathf.Clamp01(rain), 0.7f), t);
            _tires.Update(dt, 1f, t);

            // ---- trains: schedule sim along elevated rail (unity z = +140, y = 14)
            for (int i = 0; i < 2; i++)
            {
                var tr = _trainState[i];
                if (!tr.Moving)
                {
                    if (t >= tr.NextAt)
                    {
                        tr.Moving = true; tr.T0 = t;
                        tr.Dur = 110f / tr.Speed + 6;
                        tr.Dir = UnityEngine.Random.value < 0.5f ? 1 : -1;
                        tr.Speed = (float)Rr2(UnityEngine.Random.value, 18, 26);
                        tr.Dur = 110f / tr.Speed + 6;
                    }
                }
                else
                {
                    double p = (t - tr.T0) / tr.Dur;
                    if (p >= 1) { tr.Moving = false; tr.NextAt = t + (float)Rr2(UnityEngine.Random.value, 25, 55); AudioBus.SetVol(_trains[i], 0f, 0.5f, dt); }
                    else
                    {
                        float z = (float)(140 - 2200 * (tr.Dir > 0 ? p : 1 - p) + 1100);
                        z = Mathf.Clamp(z, -1100, 1100);
                        _trains[i].transform.position = new Vector3(camPos.x + (tr.Dir > 0 ? -60 : 60), 14.5f, Mathf.Lerp(-1100, 1100, (float)(tr.Dir > 0 ? p : 1 - p)));
                        float d = Vector3.Distance(_trains[i].transform.position, camPos);
                        float g = 1.6f * (1 - Smooth(500, 900, d));
                        AudioBus.SetVol(_trains[i], Mathf.Clamp01(g), 0.15f, dt);
                        _trains[i].pitch = Mathf.Lerp(0.9f, 1.1f, tr.Speed / 26f);
                    }
                }
                _trainState[i] = tr;
            }

            // ---- sirens
            for (int i = 0; i < 3; i++)
            {
                if (_sirens[i] == null)
                {
                    if (t >= _sirenSpawn[i]) SpawnSiren(i, t);
                    continue;
                }
                var sv = _sv[i];
                double p = (t - sv.T0) / sv.Dur;
                if (p >= 1)
                {
                    UnityEngine.Object.Destroy(_sirens[i].gameObject);
                    _sirens[i] = null;
                    _sirenSpawn[i] = t + Rr2(UnityEngine.Random.value, 45, 125);
                    continue;
                }
                var pos = Vector3.Lerp(sv.From, sv.To, (float)p);
                _sirens[i].transform.position = pos;
                float d = Vector3.Distance(pos, camPos);
                float occl = (sv.Path <= 2 && Math.Abs(u.x) > 14) ? 0.35f : 1f;
                float env = Mathf.Clamp01((float)Math.Min(p * 6, (1 - p) * 4));
                AudioBus.SetVol(_sirens[i], 0.8f * env * occl * Mathf.Clamp01(1 - d / 650f), 0.12f, dt);
                _sirens[i].pitch = 1f;
            }

            // ---- horns
            if (t >= _hornT)
            {
                _hornT = t + Rr2(UnityEngine.Random.value, 7, 29);
                var pos = UnityEngine.Random.value < 0.6f
                    ? new Vector3((float)Rr2(UnityEngine.Random.value, -8, 8), 1.5f, camPos.z + (float)Rr2(UnityEngine.Random.value, -220, 220))
                    : new Vector3(camPos.x + (float)Rr2(UnityEngine.Random.value, -220, 220), 1.5f, (float)Rr2(UnityEngine.Random.value, -300, 60));
                if ((pos - camPos).sqrMagnitude > 40f * 40f)
                {
                    int v = UnityEngine.Random.Range(0, 3);
                    AudioBus.PlayOneShot(v == 0 ? "horn0" : v == 1 ? "horn1" : "horn2", pos, (float)Rr2(UnityEngine.Random.value, 0.5, 1.0), 1f, 260f);
                }
            }

            // ---- neon zaps
            if (t >= _zapT)
            {
                _zapT = t + Rr2(UnityEngine.Random.value, 3, 15);
                var act = _neon.ActiveSlots();
                if (act.Count > 0)
                {
                    var s = act[UnityEngine.Random.Range(0, act.Count)];
                    AudioBus.PlayOneShot("neonZap", s.Pos, 0.8f, 1f, 90f);
                    _neon.ZapNearest(s.Pos);
                }
            }

            // ---- thunder on lightning pulse
            float li = WeatherDriver.Built ? WeatherDriver.LightningNow : 0f;
            if (li > 0.5f && _lastLightning <= 0.1f && _thunderT < 0)
                _thunderT = t + (float)Rr2(UnityEngine.Random.value, 2, 5);
            _lastLightning = li;
            if (_thunderT > 0 && t >= _thunderT)
            {
                _thunderT = -1;
                string v = UnityEngine.Random.value < 0.4f ? "thunderNear" : UnityEngine.Random.value < 0.6f ? "thunderMid" : "thunderFar";
                var pos = camPos + new Vector3(UnityEngine.Random.Range(-300, 300), UnityEngine.Random.Range(-80, 40), UnityEngine.Random.Range(-300, 300));
                AudioBus.PlayOneShot(v, pos, 1f, 1f, 3000f);
            }

            // ---- music (M toggles; level 0.2)
            if (_music != null)
            {
                bool on = AudioBus.MusicOn && !AudioBus.Muted;
                AudioBus.SetVol(_music, on ? 0.2f : 0f, 0.4f, dt);
            }
        }

        static (bool Moving, double NextAt, double T0, double Dur, float Speed, int Dir)[] _trainState =
            new (bool, double, double, double, float, int)[2];

        public static void ToggleMusic()
        {
            AudioBus.MusicOn = !AudioBus.MusicOn;
            AudioBus.SavePrefs();
        }

        public static string Describe()
        {
            int act = Built ? _neon.ActiveSlots().Count + _vending.ActiveSlots().Count + _shops.ActiveSlots().Count + _gutters.ActiveSlots().Count : 0;
            return $"ready={AudioBus.Ready} clips={AudioBus.Clips.Count} emitters={act} music={AudioBus.MusicOn} vol={AudioBus.Volume:F2} muted={AudioBus.Muted}";
        }
    }
}
