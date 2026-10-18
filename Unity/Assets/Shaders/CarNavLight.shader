// Flying AV nav lights — port of lights.js `nav`: three billboarded dots per AV
// (red left, green right, white strobe on the tail), blinking with a per-AV phase.
// One dynamic mesh holds every dot; vertices are world-space centres and the shader
// billboards them so they keep a minimum apparent size at distance.
Shader "NEON KAIDO/CarNavLight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+25" }
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

            float _SimTime;
            float4x4 unity_MatrixV;

            struct Attributes
            {
                float3 pos : POSITION;   // world-space centre
                float2 corner : TEXCOORD0;
                float2 kindPhase : TEXCOORD1;
                float4 col : COLOR;
            };
            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float2 kindPhase : TEXCOORD1;
                float4 col : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float d = distance(_WorldSpaceCameraPos, IN.pos);
                // JS: sphere r=0.11, scaled by max(1, d/110) to keep a minimum apparent size
                float r = 0.11 * max(1.0, d / 110.0);
                // rows of the world->view matrix are the camera basis in world space
                float3 right = float3(unity_MatrixV._m00, unity_MatrixV._m01, unity_MatrixV._m02);
                float3 up = float3(unity_MatrixV._m10, unity_MatrixV._m11, unity_MatrixV._m12);
                float3 wp = IN.pos + (right * IN.corner.x + up * IN.corner.y) * r;
                o.posCS = NkClip(wp);
                o.corner = IN.corner;
                o.kindPhase = IN.kindPhase;
                o.col = IN.col;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float phase = IN.kindPhase.y;
                float kind = IN.kindPhase.x;
                float blinkRG = step(0.55, frac(_SimTime * 0.9 + phase));
                float strobe = step(0.92, frac(_SimTime * 1.3 + phase * 3.0));
                float k = lerp(0.35 + blinkRG * 0.65, strobe, step(1.5, kind));
                // round the quad and fade its rim
                float m = saturate(1.0 - dot(IN.corner * 2.0, IN.corner * 2.0));
                float3 c = IN.col.rgb * k * m * 3.0;
                return half4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
