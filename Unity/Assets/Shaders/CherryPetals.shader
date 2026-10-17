// Port of the petal materials (landmarks/trees.js petalMaterial + position
// animation): 4x4 atlas cells via uv2.x, falling animation (wrap 14m cell,
// 9m fall, per-seed drift/spin), water bob + downstream drift, ground decals.
// _Kind: 0 = falling, 1 = water, 2 = ground.
Shader "NEON KAIDO/CherryPetals"
{
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Off
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
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Wetness;
            float _Rain;
            float _SimTime;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float _Kind;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float2 uv : TEXCOORD0; float cell : TEXCOORD1;
                float3 worldPos : TEXCOORD2; float3 nor : TEXCOORD3; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float4 lp = IN.pos;
                float4 b = IN.uv1; // xyz base pos (unity space), w = seed
                if (_Kind < 0.5)
                {
                    // falling: 9m loop, 14m horizontal wrap cell, per-seed spin
                    float H = 9.0;
                    float tt = _SimTime + b.w * 100;
                    float fallY = frac(b.y / H - _SimTime * lerp(0.06, 0.11, b.w)) * H + 0.02;
                    float3 drift = float3(
                        sin(tt * 0.9) * 0.6 + _SimTime * 0.35 * lerp(0.5, 1.0, b.w), 0,
                        cos(tt * 0.7) * 0.5 - _SimTime * 0.12);
                    float wx = frac(drift.x / 14.0) * 14.0 - 7.0;
                    float wz = frac(drift.z / 14.0) * 14.0 - 7.0;
                    float ang = tt * lerp(2.0, 5.0, b.w);
                    float ca = cos(ang), sa = sin(ang);
                    float3 rot = float3(lp.x * ca, lp.y, lp.x * sa + lp.y * cos(ang * 0.7) * 0.6);
                    lp = float4(b.x + wx, fallY, b.z + wz, 1) + float4(rot, 0);
                }
                else if (_Kind < 1.5)
                {
                    // water: bob + slow downstream drift
                    lp = float4(lp.x, lp.y + sin(_SimTime * 1.3 + b.w * 30) * 0.006,
                                lp.z + sin(_SimTime * 0.08 + b.w * 6.28) * 0.9, 1);
                }
                o.posCS = NkClip(lp.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(lp.xyz);
                o.uv = IN.uv;
                o.cell = IN.uv2.x;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float2 cu = float2(frac(IN.cell / 4.0), floor(IN.cell / 4.0) / 4.0);
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv * 0.25 + float2(cu.x, 0.75 - cu.y));
                clip(t.a - 0.4);
                float3 base = t.rgb * float3(1.0, 0.86, 0.9);
                if (_Kind > 1.5) base *= 0.75;
                float oRough = _Kind < 0.5 ? 0.5 : 0.25;

                float3 Ldir = normalize(_SkyLightDir.xyz);
                float3 H = normalize(Ldir + V);
                float ndl = max(dot(N, Ldir), 0);
                float ndh = max(dot(N, H), 0);
                float ndv = max(dot(N, V), 0.001);
                float a2 = oRough * oRough;
                float dnm = ndh * ndh * (a2 * a2 - 1) + 1;
                float D = a2 * a2 / (3.14159265 * dnm * dnm);
                float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
                float spec = D / (4 * ndl * ndv + 0.001) * ndl * fres;
                float3 env = NkHazeColor(reflect(-V, N), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz,
                    _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float envMix = exp(-oRough * 9.0);
                float3 oCol = base * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * base + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65);

                float emi = _Kind < 0.5 ? 0.35 : 0.08;
                oCol += base * float3(0.9, 0.45, 0.45) * emi;

                oCol += NkBPointLights(N, wp, V, base, oRough, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
