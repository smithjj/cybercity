// Port of the skyline holo material (landmarks/skyline.js holoMaterial):
// atlas-rect sampling, scanlines, flicker, glitch band, additive blend.
// uv0 = atlas uv (rect-mapped at bake), uv1 = (intensity, seed, _, _).
Shader "NEON KAIDO/SkylineHolo"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float _SimTime;
            float _Rain;
            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _GroundHaze;
            float _SkyBrightness;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 posCS : SV_POSITION; float2 screenUV : TEXCOORD9;
                float2 uv : TEXCOORD0; float4 a1 : TEXCOORD1; float3 worldPos : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.uv = IN.uv;
                o.a1 = IN.uv1;
                o.worldPos = NkWorld(IN.pos.xyz);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float intensity = IN.a1.x, seed = IN.a1.y;
                float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                float scan = sin(IN.uv.y * 380 + _SimTime * 3) * 0.12 + 0.88;
                float flick = lerp(1.0, step(0.04, frac(sin(floor(_SimTime * 14) + seed) * 43758.5)), 0.6);
                float glitchBand = step(0.985, frac(IN.uv.y * 3 + _SimTime * 0.35 + seed)) * 0.6;
                float lum = max(c.r, max(c.g, c.b));
                float3 oCol = c * intensity * scan * flick + c * glitchBand * intensity;
                oCol = NkFogApply(oCol, IN.worldPos, _WorldSpaceCameraPos.xyz, 1.0, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return half4(NkPost(oCol, IN.screenUV, _SimTime), clamp(lum * 1.5, 0, 1));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
