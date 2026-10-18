// Port of the cherry blossom material (landmarks/trees.js): alpha-tested cards,
// per-card tint + inner-canopy AO from uv1 (x=tint, y=outerness), edge-on fade,
// lantern glow from below the river side + pink city glow emissive.
Shader "NEON KAIDO/CherryBlossom"
{

    // Declared so Material.SetTexture can bind them: texture slots need a Properties
    // entry (floats/colours do not). Without these the sampler read the default white
    // texture, so clip(a-0.5) never cut and every blossom card drew as a solid quad.
    Properties
    {
        _MainTex ("Blossom Cards", 2D) = "white" {}
        _Noise ("Noise", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Off
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
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float2 uv : TEXCOORD0; float4 uv1 : TEXCOORD1;
                float3 worldPos : TEXCOORD2; float3 nor : TEXCOORD3; float facing : TEXCOORD4; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.uv = IN.uv;
                o.uv1 = IN.uv1;
                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                o.nor = nw;
                float3 vdir = normalize(_WorldSpaceCameraPos - NkWorld(IN.pos.xyz));
                o.facing = abs(dot(nw, vdir));
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(t.a - 0.5);
                float3 tint = lerp(float3(1.0, 0.58, 0.74), float3(1.0, 0.85, 0.9), IN.uv1.x);
                float ao = lerp(0.18, 1.0, smoothstep(0.2, 0.95, IN.uv1.y));
                float3 base = t.rgb * tint * ao;
                // fade cards seen edge-on
                float alpha = t.a * (smoothstep(0.08, 0.38, IN.facing) * 0.6 + 0.4);

                float oRough = 0.55;
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
                float3 oCol = base * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * base + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65);

                // translucency glow: warm lantern light from below river side + pink city
                float lowGlow = smoothstep(9.0, 2.5, wp.y);
                float riverSide = smoothstep(128.0, 141.0, wp.x);
                float3 lantern = float3(1.0, 0.55, 0.42) * (lowGlow * (riverSide * 0.6 + 0.4)) * 0.55;
                float3 city = float3(0.9, 0.35, 0.65) * 0.12;
                oCol += base * (lantern + city) * ao;

                oCol += NkBPointLights(N, wp, V, base, oRough, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
