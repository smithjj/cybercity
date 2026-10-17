// Steam plumes + low ground mist — port of src/systems/weather/steam.js (analytic GPU variant).
// Each instance derives age/position from time (stateless, deterministic). Emitter arrays are
// CPU-set (_WxEmitA/B/C); puffs sample a generated soft-noise atlas (_WxPuffTex).
Shader "NEON KAIDO/WxSteam"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float3 _SkyLightDir;
            float3 _SkyLightColor;
            float3 _AmbientSky;
            float3 _AmbientGround;
            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Wetness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"
            #include "Assets/Shaders/WxCommon.hlsl"

            #define WX_EMITS 96
            float4 _WxEmitA[WX_EMITS]; // xyz pos (unity), w type (0 plume, 1 exhaust, 2 mist)
            float4 _WxEmitB[WX_EMITS]; // xyz dir (unity), w strength
            float4 _WxEmitC[WX_EMITS]; // x size, y spread, z life, w speed
            float _WxEmitCount;
            float _WxPerEmitter;       // 48

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;  // (±0.5, ±0.5)
                float2 ek : TEXCOORD1;      // x = emitter index, y = particle index in emitter
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float3 col : TEXCOORD2;
                float alpha : TEXCOORD3;
                float2 cellUV : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                int e = (int)IN.ek.x;
                float k = IN.ek.y;
                float4 a = _WxEmitA[min(e, WX_EMITS - 1)];
                float4 b = _WxEmitB[min(e, WX_EMITS - 1)];
                float4 c = _WxEmitC[min(e, WX_EMITS - 1)];
                float fi = IN.ek.x * _WxPerEmitter + k;
                float h1 = WxHash(fi * 1.731 + 0.37), h2 = WxHash(fi * 2.113 + 5.1), h3 = WxHash(fi * 0.917 + 9.3);
                float life = c.z * (h3 * 0.5 + 0.75);
                float phase = frac(_WxTime / life + k / _WxPerEmitter + h1);
                float age = phase * life;
                bool isMist = a.w > 1.5;
                float3 dir = b.xyz;
                float speed = c.w;
                // rise with deceleration, drift with wind (stronger as it rises), wobble
                float rise = speed * age / (age * 0.35 + 1.0);
                float3 drift = float3(_WxWind.x, 0, -_WxWind.y) * _WxGust * (age * age * 0.08 + age * 0.15);
                float3 wob = float3(sin(age * 1.7 + h2 * 30.0), sin(age * 1.1 + h1 * 20.0) * 0.3, cos(age * 1.3 + h3 * 25.0)) * (age * 0.12 * c.y);
                float3 jitter = float3(h1 - 0.5, 0, h2 - 0.5) * (c.y * 0.35);
                float3 plumeP = a.xyz + jitter + dir * rise + drift + wob;
                // mist: wide random placement, slow drift, lifecycle only in alpha
                float3 mistP = a.xyz + float3((h1 - 0.5) * c.y, h3 * 0.6, (h2 - 0.5) * c.y) + float3(_WxWind.x, 0, -_WxWind.y) * (age * 0.25);
                float3 center = isMist ? mistP : plumeP;
                float size = isMist ? c.x * (h3 * 0.6 + 0.7) : c.x * (age * 0.55 + 0.35) * (h2 * 0.5 + 0.75);
                float3 toCam = _WxCam - center;
                float dist = length(toCam);
                float3 f = toCam / max(dist, 1e-4);
                float3 right = normalize(cross(float3(0, 1, 0), f) + float3(1e-5, 0, 0));
                float3 up = cross(f, right);
                float rot = h1 * 6.283 + age * (h2 > 0.5 ? 0.25 : -0.25);
                float cs = cos(rot), sn = sin(rot);
                float gx = IN.corner.x * cs - IN.corner.y * sn;
                float gy = IN.corner.x * sn + IN.corner.y * cs;
                float3 world = center + right * (gx * size) + up * (gy * size);
                float fadeIn = smoothstep(0.0, isMist ? 0.3 : 0.08, phase);
                float fadeOut = pow(1.0 - phase, isMist ? 1.0 : 1.3);
                float nearFade = smoothstep(0.4, 2.5, dist - size * 0.3);
                float lit = WxSoftClamp(WxLightAt(center, f, 0.55, 0.35) * 0.06, 0.8);
                OUT.col = _WxAmbient * 3.0 + float3(0.05, 0.055, 0.07) + lit
                        + float3(0.5, 0.55, 0.7) * _WxLightning * 1.5;
                OUT.alpha = fadeIn * fadeOut * b.w * nearFade;
                float cell = floor(h2 * 3.999);
                OUT.cellUV = float2(fmod(cell, 2.0), floor(cell / 2.0)) * 0.5;
                OUT.worldPos = world;
                OUT.corner = IN.corner;
                OUT.positionCS = NkClip(world);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.corner + 0.5;
                float puff = SAMPLE_TEXTURE2D(_WxPuffTex, sampler_WxPuffTex, IN.cellUV + uv * 0.5).a;
                float a = clamp(puff * IN.alpha, 0.0, 1.0);
                if (a < 0.003) discard;
                float3 col = IN.col;
                col = NkFogApply(col, IN.worldPos, _WorldSpaceCameraPos.xyz, 1.0, _Rain * 0.35 + 0.75,
                    _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz,
                    _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
