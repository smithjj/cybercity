// Drips & streams — port of src/systems/weather/drips.js (CPU-simulated variant).
// Emitters are overhang edges found by searching the baked heightfield (CPU float mirror):
// each drip locks onto an edge point, a bead grows, detaches, falls; streams pour continuously.
// The CPU writes streak-quad corner positions + colors per frame; shader just shades them.
Shader "NEON KAIDO/WxDrips"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;  // (±0.5, ±0.5)
                float4 col : COLOR;         // rgb (pre-multiplied gain), a = alpha
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float4 col : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = IN.positionOS;
                OUT.corner = IN.corner;
                OUT.col = IN.col;
                OUT.positionCS = NkClip(IN.positionOS);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float cy = IN.corner.y + 0.5;
                float across = 1.0 - abs(IN.corner.x * 2.0);
                float along = smoothstep(0.0, 0.2, cy) * smoothstep(1.0, 0.85, cy);
                float a = IN.col.a * across * across * 1.4 * along;
                if (a < 0.002) discard;
                float3 col = IN.col.rgb;
                col = NkFogApply(col, IN.worldPos, _WorldSpaceCameraPos.xyz, 1.0, _Rain * 0.35 + 0.75,
                    _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz,
                    _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
