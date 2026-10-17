// Port of landmarks emissiveMat() / basicEmissive(): dark body with emissive
// rgb*intensity, or fully unlit rgb*intensity (_Unlit = 1). Fog + post applied.
Shader "NEON KAIDO/LandmarkEmissive"
{
    Properties
    {
        _Emissive ("Emissive", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 3
        _Unlit ("Unlit (0/1)", Float) = 0
        _Grid ("Grid (0/1)", Float) = 0
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
            float _Rain;
            float _SimTime;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _Emissive;
                float _Intensity, _Unlit, _Grid;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 col = _Emissive.rgb * _Intensity;
                if (_Grid > 0.5)
                {
                    // lantern head: grid pattern dims to 2.5/9 of full glow
                    float grid = max(step(0.88, frac(wp.y * 9.0)), step(0.9, frac((wp.x + wp.z) * 7.0)));
                    col = lerp(col, col * (2.5 / 9.0), grid);
                }
                if (_Unlit < 0.5)
                {
                    // emissiveMat: body 0.02 + emissive; env fog applies over both
                    col = float3(0.02, 0.02, 0.02) * (_AmbientSky.xyz * 30.0) + col;
                }
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return half4(NkPost(col, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
