// Port of the bridge ledArch material (landmarks/bridge.js): slow travelling
// pulse of cyan/white along the arch ribs.
Shader "NEON KAIDO/BridgeLedArch"
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
            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Rain;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float ph = frac(wp.x * 0.02 - _SimTime * 0.15);
                float3 emissive = lerp(float3(0.2, 0.9, 1.0) * 4.0, float3(1.0, 1.0, 1.0) * 9.0,
                                       smoothstep(0.9, 1.0, ph));
                float3 oCol = float3(0.02, 0.02, 0.02) + emissive;
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
