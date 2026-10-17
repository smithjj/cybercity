// Port of the river water material (landmarks/river.js). DEVIATION: the JS uses a
// planar Reflector (real geometry mirrored); here reflection uses the atmosphere
// env function (NkHazeColor) sampled along vertically offset directions — streaks
// the sky/glow zones like the JS 9-tap blur, but does not mirror actual buildings.
// Flow noise + rain ripples in uvn (meters), fresnel emissive mix, murky deep tint,
// bank darkening, point-light specular streaks (rough 0.07).
Shader "NEON KAIDO/RiverWater"
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

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float2 uvn : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.uvn = IN.uv; // meters (sim x, z)
                return o;
            }

            float NkVn(float2 p) { return NkVnoise(p); }
            float NkFbm(float2 p, float scale, int oct)
            {
                float s = 0, a = 1;
                for (int i = 0; i < oct; i++) { s += NkVn(p * scale) * a; scale *= 2.02; a *= 0.5; }
                return s;
            }

            // expanding ripple rings (approximation of wet.js rainRipples)
            float Ripple(float2 p, float t, float rate)
            {
                float acc = 0;
                for (int k = 0; k < 2; k++)
                {
                    float2 pc = p * (k == 0 ? 1.0 : 1.9) + k * 7.3;
                    float2 cell = floor(pc); float2 f = frac(pc);
                    float h = NkVn(cell * 0.371 + 3.1);
                    float life = frac(t * rate + h);
                    float2 ctr = float2(0.2 + 0.6 * NkVn(cell * 0.113 + 1.7), 0.2 + 0.6 * NkVn(cell * 0.197 + 9.2));
                    float d = length(f - ctr);
                    float ring = smoothstep(0.09, 0.0, abs(d - life * 0.5)) * pow(1.0 - life, 2.0);
                    acc += ring;
                }
                return acc;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float2 uvn = IN.uvn;
                float t = _SimTime;
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                // flow noise normals (two time scales)
                float dir = 1.0;
                float nx = NkFbm(uvn * 0.3 + float2(t * 0.02, t * 0.024) * dir, 1, 5) * 2.0 - 1.0;
                float nz = NkFbm(uvn * 0.23 + float2(-t * 0.018, t * 0.021) * dir + 5.3, 1, 5) * 2.0 - 1.0;
                float nW_x = nx * 0.55, nW_z = nz * 0.55;
                if (_Rain > 0.01)
                {
                    float rp = Ripple(uvn * 0.45, t, 0.8);
                    float rq = Ripple(uvn * 0.3, t * 1.3, 1.1);
                    nW_x += (rp - 0.5) * 0.6 * _Rain;
                    nW_z += (rq - 0.5) * 0.6 * _Rain;
                }
                float3 nW = normalize(float3(nW_x, 1.0, nW_z));

                // streaked env reflection (planar reflector approximation)
                float fres = 1.0 - abs(dot(nW, V));
                float3 r0 = reflect(-V, nW);
                float3 e0 = NkHazeColor(r0, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float3 e1 = NkHazeColor(normalize(r0 + float3(0, 0.12, 0)), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float3 e2 = NkHazeColor(normalize(r0 - float3(0, 0.1, 0)), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float3 refl = e0 * 0.35 + e1 * 0.4 + e2 * 0.25;

                // deep murky + bank darkening
                float3 deep = float3(0.015, 0.02, 0.018);
                float bank = smoothstep(32.0, 36.0, abs(wp.x - 175.0));
                float3 oCol = lerp(deep, refl * lerp(0.16, 0.85, fres), 1.0);
                oCol *= lerp(1.0, 0.25, bank);

                // point-light specular streaks (rough 0.07)
                oCol += NkBPointLights(nW, wp, V, deep, 0.07, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(oCol, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
