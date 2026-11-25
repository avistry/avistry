Shader "Hidden/Avistry/Paint"
{
    Properties
    {
        _MainTex ("Paint so far", 2D) = "black" {}
        _StrokeTex ("Stroke coverage", 2D) = "black" {}
        _IslandTex ("Island ids", 2D) = "black" {}
        _ClipSet ("Islands in stroke", 2D) = "black" {}
        _BelowTex ("Composite below this layer", 2D) = "black" {}
        _DirTex ("Stroke direction", 2D) = "black" {}
        _StampTex ("Brush alpha", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    sampler2D _StrokeTex;
    sampler2D _IslandTex;
    sampler2D _ClipSet;
    float4 _Color;
    float4 _Params;
    float _Hardness;
    float _LockAlpha;
    float4 _PaintUvBounds;
    float _Tip;
    float _Grain;
    float4 _GrainFreq;
    sampler2D _BelowTex;
    sampler2D _DirTex;
    float4 _BlendParams;
    float4 _MainTex_TexelSize;
    #include "ATS_Stamp.cginc"

    struct dab_in { float4 pos : POSITION; float3 tc : TEXCOORD0; float2 dir : TEXCOORD2; float2 seed : TEXCOORD3; };
    struct dab_v2f { float4 pos : SV_POSITION; float3 tc : TEXCOORD0; float2 uv : TEXCOORD1; float2 dir : TEXCOORD2; float2 seed : TEXCOORD3; };

    dab_v2f vert_dab(dab_in v)
    {
        dab_v2f o;
        o.pos = UnityObjectToClipPos(v.pos);
        o.tc = v.tc;
        o.uv = v.pos.xy;
        o.dir = v.dir;
        o.seed = v.seed;
        return o;
    }


    float IslandAt(float2 uv) { return round(tex2Dlod(_IslandTex, float4(uv, 0, 0)).r * 65535.0); }

    float3 SeenAt(float2 uv)
    {
        float4 p = tex2Dlod(_MainTex, float4(uv, 0, 0));
        float3 b = tex2Dlod(_BelowTex, float4(uv, 0, 0)).rgb;
        return lerp(b, p.rgb, saturate(p.a));
    }

    float CoverageAt(float2 uv)
    {
        if (any(uv < _PaintUvBounds.xy) || any(uv > _PaintUvBounds.zw)) return 0;
        float cov = saturate(tex2D(_StrokeTex, uv).r) * _Params.x;
        if (_Params.z > 0.5)
        {
            float id = round(tex2D(_IslandTex, uv).r * 65535.0) - 1;
            float inside = id < 0 ? 0 : tex2D(_ClipSet, float2((id + 0.5) / max(_Params.w, 1), 0.5)).r;
            cov *= inside > 0.5 ? 1 : 0;
        }
        return cov;
    }

    float4 Deposit(float4 p, float3 colour, float cov)
    {
        if (_LockAlpha > 0.5) return float4(lerp(p.rgb, colour, cov * step(1e-3, p.a)), p.a);
        float outA = cov + p.a * (1 - cov);
        float3 rgb = outA > 1e-5 ? (colour * cov + p.rgb * p.a * (1 - cov)) / outA : colour;
        return float4(rgb, outA);
    }

    bool SameIsland(float2 q, float id0) { return _BlendParams.z < 0.5 || id0 < 0.5 || IslandAt(q) == id0; }

    float Hash(float2 cell)
    {
        uint2 q = (uint2)(int2)cell;
        q = q * uint2(1597334673u, 3812015801u);
        uint n = (q.x ^ q.y) * 1597334673u;
        return n * (1.0 / 4294967295.0);
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        float2 u = f * f * (3 - 2 * f);
        float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float4 frag_dab(dab_v2f i) : SV_Target
    {
        float d = _Tip > 0.5 ? max(abs(i.tc.x), abs(i.tc.y)) : length(i.tc.xy);
        float h = min(_Hardness, 0.97);
        float a = (1 - smoothstep(h, 1, d)) * i.tc.z;
        if (_StampKind > .5) a = StampAlpha(i.tc.xy, i.seed, StampMip(ddx(i.tc.xy), ddy(i.tc.xy))) * i.tc.z;
        if (_Grain > 0)
        {
            float2 p = i.uv * _GrainFreq.xy;
            float n = ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.3) * 0.3 + ValueNoise(p * 4.37 + 41.7) * 0.1;
            a *= lerp(1, smoothstep(0.32, 0.68, n), _Grain);
        }
        return float4(a, a, a, a);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            BlendOp Max
            Blend One One
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_dab
            #pragma fragment frag_dab
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2D(_MainTex, i.uv);
                float cov = CoverageAt(i.uv);
                if (_Params.y > 0.5) return float4(p.rgb, p.a * (1 - cov));
                if (_LockAlpha > 0.5)
                    return float4(lerp(p.rgb, _Color.rgb, cov * step(1e-3, p.a)), p.a);
                float outA = cov + p.a * (1 - cov);
                float3 rgb = outA > 1e-5 ? (_Color.rgb * cov + p.rgb * p.a * (1 - cov)) / outA : _Color.rgb;
                return float4(rgb, outA);
            }
            ENDCG
        }

        Pass
        {
            Blend One OneMinusSrcColor
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_dab
            #pragma fragment frag_dab
            ENDCG
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_dab
            #pragma fragment frag_dir
            float4 frag_dir(dab_v2f i) : SV_Target
            {
                float d = _Tip > 0.5 ? max(abs(i.tc.x), abs(i.tc.y)) : length(i.tc.xy);
                float2 dir = normalize(i.dir + 1e-6);
                float coverage = _StampKind > .5
                    ? StampAlpha(i.tc.xy, i.seed, StampMip(ddx(i.tc.xy), ddy(i.tc.xy))) : (d < 1 ? 1 : 0);
                return float4(dir * 0.5 + 0.5, 0, coverage > 0 ? 1 : 0);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2D(_MainTex, i.uv);
                float cov = CoverageAt(i.uv);
                if (cov <= 0.001) return p;
                float id0 = IslandAt(i.uv);
                float3 sum = SeenAt(i.uv);
                float n = 1;
                float R = max(_BlendParams.x, 1);
                for (int k = 0; k < 40; k++)
                {
                    float rr = sqrt((k + 0.5) / 40.0) * R;
                    float a = k * 2.39996323;
                    float2 q = i.uv + float2(cos(a), sin(a)) * rr * _MainTex_TexelSize.xy;
                    if (!SameIsland(q, id0)) continue;
                    sum += SeenAt(q);
                    n += 1;
                }
                return Deposit(p, lerp(sum / n, _Color.rgb, _BlendParams.y), cov);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2D(_MainTex, i.uv);
                float cov = CoverageAt(i.uv);
                if (cov <= 0.001) return p;
                float4 dt = tex2D(_DirTex, i.uv);
                if (dt.a < 0.5) return p;
                float2 dir = normalize(dt.xy * 2 - 1 + 1e-6);
                float id0 = IslandAt(i.uv);
                float L = max(_BlendParams.x, 1);
                float3 sum = 0;
                float wsum = 0;
                for (int k = 1; k <= 24; k++)
                {
                    float t = k / 24.0;
                    float2 q = i.uv - dir * (t * L) * _MainTex_TexelSize.xy;
                    if (!SameIsland(q, id0)) break;
                    float w = 1.25 - t;
                    sum += SeenAt(q) * w;
                    wsum += w;
                }
                if (wsum <= 0) return p;
                return Deposit(p, sum / wsum, cov);
            }
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_surf
            #pragma fragment frag_surf
            #include "ATS_Surface.cginc"
            ENDCG
        }

        Pass
        {
            Blend One OneMinusSrcColor
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_surf
            #pragma fragment frag_surf
            #include "ATS_Surface.cginc"
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                if (c.g > 0.5) return c;
                float best = 0, any = 0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float4 n = tex2Dlod(_MainTex, float4(i.uv + float2(x, y) * _MainTex_TexelSize.xy, 0, 0));
                    if (n.g > 0.5) { best = max(best, n.r); any = 1; }
                }
                return float4(best, any, 0, best);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float _PreviewStrength;
            float4 frag(v2f_img i) : SV_Target
            {
                return float4(1, 1, 1, tex2D(_MainTex, i.uv).r * _PreviewStrength);
            }
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float coverage = tex2D(_MainTex, i.uv).r;
                if (_Params.z > 0.5)
                {
                    float id = IslandAt(i.uv);
                    float inside = id < 0.5 ? 0 : tex2D(_ClipSet, float2((id - 0.5) / max(_Params.w, 1), 0.5)).r;
                    coverage *= step(0.5, inside);
                }
                return coverage;
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float coverage = tex2D(_MainTex, i.uv).r;
                return float4(coverage, 0, 0, coverage);
            }
            ENDCG
        }
    }
}
