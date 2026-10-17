// Animated billboard screen — port of signage/billboards.js mkMiraiMaterial (未来へ A BRIGHTER TOMORROW).
// Emissive-only: ken-burns, glitch windows w/ slice offset + chromatic aberration, sakura petals,
// light sweep, rolling refresh bar, LED rows, flash; texture is LINEAR data (JS srgb:false) squared in-shader.
Shader "NEON KAIDO/Billboard"
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            // three.js TSL hash (pcg-random.org, XlGcRh)
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
                // slow ken-burns
                float z = sin(t * 0.12) * 0.015 + 0.975;
                float2 pan = float2(sin(t * 0.07) * 0.006, cos(t * 0.05) * 0.008);
                float2 u1 = (u0 - 0.5) * z + 0.5 + pan;
                // glitch windows (~every 7 s, 0.25 s long)
                float gwin = step(0.86, NkPcgHash(floor(t * 4.0) + 17)) * step(0.5, NkPcgHash(floor(t * 0.5) + 3));
                float slice = NkPcgHash(floor(u0.y * 38.0) + floor(t * 30.0) * 57 + 11) - 0.5;
                float off = slice * 0.05 * gwin;
                float ca = 0.003 + gwin * 0.012;
                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, u1 + float2(off + ca, 0)).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, u1 + float2(off, 0)).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, u1 + float2(off - ca, 0)).b;
                float3 col = float3(r, g, b);
                col *= col; // sRGB-ish -> linear (texture is linear data)
                // sakura petals drifting across the screen
                float2 pu = u0 * float2(9.0, 13.5) + float2(sin(t * 0.3) * 0.6, t * 0.55);
                float2 cid = floor(pu);
                float h1 = NkPcgHash(cid.x + cid.y * 97 + 1000);
                float h2 = NkPcgHash(cid.x + cid.y * 97 + 2000);
                float2 pc = frac(pu) - float2(h1 * 0.6 + 0.2, h2 * 0.6 + 0.2);
                float petal = smoothstep(0.07, 0.02, length(pc * float2(1.0, 1.6))) * step(0.55, h1);
                col += float3(1.0, 0.55, 0.75) * petal * 0.9;
                // diagonal light sweep + rolling refresh bar + fine LED rows
                float sweep = smoothstep(0.06, 0.0, abs(frac(u0.x * 0.6 + u0.y * 0.4 - t * 0.07) - 0.5)) * 0.35;
                float roll = exp(abs(frac(u0.y - t * 0.18) - 0.5) * (-40.0)) * 0.12;
                float rows = sin(u0.y * 1536.0 * 3.14159) * 0.06 + 0.94;
                float flash = 1 + gwin * 0.4;
                float3 colOut = col * (1 + sweep + roll) * rows * flash * 2.6;

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
