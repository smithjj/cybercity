// Headlight beam cones — port of src/systems/vehicles/lights.js (the instanced `beams` mesh):
// two open cones per car, apex at the lamps, opening forward and tilted slightly down,
// additive, faded along the cone, by edge angle, by distance and by how much the camera
// is looking into the beam.
Shader "NEON KAIDO/CarLightBeam"
{
    Properties
    {
        _BeamColor ("Beam Color", Color) = (1.0, 0.86, 0.66, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BeamColor;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float3 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float3 nor : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 wp = NkWorld(IN.pos);
                o.posCS = NkClip(wp);
                o.wp = wp;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - IN.wp);
                // uv.y: 1 at the apex, 0 at the open end (matches ConeGeometry)
                float along = saturate(IN.uv.y);
                // shell edge falloff: grazing silhouette dims, facing the camera reads
                float edge = pow(saturate(abs(dot(normalize(IN.nor), V))), 1.6);
                float camD = distance(_WorldSpaceCameraPos, IN.wp);
                // JS: smoothstep(260,60,camD) * smoothstep(1.5,6,camD), i.e. fade out by 260 m
                float distFade = (1.0 - smoothstep(60.0, 260.0, camD)) * smoothstep(1.5, 6.0, camD);
                float3 c = _BeamColor.rgb * _Intensity * pow(along, 2.2) * edge * distFade * 0.5;
                // additive light: do NOT run the per-surface display transform, its black lift
                // would paint a visible cone silhouette even where the beam is zero.
                return half4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
