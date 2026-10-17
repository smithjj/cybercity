// Shared uniforms + helpers for the weather system (port of src/systems/weather/common.js U).
// All positions in UNITY space (sim (x,y,z) -> unity (x,y,-z)); wind is passed in sim space,
// shaders negate the z component. Included by WxRain / WxSplashes / WxSteam / WxDrips.
#ifndef NEONKAIDO_WX_INCLUDED
#define NEONKAIDO_WX_INCLUDED

float _WxTime;
float _WxDt;
float3 _WxCam;      // unity camera position
float3 _WxCamVel;   // unity camera velocity (m/s)
float2 _WxWind;     // sim-space wind * 2 (x, z); unity velocity z = -y
float _WxGust;
float _WxRain;
float _WxLightning;
float _WxPixK;      // world size of one pixel at 1 m distance
float3 _WxAmbient;  // fog colour * 1.6 + 0.006 (linear)

float4 _WxLPos[24]; // xyz unity pos, w range
float4 _WxLCol[24]; // rgb * intensity
float _WxLCount;

TEXTURE2D(_WxHeight);  SAMPLER(sampler_WxHeight);
TEXTURE2D(_WxPuffTex); SAMPLER(sampler_WxPuffTex);
float2 _WxHOrigin;  // unity (x, z) of texel (0,0) centre
float _WxHScale;    // 1 / span metres

// three.js TSL hash (same as NkTslHash in Koi.shader)
float WxHash(float seed)
{
    uint state = (uint)floor(abs(seed) + 0.5) * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (float)((word >> 22u) ^ word) * (1.0 / 4294967296.0);
}

// floored modulo (behaves for negatives)
float WxFmod(float x, float y) { return x - y * floor(x / y); }

// soft saturation of light colour (keeps hue, avoids blown blobs next to lamps)
float3 WxSoftClamp(float3 c, float k) { return c / (max(max(c.x, c.y), c.z) * k + 1.0); }

// baked top-down world height at unity xz (rain occlusion / splash surface)
float WxHeight(float2 xz)
{
    float2 uv = (xz - _WxHOrigin) * _WxHScale + 0.5;
    float inside = (uv.x > 0.001 && uv.x < 0.999 && uv.y > 0.001 && uv.y < 0.999) ? 1.0 : 0.0;
    return inside ? SAMPLE_TEXTURE2D_LOD(_WxHeight, sampler_WxHeight, uv, 0).r : 0.0;
}

// light contribution at world point p seen from the camera, with forward-scattering phase
// (port of makeLightFn; g/iso per system: rain 0.75/0.18, drips 0.7/0.25, steam 0.55/0.35)
float3 WxLightAt(float3 p, float3 viewDir, float g, float iso)
{
    float3 acc = 0;
    [unroll]
    for (int i = 0; i < 24; i++)
    {
        if (i >= (int)_WxLCount) break;
        float3 L = _WxLPos[i].xyz;
        float3 C = _WxLCol[i].rgb;
        float3 d = L - p;
        float d2 = dot(d, d);
        float dist = sqrt(d2);
        float rr = dist / max(_WxLPos[i].w, 0.01);
        float win = saturate(1.0 - rr * rr * rr * rr);
        float atten = win * win / (d2 + 0.35);
        float cosT = dot(-(d / (dist + 1e-4)), viewDir);
        float g2 = g * g;
        float hg = (1.0 - g2) / pow(max(1.0 + g2 - cosT * 2.0 * g, 1e-3), 1.5);
        acc += C * atten * (hg * 0.35 + iso);
    }
    return acc;
}

// camera-facing quad stretched along a motion vector (thin motion-blurred streak).
// corner: (±0.5, ±0.5) plane corners. Outputs world position, coverage (<1 = sub-pixel squashed).
void WxStreak(float3 head, float3 motion, float radius, float minPixels, float2 corner,
              out float3 world, out float coverage, out float dist)
{
    float3 tail = head - motion;
    float3 axis = head - tail;
    float3 center = (head + tail) * 0.5;
    float3 toCam = _WxCam - center;
    dist = length(toCam);
    float pix = dist * _WxPixK;
    float halfW = max(radius, pix * minPixels);
    coverage = radius / max(halfW, 1e-6);
    float3 side = normalize(cross(axis, toCam) + float3(1e-7, 0, 1e-7));
    float3 axisN = normalize(axis + float3(0, -1e-6, 0));
    world = center + axis * corner.y
          + axisN * (sign(corner.y) * halfW)
          + side * (corner.x * halfW * 2.0);
}

#endif
