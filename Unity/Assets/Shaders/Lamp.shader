// Street lamp / utility-pole body + lens — vertex-color albedo, one shader, two modes.
// Lens: pure emissive (tint x _LensIntensity), matching JS sb.F sp [2,9]/[2,8].
Shader "NEON KAIDO/Lamp"
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
            float _Rough;
            float _Metal;
            float _LensIntensity;

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
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 vcol : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.vcol = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float3 col;

                if (_LensIntensity > 0)
                {
                    // emissive lens: tint x intensity, tiny surface base so it reads as glass
                    col = IN.vcol.rgb * _LensIntensity + 0.02 * IN.vcol.rgb;
                }
                else
                {
                    float3 albedo = IN.vcol.rgb;
                    float rough = _Rough;
                    float metal = _Metal;

                    float ndl = dot(N, normalize(_SkyLightDir));
                    float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                    float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                    col = albedo * (hemi + _SkyLightColor * diff) * _SkyBrightness;

                    col += NkBPointLights(N, P, V, albedo, rough, metal, 1);

                    float3 R = reflect(-V, N);
                    float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                    float fres = pow(1 - saturate(dot(N, V)), 5) * 0.9 + 0.1;
                    col += env * fres * (1 - rough) * (0.06 + metal * 0.4) * _SkyBrightness;
                }

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
