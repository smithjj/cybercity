// NEON KAIDO road material — verbatim port of src/systems/ground/roadMaterial.js with the
// wet layer (materials/wet.js) and noise helpers (materials/noise.js).
// Formulas run in SIM space: x = worldPos.x, z = -worldPos.z (sim z; avenue runs along -Z).
// Deviations (documented in PORTING.md): lighting is an analytic stand-in for three's PBR
// (sky ambient + hazeColor(env reflection) GGX + dim sky light); fog is inlined height-distance
// fog with the JS constants; worley cracks approximate mx_worley_noise_vec2.
Shader "NEON KAIDO/Road"
{
    Properties
    {
        _AsphaltC ("asphalt_a c", 2D) = "white" {}
        _AsphaltN ("asphalt_a n", 2D) = "white" {}
        _AsphaltBC ("asphalt_b c", 2D) = "white" {}
        _AsphaltBN ("asphalt_b n", 2D) = "white" {}
        _PaintWearC ("paint_wear c", 2D) = "white" {}
        _ConcreteC ("concrete c", 2D) = "white" {}
        _ConcreteN ("concrete n", 2D) = "white" {}
        _GrateC ("grate c", 2D) = "white" {}
        _GrateN ("grate n", 2D) = "white" {}
        _ManholeC ("manhole c", 2D) = "white" {}
        _ManholeN ("manhole n", 2D) = "white" {}
        _MarkAtlas ("marking glyph atlas", 2D) = "black" {}
        _Wetness ("Wetness", Float) = 1.0
        _Rain ("Rain", Float) = 1.0
        _SimTime ("Sim Time", Float) = 0.0
        // lighting stand-in uniforms (sim-space values, z flipped for unity dirs)
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

            TEXTURE2D(_AsphaltC);   SAMPLER(sampler_AsphaltC);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            TEXTURE2D(_AsphaltN);   SAMPLER(sampler_AsphaltN);
            TEXTURE2D(_AsphaltBC);  SAMPLER(sampler_AsphaltBC);
            TEXTURE2D(_AsphaltBN);  SAMPLER(sampler_AsphaltBN);
            TEXTURE2D(_PaintWearC); SAMPLER(sampler_PaintWearC);
            TEXTURE2D(_ConcreteC);  SAMPLER(sampler_ConcreteC);
            TEXTURE2D(_ConcreteN);  SAMPLER(sampler_ConcreteN);
            TEXTURE2D(_GrateC);     SAMPLER(sampler_GrateC);
            TEXTURE2D(_GrateN);     SAMPLER(sampler_GrateN);
            TEXTURE2D(_ManholeC);   SAMPLER(sampler_ManholeC);
            TEXTURE2D(_ManholeN);   SAMPLER(sampler_ManholeN);
            TEXTURE2D(_MarkAtlas);  SAMPLER(sampler_MarkAtlas); // clamp

            CBUFFER_START(UnityPerMaterial)
                float4 _AsphaltC_ST;
                float _Wetness, _Rain, _SimTime;
                float4 _SkyLightDir, _SkyLightColor, _AmbientSky, _AmbientGround;
                float4 _FogColor, _MidColor, _ZenithColor, _GlowMagenta, _GlowOrange, _GlowBlue, _GroundHaze;
                float _SkyBrightness;
            CBUFFER_END

            // ---- G constants (plan.js, sim space) ----
            #define AV_HALF 6.0
            #define CR_HALF 5.0
            #define ZS 66.0
            #define ZN -426.0
            #define XW -200.0
            #define XE 128.0
            #define CORNER_R 3.0
            #define CURB_H 0.15

            // GLYPH cells
            #define G_STOP 0.0
            #define G_ARROW_L 1.0
            #define G_ARROW_R 2.0
            #define G_SPEED 3.0
            #define G_DIAMOND 4.0
            #define G_BUS 5.0
            #define N_CELLS 6.0

            struct Attributes { float4 pos : POSITION; };
            struct Varyings { float4 pos : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.pos = NkClip(IN.pos.xyz);
                o.screenUV = o.pos.xy / o.pos.w * 0.5 + 0.5;
                o.worldPos = NkWorld(IN.pos.xyz);
                return o;
            }

            // ---------------- noise.js helpers ----------------
            float NkHash21(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
                float d = dot(q, float3(q.y, q.z, q.x) + 33.33);
                float3 r = q + d;
                return frac(r.x * (r.x + r.y) * 0 + (r.x + r.y) * r.z); // (r.x+r.y)*r.z
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
            float NkLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // Antialiased band mask: 1 where |x| < halfWidth
            float NkAaBand(float x, float halfWidth)
            {
                float w = max(abs(ddx(x)) + abs(ddy(x)), 1e-5);
                return smoothstep(-w, w, halfWidth - abs(x));
            }
            float NkAaStep(float x)
            {
                float w = max(abs(ddx(x)) + abs(ddy(x)), 1e-5);
                return smoothstep(-0.5 * w, 0.5 * w, x);
            }

            // signed distance to a centered 2D box
            float NkSdBox2(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0)) + min(max(d.x, d.y), 0);
            }

            // fract-stripe with proper AA width
            float NkStripe(float coord, float period, float duty)
            {
                float t = coord / period;
                float w = max(fwidth(t), 1e-4);
                float f = abs(frac(t) - 0.5);
                return smoothstep(w + duty * 0.5, -w + duty * 0.5, f);
            }

            // approximate 2D worley (F1, F2) — deviation: TSL mx_worley_noise_vec2 exact impl unknown
            float2 NkWorley2(float2 p)
            {
                float2 ip = floor(p), fp = frac(p);
                float f1 = 8.0, f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y);
                        float3 h = NkHash23(ip + o);
                        float2 d = o + h.xy - fp;
                        float dist = dot(d, d);
                        if (dist < f1) { f2 = f1; f1 = dist; }
                        else if (dist < f2) { f2 = dist; }
                    }
                return sqrt(float2(f1, f2));
            }

            // ---------------- wet.js ----------------
            // rainRipples: several layers of jittered cells, each spawns an expanding damped ring.
            float2 NkRainRipples(float2 xz, float intensity, float scale)
            {
                float2 g = 0;
                float t = _SimTime;
                // layers: { cell, off, rate } — scalar arrays (HLSL flat-init would leave tail slots undefined)
                float LC[4] = { 0.42, 0.37, 0.51, 0.31 };
                float LR[4] = { 1.15, 1.35, 0.95, 1.55 };
                float LOx[4] = { 0.0, 0.37, 0.83, 0.13 };
                float LOy[4] = { 0.0, 0.71, 0.19, 0.52 };
                for (int i = 0; i < 4; i++)
                {
                    float Lcell = LC[i], Lrate = LR[i];
                    float2 Loff = float2(LOx[i], LOy[i]);
                    float cs = 1.0 / (Lcell * scale);
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

            // wetLayer core: porous darkening, film roughness, micro relief fill, puddles
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

            float NkPuddleMask(float2 xz, float amount, float scale)
            {
                float n = NkFbm(xz * (0.35 / scale), 4);
                float thr = lerp(0.78, 0.45, amount);
                return smoothstep(thr, thr + 0.06, n);
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                float3 wp = IN.worldPos;
                float2 P = float2(wp.x, -wp.z); // sim (x, z)
                float x = P.x, z = P.y;
                float ax = abs(x);
                // nearest cross street center (3 slots as in roadMaterial.js)
                float zc = z > (-60.0 + -180.0) / 2 ? -60.0 : (z > (-180.0 + -300.0) / 2 ? -180.0 : -300.0);
                float dz = z - zc;
                float adz = abs(dz);

                // ---- region / curb distance ----
                bool crossValid = x > XW && x < XE;
                bool avValid = z < ZS && z > ZN;
                float r = CORNER_R;
                float2 wq = float2(r - (ax - AV_HALF), r - (adz - CR_HALF));
                float cdRound = length(max(wq, 0)) + min(max(wq.x, wq.y), 0) - r;
                float cd = crossValid ? (avValid ? cdRound : CR_HALF - adz) : AV_HALF - ax;
                bool inAv = ax < AV_HALF && z < ZS + 120 && z > ZN - 200;
                bool inCr = adz < CR_HALF && crossValid;
                float road = max(inAv ? 1 : 0, inCr ? 1 : 0);
                bool curbed = (avValid || (crossValid && adz < CR_HALF + 0.5)) && (ax < AV_HALF + 0.5 || crossValid) && road > 0.5;
                bool isAvF = (AV_HALF - ax >= CR_HALF - adz) || !crossValid;
                float across = isAvF ? dz : x;
                float along = isAvF ? x : z;
                float lr = isAvF ? (adz - 2.5) : (frac(ax / 3) * 3 - 1.5);

                // ---- asphalt base: two hex-tiled variants with macro blend ----
                // (hex tap port simplified: 3 taps as in hexTaps)
                float2 uvA = P / 1.7;
                float2 uvB = P / 2.4 + float2(13.1, 7.7);
                float3 sAc = 0, sBc = 0; float2 sAn = 0, sBn = 0; float sAh = 0, sBh = 0;
                {
                    // hexTaps(uvA, scale 0.42)
                    float scale = 0.42;
                    float2 st = uvA * (scale * 2 * 1.7320508);
                    float2 sk = float2(st.x - st.y * 0.57735027, st.y * 1.15470054);
                    float2 base = floor(sk);
                    float2 f = frac(sk);
                    float tz = 1 - f.x - f.y;
                    float s = step(0, -tz);
                    float s2 = s * 2 - 1;
                    float3 h = NkHash23(base + float2(s, s) + 0.5);
                    float ang = h.z * 6.2831853;
                    float2 cs = float2(cos(ang), sin(ang));
                    float2 ruv = float2(uvA.x * cs.x - uvA.y * cs.y, uvA.x * cs.y + uvA.y * cs.x);
                    float4 cn = SAMPLE_TEXTURE2D(_AsphaltN, sampler_AsphaltN, ruv + h.xy * 7.0);
                    sAc = SAMPLE_TEXTURE2D(_AsphaltC, sampler_AsphaltC, ruv + h.xy * 7.0).rgb;
                    sAn = cn.rg * 2 - 1;
                    sAn = float2(sAn.x * cs.x + sAn.y * cs.y, sAn.y * cs.x - sAn.x * cs.y);
                    sAh = cn.b;
                    // variant B
                    float2 stB = uvB * (0.38 * 2 * 1.7320508);
                    float2 skB = float2(stB.x - stB.y * 0.57735027, stB.y * 1.15470054);
                    float2 baseB = floor(skB);
                    float2 fB = frac(skB);
                    float tzB = 1 - fB.x - fB.y;
                    float sB = step(0, -tzB);
                    float3 hB = NkHash23(baseB + float2(sB, sB) + 0.5);
                    float angB = hB.z * 6.2831853;
                    float2 csB = float2(cos(angB), sin(angB));
                    float2 ruvB = float2(uvB.x * csB.x - uvB.y * csB.y, uvB.x * csB.y + uvB.y * csB.x);
                    float4 cnB = SAMPLE_TEXTURE2D(_AsphaltBN, sampler_AsphaltBN, ruvB + hB.xy * 7.0);
                    sBc = SAMPLE_TEXTURE2D(_AsphaltBC, sampler_AsphaltBC, ruvB + hB.xy * 7.0).rgb * 0.78;
                    sBn = cnB.rg * 2 - 1;
                    sBn = float2(sBn.x * csB.x + sBn.y * csB.y, sBn.y * csB.x - sBn.x * csB.y);
                    sBh = cnB.b;
                }
                float macro = NkFbm(P * 0.028, 3);
                float macro2 = NkFbm(P * 0.11 + 9.7, 3);
                float old = smoothstep(0.38, 0.66, macro + (macro2 - 0.5) * 0.35);
                float3 albedo = lerp(sAc, sBc * 0.82, old);
                float2 nxy = lerp(sAn, sBn, old) * 0.85;
                float hAsph = lerp(sAh, sBh, old);
                albedo *= lerp(0.8, 1.12, macro2) * lerp(1.0, 0.86, smoothstep(0.55, 0.8, NkFbm(P * 0.45 + 3.3, 2)));
                float rough = lerp(0.86, 0.66, hAsph);
                float porosity = 0.85;
                float metal = 0;

                // wheel paths + oil strip
                float wheel = exp((abs(lr) - 0.85) * (abs(lr) - 0.85) * (-1 / (2 * 0.24 * 0.24))) * road;
                float oilN = NkFbm(float2(lr * 2.2, along * 0.16) + 5.1, 3);
                float oil = exp(lr * lr * (-1 / (2 * 0.3 * 0.3))) * smoothstep(0.5, 0.78, oilN) * road;
                albedo *= lerp(1.0, 0.8, wheel) * lerp(1.0, 0.55, oil);
                rough = lerp(rough, rough * 0.8, wheel);
                rough = lerp(rough, 0.35, oil);
                porosity = lerp(porosity, 0.2, oil);

                // ---- repaired patches + trench strips ----
                float cellL = 19.0;
                float pc = floor(along / cellL);
                float3 ph = NkHash23(float2(pc, (isAvF ? 7 : 0) + zc * 0.01));
                float3 ph2 = NkHash23(float2(pc + 91, (isAvF ? 1 : 0) + 3));
                float pU = (ph.x - 0.5) * lerp(7.0, 8.0, isAvF);
                float2 pHalf = float2(ph.y * 1.6 + 0.55, ph2.x * 3.4 + 0.7);
                float pV = pc * cellL + cellL * 0.5 + (ph.z - 0.5) * (cellL - 9);
                float pSd = NkSdBox2(float2(across - pU, along - pV), pHalf);
                float pExists = step(0.45, ph2.y) * road;
                float trU = lerp(-2.2, 3.6, step(0.5, ph2.z));
                float trSd = NkSdBox2(float2(across - trU, along - (pc * cellL + cellL / 2)), float2(0.32, cellL * 0.5 + 0.3));
                float trExists = step(0.72, NkHash21(float2(floor(along / (cellL * 3)), (isAvF ? 1 : 0) + 11))) * road;
                float patchSd = min(pExists > 0.5 ? pSd : 99, trExists > 0.5 ? trSd : 99);
                float patchIn = NkAaStep(-patchSd);
                float seamWob = (NkVnoise(P * 6.0) - 0.5) * 0.03;
                float patchSeam = NkAaBand(patchSd + seamWob, 0.03);
                float patchTone = lerp(0.38, 0.6, NkHash21(float2(pc, 2)));
                albedo = lerp(albedo, sBc * patchTone * float3(0.95, 0.97, 1.0), patchIn * 0.85);
                rough = lerp(rough, 0.8, patchIn * 0.5);

                // ---- tar snakes + alligator cracking ----
                float2 warp = (float2(NkFbm(P * 0.31, 2), NkFbm(P * 0.31 + 4.4, 2)) - 0.5) * 2.4;
                float tn = NkFbm(P * 0.16 + warp * 0.3, 3);
                float snake = NkAaBand(tn - 0.5, 0.0065) * smoothstep(0.52, 0.68, NkFbm(P * 0.045 + 2.2, 2)) * road;
                float tar = max(snake, patchSeam);
                float2 wn = NkWorley2(P * 0.95 + warp * 0.35);
                float crackE = wn.y - wn.x;
                float crackZone = smoothstep(0.6, 0.78, old * 0.6 + NkFbm(P * 0.07 + 8.0, 2) * 0.6) * road * (1 - patchIn);
                float crack = smoothstep(0.05, 0.0, crackE) * crackZone;
                albedo = lerp(albedo, float3(0.018, 0.017, 0.016), tar * 0.92);
                albedo *= lerp(1.0, 0.35, crack);
                rough = lerp(rough, 0.32, tar);
                porosity = lerp(porosity, 0.05, tar);
                nxy = lerp(nxy, nxy * 0.2, tar);

                // ---- markings ----
                float wear = SAMPLE_TEXTURE2D(_PaintWearC, sampler_PaintWearC, P / 1.35).r;
                float sAv = adz - CR_HALF;
                bool apprAv = (x < 0 && dz > 0) || (x > 0 && dz < 0);
                float avLines = (sAv > 6.4 ? 1 : 0) * inAv * isAvF;
                float centerY = NkAaBand(ax - 0.17, 0.065) * avLines;
                bool solidZone = sAv < 32;
                bool dashA = frac(z / 10) < 0.5;
                float laneLn = NkAaBand(ax - 3.0, 0.075) * avLines;
                float laneY = laneLn * (solidZone ? 1 : 0);
                float laneW = laneLn * (solidZone ? 0 : 1) * (dashA ? 1 : 0);
                float edgeA = NkAaBand(ax - (AV_HALF - 0.55), 0.075) * avLines;
                float zebraA = NkStripe(x + 0.225, 0.9, 0.5) * ((sAv > 1.0 && sAv < 5.0 && ax < 5.4) ? 1 : 0) * inAv;
                float stopA = NkAaBand(sAv - 6.0, 0.225) * (apprAv ? 1 : 0) * ((ax > 0.35 && ax < AV_HALF - 0.45) ? 1 : 0) * inAv;
                bool outer = ax > 3.0;
                float laneC = sign(x) * lerp(1.5, 4.5, outer ? 1 : 0);
                float uRaw = x < 0 ? x - laneC : laneC - x;
                // slot table: [start, length, width, cell]
                float s0 = -100, sLen = 1, sW = 1, sCell = 0, sOk = 0;
                {
                    float SA[5] = { 8.0, 25.0, 44.0, 55.0, 80.0 };
                    float SL[5] = { 5.0, 3.6, 4.5, 3.6, 5.5 };
                    float SW[5] = { 1.7, 1.5, 2.1, 1.5, 2.3 };
                    float SC[5] = { -1.0, 4.0, 3.0, 4.0, 5.0 };
                    for (int i = 0; i < 5; i++)
                    {
                        float a = SA[i];
                        bool inS = sAv >= a && sAv < a + SL[i];
                        float c = SC[i] < 0 ? (outer ? G_ARROW_R : G_ARROW_L) : SC[i];
                        float ok = (SC[i] == G_BUS) ? (outer ? 1 : 0) : 1;
                        s0 = inS ? a : s0; sLen = inS ? SL[i] : sLen; sW = inS ? SW[i] : sW;
                        sCell = inS ? c : sCell; sOk = inS ? ok : sOk;
                    }
                }
                float gu = uRaw / sW + 0.5;
                float gv = s0 + sLen - sAv;
                gv /= sLen;
                float gIn = ((gu > 0 && gu < 1 && gv > 0 && gv < 1) ? 1 : 0) * sOk * (apprAv ? 1 : 0) * avLines;
                float glyphA = SAMPLE_TEXTURE2D(_MarkAtlas, sampler_MarkAtlas, float2((sCell + clamp(gu, 0.01, 0.99)) / N_CELLS, clamp(gv, 0.005, 0.995))).r * gIn;

                // cross street frame
                float sCr = ax - AV_HALF;
                bool apprCr = (x < 0 && dz < 0) || (x > 0 && dz > 0);
                float crLines = (sCr > 6.4 ? 1 : 0) * inCr * (isAvF ? 0 : 1);
                float centerC = NkAaBand(dz, 0.075) * crLines * (frac(x / 9) < 0.55 ? 1 : 0);
                float edgeC = NkAaBand(adz - (CR_HALF - 0.45), 0.07) * crLines;
                float zebraC = NkStripe(dz + 0.225, 0.9, 0.5) * ((sCr > 1.0 && sCr < 5.0 && adz < 4.4) ? 1 : 0) * inCr;
                float stopC = NkAaBand(sCr - 6.0, 0.225) * (apprCr ? 1 : 0) * ((adz > 0.2 && adz < CR_HALF - 0.4) ? 1 : 0) * inCr;
                float laneCC = sign(dz) * 2.5;
                float uC = (x < 0 ? dz - laneCC : laneCC - dz) / 2.6 + 0.5;
                float vC = (7.6 + 5.0 - sCr) / 5.0;
                float gInC = ((uC > 0 && uC < 1 && vC > 0 && vC < 1) ? 1 : 0) * (apprCr ? 1 : 0) * inCr;
                float glyphC = SAMPLE_TEXTURE2D(_MarkAtlas, sampler_MarkAtlas, float2((G_STOP + clamp(uC, 0.01, 0.99)) / N_CELLS, clamp(vC, 0.005, 0.995))).r * gInC;

                float whiteRaw = max(max(max(laneW, edgeA), max(zebraA, stopA)), max(max(glyphA, glyphC), max(max(centerC, edgeC), max(zebraC, stopC))));
                float yellowRaw = max(centerY, laneY);
                float fresh = NkHash21(float2(floor(along / 23), zc + (isAvF ? 5 : 0)));
                float grit = NkFbm(P * 3.1 + 2.0, 3);
                float keep = smoothstep(0.4, 0.62, wear * 0.9 + fresh * 0.38 + (grit - 0.5) * 0.5 - wheel * 0.3 - crack);
                float white = whiteRaw * keep * lerp(0.72, 1.0, fresh);
                float yellow = yellowRaw * keep;
                float paint = max(white, yellow);
                float3 paintCol = (white > yellow - 0.001) ? float3(0.6, 0.6, 0.57) : float3(0.6, 0.36, 0.06);
                albedo = lerp(albedo, paintCol * lerp(0.55, 1.0, wear) * lerp(0.7, 1.0, grit), paint * 0.93);
                rough = lerp(rough, 0.62, paint);
                porosity = lerp(porosity, 0.18, paint);
                nxy = lerp(nxy, nxy * 0.25, paint);

                // ---- gutter strip + storm drains ----
                float gw = 0.34;
                float gutter = NkAaStep(gw - cd) * NkAaStep(cd + 0.02) * (curbed ? 1 : 0);
                float3 gTex = SAMPLE_TEXTURE2D(_ConcreteC, sampler_ConcreteC, float2(along / 1.4, cd / 1.4)).rgb;
                float2 gN = SAMPLE_TEXTURE2D(_ConcreteN, sampler_ConcreteN, float2(along / 1.4, cd / 1.4)).rg * 2 - 1;
                float gJoint = NkAaBand(abs(frac(along / 1.0) - 0.5) - 0.5, 0.006);
                float3 gutCol = gTex * float3(0.55, 0.55, 0.56) * lerp(1.0, 0.4, gJoint) * lerp(0.55, 1.0, smoothstep(0.0, 0.12, cd));
                albedo = lerp(albedo, gutCol, gutter);
                rough = lerp(rough, 0.75, gutter);
                nxy = lerp(nxy, gN * 0.5, gutter);
                albedo *= lerp(0.55, 1.0, max(smoothstep(0.0, 0.3, cd), curbed ? 0 : 1));

                float dc = floor(along / 15.0);
                float dLocal = along - (dc * 15.0 + 7.5);
                float grateIn = ((abs(dLocal) < 0.3 && cd > 0.025 && cd < gw - 0.01) ? 1 : 0) * gutter;
                float2 gUV = float2((cd - 0.025) / (gw - 0.035), dLocal / 0.6 + 0.5);
                float3 grC = SAMPLE_TEXTURE2D(_GrateC, sampler_GrateC, gUV).rgb;
                float4 grN = SAMPLE_TEXTURE2D(_GrateN, sampler_GrateN, gUV);
                float hole = smoothstep(0.12, 0.05, NkLuma(grC)) * grateIn;
                albedo = lerp(albedo, lerp(grC * 0.9, 0.004, hole), grateIn);
                rough = lerp(rough, lerp(0.42, 0.9, hole), grateIn);
                metal = lerp(metal, 0.75 * (1 - hole), grateIn);
                nxy = lerp(nxy, (grN.rg * 2 - 1) * 0.9, grateIn);
                porosity = lerp(porosity, 0.0, grateIn);

                // ---- manholes ----
                float mCellA = floor(z / 23.0);
                float3 mhA = NkHash23(float2(mCellA, 5.3));
                float2 mcA = float2(floor(mhA.x * 4) * 3 - 4.5 + (mhA.y - 0.5) * 0.7, mCellA * 23 + 4 + mhA.z * 15);
                float mExA = step(0.3, NkHash21(float2(mCellA, 9.1)));
                float mCellC = floor(x / 23.0);
                float3 mhC = NkHash23(float2(mCellC, zc * 0.1 + 1.7));
                float2 mcC = float2(mCellC * 23 + 4 + mhC.z * 15, zc + lerp(-2.5, 2.5, step(0.5, mhC.x)) + (mhC.y - 0.5) * 0.8);
                float mExC = step(0.4, NkHash21(float2(mCellC, zc)));
                float dA = mExA > 0.5 ? length(P - mcA) : 99;
                float dC = mExC > 0.5 ? length(P - mcC) : 99;
                bool useA = dA < dC;
                float2 mc = useA ? mcA : mcC;
                float md = min(dA, dC);
                float mR = 0.33;
                float mRot = NkHash21(mc * 1.3) * 6.283;
                float2 ml = P - mc;
                float2 mlr = float2(ml.x * cos(mRot) - ml.y * sin(mRot), ml.x * sin(mRot) + ml.y * cos(mRot));
                float2 mUV = mlr / (mR * 2.0) + 0.5;
                float3 mC = SAMPLE_TEXTURE2D(_ManholeC, sampler_ManholeC, mUV).rgb;
                float4 mN = SAMPLE_TEXTURE2D(_ManholeN, sampler_ManholeN, mUV);
                float mIn = NkAaStep(mR - md) * road;
                float mGap = NkAaBand(md - mR, 0.008) * road;
                float collarSd = NkSdBox2(ml, float2(0.62, 0.62));
                float collar = NkAaStep(-collarSd) * road * (1 - mIn);
                albedo = lerp(albedo, sBc * 0.6, collar * 0.8);
                albedo = lerp(albedo, 0.006, NkAaBand(collarSd, 0.02) * road * 0.85);
                albedo = lerp(albedo, mC * 0.85, mIn);
                albedo = lerp(albedo, 0.003, mGap);
                rough = lerp(rough, lerp(0.55, 0.3, mN.b), mIn);
                metal = lerp(metal, lerp(0.55, 0.9, mN.b), mIn);
                nxy = lerp(nxy, mN.rg * 2 - 1, mIn);
                porosity = lerp(porosity, 0.0, mIn);

                // ---- puddles ----
                float pn = NkFbm(P * 0.2 + 3.1, 4) + (NkFbm(P * 0.95 + 1.3, 2) - 0.5) * 0.22;
                float gutterProx = smoothstep(1.4, 0.0, cd) * (curbed ? 1 : 0);
                float crownDry = smoothstep(4.5, 0.0, abs(across)) * road * 0.07;
                float level = pn + wheel * 0.11 + gutterProx * 0.26 + crack * 0.06 + patchSeam * 0.05 - crownDry - (hAsph - 0.5) * 0.06;
                float puddle = smoothstep(0.672, 0.686, level) * (1 - grateIn);
                float flowT = _SimTime * 1.4;
                float2 fq = float2(cd * 14.0, along * 2.5 - flowT * 2.0);
                float2 fl = float2(NkVnoise(fq) - 0.5, NkVnoise(fq + float2(5.2, 1.3)) * 1.7 - 0.5) * 0.25;
                float2 flowN = float2(isAvF ? fl.y : fl.x, isAvF ? fl.x : fl.y) * gutter;
                float gutterWater = gutter * smoothstep(0.35, 0.55, NkFbm(float2(along * 0.08, 1.0), 2) + 0.15) * (1 - grateIn);
                float pudAll = max(puddle, gutterWater * 0.9);

                float2 rip = NkRainRipples(P, 1.0, 1.0) + flowN * gutterWater;
                float microR = NkFbm(P * 1.9 + 7.0, 2);
                rough *= lerp(0.75, 1.25, microR);
                float dryVar = smoothstep(0.35, 0.7, NkFbm(P * 0.06 + 21.0, 3)) * 0.3;
                float3 oAlb; float oRough; float2 oNxy; float oPud;
                NkWetLayer(albedo, rough, nxy, porosity, _Wetness * (1 - dryVar), pudAll, rip, 0.07, oAlb, oRough, oNxy, oPud);
                oRough = clamp(oRough, 0.012, 1);

                // ---------------- lighting (deviation: analytic stand-in for three's PBR) ----------------
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                // world up-facing perturbed normal (upNormalToView equivalent, in world space)
                float ny = sqrt(max(1 - dot(oNxy, oNxy), 0.04));
                float3 N = normalize(float3(oNxy.x, ny, oNxy.y));
                float3 Ldir = normalize(_SkyLightDir.xyz);
                float3 H = normalize(Ldir + V);
                float ndl = max(dot(N, Ldir), 0);
                float ndh = max(dot(N, H), 0);
                float ndv = max(dot(N, V), 0.001);
                float a = oRough * oRough;
                float dnm = ndh * ndh * (a * a - 1) + 1;
                float D = a * a / (3.14159265 * dnm * dnm);
                float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
                float spec = D / (4 * ndl * ndv + 0.001) * ndl * fres;
                // env reflection: the real sky function along the reflected ray, blurred by roughness
                float3 env = NkHazeColor(reflect(-V, N), _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz,
                    _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float envMix = exp(-oRough * 9.0); // sharp for smooth (wet) surfaces
                float3 col = oAlb * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * oAlb + spec * (metal > 0.5 ? 2 : 1) * 0.25)
                           + env * envMix * (0.35 + fres * 0.65) * lerp(1, oAlb, metal);
                col = lerp(col, env * envMix, oPud * 0.85); // puddles are near mirrors

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
