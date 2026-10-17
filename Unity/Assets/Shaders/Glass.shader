// Glass — port of materials.js createGlassMaterial: dark blue smear, faint sky reflection,
// wet specks; transparent, no depth write, renders after opaque (renderOrder 2 in JS).
Shader "NEON KAIDO/Glass"
{
    Properties
    {
        _Noise("_Noise", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                OUT.uv = IN.uv;
                OUT.aCol = IN.aCol;
                OUT.aPbr = IN.aPbr;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;

                // smear = vnoise((P.x + P.z)*3, P.y*0.6) — exact materials.js glass
                float smear = NkVnoise(float2((P.x + P.z) * 3.0, P.y * 0.6));

                float3 baseCol = float3(0.03, 0.04, 0.045);
                float opacity = lerp(0.12, 0.3, smear);
                float rough = lerp(0.03, 0.12, smear);

                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos.xyz);

                // sky light (Surf convention)
                float ndl = dot(N, normalize(_SkyLightDir));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 col = baseCol * (hemi + _SkyLightColor * diff) * _SkyBrightness;

                // point lights (glass is standard material in JS: lit dimly)
                col += NkBPointLights(N, P, V, baseCol, rough, 0.0, 1);

                // env reflection (smooth glass -> strong smear of the haze)
                float3 R = reflect(-V, N);
                float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float fres = pow(1 - saturate(dot(N, V)), 5) * 0.9 + 0.1;
                col += env * fres * (1 - rough) * 0.5 * _SkyBrightness;

                // scene fog (atmo.js fogNodeFn)
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                { float3 pc = NkPost(col, IN.screenUV, _SimTime); return float4(pc, opacity); }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
