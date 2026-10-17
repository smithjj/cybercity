// Holo text ring — port of mkRingMaterial: scrolling sign-atlas text strip, additive, double-sided.
Shader "NEON KAIDO/HoloRing"
{
    Properties { _MainTex ("tex", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float4 _Rect; // atlas rect uv: (u0, v0, u1, v1)
            float3 _Tint;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _SimTime;
                float2 u0 = IN.uv;
                float uu = frac(u0.x * 2.0 + t * 0.02);
                float3 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(lerp(_Rect.x, _Rect.z, uu), lerp(_Rect.y, _Rect.w, u0.y))).rgb;
                float lines = sin(u0.y * 60.0 - t * 4.0) * 0.25 + 0.75;
                float3 colOut = _Tint * (s.r * s.r) * lines * 3.0;

                float3 P = IN.worldPos;
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                colOut = NkFogApply(colOut, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(colOut, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
