// Port of PostFX.js display transform: AgX tone mapping (exposure 0.82) + cinematic grade
// (contrast S-curve, teal/magenta split toning, saturation, blue-lifted blacks, vignette).
// Apply via NkPost(col, screenUV, time) at the END of every fragment shader.

#ifndef NK_AGX
#define NK_AGX

float _AgxExposure; // 0.82

static const float3x3 NK_SRGB_TO_REC2020 = float3x3(
    0.6274, 0.0691, 0.0164,
    0.3293, 0.9195, 0.0880,
    0.0433, 0.0113, 0.8956);
static const float3x3 NK_REC2020_TO_SRGB = float3x3(
    1.6605, -0.1246, -0.0182,
    -0.5876, 1.1329, -0.1006,
    -0.0728, -0.0083, 1.1187);

float3 NkAgxContrast(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    return 15.5 * (x4 * x2) - 40.14 * (x4 * x) + 31.96 * x4
        - 6.868 * (x2 * x) + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

float3 NkAgxTone(float3 color)
{
    const float3x3 inset = float3x3(
        0.856627153315983, 0.137318972929847, 0.11189821299995,
        0.0951212405381588, 0.761241990602591, 0.0767994186031903,
        0.0482516061458583, 0.101439036467562, 0.811302368396859);
    const float3x3 outset = float3x3(
        1.1271005818144368, -0.1413297634984383, -0.14132976349843826,
        -0.11060664309660323, 1.157823702216272, -0.11060664309660294,
        -0.016493938717834573, -0.016493938717834257, 1.2519364065950405);
    const float minEv = -12.47393, maxEv = 4.026069;

    float3 c = color * _AgxExposure;
    c = mul(NK_SRGB_TO_REC2020, c);
    c = mul(inset, c);
    c = max(c, 1e-10);
    c = log2(c);
    c = (c - minEv) / (maxEv - minEv);
    c = saturate(c);
    c = NkAgxContrast(c);
    c = mul(outset, c);
    c = pow(max(float3(0, 0, 0), c), 2.2);
    c = mul(NK_REC2020_TO_SRGB, c);
    return saturate(c);
}

float NkLum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// full display transform; screenUV = fragcoord/size, t = sim time (grain)
float3 NkPost(float3 col, float2 screenUV, float t)
{
    float3 c = NkAgxTone(max(col, 0.0));
    // gentle S-curve contrast
    c = lerp(c, c * c * (3.0 - 2.0 * c), 0.42);
    float lum = NkLum(c);
    // split toning: teal/blue shadows, magenta-pink highlights
    float sh = pow(1.0 - lum, 3.0);
    c += float3(-0.012, 0.012, 0.022) * sh;
    c = lerp(c, c * float3(1.03, 0.97, 1.01), smoothstep(0.35, 0.9, lum));
    // saturation
    float l2 = NkLum(c);
    c = lerp(float3(l2, l2, l2), c, 1.12);
    // lifted, blue-ish blacks
    c += float3(0.0015, 0.0025, 0.006) * (1.0 - c);
    // vignette
    float d = length((screenUV - 0.5) * float2(1.0, 0.85));
    c *= 1.0 - smoothstep(0.3, 0.9, d) * 0.42;
    return max(c, 0.0);
}

#endif

