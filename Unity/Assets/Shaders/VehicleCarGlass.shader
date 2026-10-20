// Translucent-glass variant of NEON KAIDO/VehicleCar, used only for the kei (N-Box)
// parked/showroom material. Same per-vertex tags and per-car property block, but the
// whole car renders in the transparent queue with alpha blending:
//   - opaque body/interior verts output alpha 1 (they still write depth)
//   - tag 1 (glass) verts output _GlassAlpha and blend over the already-drawn interior
// The dumper emits the glass triangles LAST within each car mesh (see glassLast in
// tools/dump-vehicles.mjs), so one draw can show the interior through the tinted glass
// without a second pass or a per-car sort.
Shader "NEON KAIDO/VehicleCarGlass"
{
    Properties
    {
        _GlassAlpha ("Glass Alpha", Range(0, 1)) = 0.58
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

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

            CBUFFER_START(UnityPerMaterial)
                float4 _PaintColor;
                float _Spin;
                float3 _LightLv;
                float _GlassAlpha;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 nor : NORMAL; float2 uv : TEXCOORD0;
                float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; };
            struct Varyings { float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD9; float3 worldPos : TEXCOORD0; float3 nor : TEXCOORD2;
                float tag : TEXCOORD3; float wheel : TEXCOORD4; float3 localPos : TEXCOORD5; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float4 lp = IN.pos;
                if (IN.uv1.y > 0.5)
                {
                    // spin wheel verts around their axle (axis = car-local X through (axleY, axleZ))
                    float a = _Spin;
                    float ca = cos(a), sa = sin(a);
                    float dy = lp.y - IN.uv2.x, dz = lp.z - IN.uv2.y;
                    lp.y = IN.uv2.x + dy * ca - dz * sa;
                    lp.z = IN.uv2.y + dy * sa + dz * ca;
                }
                // Headlamp plates sit proud of the fascia by 4-18 mm; nudge as in the opaque shader.
                if (IN.uv1.x > 2.5 && IN.uv1.x < 3.5) lp.z += 0.025;
                float3 wp = NkWorld(lp.xyz);
                o.posCS = NkClip(wp);
                o.screenUV = o.posCS.xy / o.posCS.w * 0.5 + 0.5;
                o.worldPos = wp;
                o.localPos = lp.xyz;
                o.nor = normalize(mul((float3x3)unity_ObjectToWorld, IN.nor));
                o.tag = IN.uv1.x;
                o.wheel = IN.uv1.y;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float3 N = normalize(IN.nor);
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float wet = clamp(_Wetness, 0, 1);
                float tag = IN.tag;

                // base albedo by part
                float n = NkVnoise(wp.xy * 0.4 + wp.z * 0.4) * 0.5 + 0.5;
                float3 c;
                float oRough; float oMetal;
                if (tag < 0.5) {          // paint
                    c = _PaintColor.rgb * lerp(1.0, 0.72, wet);
                    oRough = clamp(lerp(0.28, 0.07, wet * lerp(0.55, 1.0, n)), 0.03, 1);
                    oMetal = 0.25;
                } else if (tag < 1.5) {   // glass (dark blue-green tint)
                    c = float3(0.018, 0.028, 0.034);
                    oRough = 0.06; oMetal = 0.0;
                } else if (tag < 2.5) {   // chrome / trim / rubber / interior
                    c = float3(0.09, 0.09, 0.095) * (0.6 + n * 0.5);
                    oRough = 0.35; oMetal = 0.7;
                } else if (tag < 3.5) {   // headlamp (emissive when lit)
                    c = float3(0.1, 0.1, 0.1);
                    oRough = 0.2; oMetal = 0.4;
                } else {                  // taillight
                    c = float3(0.12, 0.01, 0.01);
                    oRough = 0.2; oMetal = 0.3;
                }

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
                float envMix = exp(-oRough * 9.0);
                float3 oCol = c * (_AmbientSky.xyz + _AmbientGround.xyz * 0.5)
                           + _SkyLightColor.xyz * (ndl * c + spec * 0.25)
                           + env * envMix * (0.35 + fres * 0.65);

                // Lamps (same ranges as the opaque shader).
                float isHead = step(2.5, tag) * (1.0 - step(3.5, tag));
                float isTail = step(3.5, tag);
                float tailLvl = isTail * (_LightLv.y * 3.2 + _LightLv.z * 11.0);
                float seg = smoothstep(0.0, 0.25, frac(IN.localPos.x * 55.0))
                          * smoothstep(1.0, 0.75, frac(IN.localPos.x * 55.0));
                float led = seg * 0.35 + 0.65;
                oCol += float3(0.86, 0.93, 1.0) * (isHead * _LightLv.x * 38.0);
                oCol += float3(1.0, 0.035, 0.02) * (tailLvl * led)
                      + float3(0.5, 0.02, 0.02) * pow(tailLvl * led * 0.08, 2.0);

                oCol += NkBPointLights(N, wp, V, c, oRough, 0, 1);

                float patch = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, wp.xz * 0.0035 + _CloudOffset * 4.0).r * 0.9 + 0.55;
                oCol = NkFogApply(oCol, wp, _WorldSpaceCameraPos.xyz, patch, _Rain * 0.35 + 0.75, _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz, _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);

                float alpha = (tag > 0.5 && tag < 1.5) ? saturate(_GlassAlpha) : 1.0;
                return half4(NkPost(oCol, IN.screenUV, _SimTime), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
