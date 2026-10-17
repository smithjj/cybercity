// Port of the skyline beacons (landmarks/skyline.js): blinking red roof lights.
// uv1.x = per-beacon phase.
Shader "NEON KAIDO/SkylineBeacon"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float _SimTime;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 posCS : SV_POSITION; float2 screenUV : TEXCOORD9; float phase : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.phase = IN.uv1.x;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float ph = IN.phase;
                float blink = smoothstep(0.55, 0.6, frac(_SimTime * 0.75 + ph)) * smoothstep(1.0, 0.9, frac(_SimTime * 0.75 + ph));
                float3 c = float3(1.0, 0.02, 0.01) * lerp(0.6, 30, blink);
                return half4(NkPost(c, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
