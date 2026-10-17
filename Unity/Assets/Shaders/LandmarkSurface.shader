// Port of the landmark surface() material (landmarks/materials.js): wet PBR with
// geometry UVs in meters (box-projected at build), detail re-sample to break tiling,
// macro noise modulation, vertical rain streaks, wet darkening + soak roughness.
// Lighting/fog/post tail identical to Surf.shader.
Shader "NEON KAIDO/LandmarkSurface"
{
    Properties
    {
        _MainTex ("map", 2D) = "white" {}
        _BumpMap ("nmap", 2D) = "bump" {}
        _RoughMap ("rmap", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _UvScale ("UV Scale", Float) = 1
        _NormalScale ("Normal Scale", Float) = 1
        _Macro ("Macro", Range(0, 1)) = 0.5
        _MacroFreq ("Macro Freq", Float) = 0.045
        _Streaks ("Streaks", Range(0, 1)) = 0.7
        _RoughMul ("Rough Mul", Float) = 1
        _Rough ("Rough (no rmap)", Float) = 0.8
        _WetDark ("Wet Dark", Float) = 0.62
        _WetVert ("Wet Vert", Float) = 0.45
        _WetRough ("Wet Rough", Float) = 0.06
        _Metal ("Metal", Range(0, 1)) = 0
        _Detail ("Detail (0/1)", Float) = 1
        _WlLevel ("Waterline Level (off)", Float) = -100000000
        _WlAlgae ("Waterline Algae", Range(0, 1)) = 0.35
        _WlBelow ("Below-Waterline Dark", Range(0, 1)) = 0.45
    }
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _UvScale, _NormalScale, _Macro, _MacroFreq, _Streaks, _RoughMul, _Rough;
                float _WetDark, _WetVert, _WetRough, _Metal, _Detail;
                float _WlLevel, _WlAlgae, _WlBelow;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float2 uv : TEXCOORD1; float3 nor : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.uv = IN.uv * _UvScale;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                return o;
            }

            // 3D-ish value noise approximation for mx_noise_float (macro/streak modulation)
            float NkMxNoise(float3 p)
            {
                float2 q = float2(p.x * 0.71 + p.z * 0.71, p.y * 0.71 + p.x * 0.29);
                return NkVnoise(q) * 2.0 - 1.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float2 uvn = IN.uv;
                float3 macro = NkMxNoise(wp * _MacroFreq) * 0.5 + 0.5;
                float macro2 = NkMxNoise(wp * 0.21 + 13.1) * 0.5 + 0.5;

                float3 alb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvn).rgb;
                if (_Detail > 0.5)
                {
                    float3 alb2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvn * 0.37 + float2(0.31, 0.77)).rgb;
                    alb = lerp(alb, alb2, smoothstep(0.35, 0.65, macro2) * 0.5);
                }
                float3 col = alb.rgb * _Tint.rgb * lerp(1.0, lerp(0.55, 1.25, macro), _Macro);

                // vertical rain streaks / grime
                float vert = 1.0 - abs(N.y);
                if (_Streaks > 0.0)
                {
                    float sk = NkMxNoise(float3((wp.x + wp.z) * 2.3, wp.y * 0.12, 1.7)) * 0.5 + 0.5;
                    col *= lerp(1.0, lerp(0.55, 1.05, sk), vert * _Streaks);
                }
                // wet darkening
                float wet = clamp(_Wetness, 0, 1);
                col *= lerp(1.0, _WetDark, wet);

                // embankment waterline: algae band above, dark stain below (river walls)
                if (_WlLevel > -10000000.0)
                {
                    float hAbove = wp.y - _WlLevel;
                    float algae = smoothstep(0.7, 0.1, hAbove) * _WlAlgae;
                    col *= lerp(float3(1, 1, 1), float3(0.55, 0.72, 0.5), algae);
                    col *= lerp(1.0, _WlBelow, smoothstep(0.02, -0.05, hAbove));
                }

                float baseR = SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uvn).r * _RoughMul;
                float up = clamp(N.y, 0, 1);
                float soak = wet * lerp(_WetVert, 1.0, up) * lerp(0.6, 1.0, macro2);
                float oRough = clamp(lerp(baseR, baseR * 0.3 + _WetRough, soak), 0.03, 1);

                // normal perturbation (tangent-less: build frame around geometric normal)
                float4 n4 = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvn);
                float2 nxy = (n4.rg * 2 - 1) * _NormalScale * 0.5;
                float3 gn = normalize(cross(ddx(wp), ddy(wp)));
                if (dot(gn, N) < 0) gn = -gn;
                float3 T = normalize(cross(gn, abs(gn.y) > 0.9 ? float3(1, 0, 0) : float3(0, 1, 0)));
                float3 B = cross(gn, T);
                N = normalize(gn + T * nxy.x + B * nxy.y);

                float metal = _Metal;
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
                float3 oCol = col * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * col + spec * (metal > 0.5 ? 2 : 1) * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) * lerp(1, col, metal);

                oCol += NkBPointLights(N, wp, V, col, oRough, metal, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

// touch

// touch
