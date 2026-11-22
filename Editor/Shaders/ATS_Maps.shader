// Avistry: generated maps (ambient occlusion bake, normal map helpers). Used by MapBaker.cs.
//
// AO works like shadow mapping from many directions: the model is drawn in texture space (vertex = UV) to get a
// world position and normal per texel, then for each direction the occluders are drawn as a "height along the
// direction" map, and every texel checks whether something sits above it along that direction.
// Light-space maps are drawn under GL.LoadOrtho with the vertex at (lx, ly, 0), the same way texture-space maps
// are, so writing and reading agree on every graphics API.
Shader "Hidden/Avistry/Maps"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _PosTex;
    sampler2D _NrmTex;
    sampler2D _DepthTex, _DepthTex1, _DepthTex2, _DepthTex3; // peeled occluder layers, top surface first
    float4 _DepthTex_TexelSize;

    float4 _LightDir;     // xyz = unit direction towards the "sky" sample
    float4 _LightRight;   // xyz = unit vector across the light map
    float4 _LightUp;      // xyz = unit vector up the light map
    float4 _LightCentre;  // xyz = centre of the framed area, w = 1 / (2 * half width)
    float4 _AoParams;     // x = normal offset (world), y = depth bias (world), z = spread (world), w = unused
    float4 _BlurParams;   // x = radius (taps each side), y = step (texels), z = 1 / sigma^2 (world), w = unused

    float2 LightUV(float3 wp)
    {
        float3 d = wp - _LightCentre.xyz;
        return float2(dot(d, _LightRight.xyz), dot(d, _LightUp.xyz)) * _LightCentre.w + 0.5;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: texture-space world position (rgb) and coverage (a). The mesh's vertex is (u, v, 0), TEXCOORD0 the posed
        //    world position.
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            struct vin { float4 pos : POSITION; float3 wp : TEXCOORD0; float3 n : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };
            v2f vert(vin v) { v2f o; o.pos = UnityObjectToClipPos(float4(v.pos.xy, 0, 1)); o.wp = v.wp; return o; }
            float4 frag(v2f i) : SV_Target { return float4(i.wp, 1); }
            ENDCG
        }

        // 1: texture-space world normal (rgb, -1..1) and coverage (a).
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            struct vin { float4 pos : POSITION; float3 wp : TEXCOORD0; float3 n : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            v2f vert(vin v) { v2f o; o.pos = UnityObjectToClipPos(float4(v.pos.xy, 0, 1)); o.n = v.n; return o; }
            float4 frag(v2f i) : SV_Target { return float4(normalize(i.n + 1e-7), 1); }
            ENDCG
        }

        // 2: occluder height along _LightDir, keeping the highest (closest to the sky) surface per light-map texel.
        //    Depth peeling: with _Peel.x = 1, only surfaces lower than the previous layer (_PrevTex) minus _Peel.y
        //    count, so layer k holds the k-th surface from the top. The mesh's vertices are world positions.
        Pass
        {
            BlendOp Max
            Blend One One
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _PrevTex;
            float4 _Peel;
            struct v2f { float4 pos : SV_POSITION; float h : TEXCOORD0; float2 l : TEXCOORD1; };
            v2f vert(float4 p : POSITION)
            {
                v2f o;
                o.l = LightUV(p.xyz);
                o.pos = UnityObjectToClipPos(float4(o.l, 0, 1));
                o.h = dot(p.xyz - _LightCentre.xyz, _LightDir.xyz);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_Peel.x > 0.5 && i.h >= tex2Dlod(_PrevTex, float4(i.l, 0, 0)).r - _Peel.y) discard;
                return i.h;
            }
            ENDCG
        }

        // 3: gather one direction: adds (cos * visible, cos) for every covered texel facing it. Additive.
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2Dlod(_PosTex, float4(i.uv, 0, 0));
                if (p.a < 0.5) return 0;
                float3 n = tex2Dlod(_NrmTex, float4(i.uv, 0, 0)).xyz;
                float c = dot(n, _LightDir.xyz);
                if (c <= 0.0) return 0;
                float3 q = p.xyz + n * _AoParams.x;          // normal offset: step off the surface a little
                float h = dot(q - _LightCentre.xyz, _LightDir.xyz);
                float2 l = LightUV(q);
                // grazing directions need more bias (the surface itself slopes across a light-map texel)
                float bias = _AoParams.y * (1.0 + 2.0 * min(sqrt(saturate(1.0 - c * c)) / max(c, 0.05), 6.0));
                float spread = max(_AoParams.z, 1e-6);
                float occ = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 uvk = float4(l + float2((k & 1) ? 0.5 : -0.5, (k & 2) ? 0.5 : -0.5) * _DepthTex_TexelSize.xy, 0, 0);
                    // The layers go from the top surface down; the nearest occluder above the texel is the lowest
                    // layer that is still above it. (Below 4 layers, layer 4 stands in for it.)
                    float layers[4] = { tex2Dlod(_DepthTex, uvk).r, tex2Dlod(_DepthTex1, uvk).r, tex2Dlod(_DepthTex2, uvk).r, tex2Dlod(_DepthTex3, uvk).r };
                    float nearest = 1e9;
                    [unroll] for (int j = 0; j < 4; j++) if (layers[j] > h + bias) nearest = layers[j];
                    float dist = nearest - h;                 // how far above the texel the occluder is
                    if (nearest < 1e8) occ += 1.0 - smoothstep(spread * 0.6, spread, dist);
                }
                return float4(c * (1.0 - occ * 0.25), c, 0, 0);
            }
            ENDCG
        }

        // 4: resolve + smooth: AO = sum(cos * visible) / sum(cos), blurred with neighbours that are close in 3D
        //    (so it never bleeds across a UV seam into an unrelated part). Out: rgb = AO, a = coverage.
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 p0 = tex2Dlod(_PosTex, float4(i.uv, 0, 0));
                if (p0.a < 0.5) return float4(1, 1, 1, 0);
                int r = (int)_BlurParams.x;
                float stepT = _BlurParams.y;
                float sum = 0, wsum = 0;
                for (int y = -4; y <= 4; y++)
                {
                    if (abs(y) > r) continue;
                    for (int x = -4; x <= 4; x++)
                    {
                        if (abs(x) > r) continue;
                        float2 uv = i.uv + float2(x, y) * stepT * _MainTex_TexelSize.xy;
                        float4 p = tex2Dlod(_PosTex, float4(uv, 0, 0));
                        if (p.a < 0.5) continue;
                        float4 a = tex2Dlod(_MainTex, float4(uv, 0, 0));
                        if (a.g <= 1e-6) continue;
                        float3 dp = p.xyz - p0.xyz;
                        float w = exp(-dot(dp, dp) * _BlurParams.z) * exp(-(x * x + y * y) / max(r * r * 0.5, 0.5));
                        sum += saturate(a.r / a.g) * w;
                        wsum += w;
                    }
                }
                float ao = wsum > 0 ? sum / wsum : 1;
                return float4(ao, ao, ao, 1);
            }
            ENDCG
        }

        // 5: any normal map texture (DXT5nm / BC5 / plain) -> plain tangent-space normal, 0..1 encoded, z rebuilt.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float3 n = UnpackNormal(tex2D(_MainTex, i.uv));
                return float4(normalize(n) * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
}
