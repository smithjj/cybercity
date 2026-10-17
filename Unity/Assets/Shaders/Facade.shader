// Facade — port of materials.js createFacadeMaterial (TSL -> HLSL).
// Window grid + interior mapping + curtains + atlas walls; lighting via Surf-convention
// sky/hemi/env + point lights (NkBPointLights) + fog.
Shader "NEON KAIDO/Facade"
{
    Properties
    {
        _AtlasC("_AtlasC", 2D) = "white" {}
        _AtlasN("_AtlasN", 2D) = "bump" {}
        _Interior("_Interior", 2D) = "gray" {}
        _Noise("_Noise", 2D) = "black" {}
    }
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

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"
            #include "Assets/Shaders/BuildingsCommon.hlsl"

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

            TEXTURE2D(_AtlasC); SAMPLER(sampler_AtlasC);
            TEXTURE2D(_AtlasN); SAMPLER(sampler_AtlasN);
            TEXTURE2D(_Interior); SAMPLER(sampler_Interior);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 aWin : TEXCOORD1;
                float4 aWin2 : TEXCOORD2;
                float4 aWin3 : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 aWin : TEXCOORD4;
                float4 aWin2 : TEXCOORD5;
                float4 aWin3 : TEXCOORD6;
                float3 nrmAttr : TEXCOORD7;
                float4 tanAttr : COLOR0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = NkWorld(IN.positionOS);
                OUT.positionCS = NkClip(OUT.worldPos);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                OUT.normalWS = normalize(float3(IN.normalOS.x, IN.normalOS.y, -IN.normalOS.z));
                OUT.tangentWS = float4(IN.tangentOS.x, IN.tangentOS.y, -IN.tangentOS.z, IN.tangentOS.w);
                OUT.uv = IN.uv;
                OUT.aWin = IN.aWin;
                OUT.aWin2 = IN.aWin2;
                OUT.aWin3 = IN.aWin3;
                OUT.nrmAttr = IN.normalOS;       // attribute('normal') = raw interpolated attr (sim space!)
                OUT.tanAttr = IN.tangentOS;      // attribute('tangent') = raw interpolated attr
                return OUT;
            }

            float3 NkAtlasSample(TEXTURE2D_PARAM(tex, samp), float2 t, float col, float row)
            {
                float2 c = frac(t);
                float au = (col + c.x * 0.99 + 0.005) / 4.0;
                float av = 1.0 - (row + 1.0) / 2.0 + (c.y * 0.99 + 0.005) / 2.0;
                float2 dx = ddx(t) * float2(0.25 * 0.99, 0.5 * 0.99);
                float2 dy = ddy(t) * float2(0.25 * 0.99, 0.5 * 0.99);
                return SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(au, av), dx, dy).rgb;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 fuv = IN.uv;
                float3 P = IN.worldPos;
                float4 A = IN.aWin, B = IN.aWin2, C = IN.aWin3;
                float seed = A.x, floorH = A.y, bayW = A.z, startY = A.w;
                float ww = B.x, wh = B.y, litP = B.z, kind = floor(B.w + 0.5);
                float topY = C.x, layer = floor(C.y + 0.5), texScale = C.z, tint = C.w;
                float wet = saturate(_Wetness);
                float sd = floor(seed * 9973.0 + 0.5);

                float k0 = step(kind, 0.5);
                float k1 = step(0.5, kind) * step(kind, 1.5);
                float k2 = step(1.5, kind) * step(kind, 2.5);
                float k3 = step(2.5, kind) * step(kind, 3.5);
                float kRoof = step(3.5, kind) * step(kind, 4.5);
                float kPunch = k0 + k3;

                // ---------- wall texture (atlas 4x2, grad-sampled, two-scale anti-repeat blend) ----------
                float col = fmod(layer, 4.0), row = floor(layer / 4.0);
                float2 tuv = fuv / texScale + float2(sd * 0.137, sd * 0.071);
                float2 tuv2 = tuv * 0.57 + float2(0.31, 0.77);
                float macro = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, (float2(P.x + P.z, P.y) * 0.015 + sd * 0.0137)).r;
                float blendM = smoothstep(0.38, 0.62, macro);
                float3 a1 = NkAtlasSample(TEXTURE2D_ARGS(_AtlasC, sampler_AtlasC), tuv, col, row);
                float3 a2 = NkAtlasSample(TEXTURE2D_ARGS(_AtlasC, sampler_AtlasC), tuv2, col, row);
                float3 wallAlb = lerp(a1, a2, blendM);
                float3 n1 = NkAtlasSample(TEXTURE2D_ARGS(_AtlasN, sampler_AtlasN), tuv, col, row);
                float3 n2 = NkAtlasSample(TEXTURE2D_ARGS(_AtlasN, sampler_AtlasN), tuv2, col, row);
                float3 wallNrm = lerp(n1, n2, blendM);

                // building tint + macro dirt
                float3 pal = 0.5 + 0.5 * cos(float3(0.0, 2.1, 4.2) + tint * 6.283);
                float3 tintCol = lerp(1.0, pal, 0.22) * lerp(0.72, 1.12, NkBh21(float2(sd, 3.1)));
                float dirt = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(P.x + P.z * 0.7, P.y * 1.6) * 0.016).g;
                wallAlb = wallAlb * tintCol * lerp(0.7, 1.05, dirt);

                // ---------- window grid ----------
                float cx = fuv.x / bayW;
                float ix = floor(cx), fx = frac(cx);
                float ry = fuv.y - startY;
                float cy = ry / floorH;
                float iy = floor(cy), fy = frac(cy);
                float2 cellId = float2(ix + sd * 0.731, iy + sd * 0.377);
                float hA = NkBh21(cellId), hB = NkBh21(cellId + 17.1), hC = NkBh21(cellId + 41.7), hD = NkBh21(cellId + 73.3);
                float zone = step(0.0, ry) * step(fuv.y, topY - 0.5) * step(kind, 3.5);
                float present = lerp(1.0, step(hD, 0.42), k3) * zone;

                // window rect in cell coords
                float wwE = lerp(ww, ww * 0.45, k3);
                float x0 = lerp(0.5 - wwE * 0.5, 0.025, k1 + k2);
                float x1 = lerp(0.5 + wwE * 0.5, 0.975, k1 + k2);
                float y0 = lerp(lerp(0.27, 0.42, k3), 0.12, k2);
                float y1 = min(lerp(y0 + lerp(wh, wh * 0.5, k3), 0.97, k2), 0.94);

                // tangent-space view vector (world-space attrs)
                float3 T = IN.tanAttr.xyz;
                float3 Na = IN.nrmAttr;
                float3 V = P - _WorldSpaceCameraPos;
                float vz = max(-dot(V, Na), 0.001);
                float3 vt = float3(dot(V, T), V.y, vz);

                float recess = lerp(0.16, 0.03, k2);

                float2 fc = float2(fx, fy);
                float2 g = fc + float2(vt.x / vt.z * recess / bayW, vt.y / vt.z * recess / floorH);
                float opening = NkAaRect(fc, x0, x1, y0, y1) * present;
                float glassIn = NkAaRect(g, x0, x1, y0, y1);
                float reveal = opening * (1 - glassIn);
                float glassV = opening * glassIn;

                // frame / mullions at glass plane (meters)
                float ex = min(g.x - x0, x1 - g.x) * bayW;
                float ey = min(g.y - y0, y1 - g.y) * floorH;
                float fw = lerp(0.05, 0.035, k2);
                float winWm = (x1 - x0) * bayW;
                float midX = (x0 + x1) * 0.5;
                float mullion = (abs(g.x - midX) * bayW < 0.025 ? 1.0 : 0.0) * step(1.0, winWm) * (1 - k2);
                float transomY = y0 + (y1 - y0) * 0.72;
                float transom = (abs(g.y - transomY) * floorH < 0.022 ? 1.0 : 0.0) * step(0.6, hB) * (k0 + k2);
                float frame = max(max(1 - smoothstep(fw * 0.8, fw, min(ex, ey)), mullion), transom) * glassV;
                float glass = glassV * (1 - frame);

                // ---------- interior mapping (room = bay x floor x depth) ----------
                float depth = lerp(1.8, 3.4, hC);
                float3 d = float3(vt.x / bayW, vt.y / floorH, vt.z / depth);
                float sx = d.x > 0 ? 1.0 : 0.0, sy = d.y > 0 ? 1.0 : 0.0;
                float dxs = max(abs(d.x), 1e-4) * (sx * 2 - 1);
                float dys = max(abs(d.y), 1e-4) * (sy * 2 - 1);
                float2 o = float2(clamp(g.x, 0.001, 0.999), clamp(g.y, 0.001, 0.999));
                float tX = (sx - o.x) / dxs, tY = (sy - o.y) / dys, tZ = 1.0 / d.z;
                float tMin = min(min(tX, tY), tZ);
                float3 hit = float3(o.x + d.x * tMin, o.y + d.y * tMin, d.z * tMin);
                float isBack = step(tZ, min(tX, tY));
                float isSide = (1 - isBack) * step(tX, tY);
                float isFC = max((1 - isBack) - isSide, 0);
                float isFloor = isFC * (1 - sy);
                float isCeil = isFC * sy;
                float idx = floor(hA * 15.999);
                float aiu = (fmod(idx, 4.0) + hit.x * 0.96 + 0.02) / 4.0;
                float aiv = 1.0 - (floor(idx / 4.0) + 1.0) / 4.0 + (hit.y * 0.96 + 0.02) / 4.0;
                float2 gscale = float2(0.25, 0.25);
                float3 backTex = SAMPLE_TEXTURE2D_GRAD(_Interior, sampler_Interior, float2(aiu, aiv),
                    float2(ddx(cx), ddx(cy)) * gscale, float2(ddy(cx), ddy(cy)) * gscale).rgb;
                float3 roomWall = lerp(lerp(float3(0.62, 0.56, 0.47), float3(0.55, 0.57, 0.58), step(0.5, hB)), float3(0.42, 0.47, 0.52), step(0.8, hC));
                float3 floorCol = lerp(float3(0.28, 0.19, 0.12), float3(0.36, 0.38, 0.26), step(0.7, hD));
                float lampD = length(float2(hit.x - 0.5, hit.z - 0.4));
                float lamp = smoothstep(0.2, 0.07, lampD) * isCeil;
                float fall = clamp(1.3 - length(float3(hit.x - 0.5, hit.y - 0.95, hit.z - 0.4)) * 0.75, 0.25, 1.2);
                float panel = lerp(0.75, 1.0, step(0.05, frac(hit.x * 2.0)) * step(0.05, frac(hit.z * 2.5)));
                float3 interiorCol = backTex * isBack * 0.95
                    + roomWall * isSide * 0.8
                    + floorCol * isFloor * 0.8
                    + roomWall * isCeil * 0.42 * panel * (1 - smoothstep(0.03, 0.0, abs(lampD - 0.24)) * 0.8);
                interiorCol = interiorCol * fall + lamp * 3.5;

                // lights
                float lit = step(hB, litP) * present;
                float ht = NkBh21(cellId + 91.3);
                float flick = sin(_SimTime * 7.3 + hA * 60) * sin(_SimTime * 2.1 + hC * 13) * 0.35 + 0.6;
                float3 warm = float3(1.0, 0.68, 0.38), cool = float3(0.72, 0.86, 1.0), tv = float3(0.35, 0.5, 1.0) * flick;
                float3 lightCol = lerp(lerp(warm, cool, step(0.55, ht)), tv, step(0.87, ht) * (1 - k2));
                float intensity = lerp(0.35, 1.1, hC * hC) * lerp(1.0, 1.3, k2);

                // curtains / blinds / frosted
                float pxw = (g.x - x0) / max(x1 - x0, 1e-3);
                float pyw = (g.y - y0) / max(y1 - y0, 1e-3);
                float hc = NkBh21(cellId + 57.9);
                float c0 = lerp(0.08, 0.45, hD), c1 = c0 + lerp(0.12, 0.5, hA);
                float tCurt = step(0.45, hc) * step(hc, 0.7);
                float tBlind = step(0.7, hc) * step(hc, 0.83);
                float tFrost = step(0.83, hc) * step(hc, 0.93);
                float tLace = step(0.93, hc);
                float curtCover = 1 - step(c0, pxw) * step(pxw, c1);
                float slatF = pyw * 14.0;
                float slat = lerp(step(0.3, frac(slatF)), 0.7, smoothstep(0.25, 0.7, fwidth(slatF)));
                float coverRaw = tCurt * curtCover + tBlind * slat * 0.92 + tFrost + tLace * 0.7;
                float cover = coverRaw * (1 - k2 * 0.6);
                float3 curtPal = lerp(lerp(float3(0.75, 0.68, 0.55), float3(0.6, 0.65, 0.75), step(0.5, hA)), float3(0.7, 0.45, 0.42), step(0.85, hB));
                float3 curtCol = lerp(curtPal * (sin(pxw * 38.0) * lerp(0.14, 0.0, smoothstep(0.3, 1.0, fwidth(pxw * 6.0))) + 0.88),
                    float3(0.8, 0.82, 0.8), min(tBlind + tFrost + tLace, 1));
                float3 seen = lerp(interiorCol, curtCol * 0.8, cover);

                float dim = step(litP, hB) * step(hB, litP + 0.35) * present;
                float3 glassEm = seen * lightCol * (intensity * lit + dim * 0.07);
                float3 glassAlb = seen * 0.05 + float3(0.008, 0.01, 0.013);

                // frame colour per building
                float fsel = NkBh21(float2(sd, 8.8));
                float3 frameCol = lerp(lerp(float3(0.5, 0.52, 0.53), float3(0.05, 0.05, 0.055), step(0.45, fsel)), float3(0.24, 0.19, 0.14), step(0.85, fsel));

                // ---------- wall extras: sills, slab bands, streaks, wet tops ----------
                float sillMask = NkAaRect(fc, x0 - 0.05 / bayW, x1 + 0.05 / bayW, y0 - 0.07 / floorH, y0) * present * kPunch;
                float belowSill = (y0 - fy) * floorH;
                float underWin = step(x0 + 0.02, fx) * step(fx, x1 - 0.02) * step(0.0, belowSill) * present * kPunch;
                float stN = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(fuv.x * 0.11, fuv.y * 0.0055)).b;
                float winStreak = underWin * smoothstep(1.4, 0.0, belowSill) * smoothstep(0.35, 0.7, stN);
                float rainN = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(fuv.x * 0.045 + sd * 0.01, fuv.y * 0.012)).a
                    * SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(fuv.x * 0.01, fuv.y * 0.004 + sd * 0.01)).r * 1.6;
                float rainStreak = smoothstep(0.18, 0.5, rainN);
                float topWet = smoothstep(topY - 4.0, topY + 0.5, fuv.y);
                float slab = step(fy, 0.09 / floorH * 1.0) * zone * (k0 + k1);
                float groundGrime = smoothstep(1.6, 0.0, P.y) * 0.5;
                float streakAll = clamp(winStreak + rainStreak * 0.7 + topWet * 0.5, 0, 1);

                float3 wall = wallAlb * lerp(1.0, 0.82, slab) * (1 - groundGrime * 0.4);
                wall = lerp(wall, float3(0.42, 0.41, 0.39) * tintCol, sillMask * 0.8);
                wall = wall * lerp(1.0, 0.68, streakAll * wet);
                float wallLum = dot(wallAlb, float3(0.3, 0.59, 0.11));
                float wallRough = lerp(0.9 - wallLum * 0.2, 0.32, clamp(wet * 0.35 + streakAll * wet * 0.55, 0, 1));

                // ---------- roof ----------
                float puddle = smoothstep(0.56, 0.62, SAMPLE_TEXTURE2D(_Noise, sampler_Noise, fuv * 0.05 + sd * 0.01).r);
                float3 roofAlb = wallAlb * lerp(0.85, 0.45, puddle);
                float roofRough = lerp(lerp(0.85, 0.4, wet), 0.03, puddle * wet);

                // ---------- far-distance antialias ----------
                float cellPx = max(fwidth(cx), fwidth(cy));
                float aaF = smoothstep(0.18, 0.55, cellPx) * zone;
                float glassFrac = (x1 - x0) * (y1 - y0) * present;
                float3 avgEm = lightCol * intensity * lit * 0.42 * (1 - cover * 0.5) * (glassFrac * 1.4);
                float3 statEm = lightCol * 0.5 * litP * glassFrac * 0.55 * max(present, zone * 0.4);
                float3 farEm = lerp(avgEm, statEm, smoothstep(0.7, 1.6, cellPx));

                // ---------- compose ----------
                float reveal2 = reveal * (1 - aaF);
                float glass2 = glass * (1 - aaF);
                float frame2 = frame * (1 - aaF);
                float wallW = max(1 - reveal2 - glass2 - frame2, 0);
                float3 albedoC = wall * wallW + wallAlb * 0.4 * reveal2 + glassAlb * glass2 + frameCol * frame2;
                albedoC = lerp(albedoC, lerp(albedoC, 0.02, glassFrac * 0.6), aaF);
                albedoC = lerp(albedoC, roofAlb, kRoof);
                float roughC = wallRough * (wallW + reveal2)
                    + lerp(lerp(0.06, 0.035, k2) + stN * 0.05, 0.45, lit * lerp(0.6, 1.0, cover)) * glass2
                    + 0.35 * frame2;
                roughC = lerp(roughC, lerp(roughC, 0.15, glassFrac), aaF);
                roughC = lerp(roughC, roofRough, kRoof);
                float metalC = frame2 * step(fsel, 0.45) * 0.7;
                float3 emC = glassEm * glass2 + farEm * aaF;
                emC = emC * (1 - kRoof);

                // ---------- lighting ----------
                float3 N = IN.normalWS;
                // normal map perturbation (tangent frame from interpolated attrs)
                float3 Tw = IN.tangentWS.xyz;
                float3 Bt = cross(N, Tw) * IN.tangentWS.w;
                float3 wm = wallNrm * 2 - 1;
                float nScale = lerp(0.6, 0.25, streakAll * wet);
                float3 wallN = normalize(Tw * wm.x * nScale + Bt * wm.y * nScale + N * wm.z);
                float nMask = wallW * (1 - puddle * kRoof);
                N = normalize(lerp(N, wallN, nMask));

                float3 Vn = normalize(P - _WorldSpaceCameraPos);

                float ndl = dot(N, normalize(_SkyLightDir));
                float diff = saturate(ndl) * 0.7 + 0.3 * saturate(1.0 - abs(ndl));
                float3 hemi = lerp(_AmbientGround, _AmbientSky, saturate(N.y * 0.5 + 0.5));
                float3 col3 = albedoC * (hemi + _SkyLightColor * diff) * _SkyBrightness;

                col3 += NkBPointLights(N, P, Vn, albedoC, saturate(roughC), saturate(metalC), 1);

                float3 R = reflect(-Vn, N);
                float3 env = NkHazeColor(R, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                float fres = pow(1 - saturate(dot(N, Vn)), 5) * 0.9 + 0.1;
                col3 += env * fres * (1 - saturate(roughC)) * (0.06 + saturate(metalC) * 0.4) * _SkyBrightness;

                col3 += emC;

                // scene fog (atmo.js fogNodeFn)
                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                col3 = NkFogApply(col3, P, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                { float3 pc = NkPost(col3, IN.screenUV, _SimTime); return float4(pc, 1); }
            }
            ENDHLSL
        }
    }
    Fallback Off
}
