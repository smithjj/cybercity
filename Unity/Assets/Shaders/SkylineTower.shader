// Port of the skyline tower material (landmarks/skyline.js): procedural window
// grid with coherent office floors, blinds/ceiling strips, residential slabs,
// dark-style neon edge strips, crown bands, podium shopfronts, far-distance dim.
// Per-vertex baked attributes: uv1=(edge, round, neonOK, _), uv2=(seed,hgt,style,neonIdx),
// uv3=(neonR,neonG,neonB,crown), uv4=(centreX,centreZ,radius,bright) — all unity space.
Shader "NEON KAIDO/SkylineTower"
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

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0;
                float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float4 uv3 : TEXCOORD3; float4 uv4 : TEXCOORD4; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float3 nor : TEXCOORD2;
                float2 uv : TEXCOORD3; float4 a1 : TEXCOORD4; float4 a2 : TEXCOORD5; float4 a3 : TEXCOORD6; float4 a4 : TEXCOORD7; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posCS = NkClip(IN.pos.xyz);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                o.uv = IN.uv;
                o.a1 = IN.uv1; o.a2 = IN.uv2; o.a3 = IN.uv3; o.a4 = IN.uv4;
                return o;
            }

            float Hash(float x) { return frac(sin(x) * 43758.5453); }
            float NkMxN(float3 p)
            {
                float2 q = float2(p.x * 0.71 + p.z * 0.71, p.y * 0.71 + p.x * 0.29);
                return SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, q * 0.05, 0).r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                float seed = IN.a2.x, hgt = IN.a2.y, style = IN.a2.z;
                float3 bandCol = IN.a3.xyz;
                float isRoof = smoothstep(0.6, 0.8, abs(N.y));
                float3 tdir = normalize(float3(N.z, 0.0, -N.x) + float3(1e-4, 0, 0));
                float2 rel = wp.xz - IN.a4.xy;
                float u = lerp(dot(rel, tdir.xz), atan2(rel.y, rel.x) * IN.a4.z, IN.a1.y);
                float camD = distance(wp, _WorldSpaceCameraPos);
                float farDim = lerp(1.0, 0.22, smoothstep(250, 1400, camD));
                float v = wp.y;
                float h1 = Hash(seed + 1.3), h2 = Hash(seed + 7.1), h3 = Hash(seed + 13.7);
                float isRib = step(style, 0.5);
                float isRes = step(1.5, style) * step(style, 2.5);
                float isDark = step(2.5, style) * step(style, 3.5);
                float isPodium = step(3.5, style);

                float cw = lerp(lerp(1.9, 2.7, h1), lerp(1.3, 1.8, h1), max(isRib, isRes)) * lerp(1.0, 0.75, smoothstep(150, 350, hgt));
                float fh = lerp(lerp(3.6, 4.2, h2), lerp(2.9, 3.2, h2), isRes);
                float2 gc = float2(u / cw, v / fh);
                float2 cell = floor(gc);
                float2 f = frac(gc);
                float2 fw = fwidth(gc);
                float aa = clamp(max(fw.x, fw.y) * 3.0 - 0.3, 0, 1);
                float wx = lerp(lerp(0.16, 0.26, h3), 0.035, isRib);
                float wyb = lerp(0.22, 0.3, isRib), wyt = lerp(0.14, 0.08, isRib);
                float winSharp = smoothstep(wx, wx + 0.03, f.x) * smoothstep(wx, wx + 0.03, 1 - f.x)
                    * smoothstep(wyb, wyb + 0.03, f.y) * smoothstep(wyt, wyt + 0.03, 1 - f.y);
                float cover = lerp(0.36, 0.6, isRib);
                float win = lerp(winSharp, cover, aa);

                float roomN = lerp(1.0, floor(lerp(1.0, 2.99, h3)), isRib);
                float room = floor(cell.x / roomN);
                float hc = Hash(room * 17.31 + cell.y * 113.7 + seed * 3.17);
                float hf = Hash(cell.y * 7.77 + seed * 1.91);
                float occ = lerp(lerp(0.3, 0.6, h2), 0.88, isDark);
                float floorDark = step(lerp(0.8, 0.6, isDark), hf);
                float floorLit = step(hf, lerp(0.12, 0.0, isDark));
                float lit = lerp(0.1, 1.0, clamp(step(occ, hc) + floorLit, 0, 1) * (1 - floorDark));
                float hfl = Hash(cell.y * 3.71 + seed * 7.3);
                float pi = floor(Hash(room * 3.1 + cell.y * 5.3 + seed) * 9.99);
                float3 warm = float3(1.0, 0.72, 0.42), neutral = float3(1.0, 0.88, 0.72);
                float3 cool = float3(0.7, 0.86, 1.0), cyanish = float3(0.45, 0.9, 1.0);
                float3 towerTone = lerp(lerp(warm, cool, step(0.5, h1)), neutral, step(0.75, h3));
                float3 floorTone = lerp(towerTone, lerp(neutral, warm, step(0.5, h1)), step(0.7, hfl));
                float3 wc = lerp(floorTone, lerp(warm, cyanish, step(0.5, hc)), step(9.0, pi));
                wc = lerp(wc, float3(0.5, 0.35, 1.0), step(8.0, pi) * step(pi, 8.99) * isRes);
                float inten = lerp(0.5, 1.2, hfl) * lerp(0.7, 1.0, Hash(hc * 9.1 + cell.x))
                    * lerp(1.0, 0.75, isRib) * lerp(1.1, 0.8, f.y);
                float hb = Hash(cell.x * 41.7 + cell.y * 9.13 + seed);
                float blindLvl = lerp(0.35, 1.1, hb);
                float gy = (f.y - wyb) / (1 - wyb - wyt);
                float blind = lerp(1.0, 0.22, step(blindLvl, gy));
                float ceilL = smoothstep(0.78, 0.9, gy) * 0.7 * (1 - aa);
                float mullR = smoothstep(0.03, 0.0, abs(frac(gc.x * 2) - 0.5)) * isRib * (1 - aa);
                float varW = lerp(blind, 0.6, aa) + ceilL;
                varW *= (1 - mullR * 0.8);
                float3 emis = wc * win * lit * inten * varW;

                float slab = smoothstep(0.05, 0.0, abs(f.y - 0.04)) * lerp(1.0, 0.3, aa) * isRes;
                emis += float3(1.0, 0.8, 0.6) * slab * 0.25;
                float fin = smoothstep(0.04, 0.0, abs(frac(u / 1.2) - 0.5)) * isDark * (1 - aa);
                emis += bandCol * fin * 0.15;

                float shopSeg = frac(u / 7.0);
                float shop = smoothstep(4.6, 4.3, v) * smoothstep(0.6, 0.9, v) * isPodium
                    * smoothstep(0.04, 0.1, shopSeg) * smoothstep(0.96, 0.9, shopSeg)
                    * step(0.3, Hash(floor(u / 7.0) + seed * 5.0));
                float3 shopCol = lerp(float3(1.0, 0.85, 0.6), bandCol, step(0.5, Hash(floor(u / 6) + seed)));
                emis += shopCol * shop * 1.6;

                float e = IN.a1.x;
                float edgeD = min(e, 1 - e);
                float hasNeon = isDark * step(0.45, h3) * (1 - isRoof) * IN.a1.z;
                float strip = smoothstep(0.03, 0.012, edgeD) * hasNeon
                    * smoothstep(hgt * 0.5, hgt * 0.52, v) * smoothstep(hgt * 0.97, hgt * 0.95, v);
                emis += bandCol * strip * 4.5;
                float crown = smoothstep(hgt - 5.0, hgt - 4.0, v) * smoothstep(hgt - 1.0, hgt - 2.0, v)
                    * IN.a3.w * (1 - isRoof);
                emis += bandCol * crown * 5.0;
                emis *= (1 - isRoof) * farDim * IN.a4.w;

                float mull = smoothstep(0.03, 0.0, min(f.x, 1 - f.x)) * (1 - aa);
                float nn = NkMxN(wp * 0.02);
                float3 glass = float3(0.025, 0.03, 0.04) * (nn * 0.3 + 1.0);
                float3 c = lerp(lerp(glass, float3(0.06, 0.06, 0.065), mull), float3(0.03, 0.03, 0.03), isRoof);
                float oRough = lerp(0.18, 0.6, isRoof);
                float oMetal = lerp(0.6, 0.0, isRoof);

                float3 Ldir = normalize(_SkyLightDir.xyz);
                float3 H = normalize(Ldir + V);
                float ndl = max(dot(N, Ldir), 0);
                float ndh = max(dot(N, H), 0);
                float ndv = max(dot(N, V), 0.001);
                float a2 = oRough * oRough;
                float dnm = ndh * ndh * (a2 * a2 - 1) + 1;
                float D = a2 * a2 / (3.14159265 * dnm * dnm);
                float fres = oMetal * 0.9 + 0.1 + (1 - oMetal) * 0.9 * pow(1 - ndv, 5);
                float spec = D / (4 * ndl * ndv + 0.001) * ndl * fres;
                float3 env = NkHazeColor(reflect(-V, N), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz,
                    _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float envMix = exp(-oRough * 9.0) * oMetal;
                float3 oCol = c * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * c + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) + emis;

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

// t2
