#ifndef ATS_SURFACE_INCLUDED
#define ATS_SURFACE_INCLUDED

float4 _DabPos[32];
float4 _DabNrm[32];
float4 _DabTan[32];
float4 _DabStamp[32];
float _DabCount, _GroupDabs;
float4 _DabGroupMin[4], _DabGroupMax[4];
float4 _BatchMin;
float4 _BatchMax;
float _Accumulate;
float _TwoSided;

struct surf_in { float4 pos : POSITION; float3 wp : TEXCOORD0; float3 n : NORMAL; };
struct surf_v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };

surf_v2f vert_surf(surf_in v)
{
    surf_v2f o;
    o.pos = UnityObjectToClipPos(float4(v.pos.xy, 0, 1));
    o.wp = v.wp;
    o.n = v.n;
    o.uv = v.pos.xy;
    return o;
}

float4 frag_surf(surf_v2f i) : SV_Target
{
    float3 worldDx = ddx(i.wp), worldDy = ddy(i.wp);
    if (any(i.wp < _BatchMin.xyz) || any(i.wp > _BatchMax.xyz)) return float4(0, 1, 0, 0);
    float3 n = normalize(i.n);
    float h = min(_Hardness, 0.97);
    float cov = 0, keep = 1;
    int groupSize = _GroupDabs > 0.5 ? 8 : 32;
    [loop] for (int first = 0; first < _DabCount; first += groupSize)
    {
        if (_GroupDabs > 0.5 && (any(i.wp < _DabGroupMin[first / 8].xyz) || any(i.wp > _DabGroupMax[first / 8].xyz))) continue;
        for (int k = first; k < first + groupSize; k++)
        {
            if (k >= _DabCount) break;
            float facing = _TwoSided > 0.5 ? 1 : smoothstep(-0.4, 0.0, dot(n, _DabNrm[k].xyz));
            float a;
            if (_StampKind > .5)
            {
                float3 delta = (i.wp - _DabPos[k].xyz) / max(_DabPos[k].w, 1e-6);
                float3 normal = normalize(_DabNrm[k].xyz);
                if (abs(dot(delta, normal)) > _DabStamp[k].w) continue;
                float3 tangent = normalize(_DabTan[k].xyz);
                float3 across = normalize(cross(normal, tangent));
                float roundness = max(_DabStamp[k].z, .05);
                float2 local = float2(dot(delta, tangent), dot(delta, across) / roundness);
                float2 localDx = float2(dot(worldDx, tangent), dot(worldDx, across) / roundness) / max(_DabPos[k].w, 1e-6);
                float2 localDy = float2(dot(worldDy, tangent), dot(worldDy, across) / roundness) / max(_DabPos[k].w, 1e-6);
                a = StampAlpha(local, _DabStamp[k].xy, StampMip(localDx, localDy)) * _DabNrm[k].w * facing;
            }
            else
            {
                float d = distance(i.wp, _DabPos[k].xyz) / max(_DabPos[k].w, 1e-6);
                if (d >= 1) continue;
                a = (1 - smoothstep(h, 1, d)) * _DabNrm[k].w * facing;
            }
            cov = max(cov, a);
            keep *= 1 - a;
        }
    }
    float a = _Accumulate > 0.5 ? 1 - keep : cov;
    if (_Grain > 0 && a != 0)
    {
        float2 p = i.uv * _GrainFreq.xy;
        float g = ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.3) * 0.3 + ValueNoise(p * 4.37 + 41.7) * 0.1;
        a *= lerp(1, smoothstep(0.32, 0.68, g), _Grain);
    }
    return float4(a, 1, 0, a);
}
#endif
