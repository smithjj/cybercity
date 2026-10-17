// Port of the cherry bark material (landmarks/trees.js): dark wet bark with
// macro noise, moss on upward-facing limbs, roughness map, tangent-less normals.
Shader "NEON KAIDO/CherryBark"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
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
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Wetness;
            float _Rain;
            float _SimTime;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; float3 nor : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.uv = IN.uv;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            float NkMxNoise(float3 p)
            {
                float2 q = float2(p.x * 0.71 + p.z * 0.71, p.y * 0.71 + p.x * 0.29);
                float v = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, q * 0.05, 0).r;
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float wet = clamp(_Wetness, 0, 1);

                float3 alb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                float n = NkMxNoise(wp * 1.3) * 0.5 + 0.5;
                float up = clamp(N.y, 0, 1);
                float3 c = alb * float3(0.62, 0.55, 0.52) * lerp(0.6, 1.1, n) * lerp(1.0, 0.55, wet);
                c = lerp(c, float3(0.05, 0.07, 0.03), up * 0.35 * smoothstep(0.4, 0.8, n));

                float r = SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, IN.uv).r;
                float oRough = clamp(lerp(r, r * 0.35 + 0.1, wet * lerp(0.55, 1.0, up)), 0.08, 1);

                float4 n4 = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv * 1.6);
                float2 nxy = (n4.rg * 2 - 1) * 0.8;
                float3 gn = normalize(cross(ddx(wp), ddy(wp)));
                if (dot(gn, N) < 0) gn = -gn;
                float3 T = normalize(cross(gn, abs(gn.y) > 0.9 ? float3(1, 0, 0) : float3(0, 1, 0)));
                float3 B = cross(gn, T);
                N = normalize(gn + T * nxy.x + B * nxy.y);

                float3 Ldir = normalize(_SkyLightDir.xyz);
                float3 H = normalize(Ldir + V);
                float ndl = max(dot(N, Ldir), 0);
                float ndh = max(dot(N, H), 0);
                float ndv = max(dot(N, V), 0.001);
                float a2 = oRough * oRough;
                float dnm = ndh * ndh * (a2 * a2 - 1) + 1;
                float D = a2 * a2 / (3.14159265 * dnm * dnm);
                float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
                float spec = D / (4 * ndl * ndv + 0.001) * ndl * fres;
                float3 env = NkHazeColor(reflect(-V, N), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz,
                    _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float envMix = exp(-oRough * 9.0);
                float3 oCol = c * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * c + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65);

                oCol += NkBPointLights(N, wp, V, c, oRough, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
