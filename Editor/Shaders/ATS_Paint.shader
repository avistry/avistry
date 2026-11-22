// Avistry brush.
// Pass 0: one stroke's coverage. Dabs are GL quads combined with Max, so overlapping dabs in a stroke
//         don't build up (stroke opacity behaves like Photoshop's opacity, not flow).
// Pass 1: lays that stroke onto a paint layer (straight alpha), optionally clipped to a set of UV islands.
// Pass 2: like pass 0 but dabs accumulate (airbrush / build-up).
// Pass 3: stroke direction under each dab (for Smudge).
// Pass 4: Blur stroke. Pass 5: Smudge stroke. Both read what you see (the layers below + this layer's paint) and lay
//         the softened / dragged colour onto the paint layer like a normal stroke, so they stay non-destructive.
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
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    sampler2D _StrokeTex;
    sampler2D _IslandTex;
    sampler2D _ClipSet;
    float4 _Color;      // stroke colour (file values)
    float4 _Params;     // x = opacity, y = erase, z = clip enabled, w = island count
    float _Hardness;
    float _LockAlpha;   // 1 = Lock transparent pixels
    float _Tip;         // 0 = round, 1 = square
    float _Grain;       // 0..1
    float4 _GrainFreq;  // grain cells per UV unit (x, y)
    sampler2D _BelowTex;
    sampler2D _DirTex;
    float4 _BlendParams; // x = blur radius / smudge length in texels, y = amount of brush colour (mixing brush), z = 1: only sample the same UV island
    float4 _MainTex_TexelSize;

    struct dab_in { float4 pos : POSITION; float3 tc : TEXCOORD0; float2 uv : TEXCOORD1; float2 dir : TEXCOORD2; };
    struct dab_v2f { float4 pos : SV_POSITION; float3 tc : TEXCOORD0; float2 uv : TEXCOORD1; float2 dir : TEXCOORD2; };

    dab_v2f vert_dab(dab_in v)
    {
        dab_v2f o;
        o.pos = UnityObjectToClipPos(v.pos);
        o.tc = v.tc;
        o.uv = v.uv;
        o.dir = v.dir;
        return o;
    }


    float IslandAt(float2 uv) { return round(tex2Dlod(_IslandTex, float4(uv, 0, 0)).r * 65535.0); }

    // What you see at uv: this layer's paint (straight alpha) over the composite below it.
    float3 SeenAt(float2 uv)
    {
        float4 p = tex2Dlod(_MainTex, float4(uv, 0, 0));
        float3 b = tex2Dlod(_BelowTex, float4(uv, 0, 0)).rgb;
        return lerp(b, p.rgb, saturate(p.a));
    }

    // Stroke coverage with opacity and the optional island clip (as in pass 1).
    float CoverageAt(float2 uv)
    {
        float cov = saturate(tex2D(_StrokeTex, uv).r) * _Params.x;
        if (_Params.z > 0.5)
        {
            float id = round(tex2D(_IslandTex, uv).r * 65535.0) - 1;
            float inside = id < 0 ? 0 : tex2D(_ClipSet, float2((id + 0.5) / max(_Params.w, 1), 0.5)).r;
            cov *= inside > 0.5 ? 1 : 0;
        }
        return cov;
    }

    // Lay a per-pixel colour onto the paint layer with coverage (same maths as a normal brush stroke).
    float4 Deposit(float4 p, float3 colour, float cov)
    {
        if (_LockAlpha > 0.5) return float4(lerp(p.rgb, colour, cov * step(1e-3, p.a)), p.a);
        float outA = cov + p.a * (1 - cov);
        float3 rgb = outA > 1e-5 ? (colour * cov + p.rgb * p.a * (1 - cov)) / outA : colour;
        return float4(rgb, outA);
    }

    bool SameIsland(float2 q, float id0) { return _BlendParams.z < 0.5 || id0 < 0.5 || IslandAt(q) == id0; }

    // Integer hash: stable for large cell coordinates (no float precision loss).
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
        if (_Grain > 0)
        {
            // Texture-space grain: the same "paper" under every stroke, like chalk or crayon.
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

        // 0: dab coverage, Max
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

        // 1: apply the stroke to the paint layer
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2D(_MainTex, i.uv);
                float cov = saturate(tex2D(_StrokeTex, i.uv).r) * _Params.x;
                if (_Params.z > 0.5)
                {
                    float id = round(tex2D(_IslandTex, i.uv).r * 65535.0) - 1;
                    float inside = id < 0 ? 0 : tex2D(_ClipSet, float2((id + 0.5) / max(_Params.w, 1), 0.5)).r;
                    cov *= inside > 0.5 ? 1 : 0;
                }
                if (_Params.y > 0.5) return float4(p.rgb, p.a * (1 - cov));   // erase
                if (_LockAlpha > 0.5)                                        // Lock transparent pixels: recolour only, keep coverage
                    return float4(lerp(p.rgb, _Color.rgb, cov * step(1e-3, p.a)), p.a);
                float outA = cov + p.a * (1 - cov);
                float3 rgb = outA > 1e-5 ? (_Color.rgb * cov + p.rgb * p.a * (1 - cov)) / outA : _Color.rgb;
                return float4(rgb, outA);
            }
            ENDCG
        }

        // 2: dab coverage, accumulating (dst = a + dst * (1 - a))
        Pass
        {
            Blend One OneMinusSrcColor
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_dab
            #pragma fragment frag_dab
            ENDCG
        }

        // 3: stroke direction (RG = direction * 0.5 + 0.5), later dabs replace earlier ones
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
                return float4(dir * 0.5 + 0.5, 0, d < 1 ? 1 : 0);
            }
            ENDCG
        }

        // 4: Blur. Averages what you see in a disc of radius _BlendParams.x texels (golden-angle taps),
        //    only from the same UV island, and lays the result down with the stroke's coverage.
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
                return Deposit(p, lerp(sum / n, _Color.rgb, _BlendParams.y), cov); // y > 0: colour mixing brush
            }
            ENDCG
        }

        // 5: Smudge (finger tip). Each pixel takes colour from behind it along the brush direction, up to
        //    _BlendParams.x texels back (nearer colour counts more), only from the same UV island.
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
                    if (!SameIsland(q, id0)) break; // don't drag colour across a UV seam
                    float w = 1.25 - t;
                    sum += SeenAt(q) * w;
                    wsum += w;
                }
                if (wsum <= 0) return p;
                return Deposit(p, sum / wsum, cov);
            }
            ENDCG
        }

        // 6: 3D brush coverage, Max. The mesh is drawn in texture space (vertex = UV) carrying its posed world position
        //    and normal, and each texel measures its 3D distance to up to 32 dab centres. R = coverage, G = 1 where the
        //    mesh covers the texel (used by pass 8 to grow coverage into the seam padding).
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

        // 7: 3D brush coverage, accumulating (airbrush / build-up)
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

        // 8: grow 3D-brush coverage by one texel into texels the mesh doesn't cover (seam padding)
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
    }
}
