// Rain — port of src/systems/weather/rain.js. Three camera-following shells of streak quads
// (near big drops / mid / far fine spray), fully analytic in the vertex shader (no compute):
// each drop's position derives from its index + time, wrapping around the camera; the baked
// heightfield respawns drops that fall below a surface. Lit by the 24 nearest pooled lights.
Shader "NEON KAIDO/WxRain"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+30" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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
            float3 _GroundHaze;
            float _SkyBrightness;
            float _Wetness;
            float _Rain;
            float _SimTime;
            float3 _GlowMagenta;
            float3 _GlowOrange;
            float3 _GlowBlue;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"
            #include "Assets/Shaders/WxCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;  // (±0.5, ±0.5)
                float idx : TEXCOORD1;      // drop index
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float3 col : TEXCOORD2;
                float alpha : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float i = IN.idx;
                bool near = i < 32768.0;
                bool mid = i < 196608.0;
                float R = near ? 12.0 : (mid ? 34.0 : 85.0);
                float top = near ? 9.0 : (mid ? 20.0 : 32.0);
                float bot = near ? 5.0 : (mid ? 12.0 : 32.0);
                float A = near ? 0.8 : (mid ? 0.6 : 0.45);
                float fi = i;
                float r1 = WxHash(fi * 1.731 + 0.5), r2 = WxHash(fi * 2.917 + 11.3);
                float r3 = WxHash(fi * 0.613 + 71.9), r4 = WxHash(fi * 5.11 + 3.7);
                float radius = near ? lerp(0.0009, 0.0024, r4 * r4) : lerp(0.0006, 0.0016, r4);
                float term = clamp(radius * 2600.0 + 3.2, 3.5, 9.5);
                float span = top + bot;
                float px = _WxCam.x + (r1 - 0.5) * R * 2.0;
                float pz = _WxCam.z + (r3 - 0.5) * R * 2.0;
                float py = _WxCam.y - bot + WxFmod(r2 * span - term * _WxTime, span);
                // heightfield collision -> analytic respawn above the top
                float h = WxHeight(float2(px, pz));
                if (py < h) py += span;
                // gusts: global multiplier + travelling spatial wave
                float seed = WxHash(fi * 9.13 + 1.1);
                float wave = sin(_WxTime * 0.9 + px * 0.045 + pz * 0.031) * 0.5
                           + sin(_WxTime * 2.3 + pz * 0.13 + seed * 6.28) * 0.2;
                float g = _WxGust * (wave * 0.35 + 1.0);
                float3 flutter = float3(sin(_WxTime * 7.0 + seed * 40.0), 0, sin(_WxTime * 6.1 + seed * 31.0)) * 0.25;
                float3 v = float3(_WxWind.x * g, -term, -_WxWind.y * g) + flutter;
                float3 head = float3(px, py, pz);
                float3 world; float coverage; float dist;
                WxStreak(head, (v - _WxCamVel * 0.7) * 0.055, radius, 1.15, IN.corner, world, coverage, dist);
                float nearFade = smoothstep(0.35, 1.2, dist);
                float hd = length(head.xz - _WxCam.xz) / R;
                float edgeFade = 1.0 - smoothstep(0.75, 1.0, hd);
                float glint = seed * seed * seed * 1.4 + 0.6;
                float lit = WxSoftClamp(WxLightAt(head, normalize(_WxCam - head), 0.75, 0.18) * 0.10, 0.25);
                float3 c = _WxAmbient * 2.5 + float3(0.05, 0.056, 0.072) + lit
                         + float3(0.55, 0.6, 0.75) * _WxLightning * 2.5;
                OUT.worldPos = world;
                OUT.corner = IN.corner;
                OUT.col = c * glint;
                OUT.alpha = pow(coverage, 0.45) * nearFade * edgeFade * A * _WxRain * 1.7;
                OUT.positionCS = NkClip(world);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float cy = IN.corner.y + 0.5;
                float across = 1.0 - abs(IN.corner.x * 2.0);
                float along = smoothstep(0.0, 0.25, cy) * smoothstep(1.0, 0.8, cy) * (cy * 0.5 + 0.5);
                float a = IN.alpha * across * across * along;
                if (a < 0.002) discard;
                float3 col = IN.col;
                col = NkFogApply(col, IN.worldPos, _WorldSpaceCameraPos.xyz, 1.0, _Rain * 0.35 + 0.75,
                    _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz,
                    _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
