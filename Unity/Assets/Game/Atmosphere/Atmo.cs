using UnityEngine;

namespace NeonKaido
{
    /// Port of the atmosphere shared state (src/systems/atmosphere/atmo.js U + index.js logic).
    /// All colors LINEAR (pre-exposure, pre-tonemap) — same as the JS.
    public static class Atmo
    {
        // Horizon haze color = the color everything fades into
        public static readonly Vector3 FogColor = new Vector3(0.085f, 0.058f, 0.16f);
        public static readonly Vector3 MidColor = new Vector3(0.045f, 0.034f, 0.10f);
        public static readonly Vector3 ZenithColor = new Vector3(0.010f, 0.011f, 0.030f);
        public static readonly Vector3 GlowMagenta = new Vector3(0.12f, 0.025f, 0.10f);
        public static readonly Vector3 GlowOrange = new Vector3(0.13f, 0.05f, 0.018f);
        public static readonly Vector3 GlowBlue = new Vector3(0.015f, 0.04f, 0.12f);
        public static readonly Vector3 GroundHaze = new Vector3(0.07f, 0.045f, 0.12f);
        public const float SkyBrightness = 1.0f;

        public const float FogDensity = 0.0022f;   // density at fogBase height
        public const float FogFalloff = 0.028f;    // exponential height falloff (1/m)
        public const float FogBase = 0.0f;
        public const float FogStart = 14.0f;       // metres of clear air in front of the camera
        public const float HazeDensity = 0.0002f;  // height-independent distance haze
        public const float FogMax = 0.985f;
        public const float GroundGlow = 0.18f;     // street-level inscatter boost
        public const float CloudCover = 0.5f;

        public static readonly Vector3 LightningDir = new Vector3(0.3f, 0.35f, -0.9f).normalized;
        public static readonly Vector3 LightningColor = new Vector3(0.55f, 0.62f, 1.0f);

        public const float EnvIntensityBase = 0.3f;
        public const float HemiBase = 0.12f;
        public static readonly Color HemiSky = new Color(0x4a / 255f, 0x5a / 255f, 0x9a / 255f);
        public static readonly Color HemiGround = new Color(0x1a / 255f, 0x10 / 255f, 0x20 / 255f);

        /// Per-frame atmosphere state (JS: system update). The scene is permanent night —
        /// there is no time-of-day in the JS game.
        public class State
        {
            public Vector2 cloudOffset;
            public float rain = 1f;
            public float flash;
            public double t;

            // JS: this.flash = target > this.flash ? target : max(target, flash - dt*6)
            public void Update(float dt, GameContext ctx)
            {
                var w = ctx.weather;
                cloudOffset += new Vector2(w.wind.x, w.wind.y) * dt * 0.0018f;
                rain = w.rain;
                double target = w.lightning;
                flash = target > flash ? (float)target : Mathf.Max((float)target, flash - dt * 6f);
                t += dt;
            }

            public float LightningUniform => flash * 1.6f;
            public float EnvIntensity => EnvIntensityBase * (1 + flash * 3f);
            public float HemiIntensity => HemiBase * (1 + flash * 8f);
        }

        static readonly State _state = new State();

        /// Push the atmosphere constants + per-frame state into the shader globals
        /// shared by every NkCommon-based shader (fog/ambient/glow/rain/clouds).
        /// JS: hemisphere light 0x4a5a9a / 0x1a1020 at 0.12; no directional light.
        public static void PushGlobals()
        {
            Shader.SetGlobalVector("_FogColor", FogColor);
            Shader.SetGlobalVector("_MidColor", MidColor);
            Shader.SetGlobalVector("_ZenithColor", ZenithColor);
            Shader.SetGlobalVector("_GlowMagenta", GlowMagenta);
            Shader.SetGlobalVector("_GlowOrange", GlowOrange);
            Shader.SetGlobalVector("_GlowBlue", GlowBlue);
            Shader.SetGlobalVector("_GroundHaze", GroundHaze);
            Shader.SetGlobalFloat("_SkyBrightness", 1f);
            Shader.SetGlobalFloat("_Rain", _state.rain);
            Shader.SetGlobalVector("_CloudOffset", new Vector4(_state.cloudOffset.x, _state.cloudOffset.y, 0, 0));
            float hi = _state.HemiIntensity;
            Shader.SetGlobalColor("_AmbientSky", new Color(HemiSky.r * hi, HemiSky.g * hi, HemiSky.b * hi));
            Shader.SetGlobalColor("_AmbientGround", new Color(HemiGround.r * hi, HemiGround.g * hi, HemiGround.b * hi));
            // JS has no directional sun/moon: keep sky-light color at zero
            Shader.SetGlobalVector("_SkyLightDir", new Vector4(0.3f, 0.8f, 0.3f, 0));
            Shader.SetGlobalVector("_SkyLightColor", Vector4.zero);
        }

        /// Build the sky dome: camera-tracking inner sphere rendering the verbatim
        /// JS skyColor port (NEON KAIDO/Sky). JS renders this as a WebGPU
        /// scene.backgroundNode; in Unity it must be real geometry that follows
        /// the camera (SkyFollow LateUpdate) so ray directions match per-pixel.
        public static void BuildSkyDome()
        {
            var old = GameObject.Find("skydome");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var go = GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Sphere);
            go.name = "skydome";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());   // no physics — camera lives inside it
            var mr = go.GetComponent<UnityEngine.Renderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var mat = new Material(Shader.Find("NEON KAIDO/Sky"));
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.LoadRawTextureData(AtmoNoise.Create(256));   // verbatim JS fBm noise
            tex.Apply(false, false);
            mat.SetTexture("_NoiseTex", tex);
            mr.sharedMaterial = mat;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(1800f, 1800f, 1800f);   // radius 900 < cam far 2000
            go.AddComponent<SkyFollow>();
        }
    }

    /// Tiny helper: keep the sky dome centered on the camera so its ray
    /// directions match the JS backgroundNode() per-pixel mapping.
    public class SkyFollow : MonoBehaviour
    {
        Transform _cam;
        void LateUpdate()
        {
            var c = Camera.main;
            if (c != null) _cam = c.transform;
            if (_cam != null) transform.position = _cam.position;
        }
    }
}
