// Shared atmosphere functions (port of src/systems/atmosphere/atmo.js hazeColor) —
// included by Sky.shader and Road.shader (wet road reflects the real sky).
// All colors LINEAR. Unity-space directions: sim (x,y,z) == unity (x,y,-z), so the
// azimuth atan2(dir.x, -dir.z) in sim space becomes atan2(dir.x, dir.z) in unity space.
#ifndef NEONKAIDO_ATMO_INCLUDED
#define NEONKAIDO_ATMO_INCLUDED

float3 NkHazeColor(float3 dir, float3 fogColor, float3 midColor, float3 zenithColor,
    float3 glowMagenta, float3 glowOrange, float3 glowBlue, float3 groundHaze, float skyBrightness)
{
    float e = dir.y;
    float h = max(e, 0.0);
    float az = atan2(dir.x, dir.z);
    float3 c = lerp(fogColor, midColor, smoothstep(0.0, 0.2, h));
    c = lerp(c, zenithColor, smoothstep(0.1, 0.7, h));
    float band = exp(-7.0 * h);
    float mag = pow(cos(az - 0.3) * 0.5 + 0.5, 2.0);
    float or1 = pow(cos(az + 1.9) * 0.5 + 0.5, 3.0);
    float or2 = pow(cos(az + 0.6) * 0.5 + 0.5, 10.0);
    float blu = pow(cos(az - 1.05) * 0.5 + 0.5, 8.0) + pow(cos(az + 2.4) * 0.5 + 0.5, 4.0);
    c += glowMagenta * band * (mag * 0.75 + 0.25);
    c += glowOrange * exp(-13.0 * h) * (or1 + or2 * 0.9);
    c += glowBlue * exp(-5.0 * h) * blu;
    c = lerp(c, groundHaze + c * 0.5, 1.0 - smoothstep(-0.3, 0.0, e));
    return c * skyBrightness;
}

#endif

// ---- three.js point-light accumulation (moved from BuildingsCommon so all shaders get it)
float _BLCount; // number of active point lights
float4 _BLPos[384];   // xyz = unity pos, w = range
float4 _BLColor[384]; // rgb = color, w = intensity
// dynamic car lamps: VehicleTraffic pushes the nearest lit cars each frame
// (JS lights.js: 28 tail lights range 7, 14 head lights range 16)
float _CarLCount;
float4 _CarLPos[42];
float4 _CarLColor[42];

float NkBh21(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    float d = dot(q, float3(q.y, q.z, q.x) + 33.33);
    float3 r = q + d;
    return frac((r.x + r.y) * r.z);
}

float NkVnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (f * -2 + 3);
    float a = NkBh21(i), b = NkBh21(i + float2(1, 0)), c = NkBh21(i + float2(0, 1)), d = NkBh21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float NkFbm3(float2 p)
{
    return NkVnoise(p) * 0.55 + NkVnoise(p * 2.13 + 5.2) * 0.3 + NkVnoise(p * 4.7 + 1.7) * 0.15;
}

// axis-aligned rect with AA edges (materials.js aaRect)
float NkAaRect(float2 p, float x0, float x1, float y0, float y1)
{
    float2 w = max(fwidth(p), 1e-5);
    float mx = smoothstep(-w.x, w.x, p.x - x0) * smoothstep(-w.x, w.x, x1 - p.x);
    float my = smoothstep(-w.y, w.y, p.y - y0) * smoothstep(-w.y, w.y, y1 - p.y);
    return mx * my;
}

// three.js point-light accumulation on a standard-ish surface
float3 NkBPointLights(float3 N, float3 P, float3 V, float3 albedo, float rough, float metal, float3 specTint)
{
    float3 acc = 0;
    [loop]
    for (int i = 0; i < 384; i++)
    {
        if (i >= (int)_BLCount) break;
        float3 toL = _BLPos[i].xyz - P;
        float d2 = dot(toL, toL);
        float range = max(_BLPos[i].w, 0.01);
        if (d2 > range * range) continue;
        float d = sqrt(d2);
        float att = 1.0 / max(d * d, 0.01);
        float w = saturate(1.0 - pow(d / range, 4.0));
        att *= w * w;
        float3 L = toL / max(d, 1e-5);
        float ndl = saturate(dot(N, L));
        if (ndl <= 0) continue;
        float3 c = _BLColor[i].rgb * _BLColor[i].w * att;
        acc += c * (ndl * albedo);
        float3 H = normalize(L + V);
        float shin = exp2(max(2.0, -10.0 * rough + 12.0));
        float spec = pow(saturate(dot(N, H)), shin) * (1.0 - rough) * (0.5 + 0.5 * metal);
        acc += c * spec * 0.35 * specTint;
    }
    // car head/tail point lights (lights.js tail/head pools)
    [loop]
    for (int j = 0; j < 42; j++)
    {
        if (j >= (int)_CarLCount) break;
        float3 toL = _CarLPos[j].xyz - P;
        float d2 = dot(toL, toL);
        float range = max(_CarLPos[j].w, 0.01);
        if (d2 > range * range) continue;
        float d = sqrt(d2);
        float att = 1.0 / max(d * d, 0.01);
        float w = saturate(1.0 - pow(d / range, 4.0));
        att *= w * w;
        float3 L = toL / max(d, 1e-5);
        float ndl = saturate(dot(N, L));
        if (ndl <= 0) continue;
        float3 c = _CarLColor[j].rgb * _CarLColor[j].w * att;
        acc += c * (ndl * albedo);
        float3 H = normalize(L + V);
        float shin = exp2(max(2.0, -10.0 * rough + 12.0));
        float spec = pow(saturate(dot(N, H)), shin) * (1.0 - rough) * (0.5 + 0.5 * metal);
        acc += c * spec * 0.35 * specTint;
    }
    return acc;
}

// ---- wet-street neon reflection -------------------------------------------------------------
// The browser gets the signature coloured asphalt from half-res SSR plus a 7-tap vertical smear
// of it (src/engine/PostFX.js + the streak filter). There is no scene colour buffer to march here
// and Road.shader's env reflection is only a sky gradient (NkHazeColor), so the asphalt could
// never reflect signage at all. Instead mirror each point light under the road plane and
// accumulate an anisotropic specular lobe: tight across the street, long along the view
// direction — which is exactly what an SSR streak of a sign looks like on wet tarmac.
//   N     surface normal (up for roads)
//   V     P - camPos (the sign convention NkBPointLights uses); flipped internally
//   wet   0 dry .. 1 soaked
//   gloss 0 rough .. 1 mirror  (puddles push this toward 1)
float3 NkWetStreaks(float3 N, float3 P, float3 V, float wet, float gloss)
{
    if (wet <= 0.01) return float3(0, 0, 0);
    float3 Ve = -V;                                   // toward the eye
    float3 R = reflect(-Ve, N);                       // mirror direction for this point
    float3 T = float3(Ve.x, 0.0, Ve.z);
    float tl = length(T);
    T = tl > 1e-4 ? T / tl : float3(0, 0, 1);         // streak axis on the ground
    float3 B = cross(N, T);                           // across the street
    // tighter across than along -> the lobe stretches toward the viewer, i.e. vertically on screen
    float sigAlong = lerp(0.10, 0.30, wet);
    float sigAcross = lerp(0.020, 0.008, gloss);
    float iAlong = 1.0 / (2.0 * sigAlong * sigAlong);
    float iAcross = 1.0 / (2.0 * sigAcross * sigAcross);
    float3 acc = 0;
    [loop]
    for (int i = 0; i < 384; i++)
    {
        if (i >= (int)_BLCount) break;
        float range = max(_BLPos[i].w, 0.01);
        float3 Lp = _BLPos[i].xyz;
        if (Lp.y <= 0.05) continue;                   // only lights above the plane reflect
        float3 toL = Lp - P;
        float d2 = dot(toL, toL);
        if (d2 > range * range * 9.0) continue;
        float d = sqrt(max(d2, 1e-6));
        float3 L = toL / d;
        float3 e = L - R;
        float a = dot(e, T), b = dot(e, B);
        float lobe = exp(-(a * a * iAlong + b * b * iAcross));
        // A reflection preserves radiance, so this must NOT use the 1/d^2 irradiance falloff
        // NkBPointLights uses — the bright core of a reflected sign is as bright as the sign.
        // The range window supplies locality, plus a mild rolloff for very long grazing shots.
        float w = saturate(1.0 - pow(min(d / range, 1.0), 4.0));
        float att = w * w / (1.0 + d * 0.05);
        acc += _BLColor[i].rgb * _BLColor[i].w * att * lobe;
    }
    // the streak carries at most the wet film's fresnel-weighted energy
    return acc * 0.35 * (0.35 + 0.65 * wet) * (0.25 + 0.75 * gloss);
}

// ---- planar (mirror) reflection of the street ------------------------------------------------
// _NkReflTex is the scene rendered from the camera reflected through the ground plane (see
// NkPlanarReflection.cs). For a point P *on* that plane the mirrored camera sees, in direction of P,
// exactly what reflects off the asphalt — so projecting P with the mirror camera's view-projection
// is a true mirror lookup. This is the port's replacement for the browser's half-res SSR + 7-tap
// vertical wet smear, which the custom shaders could not participate in (they write their own
// tonemapped colour and never fill HDRP's G-buffer, so HDRP's SSR had nothing to march).
// The RT already holds display-referred colour (every NEON KAIDO shader applies AgX itself), so the
// caller must blend the result *after* NkPost, not before.
TEXTURE2D(_NkReflTex);
SAMPLER(sampler_NkReflTex);
float4x4 _NkReflVP;
float _NkGroundY;
float _NkReflOn;     // 1 when the mirror buffer is bound and current
float _NkReflPass;   // 1 while the mirror camera itself is rendering (never read the target)

// rgb = reflected colour, a = blend weight. Zero outside the plane / on dry ground.
// V must point *toward* the eye (dot(N,V) is the usual N·V), matching Road.shader's V.
float4 NkPlanarRefl(float3 P, float3 N, float3 V, float wet, float gloss, float time)
{
    float4 none = float4(0, 0, 0, 0);
    if (wet <= 0.01 || _NkReflOn < 0.5 || _NkReflPass > 0.5) return none;
    float4 clip = mul(_NkReflVP, float4(P, 1.0));
    if (clip.w <= 0.0) return none;
    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return none;
    // the water film ripples: distort more when rough (less gloss), less in a mirror-flat puddle
    float2 rp = P.xz * 1.9 + float2(time * 0.23, time * 0.31);
    float n1 = NkVnoise(rp), n2 = NkVnoise(rp * 2.7 + 13.1);
    float2 ripple = (float2(n1, n2) - 0.5) * lerp(0.035, 0.010, saturate(gloss)) * wet;
    float2 suv = uv + ripple;
    // PostFX.js ssrStreak: 7 Gaussian taps each way, vertically, so wet asphalt reflections stretch
    // on screen (anisotropic microfacets). spread = mix(0.0016, 0.0085, smoothstep(0.02,0.45,rough))
    // * streak * up + 0.0004 — a mirror-sharp planar reflection alone reads as thin hard ribbons,
    // this is what makes the browser's road a long colourful smear.
    float spread = lerp(0.0016, 0.0085, saturate(1.0 - gloss)) + 0.0004;
    float3 acc = SAMPLE_TEXTURE2D(_NkReflTex, sampler_NkReflTex, suv).rgb;
    float wsum = 1.0;
    [unroll]
    for (int k = 1; k <= 7; k++)
    {
        float w = exp(-(k * k) / 24.5);          // 2 * 3.5^2, browser's N*0.5
        float o = spread * (k - 0.5);
        acc += (SAMPLE_TEXTURE2D(_NkReflTex, sampler_NkReflTex, suv + float2(0, o)).rgb
              + SAMPLE_TEXTURE2D(_NkReflTex, sampler_NkReflTex, suv - float2(0, o)).rgb) * w;
        wsum += 2.0 * w;
    }
    float3 refl = acc / wsum;
    // grazing angles reflect more; a puddle (gloss -> 1) is almost a mirror, damp asphalt is not.
    // The floor keeps a floor-facing wet surface reflecting even when the roughness estimate is high
    // (PostFX.js does the same: reflectivity = max(metalRefl, gloss * 0.92), never zero on a floor).
    float fres = pow(1.0 - saturate(dot(N, V)), 5.0);
    float w = (0.05 + 0.95 * fres) * wet * saturate(0.35 + gloss);
    return float4(refl, saturate(w));
}


// ---- scene fog: exact port of atmo.js fogNodeFn (height+distance fog, mist patches,
// ---- neon ground-glow inscatter). patch + rainBoost + fogColor come from the caller
// (shader-local uniforms; avoids declaration-order/redefinition issues).
float _FogDensity;   // 0.0022
float _FogFalloff;   // 0.028
float _FogBase;      // 0
float _FogStart;     // 14
float _HazeDensity;  // 0.0002
float _FogMax;       // 0.985
float _GroundGlow;   // 0.18
float2 _CloudOffset; // drifting mist offset

float3 NkFogApply(float3 col, float3 P, float3 camPos, float patch, float rainBoost, float3 fogColor,
    float3 midColor, float3 zenithColor, float3 glowMagenta, float3 glowOrange, float3 glowBlue,
    float3 groundHaze, float skyBrightness)
{
    float3 V = P - camPos;
    float dist = max(length(V), 1e-3);
    float3 dir = V / dist;
    float x = _FogFalloff * V.y;
    float fx = abs(x) > 0.0005 ? (1.0 - exp(-x)) / x : 1.0 - x * 0.5;
    float gd = _FogDensity * exp(-min(_FogFalloff * (camPos.y - _FogBase), 8.0)) * rainBoost;
    float fd = max(dist - _FogStart, 0.0);
    float optical = gd * fd * fx * patch + _HazeDensity * rainBoost * fd;
    float f = min(1.0 - exp(-optical), _FogMax);
    float3 fdir = normalize(float3(dir.x, dir.y * 0.5, dir.z));
    float3 fcol = NkHazeColor(fdir, fogColor, midColor, zenithColor,
        glowMagenta, glowOrange, glowBlue, groundHaze, skyBrightness);
    fcol *= 1.0 + exp(-max(P.y, 0.0) / 35.0) * _GroundGlow;
    return lerp(col, fcol, saturate(f));
}
