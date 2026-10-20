// Crowd character shader — lambert + map + normal + per-material tint, city point lights,
// haze fog, AgX post. Near LOD: skinned renderers, tint via MPB _Tint.
// Far LOD: instanced pose meshes; body verts (uv2.x=0) use _SkinColor, garment verts use _Tint*map.
Shader "NEON KAIDO/Crowd"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _SkinColor("Skin Color", Color) = (0.55, 0.42, 0.34, 1)
        _UseMap("Use Map", Float) = 1
        _Emissive("Emissive", Color) = (0, 0, 0, 0)
        _Rough("Roughness", Float) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+50" }
        Pass
        {
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "Assets/Shaders/NkCommon.hlsl"
            #include "Assets/Shaders/AtmoCommon.hlsl"
            #include "Assets/Shaders/AgX.hlsl"

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _Tint;
            float4 _SkinColor;
            float4 _Emissive;
            float _UseMap;
            float _Rough;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 flag : TEXCOORD1; // far: x = 0 body / 1 garment
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD9;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 tangent : TEXCOORD2;
                float3 bitangent : TEXCOORD3;
                float2 uv : TEXCOORD4;
                float flag : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = NkWorld(IN.positionOS);
                OUT.worldPos = wp;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                OUT.normal = n;
                OUT.tangent = normalize(mul((float3x3)unity_ObjectToWorld, IN.tangentOS.xyz));
                OUT.bitangent = cross(n, OUT.tangent) * IN.tangentOS.w;
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                OUT.flag = IN.flag.x;
                OUT.positionCS = NkClip(wp);
                OUT.screenUV = OUT.positionCS.xy / OUT.positionCS.w * 0.5 + 0.5;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normal);
                float3 albedo;
                if (_UseMap > 0.5)
                {
                    float3 mapCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                    albedo = mapCol * lerp(_SkinColor.rgb, _Tint.rgb, IN.flag);
                }
                else
                {
                    albedo = lerp(_SkinColor.rgb, _Tint.rgb, IN.flag);
                }
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);
                float ndl = saturate(dot(N, _SkyLightDir));
                float3 amb = _AmbientSky * saturate(N.y * 0.5 + 0.5)
                           + _AmbientGround * saturate(-N.y * 0.5 + 0.5);
                // characters.js materials are MeshStandard under a 0.12 hemisphere light plus a
                // 0.3 env map built from horizon neon panels, so the reference crowd always reads
                // as figures. The port's ambient is the city's sky/ground bounce only (~0.04),
                // which left every NPC a black cut-out; restore that fill from the atmosphere.
                float3 env = _GlowMagenta * 0.55 + _GlowOrange * 0.75 + _GlowBlue * 0.35;
                float3 lit = _SkyLightColor * ndl + amb * 6.0 + env * 1.1 * (1.0 - saturate(N.y));
                lit += NkBPointLights(N, IN.worldPos, V, albedo, 0.7, 0.0, albedo);
                float3 col = albedo * lit + _Emissive.rgb * _Emissive.a;
                // Wet sheen: the reference gets a grazing rim off the env map on soaked coats.
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0) * _Wetness;
                col += rim * (amb * 5.0 + env * 1.2) * 0.35;
                col = NkFogApply(col, IN.worldPos, _WorldSpaceCameraPos.xyz, 0.02, 1.0,
                    _FogColor.xyz, _MidColor.xyz, _ZenithColor.xyz, _GlowMagenta.xyz, _GlowOrange.xyz,
                    _GlowBlue.xyz, _GroundHaze.xyz, _SkyBrightness);
                return float4(NkPost(col, IN.screenUV, _SimTime), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
