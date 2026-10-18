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
