// Floating holograms — port of mkHoloMaterial: additive, flicker, row jitter, scanlines, edge fade,
// fresnel boost, tint mix; ads_grid cell selection via _Cell. Double-sided, no depth write.
Shader "NEON KAIDO/Holo"
{
    Properties { _MainTex ("tex", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float3 _FogColor;
            float3 _MidColor;
            float3 _ZenithColor;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _Tint;
            float2 _Cell; // ads-grid cell (col, row)

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            float NkPcgHash(float seed)
            {
                uint state = (uint)floor(seed) * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _SimTime;
                float2 u0 = IN.uv;
                float flick = step(0.06, NkPcgHash(floor(t * 14.0) + 5)) * 0.25 + 0.75;
                float jitter = step(0.95, NkPcgHash(floor(t * 6.0) + 9)) * (NkPcgHash(floor(u0.y * 60.0) + floor(t * 40.0)) - 0.5) * 0.03;
                float2 suv = float2(_Cell.x + clamp(u0.x + jitter, 0.0, 1.0), (1 - _Cell.y) + u0.y) * 0.5;
                float3 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, suv).rgb;
                float lum = dot(s, float3(0.3, 0.55, 0.15));
                float lines = pow(sin(u0.y * 420.0 - t * 6.0) * 0.5 + 0.5, 2.0) * 0.55 + 0.45;
                float edge = smoothstep(0.0, 0.06, u0.x) * smoothstep(1.0, 0.94, u0.x) * smoothstep(0.0, 0.05, u0.y) * smoothstep(1.0, 0.95, u0.y);
                float3 base = lerp(_Tint * lum, s * s, 0.45);
                float3 V = normalize(IN.worldPos - _WorldSpaceCameraPos);
                float fres = (1 - abs(dot(IN.normalWS, V))) * 0.5 + 0.6;
                float3 colOut = base * lines * flick * edge * fres * 1.6;

                float3 P = IN.worldPos;
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                colOut = NkFogApply(colOut, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(colOut, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
