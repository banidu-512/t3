#include "lib/shared/hash-functions.hlsl"
#include "lib/shared/noise-functions.hlsl"
#include "lib/shared/point.hlsl"
#include "lib/shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Range;
    float Offset;
    float Scale;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;        
StructuredBuffer<Point> Points : t1;        
RWStructuredBuffer<PbrVertex> ResultVertices : u0;   

static float f;
static float3 posA;
static float3 posB;
static float4x4 orientationA;
static float4x4 orientationB;

float3 TransformVector(float3 v) {
    float3 v2 = float3(0,v.yz) * Scale + lerp(posA, posB, f);
    v2 = lerp(mul( float4(v2 - posA, 1), orientationA).xyz + posA,
              mul( float4(v2 - posB, 1), orientationB).xyz + posB,
              f);
    return v2;
} 

float3 TransformDirection(float3 v) {
    float3 v2 = float3(0,v.yz) + lerp(posA, posB, f);
    v2 = lerp(mul( float4(v2 - posA, 0), orientationA).xyz,
              mul( float4(v2 - posB, 0), orientationB).xyz,
              f);
    return v2;
} 



[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;

    uint vertexCount, stride;
    SourceVertices.GetDimensions(vertexCount, stride);
    if(vertexIndex >= vertexCount) {
        return;
    }

    uint pointCount;
    Points.GetDimensions(pointCount, stride);

    float weight = 1;
    float3 offset;

    PbrVertex v = SourceVertices[vertexIndex];
    float3 posInWorld = v.Position;

    float floatIndex = posInWorld.x * (Range) * pointCount * Scale   + Offset * pointCount;

    float aIndex = (int)clamp(floatIndex,0, pointCount-2);
    float bIndex = aIndex + 1;
    f = floatIndex - aIndex; 

    Point pointA = Points[(int)aIndex];
    Point pointB = Points[(int)bIndex];

    orientationA = transpose(quaternion_to_matrix(pointA.rotation));
    orientationB = transpose(quaternion_to_matrix(pointB.rotation));
    posA = pointA.position;
    posB = pointB.position;

    v.Position = TransformVector(v.Position);
    v.Normal = TransformDirection(v.Normal);
    v.Tangent = TransformDirection(v.Tangent);
    v.Bitangent = TransformDirection(v.Bitangent);

    ResultVertices[vertexIndex] = v;
}

