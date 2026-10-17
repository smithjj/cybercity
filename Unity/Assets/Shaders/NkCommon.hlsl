// Minimal per-draw/per-frame bindings for raw SRP shaders (Unity's custom-pipeline pattern).
// The runtime binds these buffers by NAME for SRP-rendered shaders; SRP batcher requires
// UnityPerDraw to start with unity_ObjectToWorld/unity_WorldToObject.
// Do NOT include SpaceTransforms.hlsl in raw shaders (needs UNITY_MATRIX_M from the
// instancing machinery); do the transforms manually instead.
#ifndef NEONKAIDO_NKCOMMON_INCLUDED
#define NEONKAIDO_NKCOMMON_INCLUDED

CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
CBUFFER_END

float4x4 unity_MatrixVP;

float3 _WorldSpaceCameraPos; // (Common.hlsl does not declare it for raw shaders)

float3 NkWorld(float3 localPos)
{
    return mul(unity_ObjectToWorld, float4(localPos, 1)).xyz;
}

float4 NkClip(float3 worldPos)
{
    return mul(unity_MatrixVP, float4(worldPos, 1));
}

#endif
