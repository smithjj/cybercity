// NEON KAIDO ground surface materials — port of src/systems/ground/surfaces.js.
// One shader, five modes: 0 sidewalk pavers, 1 granite curb (uv = along/profile),
// 2 promenade pavers, 3 alley paving, 4 embankment wall. World-space mapped, wet via
// the same wet-layer port as Road.shader. Deviations as in Road.shader (analytic lighting,
// inline fog, slopeFromHeight via screen-space derivatives).
Shader "NEON KAIDO/Surf"
{
    Properties
    {
        _Mode ("Mode (0 sw, 1 curb, 2 prom, 3 alley, 4 wall)", Float) = 0
        _SidewalkC ("sidewalk c", 2D) = "white" {}
        _SidewalkN ("sidewalk n", 2D) = "white" {}
        _TactLineC ("tactile paving c", 2D) = "white" {}
        _TactLineN ("tactile paving n", 2D) = "white" {}
        _TactDotsC ("tactile dots c", 2D) = "white" {}
        _TactDotsN ("tactile dots n", 2D) = "white" {}
        _GraniteC ("granite c", 2D) = "white" {}
        _GraniteN ("granite n", 2D) = "white" {}
        _StoneC ("stone c", 2D) = "white" {}
        _StoneN ("stone n", 2D) = "white" {}
        _AlleyC ("alley c", 2D) = "white" {}
        _AlleyN ("alley n", 2D) = "white" {}
        _ConcreteC ("concrete c", 2D) = "white" {}
        _ConcreteN ("concrete n", 2D) = "white" {}
        _Wetness ("Wetness", Float) = 1.0
        _Rain ("Rain", Float) = 1.0
        _SimTime ("Sim Time", Float) = 0.0
        _SkyLightDir ("Sky Light Dir (unity)", Vector) = (0.5, 0.76, 0.43, 0)
        _SkyLightColor ("Sky Light Color", Vector) = (0.29, 0.35, 0.6, 1)
        _AmbientSky ("Ambient Sky", Vector) = (0.02, 0.025, 0.06, 1)
        _AmbientGround ("Ambient Ground", Vector) = (0.03, 0.02, 0.05, 1)
        _FogColor ("Fog Color", Vector) = (0.085, 0.058, 0.16, 1)
        _SkyBrightness ("Sky Brightness", Float) = 1.0
        _MidColor ("Mid Color", Vector) = (0.045, 0.034, 0.10, 1)
        _ZenithColor ("Zenith Color", Vector) = (0.010, 0.011, 0.030, 1)
        _GlowMagenta ("Glow Magenta", Vector) = (0.12, 0.025, 0.10, 1)
        _GlowOrange ("Glow Orange", Vector) = (0.13, 0.05, 0.018, 1)
        _GlowBlue ("Glow Blue", Vector) = (0.015, 0.04, 0.12, 1)
        _GroundHaze ("Ground Haze", Vector) = (0.07, 0.045, 0.12, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_SidewalkC);  SAMPLER(sampler_SidewalkC);
            TEXTURE2D(_SidewalkN);  SAMPLER(sampler_SidewalkN);
            TEXTURE2D(_TactLineC);  SAMPLER(sampler_TactLineC);
            TEXTURE2D(_TactLineN);  SAMPLER(sampler_TactLineN);
            TEXTURE2D(_TactDotsC);  SAMPLER(sampler_TactDotsC);
            TEXTURE2D(_TactDotsN);  SAMPLER(sampler_TactDotsN);
            TEXTURE2D(_GraniteC);   SAMPLER(sampler_GraniteC);
            TEXTURE2D(_GraniteN);   SAMPLER(sampler_GraniteN);
            TEXTURE2D(_StoneC);     SAMPLER(sampler_StoneC);
            TEXTURE2D(_StoneN);     SAMPLER(sampler_StoneN);
            TEXTURE2D(_AlleyC);     SAMPLER(sampler_AlleyC);
            TEXTURE2D(_AlleyN);     SAMPLER(sampler_AlleyN);
            TEXTURE2D(_ConcreteC);  SAMPLER(sampler_ConcreteC);
            TEXTURE2D(_ConcreteN);  SAMPLER(sampler_ConcreteN);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _SidewalkC_ST;
                float _Mode, _Wetness, _Rain, _SimTime;
                float4 _SkyLightDir, _SkyLightColor, _AmbientSky, _AmbientGround;
                float4 _FogColor, _MidColor, _ZenithColor, _GlowMagenta, _GlowOrange, _GlowBlue, _GroundHaze;
                float _SkyBrightness;
            CBUFFER_END

            // G constants (plan.js, sim space)
            #define AV_HALF 6.0
            #define AV_SIDE 4.6
            #define CR_HALF 5.0
            #define CR_SIDE 3.6
            #define CURB_H 0.15

            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.pos = NkClip(IN.pos.xyz);
                o.screenUV = o.pos.xy / o.pos.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                o.uv = IN.uv;
                return o;
            }

            // ---------------- noise helpers (as Road.shader) ----------------
            float NkHash21(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
                float d = dot(q, float3(q.y, q.z, q.x) + 33.33);
                float3 r = q + d;
                return frac((r.x + r.y) * r.z);
            }
            float3 NkHash23(float3 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.103, 0.0973));
                float d = dot(q, float3(q.y, q.x, q.z) + 33.33);
                float3 r = q + d;
                return frac(float3((r.x + r.y) * r.z, (r.x + r.z) * r.y, (r.y + r.x) * r.x));
            }
            float3 NkHash23(float2 p) { return NkHash23(float3(p, 0)); } // TSL auto-promotes vec2->vec3
// NkVnoise now provided by AtmoCommon (same math, NkBh21 hash)
            float NkFbm(float2 p, int oct)
            {
                float s = 0, a = 0.5, norm = 0;
                float2 q = p;
                for (int i = 0; i < oct; i++)
                {
                    s += NkVnoise(q) * a; norm += a;
                    q = float2(q.x * 0.8 - q.y * 0.6, q.x * 0.6 + q.y * 0.8) * 2.03 + 17.3 * (i + 1);
                    a *= 0.5;
                }
                return s / norm;
            }
            float NkAaBand(float x, float halfWidth)
            {
                float w = max(abs(ddx(x)) + abs(ddy(x)), 1e-5);
                return smoothstep(-w, w, halfWidth - abs(x));
            }

            // pairSampleGrad: color + unpacked normal + height, with derivative UVs
            void NkPair(float2 uv, float2 dx, float2 dy, TEXTURE2D(tc), SAMPLER(sc), TEXTURE2D(tn), SAMPLER(sn),
                        out float3 col, out float2 nxy, out float h)
            {
                col = SAMPLE_TEXTURE2D_GRAD(tc, sc, uv, dx, dy).rgb;
                float4 n = SAMPLE_TEXTURE2D_GRAD(tn, sn, uv, dx, dy);
                nxy = n.rg * 2 - 1;
                h = n.b;
            }

            // rain ripples (same as Road.shader)
            float2 NkRainRipples(float2 xz, float intensity)
            {
                float2 g = 0;
                float t = _SimTime;
                float LC[4] = { 0.42, 0.37, 0.51, 0.31 };
                float LR[4] = { 1.15, 1.35, 0.95, 1.55 };
                float LOx[4] = { 0.0, 0.37, 0.83, 0.13 };
                float LOy[4] = { 0.0, 0.71, 0.19, 0.52 };
                for (int i = 0; i < 4; i++)
                {
                    float Lcell = LC[i], Lrate = LR[i];
                    float2 Loff = float2(LOx[i], LOy[i]);
                    float cs = 1.0 / Lcell;
                    float2 q = xz * cs + Loff * 17.0;
                    float2 id = floor(q);
                    float2 f = frac(q) - 0.5;
                    float3 h = NkHash23(id + i * 31.7);
                    float2 c = (h.xy - 0.5) * 0.36;
                    float ph = frac(t * Lrate + h.z);
                    float active = smoothstep(h.x * 0.6 + 0.2, h.x * 0.6 + 0.3, _Rain);
                    float2 dv = f - c;
                    float d = length(dv);
                    float r = ph * 0.32;
                    float x = d - r;
                    float env = exp(x * x * -900.0) * pow(1.0 - ph, 2.2) * smoothstep(0.0, 0.04, ph);
                    float dh = cos(x * 80.0) * env * active;
                    float2 dir = dv / max(d, 1e-3);
                    g += dir * dh;
                }
                return -g * (0.22 * intensity);
            }

            // wet layer (shared approximation, as Road.shader)
            void NkWetLayer(float3 albedo, float rough, float2 nxy, float porosity,
                            float wetness, float puddle, float2 ripple, float filmRipple,
                            out float3 oAlbedo, out float oRough, out float2 oNxy, out float oPuddle)
            {
                float w = clamp(wetness, 0, 1);
                float por = porosity;
                float dark = lerp(1, lerp(0.88, 0.42, por), w);
                float3 a1 = albedo * dark;
                float filmR = clamp(rough * 0.2 + 0.035, 0.02, 0.5);
                float r1 = lerp(rough, filmR, w * lerp(0.85, 1.0, por));
                float2 n1 = nxy * lerp(1.0, 0.42, w);
                n1 += ripple * (filmRipple * w);
                float p = clamp(puddle, 0, 1) * w;
                float3 a2 = a1 * lerp(1, 0.5, p);
                float r2 = lerp(r1, 0.012, smoothstep(0.0, 0.6, p));
                float2 n2 = lerp(n1, 0, smoothstep(0.0, 0.5, p));
                n2 += ripple * p;
                oAlbedo = a2; oRough = r2; oNxy = n2; oPuddle = p;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                float3 wp = IN.worldPos;
                float2 P = float2(wp.x, -wp.z); // sim (x, z)
                float x = P.x, z = P.y;
                float ax = abs(x);
                float3 albedo; float rough; float2 nxy; float porosity; float puddle; float metal = 0;

                if (_Mode < 0.5)
                {
                    // ---------------- sidewalk: shuffled photoreal pavers + tactile ----------------
                    float tile = 0.3;
                    float2 w = P / tile;
                    float jw = floor(w.y);
                    float off = fmod(jw, 2) * 0.5;
                    float iw = floor(w.x - off);
                    float2 local = float2(frac(w.x - off), frac(w.y));
                    float3 th = NkHash23(float2(iw, jw) + 0.37);
                    float ri = floor(th.x * 8), rj = floor(th.y * 8);
                    float offT = fmod(rj, 2) > 0.5 ? 0 : 0.5;
                    float2 tuv = float2((ri + offT + local.x) / 8, (rj + local.y) / 8);
                    float2 gdx = ddx(w) / 8, gdy = ddy(w) / 8;
                    float3 sCol; float2 sNxy; float sH;
                    NkPair(tuv, gdx, gdy, _SidewalkC, sampler_SidewalkC, _SidewalkN, sampler_SidewalkN, sCol, sNxy, sH);
                    float3 th2 = NkHash23(float2(iw, jw) + 11.3);
                    albedo = sCol * lerp(0.62, 0.78, th2.x) * lerp(0.88, 1.06, NkFbm(P * 0.18, 2));
                    float onAv = ax < AV_HALF + AV_SIDE + 0.3 ? 1 : 0;
                    float front = smoothstep(AV_HALF + 2.5, AV_HALF + AV_SIDE, ax) * onAv;
                    albedo *= lerp(1.0, 0.72, front) * lerp(1.0, 0.75, smoothstep(0.55, 0.8, NkFbm(P * 0.6 + 4.0, 3)));
                    float jointT = smoothstep(0.32, 0.12, sH);
                    albedo *= lerp(1.0, 0.4, jointT);
                    nxy = sNxy * 0.9 + (th2.yz - 0.5) * 0.035;
                    rough = lerp(0.78, 0.9, jointT);
                    porosity = lerp(0.65, 0.9, jointT);
                    // tactile strips + dots (nearest cross street z, as road shader)
                    float zc = P.y > (-60.0 + -180.0) / 2 ? -60.0 : (P.y > (-180.0 + -300.0) / 2 ? -180.0 : -300.0);
                    float adz = abs(P.y - zc);
                    float lineAv = (ax > 7.8 && ax < 8.1 ? 1 : 0) * onAv * (adz > CR_HALF + 0.3 ? 1 : 0);
                    float lineCr = (adz > 6.6 && adz < 6.9 && x > AV_HALF + AV_SIDE ? 1 : 0);
                    float dotsAv = (ax > 6.2 && ax < 6.8 && adz > 6.2 && adz < 9.8 ? 1 : 0);
                    float dotsCr = (ax > 7.2 && ax < 10.8 && adz > 5.2 && adz < 5.8 ? 1 : 0);
                    float dots = max(dotsAv, dotsCr);
                    float tact = max(max(lineAv, lineCr), dots);
                    float2 tlAv = float2((ax - 7.8) / 0.3, frac(z / 0.3));
                    float2 tlCr = float2((adz - 6.6) / 0.3, frac(x / 0.3));
                    float2 tlD = float2(frac(x / 0.3), frac(z / 0.3));
                    float2 tl = dots > 0.5 ? tlD : (lineAv > 0.5 ? tlAv : tlCr);
                    float2 tid = lineAv > 0.5 ? float2(7, z / 0.3) : (lineCr > 0.5 ? float2(x / 0.3, 3) : P / 0.3);
                    tid = floor(tid);
                    float2 tq = floor(NkHash23(tid).xy * 2);
                    float2 tuvT = (tq + (0.03 + clamp(tl, 0, 1) * 0.94)) * 0.5;
                    float2 tdx = ddx(P) / 0.6, tdy = ddy(P) / 0.6;
                    float3 tlCol; float2 tlNxy; float tlH;
                    NkPair(tuvT, tdx, tdy, _TactLineC, sampler_TactLineC, _TactLineN, sampler_TactLineN, tlCol, tlNxy, tlH);
                    float3 tdCol; float2 tdNxy; float tdH;
                    NkPair(tuvT, tdx, tdy, _TactDotsC, sampler_TactDotsC, _TactDotsN, sampler_TactDotsN, tdCol, tdNxy, tdH);
                    float3 tCol = lerp(tlCol, tdCol, dots) * lerp(0.55, 0.75, NkHash21(tid));
                    float2 tN = lerp(tlNxy, tdNxy, dots);
                    float tJoint = max(1 - NkAaBand(tl.x - 0.5, 0.47), 1 - NkAaBand(tl.y - 0.5, 0.47));
                    albedo = lerp(albedo, lerp(tCol, float3(0.03, 0.025, 0.02), tJoint), tact);
                    nxy = lerp(nxy, tN * 2.2, tact);
                    rough = lerp(rough, 0.62, tact);
                    porosity = lerp(porosity, 0.3, tact);
                    float pn = NkFbm(P * 0.33 + 1.7, 3);
                    float sunk = th2.z * 0.25;
                    float level = pn + sunk + jointT * 0.08 - tact * 0.1;
                    puddle = min(smoothstep(0.74, 0.79, level) + jointT * 0.35, 1);
                }
                else if (_Mode < 1.5)
                {
                    // ---------------- curb (uv.x = along, uv.y = profile) ----------------
                    float along = IN.uv.x, prof = IN.uv.y;
                    float bi = floor(along);
                    float3 bh = NkHash23(float2(bi, 3.3));
                    float2 t = float2(along + bh.x * 37, prof + bh.y * 11) / 0.8;
                    float3 c = SAMPLE_TEXTURE2D(_GraniteC, sampler_GraniteC, t).rgb;
                    float4 n4 = SAMPLE_TEXTURE2D(_GraniteN, sampler_GraniteN, t);
                    float joint = NkAaBand(abs(frac(along) - 0.5) - 0.5, 0.004);
                    float chip = smoothstep(0.7, 0.85, NkVnoise(float2(along * 9, prof * 30)));
                    albedo = c * lerp(0.5, 0.7, bh.z) * lerp(1.0, 0.25, joint) * lerp(1.0, 0.7, chip);
                    albedo *= lerp(0.4, 1.0, smoothstep(0.0, 0.12, prof));
                    nxy = (n4.rg * 2 - 1) * 0.5;
                    rough = 0.55 + n4.b * 0.2;
                    porosity = 0.35;
                    puddle = 0;
                    // perturbation is applied to the geometric normal at lighting time (curb faces are vertical)
                }
                else if (_Mode < 2.5)
                {
                    // ---------------- promenade: running-bond granite pavers ----------------
                    float2 w = float2(z / 0.8, x / 0.4);
                    float jw = floor(w.y);
                    float off = fmod(jw, 2) * 0.5;
                    float iw = floor(w.x - off);
                    float2 local = float2(frac(w.x - off), frac(w.y));
                    float2 id = float2(iw, jw);
                    float3 h = NkHash23(id + 0.5);
                    float3 h2 = NkHash23(id + 17.1);
                    float2 suv = P / 1.6 + h.xy * 13.0;
                    float2 dx = ddx(P) / 1.6, dy = ddy(P) / 1.6;
                    float3 sCol; float2 sNxy; float sH;
                    NkPair(suv, dx, dy, _StoneC, sampler_StoneC, _StoneN, sampler_StoneN, sCol, sNxy, sH);
                    float3 gCol; float2 gNxy; float gH;
                    NkPair(P / 0.7 + h2.xy * 5.0, ddx(P) / 0.7, ddy(P) / 0.7, _GraniteC, sampler_GraniteC, _GraniteN, sampler_GraniteN, gCol, gNxy, gH);
                    float3 palette = h.z < 0.3 ? float3(0.75, 0.75, 0.76) : h.z < 0.55 ? float3(0.5, 0.5, 0.52) : h.z < 0.8 ? float3(0.82, 0.7, 0.68) : float3(0.62, 0.6, 0.58);
                    albedo = lerp(sCol, gCol, 0.55) * palette * lerp(0.55, 0.75, h2.z);
                    float ex = min(local.x, 1 - local.x) * 0.8;
                    float ey = min(local.y, 1 - local.y) * 0.4;
                    float e = min(ex, ey);
                    float joint = smoothstep(0.009, 0.004, e);
                    float bevel = smoothstep(0.0, 0.02, e);
                    albedo = lerp(albedo, float3(0.02, 0.02, 0.018), joint);
                    albedo *= lerp(1.0, 0.7, smoothstep(0.6, 0.85, NkFbm(P * 0.4 + 2.0, 3)));
                    float coping = smoothstep(128 - 0.6, 128 - 0.58, x);
                    albedo = lerp(albedo, gCol * 0.42, coping);
                    float height = bevel * 0.004 + h.x * 0.003 + sH * 0.0015;
                    float2 sl = -float2(ddx(height), ddy(height)) * 400; // slopeFromHeight approximation
                    nxy = -sl + sNxy * 0.35 + gNxy * 0.25 + (h2.xy - 0.5) * 0.03;
                    rough = lerp(0.72, 0.9, joint);
                    float sunk = h.x * 0.06;
                    float level = NkFbm(P * 0.3 + 6.0, 3) + sunk + joint * 0.25 - coping * 0.3;
                    puddle = min(smoothstep(0.7, 0.72, level) + joint * 0.5, 1);
                    porosity = 0.6;
                }
                else if (_Mode < 3.5)
                {
                    // ---------------- alley: worn slabs + concrete, grime, drain channel ----------------
                    float dz = z - (-100.0);
                    float adz = abs(dz);
                    float3 a = SAMPLE_TEXTURE2D(_AlleyC, sampler_AlleyC, P / 2.6).rgb;
                    float4 an = SAMPLE_TEXTURE2D(_AlleyN, sampler_AlleyN, P / 2.6);
                    // concrete via single tap (hex taps approximation)
                    float2 cuv = P / 1.8;
                    float3 hcx = NkHash23(floor(cuv * 0.4 * 3.464) + 0.5);
                    float2 ruv = cuv + hcx.xy * 5.0;
                    float3 cCol = SAMPLE_TEXTURE2D(_ConcreteC, sampler_ConcreteC, ruv).rgb;
                    float4 cN = SAMPLE_TEXTURE2D(_ConcreteN, sampler_ConcreteN, ruv);
                    float mixc = smoothstep(0.45, 0.6, NkFbm(P * 0.12 + 3.0, 3));
                    albedo = lerp(a, cCol * 0.55, mixc * 0.6) * 0.62;
                    float2 anxy = an.rg * 2 - 1;
                    float2 cnxy = cN.rg * 2 - 1;
                    nxy = lerp(anxy, cnxy, mixc * 0.6);
                    float hh = lerp(an.b, cN.b, mixc * 0.6);
                    float wall = smoothstep(2.25 - 0.7, 2.25, adz);
                    float drip = NkAaBand(adz - (2.25 - 0.35), 0.12) * smoothstep(0.3, 0.6, NkVnoise(float2(x * 1.3, 0.5)));
                    albedo *= lerp(1.0, 0.45, wall) * lerp(0.75, 1.1, NkFbm(P * 0.5, 3));
                    float ch = NkAaBand(dz, 0.15);
                    float lid = floor(x / 0.5);
                    float lidJ = NkAaBand(abs(frac(x / 0.5) - 0.5) - 0.5, 0.01);
                    float slots = NkAaBand(frac(dz / 0.05 + 0.5) - 0.5, 0.18) * ((abs(frac(x / 0.5) - 0.5) < 0.38) ? 1 : 0);
                    float3 chCol = lerp(float3(0.05, 0.048, 0.045) * lerp(0.8, 1.3, NkHash21(float2(lid, 2.0))), float3(0.003, 0.003, 0.003), max(lidJ, slots * ((adz < 0.1) ? 1 : 0)));
                    albedo = lerp(albedo, chCol, ch);
                    rough = lerp(0.8, 0.5, (1 - hh) * 0.3);
                    rough = lerp(rough, 0.4, ch);
                    metal = ch * 0.6;
                    float level = NkFbm(P * 0.4 + 9.0, 3) + (1 - hh) * 0.1 + drip * 0.25 + smoothstep(0.6, 0.0, adz) * 0.06;
                    puddle = smoothstep(0.66, 0.71, level);
                    porosity = 0.75;
                    nxy *= 0.9;
                }
                else
                {
                    // ---------------- embankment wall (u = z, v = y) ----------------
                    float u = P.y;      // sim z
                    float v = wp.y;     // sim y == unity y
                    float2 wv = float2(u / 1.2, v / 0.5);
                    float jw = floor(wv.y);
                    float iw = floor(wv.x - fmod(jw, 2) * 0.5);
                    float h = NkHash21(float2(iw, jw));
                    float3 c = SAMPLE_TEXTURE2D(_GraniteC, sampler_GraniteC, float2(u, v) / 1.1 + h).rgb;
                    float lx = frac(wv.x - fmod(jw, 2) * 0.5), ly = frac(wv.y);
                    float joint = max(smoothstep(0.02, 0.0, min(lx, 1 - lx)), smoothstep(0.04, 0.0, min(ly, 1 - ly)));
                    float algae = smoothstep(-1.2, -2.8, v) * 0.7;
                    albedo = c * lerp(0.28, 0.42, h) * lerp(1.0, 0.2, joint) * lerp(float3(1, 1, 1), float3(0.45, 0.55, 0.4), algae);
                    rough = lerp(0.18, 0.5, joint);
                    porosity = 0.4;
                    nxy = float2(0, 0);
                    puddle = 0;
                    metal = 0;
                }

                // ---------------- shared: wet + lighting + fog ----------------
                float2 rip = NkRainRipples(P, 1.0);
                float3 oAlb; float oRough; float2 oNxy; float oPud;
                NkWetLayer(albedo, rough, nxy, porosity, _Wetness, puddle, rip, 0.12, oAlb, oRough, oNxy, oPud);
                oRough = clamp(oRough, 0.012, 1);

                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float3 N;
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    // curb: perturb the geometric normal (not always up)
                    float3 gn = normalize(cross(ddx(wp), ddy(wp))); // screen-space geometric normal
                    if (gn.y < 0) gn = -gn;
                    N = normalize(gn + float3(oNxy.x, 0, oNxy.y));
                }
                else
                {
                    float ny = sqrt(max(1 - dot(oNxy, oNxy), 0.04));
                    N = normalize(float3(oNxy.x, ny, oNxy.y));
                }
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
                float3 col = oAlb * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * oAlb + spec * (metal > 0.5 ? 2 : 1) * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) * lerp(1, oAlb, metal);
                col = lerp(col, env * envMix, oPud * 0.85);

                // point lights (JS MeshStandard: 92 lamp lights hit the ground too)
                col += NkBPointLights(N, wp, V, oAlb, oRough, metal, 1);

                // scene fog (atmo.js fogNodeFn)
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col = NkFogApply(col, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                return half4(NkPost(col, IN.screenUV, _SimTime), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

// fog-port touch

// touch 1792039432
