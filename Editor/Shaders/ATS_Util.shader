// Avistry: colour-space conversions and final output.
Shader "Hidden/Avistry/Util"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float _Convert;
    float _Dither;

    // Exact sRGB transfer functions (UnityCG's versions are approximations).
    float3 SRGBToLinear(float3 c)
    {
        c = saturate(c);
        return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
    }

    float3 LinearToSRGB(float3 c)
    {
        c = saturate(c);
        return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    }

    float Hash(float2 p)
    {
        p = frac(p * float2(443.897, 441.423));
        p += dot(p, p.yx + 19.19);
        return frac((p.x + p.y) * p.x);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: imported (hardware-decoded) texture -> raw values. _Convert = 1 re-encodes to sRGB.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                if (_Convert > 0.5) c.rgb = LinearToSRGB(c.rgb);
                return c;
            }
            ENDCG
        }

        // 1: raw values -> preview render texture. _Convert = 1 decodes sRGB so an sRGB RT re-encodes it,
        //    making the RT sample exactly like the original imported sRGB texture.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                c.rgb = saturate(c.rgb);
                if (_Convert > 0.5) c.rgb = SRGBToLinear(c.rgb);
                return c;
            }
            ENDCG
        }

        // 2: final 8-bit output with optional dither (avoids banding in smooth gradients).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float n = Hash(i.uv * _MainTex_TexelSize.zw) - 0.5;
                c.rgb = saturate(c.rgb + n * (_Dither / 255.0));
                return saturate(c);
            }
            ENDCG
        }

        // 3: layer-mask overlay for the canvas: hidden areas in translucent red (like a quick mask).
        //    _MainTex = mask strokes (alpha = coverage); _Convert = 1 when the mask started hidden (coverage reveals).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float c = saturate(tex2D(_MainTex, i.uv).a);
                float hidden = _Convert > 0.5 ? 1 - c : c;
                return float4(1, 0.18, 0.18, hidden * 0.55);
            }
            ENDCG
        }

        // 4: eyedropper. Averages the (2r+1)^2 texels around _Pick.xy (UV of a texel centre), r = _Pick.z (max 12).
        //    _Pick.w = 1 weights by alpha (straight-alpha paint layers: transparent pixels don't count).
        //    Rendered into a 1x1 target; sampling by UV avoids per-API differences in ReadPixels origins.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 _Pick;
            float4 frag(v2f_img i) : SV_Target
            {
                int r = (int)round(clamp(_Pick.z, 0, 12));
                float3 rgb = 0; float wsum = 0, asum = 0, n = 0;
                for (int y = -12; y <= 12; y++)
                for (int x = -12; x <= 12; x++)
                {
                    if (abs(x) > r || abs(y) > r) continue;
                    float4 c = tex2Dlod(_MainTex, float4(_Pick.xy + float2(x, y) * _MainTex_TexelSize.xy, 0, 0));
                    float w = _Pick.w > 0.5 ? c.a : 1;
                    rgb += c.rgb * w; wsum += w; asum += c.a; n += 1;
                }
                return float4(wsum > 1e-5 ? rgb / wsum : 0, asum / max(n, 1));
            }
            ENDCG
        }
    }
}
