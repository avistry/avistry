// TextureCraft procedural alpha/noise formulas (MIT); see Third Party Notices.md.
#ifndef ATS_STAMP_INCLUDED
#define ATS_STAMP_INCLUDED
sampler2D _StampTex;
float4 _StampTex_TexelSize;
float _StampKind;

uint3 StampPcg(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v;
}

float StampHash(int3 p, uint seed)
{
    return StampPcg(uint3((uint)p.x ^ seed * 0x9E3779B9u, (uint)p.y, (uint)p.z)).x * (1.0 / 4294967295.0);
}

float StampGradient(int3 p, uint seed, float3 f)
{
    uint code = StampPcg(uint3((uint)p.x ^ seed * 0x85EBCA6Bu, (uint)p.y, (uint)p.z)).y % 12;
    if (code == 0) return f.x + f.y;
    if (code == 1) return -f.x + f.y;
    if (code == 2) return f.x - f.y;
    if (code == 3) return -f.x - f.y;
    if (code == 4) return f.x + f.z;
    if (code == 5) return -f.x + f.z;
    if (code == 6) return f.x - f.z;
    if (code == 7) return -f.x - f.z;
    if (code == 8) return f.y + f.z;
    if (code == 9) return -f.y + f.z;
    if (code == 10) return f.y - f.z;
    return -f.y - f.z;
}

float StampNoise(float3 p, uint seed, bool gradient)
{
    float3 floorValue = floor(p);
    int3 cell = (int3)clamp(floorValue, -2147483648.0, 2147483520.0);
    cell.x = floorValue.x >= 2147483648.0 ? 2147483647 : cell.x;
    cell.y = floorValue.y >= 2147483648.0 ? 2147483647 : cell.y;
    cell.z = floorValue.z >= 2147483648.0 ? 2147483647 : cell.z;
    float3 f = p - floorValue;
    float3 s = f * f * f * (f * (f * 6 - 15) + 10);
    float value = 0;
    [unroll] for (int z = 0; z <= 1; z++)
    [unroll] for (int y = 0; y <= 1; y++)
    [unroll] for (int x = 0; x <= 1; x++)
    {
        int3 d = int3(x, y, z);
        float3 w = lerp(1 - s, s, (float3)d);
        float v = gradient ? StampGradient(cell + d, seed, f - d) : StampHash(cell + d, seed);
        value += w.x * w.y * w.z * v;
    }
    return gradient ? saturate(value * .5 + .5) : value;
}

float StampFbm(float3 p, uint seed, int octaves, bool valueNoise)
{
    float sum = 0, amplitude = .5, normalization = 0;
    [loop] for (int octave = 0; octave < octaves; octave++)
    {
        sum += amplitude * StampNoise(p, seed + (uint)octave * 31u, !valueNoise);
        normalization += amplitude;
        p = p * 2 + 17.13;
        amplitude *= .5;
    }
    return sum / normalization;
}

float StampMip(float2 localDx, float2 localDy)
{
    float2 dx = localDx * .5 * _StampTex_TexelSize.zw;
    float2 dy = localDy * .5 * _StampTex_TexelSize.zw;
    return max(0, .5 * log2(max(1, max(dot(dx, dx), dot(dy, dy)))));
}

float StampAlpha(float2 p, float2 encodedSeed, float mip)
{
    if (any(abs(p) > 1)) return 0;
    int kind = (int)round(_StampKind) - 1;
    if (kind == 0) return saturate(tex2Dlod(_StampTex, float4(p * .5 + .5, 0, mip)).r);
    uint seed = (uint)round(encodedSeed.x) | ((uint)round(encodedSeed.y) << 16);
    float spatialSeed = (float)((seed ^ (seed >> 16)) & 4095u);
    float r = length(p), h = min(_Hardness, .999);
    float fall = 1 - smoothstep(h, 1, r);
    if (kind == 1) return fall;
    if (kind == 2) return 1 - smoothstep(h, 1, max(abs(p.x), abs(p.y)));
    if (kind == 3) return fall * (StampNoise(float3(p * 14, spatialSeed * .37), seed, false) > .72 ? 1 : 0);
    if (kind == 4) return fall * saturate((StampFbm(float3(p.x * 2.5 + spatialSeed * .13, p.y * 2.5, .5), seed, 5, false) - .42) * 5);
    if (kind == 5) return fall * StampFbm(float3(p * 1.8, spatialSeed * .21), seed, 4, true);
    if (kind == 6)
    {
        float k = abs(cos(atan2(p.y, p.x) * 2.5));
        return 1 - smoothstep(h, 1, r / (.45 + .55 * k * k));
    }
    if (kind == 7) return fall * saturate((StampNoise(float3(p.x * .8, p.y * 18, spatialSeed * .7), seed, false) - .35) * 3);
    return 0;
}
#endif
