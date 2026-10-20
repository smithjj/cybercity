// Weather driver — port of src/systems/weather/{index,common,lightning,lights,heightfield}.js
// plus CPU-simulated drips/streams (drips.js compute logic). Rain/splashes/steam are analytic
// GPU (WxRain/WxSplashes/WxSteam shaders); this class owns the baked heightfield, the lightning
// state machine, the gust/wind/wetness easing, the 24-light gather and the drip simulation.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonKaido
{
    public static class WeatherDriver
    {
        // ---- heightfield bake (port of heightfield.js, static bake instead of per-frame capture)
        const int HTexRes = 1024;
        const float HSpan = 1024f;
        static readonly Vector2 HOrigin = new Vector2(-512f, -692f); // unity (x, z) of texel (0,0)
        static Texture2D _heightTex;
        static float[] _heights; // CPU mirror, unity row-major (z major)

        // ---- lightning (port of lightning.js)
        class StrikeData { public double t0, end; public List<Vector3> pulses; } // (t, dur, amp)
        static Mulberry32 _lrng;
        static double _lNext;
        static StrikeData _strike;
        static double _lightning;

        // ---- light gather (port of lights.js)
        static readonly List<Light> _lights = new List<Light>();
        static float _nextScan = -1f;
        static readonly Vector4[] _lPos = new Vector4[24];
        static readonly Vector4[] _lCol = new Vector4[24];
        static int _lCount;

        // ---- drips / streams (port of drips.js, CPU)
        const int NDrips = 512;
        const int NStreams = 8;
        const int PerStream = 64;
        const int NTotal = NDrips + NStreams * PerStream;
        const float SearchR = 26f;
        static Vector4[] _dPos;  // xyz, radius
        static Vector4[] _dVel;  // xyz, hang timer (>0 hanging)
        static Vector4[] _dEmit; // xyz, dir index 1..4 (leader only)
        static Vector3[] _dCol;  // lit rgb, w visible
        static Mesh _dripMesh;
        static Vector3[] _dripVerts;
        static Color[] _dripColsV;
        static int _frame;

        // ---- camera tracking
        static Vector3 _prevCam;
        static Vector3 _camVel;
        static bool _prevValid;
        static float _wetness = 1f;
        static float _gust = 1f;

        static GameObject _rainGo, _splashGo, _steamGo, _dripGo;

        public static bool Built { get; private set; }
        // audio hooks
        static float _rainNow, _lightningNow;
        public static float RainNow => _rainNow;
        public static float LightningNow => _lightningNow;
        public static float GustNow => _gust;
        public static double LastStrikeT = -100;
        public static float LastStrikeDist = 800;
        // <0 = storm schedule as usual; 0 = clear skies (R toggle); >0 = pinned storm
        public static float RainOverride = -1f;

        // ------------------------------------------------------------------ build

        public static string Build(Transform parent)
        {
            var old = GameObject.Find("Weather");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("Weather");
            root.transform.SetParent(parent, false);

            BakeHeights();
            MakePuffAtlas();

            var rainMat = new Material(Shader.Find("NEON KAIDO/WxRain"));
            _rainGo = QuadObject(root.transform, "rain", rainMat, 327680, (i) => (i, 0f), 4000f);

            var splashMat = new Material(Shader.Find("NEON KAIDO/WxSplashes"));
            _splashGo = QuadObject(root.transform, "splashes", splashMat, 32768 * 6,
                (i) => (i / 6, i % 6), 4000f);

            var steamMat = new Material(Shader.Find("NEON KAIDO/WxSteam"));
            BuildSteamEmitters();
            int E = _emitA.Count;
            _steamGo = QuadObject(root.transform, "steam", steamMat, E * 48,
                (i) => (i / 48, i % 48), 4000f);

            var dripMat = new Material(Shader.Find("NEON KAIDO/WxDrips"));
            BuildDrips(root.transform, dripMat);

            // HDRP only draws transparent queues inside its accepted range (3000..~4000)
            steamMat.renderQueue = 3000;
            rainMat.renderQueue = 3004;
            splashMat.renderQueue = 3008;
            dripMat.renderQueue = 3012;

            _lrng = new Mulberry32(0x5eed1u);
            _lNext = 6.0 + _lrng.Next() * 10.0;
            _strike = null;

            Built = true;
            return $"weather: h=1024^2 rain=327680 splash={32768 * 6} steam={E * 48} drips={NTotal}";
        }

        static GameObject QuadObject(Transform parent, string name, Material mat, int quads,
                                     Func<int, (float, float)> perQuad, float boundsSize)
        {
            var verts = new Vector3[quads * 4];
            var uvs = new Vector2[quads * 4];
            var uvs1 = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            var corners = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            for (int q = 0; q < quads; q++)
            {
                var (a, b) = perQuad(q);
                for (int c = 0; c < 4; c++)
                {
                    int v = q * 4 + c;
                    uvs[v] = corners[c];
                    uvs1[v] = new Vector2(a, b);
                }
                int t = q * 6;
                tris[t] = q * 4; tris[t + 1] = q * 4 + 2; tris[t + 2] = q * 4 + 1;
                tris[t + 3] = q * 4 + 1; tris[t + 4] = q * 4 + 2; tris[t + 5] = q * 4 + 3;
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.uv2 = uvs1;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsSize);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        // ---- static heightfield bake: ground + landmarks decks + building lot tops
        static void BakeHeights()
        {
            _heights = new float[HTexRes * HTexRes];
            var lots = BuildPlan.Lots;
            if (lots.Count == 0) lots = BuildPlan.Run(); // fresh domain: repopulate (deterministic)
            for (int j = 0; j < HTexRes; j++)
            {
                float uz = HOrigin.y + j + 0.5f;
                double simZ = -uz;
                for (int i = 0; i < HTexRes; i++)
                {
                    float ux = HOrigin.x + i + 0.5f;
                    double simX = ux;
                    float h = (float)GroundPlan.HeightAt(simX, simZ);
                    // expressway deck (sim z -240±8, x ±400, top ~22.45)
                    if (simZ > -248 && simZ < -232 && simX > -400 && simX < 400 && h < 22.45f) h = 22.45f;
                    // rail viaduct (sim z -140±4.5, top ~14.4)
                    if (simZ > -144.5 && simZ < -135.5 && h < 14.4f) h = 14.4f;
                    // bridge deck (sim z -260±5, deck y 8)
                    if (simZ > -265 && simZ < -255 && h < 8.4f) h = 8.4f;
                    _heights[j * HTexRes + i] = h;
                }
            }
            // building lots: rasterize their union top over the grid (unity z = -simZ)
            foreach (var lot in lots)
            {
                float top = (float)(lot.BaseY + lot.H);
                int i0 = Mathf.Clamp((int)((float)lot.X0 - HOrigin.x), 0, HTexRes - 1);
                int i1 = Mathf.Clamp((int)((float)lot.X1 - HOrigin.x) + 1, 0, HTexRes - 1);
                float uz0 = -(float)lot.Z1, uz1 = -(float)lot.Z0;
                int j0 = Mathf.Clamp((int)(uz0 - HOrigin.y), 0, HTexRes - 1);
                int j1 = Mathf.Clamp((int)(uz1 - HOrigin.y) + 1, 0, HTexRes - 1);
                for (int j = j0; j <= j1; j++)
                {
                    int row = j * HTexRes;
                    for (int i = i0; i <= i1; i++)
                        if (_heights[row + i] < top) _heights[row + i] = top;
                }
            }
            // upload as RFloat
            if (_heightTex == null)
            {
                _heightTex = new Texture2D(HTexRes, HTexRes, TextureFormat.RFloat, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var bytes = new byte[HTexRes * HTexRes * 4];
            for (int k = 0; k < _heights.Length; k++)
            {
                var b = BitConverter.GetBytes(_heights[k]);
                bytes[k * 4] = b[0]; bytes[k * 4 + 1] = b[1]; bytes[k * 4 + 2] = b[2]; bytes[k * 4 + 3] = b[3];
            }
            _heightTex.LoadRawTextureData(bytes);
            _heightTex.Apply(false, false);
        }

        static float HeightCPU(float ux, float uz)
        {
            float fx = ux - HOrigin.x, fz = uz - HOrigin.y;
            if (fx < 0 || fz < 0 || fx >= HSpan || fz >= HSpan) return 0f;
            return _heights[(int)fz * HTexRes + (int)fx];
        }

        // ---- steam emitters (port of steamEmitters, unity space)
        static readonly List<Vector4> _emitA = new List<Vector4>();
        static readonly List<Vector4> _emitB = new List<Vector4>();
        static readonly List<Vector4> _emitC = new List<Vector4>();

        static void AddEmitter(double sx, double sy, double sz, double dx, double dy, double dz,
                               double strength, double size, double spread, double life, double speed, double type)
        {
            _emitA.Add(new Vector4((float)sx, (float)sy, (float)(-sz), (float)type));
            _emitB.Add(new Vector4((float)dx, (float)dy, (float)(-dz), (float)strength));
            _emitC.Add(new Vector4((float)size, (float)spread, (float)life, (float)speed));
        }

        static void BuildSteamEmitters()
        {
            _emitA.Clear(); _emitB.Clear(); _emitC.Clear();
            double H(double x, double z) { var h = GroundPlan.HeightAt(x, z); return double.IsFinite(h) ? h : 0; }
            // street manholes / vents on the avenue
            var man = new[] {
                (-2.6, 14.0, 1.0), (2.8, -24.0, 0.9), (-1.8, -52.0, 1.1),
                (3.2, -92.0, 0.9), (-3.0, -128.0, 1.0), (1.0, -170.0, 1.0) };
            foreach (var (x, z, s) in man)
                AddEmitter(x, H(x, z) + 0.02, z, 0, 1, 0, 0.5 * s, 1.7, 0.7, 5.0, 1.5, 0);
            // cross street vents
            foreach (var z in Layout.CrossZs)
                AddEmitter(-24, H(-24, z) + 0.02, z + 1.5, 0, 1, 0, 0.3, 1.4, 0.6, 4.5, 1.3, 0);
            // ramen / shop exhaust (fallback positions — facade scan deferred)
            AddEmitter(-10.7, 3.3, 16, 0.9, 0.6, 0, 0.3, 0.9, 0.3, 3.5, 1.2, 1);
            AddEmitter(10.7, 3.6, -36, -0.9, 0.6, 0, 0.25, 0.9, 0.3, 3.5, 1.2, 1);
            // alley grates + vents
            var grate = new[] { (-18.0, 1.0), (-33.0, 0.8), (-47.0, 0.9), (-70.0, 1.0) };
            foreach (var (x, s) in grate)
            {
                double off = (x % 2 != 0) ? 1.2 : -1.2;
                AddEmitter(x, H(x, Layout.AlleyZ) + 0.02, Layout.AlleyZ + off, 0, 1, 0,
                    0.3 * s, 1.2, 0.5, 4.0, 1.1, 0);
            }
            // alley low mist pooling
            for (double x = -14; x > -88; x -= 10)
                AddEmitter(x, H(x, Layout.AlleyZ) + 0.05, Layout.AlleyZ, 0, 1, 0, 0.10, 4.5, 9, 9, 0, 2);
            // avenue low mist near ground
            for (double z = 30; z > -200; z -= 22)
                AddEmitter(0, 0.05, z, 0, 1, 0, 0.05, 6, 16, 10, 0, 2);
            // river mist
            double rc = Layout.RiverXCenter, hw = Layout.RiverHalfWidth, wl = Layout.RiverWaterLevel;
            for (double z = 40; z > -320; z -= 18)
            {
                AddEmitter(rc - hw * 0.5, wl + 0.15, z, 0, 1, 0, 0.12, 9, 30, 12, 0, 2);
                AddEmitter(rc + hw * 0.4, wl + 0.15, z - 9, 0, 1, 0, 0.12, 9, 30, 12, 0, 2);
            }
            var a = _emitA.ToArray(); var b = _emitB.ToArray(); var c = _emitC.ToArray();
            Shader.SetGlobalVectorArray("_WxEmitA", a);
            Shader.SetGlobalVectorArray("_WxEmitB", b);
            Shader.SetGlobalVectorArray("_WxEmitC", c);
            Shader.SetGlobalFloat("_WxEmitCount", a.Length);
            Shader.SetGlobalFloat("_WxPerEmitter", 48f);
        }

        // ---- puff atlas (port of makePuffAtlas)
        static Texture2D _puffTex;
        static void MakePuffAtlas()
        {
            if (_puffTex != null) return;   // runtime texture: rebuild only when a scene load killed it
            const int size = 256, half = 128, N = 64;
            var rng = new Mulberry32(777u);
            var grid = new float[N * N];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.Next();
            float S(float t) => t * t * (3 - 2 * t);
            float G(int x, int y) => grid[((y & (N - 1)) * N) + (x & (N - 1))];
            float Vn(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float xf = x - xi, yf = y - yi;
                float a = G(xi, yi), b = G(xi + 1, yi), c = G(xi, yi + 1), d = G(xi + 1, yi + 1);
                return a + (b - a) * S(xf) + (c - a) * S(yf) + (a - b - c + d) * S(xf) * S(yf);
            }
            var data = new Color32[size * size];
            for (int q = 0; q < 4; q++)
            {
                int ox = (q % 2) * half, oy = (q / 2) * half;
                float sx = q * 17.3f, sy = q * 9.1f;
                for (int y = 0; y < half; y++)
                    for (int x = 0; x < half; x++)
                    {
                        float u = x / (float)half - 0.5f, v = y / (float)half - 0.5f;
                        float n = 0, amp = 0.5f, f = 4;
                        for (int o = 0; o < 5; o++) { n += amp * Vn(u * f + sx, v * f + sy); f *= 2.03f; amp *= 0.5f; }
                        float r = Mathf.Sqrt(u * u + v * v) * 2;
                        float fall = Mathf.Pow(Mathf.Max(0, 1 - r), 1.4f);
                        float al = fall * (0.35f + 1.1f * n) - 0.18f * (1 - fall);
                        al = Mathf.Clamp01(al * 1.25f);
                        data[(oy + y) * size + ox + x] = new Color32(255, 255, 255, (byte)(al * 255));
                    }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels32(data);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(true, false);
            _puffTex = tex;
            Shader.SetGlobalTexture("_WxPuffTex", tex);
        }

        // ---- drips/streams build (CPU)
        static void BuildDrips(Transform parent, Material mat)
        {
            _dPos = new Vector4[NTotal];
            _dVel = new Vector4[NTotal];
            _dEmit = new Vector4[NTotal];
            _dCol = new Vector3[NTotal];
            for (int i = 0; i < NTotal; i++)
            {
                _dPos[i] = new Vector4(0, -1000, 0, 0.001f);
                bool isStream = i >= NDrips;
                float k = isStream ? ((i - NDrips) % PerStream) : 0;
                _dVel[i] = new Vector4(0, 0, 0, isStream ? k / PerStream * 2.2f : WxHashC(i * 3.3f) * 3.0f);
                _dEmit[i] = Vector4.zero;
                _dCol[i] = Vector3.zero;
            }
            // mesh: 4 verts per particle, CPU-updated
            _dripVerts = new Vector3[NTotal * 4];
            _dripColsV = new Color[NTotal * 4];
            var uvs = new Vector2[NTotal * 4];
            var tris = new int[NTotal * 6];
            var corners = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            for (int q = 0; q < NTotal; q++)
            {
                for (int c = 0; c < 4; c++) uvs[q * 4 + c] = corners[c];
                int t = q * 6;
                tris[t] = q * 4; tris[t + 1] = q * 4 + 2; tris[t + 2] = q * 4 + 1;
                tris[t + 3] = q * 4 + 1; tris[t + 4] = q * 4 + 2; tris[t + 5] = q * 4 + 3;
            }
            _dripMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _dripMesh.vertices = _dripVerts;
            _dripMesh.uv = uvs;
            _dripMesh.colors = _dripColsV;
            _dripMesh.triangles = tris;
            _dripMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            _dripGo = new GameObject("drips");
            _dripGo.transform.SetParent(parent, false);
            _dripGo.AddComponent<MeshFilter>().sharedMesh = _dripMesh;
            _dripGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _dripGo.GetComponent<MeshRenderer>().motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        // TSL hash on CPU (same bit recipe as WxHash)
        static float WxHashC(double seed)
        {
            uint state = (uint)(Math.Floor(Math.Abs(seed) + 0.5)) * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return ((word >> 22) ^ word) * (1.0f / 4294967296f);
        }

        // ------------------------------------------------------------------ update

        public static void Update(float dt, float t, Vector3 camPos)
        {
            // statics wiped by a domain reload: rebuild deterministically (same as VehicleTraffic)
            if (!Built) Build(null);
            // camera velocity (teleport reset)
            if (!_prevValid) { _prevValid = true; _prevCam = camPos; _camVel = Vector3.zero; }
            var v = (camPos - _prevCam) / Math.Max(dt, 1e-3f);
            if (v.magnitude > 80f) v = Vector3.zero;
            _camVel = Vector3.Lerp(_camVel, v, Mathf.Min(1f, dt * 8f));
            _prevCam = camPos;

            var ctx = GameContext.I;
            float rain;
            if (RainOverride >= 0f)
            {
                // R toggle holds an eternal regime; glide toward it (0 = clear skies).
                // Fall back to our own last value, NOT 1f: with a null ctx the old code
                // re-read 1f every frame, so the override snapped back to ~1 and the
                // R key appeared dead.
                var cur = ctx != null ? ctx.weather.rain : _rainNow;
                rain = Mathf.Lerp(cur, RainOverride, Mathf.Min(1f, dt * 0.35f));
            }
            else rain = ctx != null ? ctx.weather.rain : 1f;
            // lightning (before easing: feeds ctx.weather.lightning -> Atmo flash)
            float li = RainOverride >= 0f ? 0f : LightningUpdate(dt, t, rain);
            if (ctx != null) ctx.weather.lightning = li;
            // storm drift toward 1 only when in auto mode
            if (RainOverride < 0f) rain += (1f - rain) * Mathf.Min(1f, dt * 0.5f);
            _wetness += ((rain > 0.05f ? 1f : 0f) - _wetness) * Mathf.Min(1f, dt * (rain > 0.05f ? 0.2f : 0.02f));
            if (ctx != null) { ctx.weather.rain = rain; ctx.weather.wetness = _wetness; }
            // gusts
            _gust = 1f + 0.35f * Mathf.Sin(t * 0.37f) + 0.25f * Mathf.Sin(t * 0.91f + 1.3f)
                  + 0.15f * Mathf.Sin(t * 2.3f + 0.4f)
                  + 1.1f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.13f + 2f)), 6f);
            var wind = ctx != null ? ctx.weather.wind : new Vector2(0.6f, -0.25f);
            _frame++;

            _rainNow = rain; _lightningNow = li;
            GatherLights(camPos, t);
            PushGlobals(dt, t, camPos, rain, li, wind);
            DripsUpdate(dt, t, camPos, wind);
        }

        /// Editor-side equivalent of the Update-driven globals. The puff atlas and the heightfield
        /// are runtime textures (never serialized) and every weather uniform is only written from
        /// Update, i.e. Play. After a scene load / editor restart the atlas is gone, so
        /// _WxPuffTex resolves to Unity's white default and the steam quads lose their soft alpha
        /// mask: the alley turns into an opaque lavender veil crossed by hard-edged rectangles.
        /// Rebuild the cheap runtime resources and pin the camera/time so the edit view is the game.
        public static void PushEditorGlobals()
        {
            if (_puffTex == null) MakePuffAtlas();
            if (_heights == null || _heightTex == null) BakeHeights();
            if (_emitA.Count == 0) BuildSteamEmitters();
            var cam = Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            _prevValid = false; _camVel = Vector3.zero;
            GatherLights(camPos, 0f);
            PushGlobals(1f / 60f, 0f, camPos, 1f, 0f, new Vector2(0.6f, -0.25f));
            // Deliberately do NOT set Built: the drip simulation arrays only exist after Build(),
            // and Play mode keeps statics (DisableDomainReload), so claiming Built here would make
            // WeatherDriver.Update skip the build and then null-ref in DripsUpdate.
        }

        /// Cheap per-editor-tick camera update: rain is a camera-centred volume and the steam
        /// billboards orient to _WxCam, so moving the Scene camera without Play mode must move
        /// the weather rig too or the puffs/streaks are left behind at the old pose.
        public static void PushEditorCamera(Vector3 camPos)
        {
            Shader.SetGlobalVector("_WxCam", camPos);
            Shader.SetGlobalVector("_WxCamVel", Vector3.zero);
            var cam = Camera.main;
            if (cam != null)
                Shader.SetGlobalFloat("_WxPixK", 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight));
        }

        static void PushGlobals(float dt, float t, Vector3 camPos, float rain, float lightning, Vector2 wind)
        {
            Shader.SetGlobalFloat("_WxTime", t);
            Shader.SetGlobalFloat("_WxDt", Mathf.Min(dt, 1f / 20f));
            Shader.SetGlobalVector("_WxCam", camPos);
            Shader.SetGlobalVector("_WxCamVel", _camVel);
            Shader.SetGlobalVector("_WxWind", new Vector4(wind.x * 2f, wind.y * 2f, 0, 0));
            Shader.SetGlobalFloat("_WxGust", _gust);
            Shader.SetGlobalFloat("_WxRain", rain);
            Shader.SetGlobalFloat("_WxLightning", lightning);
            Shader.SetGlobalFloat("_WxWet", _wetness);
            var cam = Camera.main;
            float pixK = 0.001f;
            if (cam != null)
                pixK = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            Shader.SetGlobalFloat("_WxPixK", pixK);
            Shader.SetGlobalVector("_WxAmbient", new Vector4(
                Atmo.FogColor.x * 1.6f + 0.006f, Atmo.FogColor.y * 1.6f + 0.006f, Atmo.FogColor.z * 1.6f + 0.006f, 0));
            Shader.SetGlobalFloat("_WxLCount", _lCount);
            Shader.SetGlobalVectorArray("_WxLPos", _lPos);
            Shader.SetGlobalVectorArray("_WxLCol", _lCol);
            if (_heightTex != null)
            {
                Shader.SetGlobalTexture("_WxHeight", _heightTex);
                Shader.SetGlobalVector("_WxHOrigin", new Vector4(HOrigin.x, HOrigin.y, 0, 0));
                Shader.SetGlobalFloat("_WxHScale", 1f / HSpan);
            }
        }

        // 24 nearest pooled lights to the camera (port of lights.js LightGather)
        static void GatherLights(Vector3 cam, float t)
        {
            if (t >= _nextScan)
            {
                _lights.Clear();
                foreach (var rn in new[] { "/Buildings", "/StreetLamps", "/Signage", "/FacadeSigns", "/LandmarksRiver", "/LandmarksRail", "/LandmarksExpress", "/LandmarksBridge" })
                {
                    var root = GameObject.Find(rn);
                    if (root != null) _lights.AddRange(root.GetComponentsInChildren<Light>());
                }
                _lights.RemoveAll(l => l == null);   // purge refs killed by scene rebuilds
                _nextScan = t + 2f;
            }
            _lights.RemoveAll(l => l == null);
            var scored = new List<(Light l, Vector3 p, float range, float s)>();
            foreach (var l in _lights)
            {
                var p = l.transform.position;
                float d2 = (p - cam).sqrMagnitude;
                float range = l.range > 0 ? l.range : 25f;
                if (d2 > (range + 45) * (range + 45)) continue;
                float sScore = l.intensity * Mathf.Max(range, 4f) / (d2 + 25f);
                scored.Add((l, p, range, sScore));
            }
            scored.Sort((a, b) => b.s.CompareTo(a.s));
            _lCount = Mathf.Min(24, scored.Count);
            for (int i = 0; i < 24; i++)
            {
                if (i < _lCount)
                {
                    var (l, p, range, _) = scored[i];
                    _lPos[i] = new Vector4(p.x, p.y, p.z, range);
                    _lCol[i] = new Vector4(l.color.r * l.intensity, l.color.g * l.intensity, l.color.b * l.intensity, 0);
                }
                else { _lPos[i] = new Vector4(0, -9999, 0, 1); _lCol[i] = Vector4.zero; }
            }
        }

        // ------------------------------------------------------------------ lightning (lightning.js port)

        public static void Strike(double t, double intensity = 1.0)
        {
            var r = _lrng;
            var pulses = new List<Vector3>();
            double tt = 0;
            int n = 1 + (int)(r.Next() * 4);
            for (int i = 0; i < n; i++)
            {
                pulses.Add(new Vector3((float)tt, (float)(0.05 + r.Next() * 0.12), (float)((i == 0 ? 0.6 : 0.4 + r.Next() * 0.6) * intensity)));
                tt += 0.06 + r.Next() * 0.22;
            }
            _strike = new StrikeData { t0 = t, end = tt + 0.6, pulses = pulses };
        }

        static float LightningUpdate(float dt, double t, float rain)
        {
            if (_strike == null && t >= _lNext && rain > 0.3f)
            {
                Strike(t, 0.6 + _lrng.Next() * 0.4);
                _lNext = t + 9 + _lrng.Next() * 22;
            }
            double v = 0;
            if (_strike != null)
            {
                double lt = t - _strike.t0;
                foreach (var p in _strike.pulses)
                {
                    double x = lt - p.x;
                    if (x >= 0) v = Math.Max(v, p.z * (x < 0.015 ? x / 0.015 : Math.Exp(-(x - 0.015) / p.y)));
                }
                if (lt > _strike.end) _strike = null;
            }
            _lightning = v;
            return (float)v;
        }

        // ------------------------------------------------------------------ drips/streams (drips.js port, CPU)

        static void DripsUpdate(float dt, double t, Vector3 cam, Vector2 windSim)
        {
            float camX = cam.x, camZ = cam.z, camY = cam.y;
            float uwx = windSim.x * 2f, uwz = -windSim.y * 2f;
            var pixK = 0.001f;
            var cm = Camera.main;
            if (cm != null) pixK = 2f * Mathf.Tan(cm.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cm.pixelHeight);

            for (int i = 0; i < NTotal; i++)
            {
                bool isStream = i >= NDrips;
                int leader = isStream ? i - ((i - NDrips) % PerStream) : i;
                var E = _dEmit[leader];
                if (leader == i)
                {
                    if (E.w > 0.5f)
                    {
                        float dx = E.x - camX, dz = E.z - camZ;
                        if (Mathf.Sqrt(dx * dx + dz * dz) > SearchR * 1.3f) { E.w = 0; _dEmit[i] = E; }
                    }
                    if (E.w < 0.5f)
                    {
                        float fi = i;
                        float a = WxHashC(fi * 1.37 + _frame * 0.1234 + 0.3);
                        float b = WxHashC(fi * 2.71 + _frame * 0.3141 + 7.7);
                        float rr = isStream ? SearchR * 1.1f : SearchR;
                        float cx = camX + (a - 0.5f) * rr * 2f;
                        float cz = camZ + (b - 0.5f) * rr * 2f;
                        float h0 = HeightCPU(cx, cz);
                        float minDrop = isStream ? 3.5f : 1.0f;
                        bool okH = isStream ? (h0 > 4f && h0 < 26f) : (h0 > 1.4f && h0 < camY + 22f);
                        float lowPref = isStream ? 1f : (h0 < 9f ? 1f : 0.25f);
                        if (okH && WxHashC(fi + _frame * 0.77) < lowPref)
                        {
                            float found = 0f; float fx = 0, fy = 0, fz2 = 0;
                            TryDir(cx, cz, 1, 0, 1, h0, minDrop, ref found, ref fx, ref fy, ref fz2);
                            TryDir(cx, cz, -1, 0, 2, h0, minDrop, ref found, ref fx, ref fy, ref fz2);
                            TryDir(cx, cz, 0, 1, 3, h0, minDrop, ref found, ref fx, ref fy, ref fz2);
                            TryDir(cx, cz, 0, -1, 4, h0, minDrop, ref found, ref fx, ref fy, ref fz2);
                            if (found > 0.5f) { E = new Vector4(fx, fy, fz2, found); }
                        }
                    }
                    _dEmit[i] = E; // followers read this every frame (JS: emit.element(i).assign(E))
                }
                var P = _dPos[i];
                var V = _dVel[i];
                float seed = WxHashC(i * 0.917 + 4.1);
                float visible = 0f;
                if (E.w < 0.5f)
                {
                    P = new Vector4(0, -1000, 0, 0.001f);
                }
                else if (V.w > 0f)
                {
                    // hanging at the edge
                    V.w -= dt;
                    float grow = Mathf.Clamp(1f - V.w / 2f, 0.25f, 1f);
                    P = new Vector4(E.x, E.y - grow * 0.004f, E.z,
                        isStream ? 0f : Mathf.Lerp(0.0008f, 0.0026f, grow));
                    V.x = V.y = V.z = 0;
                    if (V.w <= 0f)
                    {
                        // detach
                        float dX = 0, dZ = 0;
                        float k = Mathf.Round(E.w - 1);
                        if (k < 0.5f) { dX = 1; dZ = 0; }
                        else if (k < 1.5f) { dX = -1; dZ = 0; }
                        else if (k < 2.5f) { dX = 0; dZ = 1; }
                        else { dX = 0; dZ = -1; }
                        float push = isStream ? WxHashC(i + _frame) * 0.35f + 0.45f : 0.02f;
                        V.x = dX * push; V.y = isStream ? -0.3f : -0.4f; V.z = dZ * push;
                        P.w = isStream ? Mathf.Lerp(0.0025f, 0.0055f, seed) : Mathf.Lerp(0.0018f, 0.003f, seed);
                    }
                    visible = isStream ? 0f : 1f;
                }
                else
                {
                    // falling
                    V.y -= dt * 9.81f;
                    V.x = Mathf.Lerp(V.x, uwx * 0.35f, dt * 0.4f);
                    V.z = Mathf.Lerp(V.z, uwz * 0.35f, dt * 0.4f);
                    float jit = isStream ? 0.6f : 0.05f;
                    V.x += Mathf.Sin((float)t * 13.0f + seed * 50.0f) * jit * dt;
                    V.z += Mathf.Sin((float)t * 11.0f + seed * 37.0f) * jit * dt;
                    P.x += V.x * dt; P.y += V.y * dt; P.z += V.z * dt;
                    float h = HeightCPU(P.x, P.z);
                    visible = 1f;
                    if (P.y < h || P.y < camY - 40f)
                    {
                        P = new Vector4(E.x, E.y, E.z, 0);
                        V = new Vector4(0, 0, 0, isStream ? WxHashC(i + _frame * 0.5) * 0.03f : Mathf.Lerp(0.5f, 3.5f, WxHashC(i + _frame * 0.13)));
                        visible = 0f;
                    }
                }
                _dPos[i] = P;
                _dVel[i] = V;
                // lit colour
                float dxl = camX - P.x, dyl = camY - P.y, dzl = camZ - P.z;
                float dl = Mathf.Sqrt(dxl * dxl + dyl * dyl + dzl * dzl) + 1e-4f;
                var lit = LightAtC(P.x, P.y, P.z, dxl / dl, dyl / dl, dzl / dl, 0.7f, 0.25f);
                lit = SoftClampC(lit * 0.12f, 0.3f);
                var c = new Vector3(0.142f, 0.099f, 0.262f) + lit + new Vector3(0.55f, 0.6f, 0.75f) * (float)_lightning * 2.5f;
                _dCol[i] = c * (visible > 0.5f ? 1f : 0f);
            }
            WriteDripMesh(t, cam, pixK);
        }

        static void TryDir(float cx, float cz, float dx, float dz, float dirId, float h0, float minDrop,
                           ref float found, ref float fx, ref float fy, ref float fz2)
        {
            if (found > 0.5f) return;
            float h1 = HeightCPU(cx + dx * 0.45f, cz + dz * 0.45f);
            if (h0 - h1 > minDrop)
            {
                float hm = HeightCPU(cx + dx * 0.22f, cz + dz * 0.22f);
                float off = (h0 - hm > minDrop) ? 0.16f : 0.36f;
                fx = cx + dx * off; fz2 = cz + dz * off; fy = h0 - 0.03f;
                found = dirId;
            }
        }

        static Vector3 LightAtC(float px, float py, float pz, float vx, float vy, float vz, float g, float iso)
        {
            var acc = Vector3.zero;
            for (int i = 0; i < _lCount; i++)
            {
                float lx = _lPos[i].x - px, ly = _lPos[i].y - py, lz = _lPos[i].z - pz;
                float d2 = lx * lx + ly * ly + lz * lz;
                float dist = Mathf.Sqrt(d2);
                float rr = dist / Mathf.Max(_lPos[i].w, 0.01f);
                float win = Mathf.Clamp01(1f - rr * rr * rr * rr);
                float atten = win * win / (d2 + 0.35f);
                float cosT = -(lx / (dist + 1e-4f) * vx + ly / (dist + 1e-4f) * vy + lz / (dist + 1e-4f) * vz);
                float g2 = g * g;
                float hg = (1f - g2) / Mathf.Pow(Mathf.Max(1f + g2 - cosT * 2f * g, 1e-3f), 1.5f);
                acc += (Vector3)(_lCol[i] * (atten * (hg * 0.35f + iso)));
            }
            return acc;
        }

        static Vector3 SoftClampC(Vector3 c, float k)
        {
            float m = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            return c / (m * k + 1f);
        }

        static void WriteDripMesh(double t, Vector3 cam, float pixK)
        {
            for (int i = 0; i < NTotal; i++)
            {
                bool isStream = i >= NDrips;
                var P = _dPos[i];
                var V = _dVel[i];
                var C = _dCol[i];
                // streak quad (port of WxStreak, CPU)
                float T = isStream ? 0.07f : 0.045f;
                float mx = (V.x - _camVel.x * 0.7f) * T;
                float my = (V.y - _camVel.y * 0.7f) * T;
                float mz = (V.z - _camVel.z * 0.7f) * T;
                float hx = P.x, hy = P.y, hz = P.z;
                float tx = hx - mx, ty = hy - my, tz = hz - mz;
                float cx2 = (hx + tx) * 0.5f, cy2 = (hy + ty) * 0.5f, cz2 = (hz + tz) * 0.5f;
                float tcx = cam.x - cx2, tcy = cam.y - cy2, tcz = cam.z - cz2;
                float dist = Mathf.Sqrt(tcx * tcx + tcy * tcy + tcz * tcz) + 1e-5f;
                // axis = motion, side = normalize(cross(axis, toCam))
                float sx = my * tcz - mz * tcy, sy = mz * tcx - mx * tcz, sz = mx * tcy - my * tcx;
                float sl = Mathf.Sqrt(sx * sx + sy * sy + sz * sz) + 1e-7f;
                sx /= sl; sy /= sl; sz /= sl;
                float radius = Mathf.Max(P.w, 0.0003f);
                float halfW = Mathf.Max(radius, dist * pixK * 0.8f);
                float coverage = radius / halfW;
                // axisN = normalize(axis + (0,-1e-6,0))
                float al = Mathf.Sqrt(mx * mx + my * my + mz * mz) + 1e-7f;
                float ax = mx / al, ay = my / al, az = mz / al;
                bool hanging = V.w > 0f;
                float distFade = 1f - SmoothStep(22f, 34f, dist);
                float gain = isStream ? 0.85f : (hanging ? 1.0f : 0.8f);
                float rainNow = GameContext.I != null ? GameContext.I.weather.rain : 1f;
                float alpha = coverage * (C.magnitude > 0.001f ? 1f : 0f) * distFade * gain * (rainNow * 0.8f + 0.2f);
                var col = new Color(C.x * (hanging ? 3f : 1.6f), C.y * (hanging ? 3f : 1.6f), C.z * (hanging ? 3f : 1.6f), alpha);
                for (int cn = 0; cn < 4; cn++)
                {
                    float gy = (cn >= 2) ? 0.5f : -0.5f;
                    float gx = (cn % 2 == 1) ? 0.5f : -0.5f;
                    float sgn = gy >= 0 ? 1f : -1f;
                    int vi = i * 4 + cn;
                    _dripVerts[vi] = new Vector3(
                        cx2 + ax * gy + sx * (gx * halfW * 2f),
                        cy2 + ay * gy + sgn * halfW + sy * (gx * halfW * 2f),
                        cz2 + az * gy + sz * (gx * halfW * 2f));
                    _dripColsV[vi] = col;
                }
            }
            _dripMesh.vertices = _dripVerts;
            _dripMesh.colors = _dripColsV;
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
