// Chōchin / andon paper — port of signage/lanterns.js paperMat.
// Sway in the vertex shader around the hang pivot (TEXCOORD1), ink kanji from the
// colour atlas cell (TEXCOORD2), glow/ribs/washi from lp attrs (TEXCOORD3 = seed, glow, swayAmp, kind).
// All baked verts are world-space (identity object transform), sway applied in world space.
Shader "NEON KAIDO/LanternPaper"
{
    Properties
    {
        _Paper ("Paper", 2D) = "white" {}
        _ColorAtlas ("Color Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
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
            #include "Assets/Shaders/BuildingsCommon.hlsl"

            TEXTURE2D(_Paper); SAMPLER(sampler_Paper);
            TEXTURE2D(_ColorAtlas); SAMPLER(sampler_ColorAtlas);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 pivot : TEXCOORD1;
                float4 cell : TEXCOORD2;
                float4 lp : TEXCOORD3; // seed, glow, swayAmp, kind
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 cell : TEXCOORD2;
                float4 lp : TEXCOORD3;
            };

            // JS swayPosition(): rotate about X then Z by small sines; mirror flips angle sign
            float3 NkSway(float3 pos, float3 pivot, float4 lp)
            {
                float s = lp.x;
                float amp = lp.z * (0.6 * 0.6 + 0.3); // uWind = 0.6
                float t = _SimTime;
                float ax = (sin(t * 1.13 + s * 2.1) * 0.07 + sin(t * 2.71 + s * 5.3) * 0.025) * amp;
                float az = (sin(t * 0.93 + s * 3.7) * 0.06 + sin(t * 2.3 + s * 1.3) * 0.02) * amp;
                ax = -ax; az = -az; // mirrored sim->unity
                float3 rel = pos - pivot;
                float y1 = rel.y * cos(ax) - rel.z * sin(ax);
                float z1 = rel.y * sin(ax) + rel.z * cos(ax);
                float x2 = rel.x * cos(az) - y1 * sin(az);
                float y2 = rel.x * sin(az) + y1 * cos(az);
                return pivot + float3(x2, y2, z1);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 P = NkSway(IN.positionOS, IN.pivot.xyz, IN.lp);
                OUT.worldPos = P;
                OUT.positionCS = NkClip(P);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.uv = IN.uv;
                OUT.cell = IN.cell;
                OUT.lp = IN.lp;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float kind = IN.lp.w;

                // paper texture (uv scaled 3.0 x, 1.0 / 2.4 y by kind)
                float2 puv = float2(IN.uv.x * 3.0, IN.uv.y * (kind > 0.5 ? 2.4 : 1.0));
                float3 pt = SAMPLE_TEXTURE2D(_Paper, sampler_Paper, puv).rgb;

                // kanji ink from the colour atlas cell (two copies around front/back)
                float uu = (frac(IN.uv.x * 2.0) - 0.25) / 0.5;
                float v = 1.0 - IN.uv.y;
                float vv = (kind > 0.5 && kind < 1.5) ? (v - 0.1) / 0.8 : (v - 0.2) / 0.6;
                float inCell = step(0.0, uu) * step(uu, 1.0) * step(0.0, vv) * step(vv, 1.0);
                float2 cuv = IN.cell.xy + float2(saturate(uu), saturate(vv)) * IN.cell.zw;
                float ink = (1.0 - SAMPLE_TEXTURE2D(_ColorAtlas, sampler_ColorAtlas, cuv).r) * inCell * (kind > 1.5 ? 0.0 : 1.0);

                // inner bulb profile + bamboo rib shadow lines
                float prof = kind > 1.5 ? 1.0 : pow(sin(v * 3.14159265), 1.6);
                float ribN = kind > 0.5 ? 34.0 : 16.0;
                float rib = smoothstep(0.0, 0.22, abs(frac(v * ribN) - 0.5) * 2.0) * 0.35 + 0.65;
                // andon (kind 2): bleached washi instead of red paper
                float3 ptA = kind > 1.5 ? float3(pt.r * 0.35 + 0.62, pt.r * 0.35 + 0.62, pt.r * 0.35 + 0.62) : pt;
                float3 paperLin = ptA * ptA;
                float3 warm = kind > 1.5 ? float3(1.0, 0.82, 0.55) : float3(1.0, 1.0, 1.0);
                float3 hot = float3(1.0, 0.42, 0.12) * pow(prof, 4.0) * 0.35;
                float3 glow = (paperLin * warm * (prof * 2.0 + 0.12) + hot) * IN.lp.y * (1.0 - ink * 0.96) * (kind > 1.5 ? 1.0 : rib);

                float3 albedo = paperLin * 0.25 * (1.0 - ink * 0.9);
                float3 N = float3(0, 1, 0);
                float3 col = albedo * (_AmbientSky * 0.5 + _AmbientGround * 0.5) * _SkyBrightness;
                col += NkBPointLights(N, P, V, albedo, 0.7, 0.0, 1);
                col += glow * (kind > 1.5 ? 1.6 : 1.7);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                { float3 pc = NkPost(col, IN.screenUV, _SimTime); return float4(pc, 1); }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
