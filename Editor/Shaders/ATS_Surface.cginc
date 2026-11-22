// Avistry: 3D brush dabs (included by Hidden/Avistry/Paint passes 6 and 7).
#ifndef ATS_SURFACE_INCLUDED
#define ATS_SURFACE_INCLUDED

float4 _DabPos[32];   // xyz = dab centre (world), w = radius (world)
float4 _DabNrm[32];   // xyz = surface normal at the dab, w = dab strength
float _DabCount;
float4 _BatchMin;     // world-space box around this batch of dabs (skip everything outside it quickly)
float4 _BatchMax;
float _Accumulate;    // 1 = dabs build up inside the batch (airbrush)
float _TwoSided;      // 1 = the material shows both sides of its faces: no facing test

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
    if (any(i.wp < _BatchMin.xyz) || any(i.wp > _BatchMax.xyz)) return float4(0, 1, 0, 0);
    float3 n = normalize(i.n);
    float h = min(_Hardness, 0.97);
    float cov = 0, keep = 1;
    for (int k = 0; k < 32; k++)
    {
        if (k >= _DabCount) break;
        float d = distance(i.wp, _DabPos[k].xyz) / max(_DabPos[k].w, 1e-6);
        if (d >= 1) continue;
        // Skip surfaces facing away from where the brush touched (the back of a thin part, the inside of the mouth).
        float facing = _TwoSided > 0.5 ? 1 : smoothstep(-0.4, 0.0, dot(n, _DabNrm[k].xyz));
        float a = (1 - smoothstep(h, 1, d)) * _DabNrm[k].w * facing;
        cov = max(cov, a);
        keep *= 1 - a;
    }
    float a = _Accumulate > 0.5 ? 1 - keep : cov;
    if (_Grain > 0)
    {
        float2 p = i.uv * _GrainFreq.xy;
        float g = ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.3) * 0.3 + ValueNoise(p * 4.37 + 41.7) * 0.1;
        a *= lerp(1, smoothstep(0.32, 0.68, g), _Grain);
    }
    return float4(a, 1, 0, a);
}
#endif
