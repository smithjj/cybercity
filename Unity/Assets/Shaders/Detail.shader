// Detail material — port of materials.js createDetailMaterial (TSL -> HLSL).
// aCol(UV1): rgb tint, w = emissive. aPbr(UV2): (rough, metal, texMix, grime).
// texMix > 0 -> sample the 4x2 wall atlas; plus noise grime.
Shader "NEON KAIDO/Detail"
{
    Properties
    {
        _AtlasC("_AtlasC", 2D) = "white" {}
        _AtlasN("_AtlasN", 2D) = "bump" {}
        _Noise("_Noise", 2D) = "black" {}
    }
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

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"
            #include "Assets/Shaders/BuildingsCommon.hlsl"

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

            TEXTURE2D(_AtlasC); SAMPLER(sampler_AtlasC);
            TEXTURE2D(_AtlasN); SAMPLER(sampler_AtlasN);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 aCol : TEXCOORD1;
                float4 aPbr : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 aCol : TEXCOORD4;
                float4 aPbr : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.tangentWS = float4(IN.tangentOS.x, IN.tangentOS.y, -IN.tangentOS.z, IN.tangentOS.w);
                OUT.uv = IN.uv;
                OUT.aCol = IN.aCol;
                OUT.aPbr = IN.aPbr;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float4 Ca = IN.aCol;
                float4 Pb = IN.aPbr;
                float wet = saturate(_Wetness);

                float3 tx = SAMPLE_TEXTURE2D(_AtlasC, sampler_AtlasC, IN.uv).rgb;
                float3 base = lerp(1.0, tx, saturate(Pb.z)) * Ca.rgb;

                float4 N0 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(P.x + P.z * 0.83, P.y * 1.7) * 0.11);
                float4 N1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(P.x * 0.7 + P.z, P.y * 0.25) * 0.6);
                // blotchy grime + vertical rust/drip streaks (only where grime weight > 0)
                float gr = N0.g * 0.6 + N1.a * 0.4;
                float grime = lerp(1.0, lerp(0.45, 1.08, gr), saturate(Pb.w));
                float3 rustTint = lerp(1.0, float3(1.05, 0.85, 0.65), smoothstep(0.55, 0.8, N1.a) * saturate(Pb.w) * saturate(Pb.y + 0.3));
                float up = smoothstep(0.5, 0.9, IN.normalWS.y);
                float3 albedo = base * grime * rustTint * lerp(1.0, 0.75, wet * saturate(Pb.w));
                float rough = clamp(lerp(Pb.x, Pb.x * 0.45, wet * lerp(0.5, 1.0, up)) * lerp(0.8, 1.2, N0.b), 0.03, 1);
                float metal = saturate(Pb.y);
                float3 em = base * saturate(Ca.w);

                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos);

                // sky light (Surf convention)
                float ndl = dot(N, normalize(_SkyLightDir));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 col = albedo * (hemi + _SkyLightColor * diff) * _SkyBrightness;

                // point lights
                col += NkBPointLights(N, P, V, albedo, rough, metal, 1);

                // env spec
                float3 R = reflect(-V, N);
                float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float fres = pow(1 - saturate(dot(N, V)), 5) * 0.9 + 0.1;
                col += env * fres * (1 - rough) * (0.06 + metal * 0.4) * _SkyBrightness;

                // emissive
                col += em;

                // scene fog (atmo.js fogNodeFn)
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                { float3 pc = NkPost(col, IN.screenUV, _SimTime); return float4(pc, 1); }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
