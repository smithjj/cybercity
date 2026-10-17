// Holographic koi — additive, tail sway, fresnel, dappled two-tone, scan + flicker.
// Port of the mkKoiMat in signage/billboards.js. Mesh local space is UNITY-mirrored
// (head at -Z); sway x-shift mirrors to sin(-p.z*0.9 - t*3.2), tailW flips edges.
Shader "NEON KAIDO/Koi"
{
    Properties
    {
        _ColA ("Colour A", Color) = (1, 0.35, 0.08, 1)
        _ColB ("Colour B", Color) = (0.9, 0.95, 1, 1)
        _Phase ("Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend One One // additive

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

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            float4 _ColA;
            float4 _ColB;
            float _Phase;

            // three.js TSL hash — seed.toUint() truncates toward zero
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 localPos : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 p = IN.positionOS;
                float t = _SimTime + _Phase;
                // tail sway — tailW is 1 at the tail (local +Z after mirror), 0 at the head (-Z)
                float tailW = pow(smoothstep(-3.15, 3.5, p.z), 1.6);
                p.x += sin(-p.z * 0.9 - t * 3.2) * 0.55 * tailW;
                float3 wp = NkWorld(p);
                OUT.worldPos = wp;
                OUT.localPos = p;
                OUT.normalWS = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                OUT.positionCS = NkClip(wp);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 P = IN.worldPos;
                float3 p = IN.localPos;
                float t = _SimTime + _Phase;
                float3 V = normalize(P - _WorldSpaceCameraPos);
                float3 N = normalize(IN.normalWS);

                float fres = pow(1.0 - abs(dot(N, V)), 1.5);
                float patch = smoothstep(0.45, 0.6, NkTslHash(floor(p.z * 1.3) + floor((p.x + 2.0) * 1.6) * 13.0 + 7.0));
                float3 col = lerp(_ColA.rgb, _ColB.rgb, patch);
                float scan = pow(sin(p.z * 22.0 - t * 9.0) * 0.5 + 0.5, 3.0) * 0.8 + 0.35;
                float flick = step(0.05, NkTslHash(floor(t * 11.0) + 23.0)) * 0.3 + 0.7;

                float3 outCol = col * (fres * 2.6 + 0.12) * scan * flick * 1.3;
                float fogPatch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                outCol = NkFogApply(outCol, P, _WorldSpaceCameraPos.xyz, fogPatch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(outCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

// touch

// touch2
