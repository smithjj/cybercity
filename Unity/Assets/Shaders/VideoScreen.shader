// Corner video screens — port of mkScreenMaterial: cycles the 2x2 ad grid (ads_grid.png, LINEAR data),
// wipe to next ad, slow zoom, scanlines, wipe bar. Per-vertex sd = (seed, intensity) in UV2.
Shader "NEON KAIDO/VideoScreen"
{
    Properties { _MainTex ("tex", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Cull Back
            ZWrite On

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

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            float NkPcgHash(float seed)
            {
                uint state = (uint)floor(seed) * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 sd : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 sd : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.uv = IN.uv;
                OUT.sd = IN.sd;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _SimTime;
                float2 u0 = IN.uv;
                float2 sd = IN.sd;
                float period = 6.0;
                float tt = t + sd.x * 2.3;
                float ph = frac(tt / period);
                // cell index cycling the 2x2 grid
                float k = frac((tt / period) * 0.25) * 4.0 + 0.01;
                k = floor(k);
                float2 ca = float2(fmod(k, 2.0), floor(k / 2.0));
                float k2 = frac((tt / period + 0.25) * 0.25) * 4.0 + 0.01;
                k2 = floor(k2);
                float2 cb = float2(fmod(k2, 2.0), floor(k2 / 2.0));
                float wipe = smoothstep(0.9, 1.0, ph);
                float showB = step(u0.x, wipe);
                float zoom = 1 - ph * 0.04;
                float2 lu = (u0 - 0.5) * zoom + 0.5;
                float3 sA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(ca.x + lu.x, 1 - ca.y + lu.y) * 0.5).rgb;
                float3 sB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(cb.x + u0.x, 1 - cb.y + u0.y) * 0.5).rgb;
                float3 c = lerp(sA, sB, showB);
                float3 lin = c * c;
                float scan = sin(u0.y * 720.0) * 0.08 + 0.92;
                float bar = smoothstep(0.02, 0.0, abs(u0.x - wipe)) * step(0.9, ph) * 3.0;
                float3 colOut = lin * scan * sd.y + float3(0.6, 0.9, 1.0) * bar;

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
