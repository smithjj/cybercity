// Port of the expressway asphalt material (landmarks/expressway.js): dark deck
// with dashed lane lines, solid edge lines, yellow median lines — all derived
// from world position. Wet response like the JS.
Shader "NEON KAIDO/ExpresswayDeck"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
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

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float _DeckZ;      // expressway centre line (unity z, mirrored)
                float _LaneW;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float3 nor : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            float LineAt(float lz, float zc, float w) { return smoothstep(w, w * 0.6, abs(lz - zc)); }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float n = NkVnoise(wp.xy * 0.4 + wp.z * 0.4) * 0.5 + 0.5;
                float lz = wp.z - _DeckZ;
                // dashed lane-boundary lines, solid edge lines
                float dash = step(0.5, frac(wp.x / 12.0));
                float marks = max(max(LineAt(lz, -_LaneW, 0.08) * dash, LineAt(lz, _LaneW, 0.08) * dash),
                                  max(LineAt(lz, -_LaneW * 2 + 0.1, 0.1), LineAt(lz, _LaneW * 2 - 0.1, 0.1)));
                float yellow = max(LineAt(lz, -0.45, 0.08), LineAt(lz, 0.45, 0.08));
                float3 c = float3(0.045, 0.045, 0.05) * lerp(0.7, 1.2, n);
                c = lerp(c, float3(0.6, 0.6, 0.58), marks * 0.8);
                c = lerp(c, float3(0.65, 0.45, 0.08), yellow * 0.8);
                float wet = clamp(_Wetness, 0, 1);
                c *= lerp(1.0, 0.7, wet);
                float oRough = clamp(lerp(0.75, 0.12, wet * lerp(0.6, 1.0, n)), 0.05, 1);

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
                float3 oCol = c * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * c + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65);

                oCol += NkBPointLights(N, wp, V, c, oRough, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

// touch
