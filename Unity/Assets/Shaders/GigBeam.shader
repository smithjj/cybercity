// Waypoint beam/ring materials for the gig marker. Port of gigs.js beamMaterial + ringMaterial.
// Additive, depthWrite off, transparent queue 3004 (HDRP drops queues > ~4000).
Shader "NEON KAIDO/GigBeam"
{
    Properties
    {
        _Color ("Beam Color", Color) = (1, 0.886, 0.227, 1)
        _Gain ("Gain", Float) = 1
        _NearK ("Near K", Float) = 0.4
        _Mode ("Mode (0=beam 1=ring)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+4" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Cull Off
            ZWrite Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Gain;
                float _NearK;
                float _Mode;
            CBUFFER_END

            float _GigNear;
            float4x4 unity_MatrixV;
            float _SimTime;

            struct Attr { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Vary { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 nrmWS : TEXCOORD1; float2 uv : TEXCOORD2; float3 positionOS : TEXCOORD3; };

            Vary Vert(Attr IN)
            {
                Vary o;
                float3 posWS = NkWorld(IN.positionOS);
                o.positionCS = NkClip(posWS);
                o.positionWS = posWS;
                o.nrmWS = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                o.uv = IN.uv;
                o.positionOS = IN.positionOS;
                return o;
            }

            float4 Frag(Vary IN) : SV_Target
            {
                float t = _SimTime;
                float a;
                if (_Mode < 0.5)
                {
                    // beam: fresnel edge + scanline + rising bands + vertical fade
                    float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                    // view-space normal via the view matrix rows (raw shader: no UNITY_MATRIX_V)
                    float3x3 vm = (float3x3)unity_MatrixV;
                    float3 nv = normalize(mul(vm, IN.nrmWS));
                    float edge = pow(saturate(1.0 - abs(nv.z)), 1.6);
                    float v = IN.uv.y;
                    float scan = sin(v * 420.0 - t * 9.0) * 0.5 + 0.5;
                    float bands = smoothstep(0.92, 1.0, frac(v * 14.0 - t * 0.35));
                    float fade = smoothstep(0.0, 0.02, v) * (1.0 - smoothstep(0.25, 1.0, v));
                    a = (edge * 0.85 + 0.12) * (scan * 0.35 + 0.65) + bands * 0.6;
                    a *= fade;
                    float g = lerp(_NearK, 1.0, _GigNear) * _Gain;
                    return float4(_Color.rgb * a * g * 3.2, 1);
                }
                else
                {
                    // ring: 24 rotating dashes + expanding pulse wave (local XY plane)
                    float2 p = IN.positionOS.xy;
                    float ang = atan2(p.y, p.x) / (2.0 * 3.14159265) + 0.5;
                    float seg = step(0.35, frac(ang * 24.0 + t * 0.25));
                    float r = length(p);
                    float pulse = frac(t * 0.6);
                    float wave = smoothstep(0.12, 0.0, abs(r / 2.2 - pulse)) * (1.0 - pulse);
                    a = seg * 0.8 + wave * 1.2;
                    return float4(_Color.rgb * a * 3.0, 1);
                }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
