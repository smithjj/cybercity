// Chōchin top/bottom caps — dark lacquer, sways with the paper (same pivot/lp attrs).
Shader "NEON KAIDO/LanternCaps"
{
    Properties {}
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
            #include "Assets/Shaders/BuildingsCommon.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 pivot : TEXCOORD1;
                float4 lp : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            float3 NkSway(float3 pos, float3 pivot, float4 lp)
            {
                float s = lp.x;
                float amp = lp.z * (0.6 * 0.6 + 0.3);
                float t = _SimTime;
                float ax = (sin(t * 1.13 + s * 2.1) * 0.07 + sin(t * 2.71 + s * 5.3) * 0.025) * amp;
                float az = (sin(t * 0.93 + s * 3.7) * 0.06 + sin(t * 2.3 + s * 1.3) * 0.02) * amp;
                ax = -ax; az = -az;
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
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos);

                float3 albedo = float3(0.015, 0.012, 0.01);
                float rough = 0.3, metal = 0.1;
                float ndl = dot(N, normalize(_SkyLightDir));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 col = albedo * (hemi + _SkyLightColor * diff) * _SkyBrightness;
                col += NkBPointLights(N, P, V, albedo, rough, metal, 1);

                float3 R = reflect(-V, N);
                float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float fres = pow(1 - saturate(dot(N, V)), 5) * 0.9 + 0.1;
                col += env * fres * (1 - rough) * (0.06 + metal * 0.4) * _SkyBrightness;

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                { float3 pc = NkPost(col, IN.screenUV, _SimTime); return float4(pc, 1); }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
