// Port of the landmark metal() material (landmarks/materials.js): painted/bare
// metal with two-scale procedural grime, wet darkening. No textures.
Shader "NEON KAIDO/LandmarkMetal"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.2, 0.2, 0.22, 1)
        _Rough ("Rough", Float) = 0.45
        _Metal ("Metal", Range(0, 1)) = 0.85
        _Freq ("Grime Freq", Float) = 0.8
    }
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
                float4 _Tint;
                float _Rough, _Metal, _Freq;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float3 nor : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                // World-space clip: this material is also used by the train undercarriage
                // and pantographs, which are Transform-moved (identity-space landmarks
                // render identically either way).
                float3 wp = NkWorld(IN.pos.xyz);
                o.posCS = NkClip(wp);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = wp;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            float NkMxNoise(float3 p)
            {
                float2 q = float2(p.x * 0.71 + p.z * 0.71, p.y * 0.71 + p.x * 0.29);
                return NkVnoise(q) * 2.0 - 1.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float n = NkMxNoise(wp * _Freq) * 0.5 + 0.5;
                float n2 = NkMxNoise(wp * 6.0) * 0.5 + 0.5;
                float wet = clamp(_Wetness, 0, 1);
                float3 col = _Tint.rgb * lerp(0.6, 1.15, n) * lerp(0.85, 1.05, n2) * lerp(1.0, 0.75, wet);
                float oRough = clamp(lerp(_Rough, _Rough * 0.45, wet) + (n2 - 0.5) * 0.2, 0.05, 1);
                float metal = _Metal;

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
                float3 oCol = col * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * col + spec * (metal > 0.5 ? 2 : 1) * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) * lerp(1, col, metal);

                oCol += NkBPointLights(N, wp, V, col, oRough, metal, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
