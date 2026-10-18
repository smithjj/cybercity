// Headlight ground pool — port of lights.js `pools`: an additive ellipse decal on the
// road ahead of each lit moving car (PlaneGeometry 5.5 x 11, centred 7.5 m forward).
Shader "NEON KAIDO/CarLightPool"
{
    Properties
    {
        _PoolColor ("Pool Color", Color) = (1.0, 0.88, 0.70, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+15" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PoolColor;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float3 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 wp = NkWorld(IN.pos);
                o.posCS = NkClip(wp);
                o.wp = wp;
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // JS: pu = (uv - vec2(0.5,0.15)) * vec2(2.0,1.1); pf = smoothstep(1,0,|pu|)^1.8
                float2 pu = (IN.uv - float2(0.5, 0.15)) * float2(2.0, 1.1);
                float pf = pow(saturate(1.0 - length(pu)), 1.8);
                float d = distance(_WorldSpaceCameraPos, IN.wp);
                float fade = 1.0 - smoothstep(40.0, 220.0, d);
                float3 c = _PoolColor.rgb * pf * fade * _Intensity * 0.12;
                return half4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
