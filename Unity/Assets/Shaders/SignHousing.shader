// Sign housings / brackets / poles — port of makeHousingMaterial: vertex colour + per-vertex
// metal/rough (UV2) with procedural grime streaks. Lit by hemi + sky + shared point lights.
Shader "NEON KAIDO/SignHousing"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Cull Back
            ZWrite On

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
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            float NkTslHash(float seed)
            {
                uint state = (uint)floor(abs(seed) + 0.5) * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 mr : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 vcol : TEXCOORD2;
                float2 mr : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.vcol = IN.color;
                OUT.mr = IN.mr;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float3 N = IN.normalWS;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float n1 = NkTslHash(floor(P.x * 3.1) + floor(P.z * 3.1) * 17.0 + floor(P.y * 0.6) * 5.0);
                float streak = NkTslHash(floor(P.x * 9.0) + floor(P.z * 9.0));
                float grime = lerp(0.75, 1.05, n1) * lerp(0.85, 1.0, streak);
                float3 albedo = IN.vcol.rgb * grime;
                float metal = saturate(IN.mr.x);
                float rough = clamp(IN.mr.y * lerp(0.7, 1.2, streak), 0.05, 1.0);

                float ndl = saturate(dot(N, normalize(_SkyLightDir)));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 col = albedo * (hemi + _SkyLightColor * diff) * _SkyBrightness;
                col += NkBPointLights(N, P, V, albedo, rough, metal, 1);
                float3 R = reflect(-V, N);
                float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float fres = pow(1 - saturate(dot(N, V)), 5) * 0.9 + 0.1;
                col += env * fres * (1 - rough) * (0.06 + metal * 0.4) * _SkyBrightness;

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
