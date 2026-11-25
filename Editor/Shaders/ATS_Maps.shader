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
    sampler2D _DepthTex, _DepthTex1, _DepthTex2, _DepthTex3;
    float4 _DepthTex_TexelSize;

    float4 _LightDir;
    float4 _LightRight;
    float4 _LightUp;
    float4 _LightCentre;
    float4 _AoParams;
    float4 _BlurParams;

    float2 LightUV(float3 wp)
    {
        float3 d = wp - _LightCentre.xyz;
        return float2(dot(d, _LightRight.xyz), dot(d, _LightUp.xyz)) * _LightCentre.w + 0.5;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

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
                float3 q = p.xyz + n * _AoParams.x;
                float h = dot(q - _LightCentre.xyz, _LightDir.xyz);
                float2 l = LightUV(q);
                float bias = _AoParams.y * (1.0 + 2.0 * min(sqrt(saturate(1.0 - c * c)) / max(c, 0.05), 6.0));
                float spread = max(_AoParams.z, 1e-6);
                float occ = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 uvk = float4(l + float2((k & 1) ? 0.5 : -0.5, (k & 2) ? 0.5 : -0.5) * _DepthTex_TexelSize.xy, 0, 0);
                    float layers[4] = { tex2Dlod(_DepthTex, uvk).r, tex2Dlod(_DepthTex1, uvk).r, tex2Dlod(_DepthTex2, uvk).r, tex2Dlod(_DepthTex3, uvk).r };
                    float nearest = 1e9;
                    [unroll] for (int j = 0; j < 4; j++) if (layers[j] > h + bias) nearest = layers[j];
                    float dist = nearest - h;
                    if (nearest < 1e8) occ += 1.0 - smoothstep(spread * 0.6, spread, dist);
                }
                return float4(c * (1.0 - occ * 0.25), c, 0, 0);
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
