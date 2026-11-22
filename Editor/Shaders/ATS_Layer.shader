// Avistry: one layer of the stack. All values are the raw texture values
// (gamma/sRGB-encoded for colour textures), so adjustments behave like Photoshop / Clip Studio.
// Formulas follow TextureCraft (MIT OR Apache-2.0): crates/core/src/blend.rs and crates/core/src/layer.rs.
Shader "Hidden/Avistry/Layer"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _GradTex ("Root to tip", 2D) = "black" {}
        _LUT ("Gradient LUT", 2D) = "white" {}
        _PaintTex ("Paint", 2D) = "black" {}
        _ClipTex ("Clip base coverage", 2D) = "white" {}
        _HideTex ("Painted mask", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _MaskTex;
    sampler2D _GradTex;
    sampler2D _LUT;
    sampler2D _PaintTex;
    float4 _Params0;
    float4 _Params1;
    float4 _Color0;
    float _Opacity;
    float _Blend;
    float _InvertMask;
    float _Aspect;              // texture width / height (drawn gradients)
    float4 _MainTex_TexelSize;
    sampler2D _ClipTex;         // Clip to layer below: coverage of the base layer (white = not clipped)
    sampler2D _HideTex;         // painted layer mask strokes (alpha = coverage)
    float _HideMode;            // 0 = no painted mask, 1 = coverage hides, 2 = coverage reveals
    float _CoverageOut;         // 1 = output this layer's coverage instead of the composite (for clipping)
    sampler2D _ImageTex;        // Image layer: the picture
    sampler2D _ImageMaskTex;    // Image layer: optional mask (sampled at the texture UV)
    float4 _ImageST;            // the picture.s own tiling (xy) and offset (zw)
    float _MaskConvert;         // 1 = the mask picture was decoded from sRGB by the GPU: encode it again (default 0 = as sampled)
    float _ImageConvert;        // 1 = the picture was decoded from sRGB by the GPU: encode it again (raw file values)
    float4 _ChannelMask;        // which channels this layer writes (1 = write); alpha takes the paint.s brightness

    float Lum(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }

    float3 ClipColor(float3 c)
    {
        float l = Lum(c);
        float n = min(c.r, min(c.g, c.b));
        float x = max(c.r, max(c.g, c.b));
        if (n < 0) c = l + (c - l) * l / max(l - n, 1e-6);
        if (x > 1) c = l + (c - l) * (1 - l) / max(x - l, 1e-6);
        return c;
    }

    float3 SetLum(float3 c, float l) { return ClipColor(c + (l - Lum(c))); }
    float Sat(float3 c) { return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b)); }

    float3 SetSat(float3 c, float s)
    {
        float mx = max(c.r, max(c.g, c.b));
        float mn = min(c.r, min(c.g, c.b));
        float range = mx - mn;
        return range > 1e-6 ? (c - mn) * s / max(range, 1e-6) : float3(0, 0, 0);
    }

    float Overlay1(float a, float b) { return a < 0.5 ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b); }

    float SoftLight1(float a, float b)
    {
        if (b <= 0.5) return a - (1 - 2 * b) * a * (1 - a);
        float d = a <= 0.25 ? ((16 * a - 12) * a + 4) * a : sqrt(a);
        return a + (2 * b - 1) * (d - a);
    }

    float3 BlendRGB(float3 a, float3 b, int mode)
    {
        if (mode == 1) return a * b;                                         // Multiply
        if (mode == 2) return 1 - (1 - a) * (1 - b);                         // Screen
        if (mode == 3) return float3(Overlay1(a.r, b.r), Overlay1(a.g, b.g), Overlay1(a.b, b.b));
        if (mode == 4) return float3(SoftLight1(a.r, b.r), SoftLight1(a.g, b.g), SoftLight1(a.b, b.b));
        if (mode == 5) return saturate(a + b);                               // Add
        if (mode == 6) return saturate(a - b);                               // Subtract
        if (mode == 7) return SetLum(b, Lum(a));                             // Color
        if (mode == 8) return SetLum(SetSat(b, Sat(a)), Lum(a));             // Hue
        if (mode == 9) return SetLum(SetSat(a, Sat(b)), Lum(a));             // Saturation
        if (mode == 10) return SetLum(a, Lum(b));                            // Luminosity
        if (mode == 11) return min(a, b);                                    // Darken
        if (mode == 12) return max(a, b);                                    // Lighten
        // CLIP STUDIO PAINT modes (CSP documents behaviour, not formulas; these are the standard per-channel ones).
        if (mode == 13) return 1 - saturate((1 - a) / max(b, 1e-4));         // Color burn
        if (mode == 14) return saturate(a + b - 1);                          // Linear burn
        if (mode == 15) return saturate(a / max(1 - b, 1e-4));               // Color dodge
        if (mode == 16) return saturate(a / max(1 - saturate(b * 1.25), 1e-4)); // Glow dodge (stronger dodge; approximation)
        if (mode == 17) return saturate(a + b * 1.35);                       // Add (Glow) (stronger add; approximation)
        if (mode == 18) return float3(Overlay1(b.r, a.r), Overlay1(b.g, a.g), Overlay1(b.b, a.b)); // Hard light = overlay with roles swapped
        if (mode == 19)                                                      // Vivid light: burn below 0.5, dodge above
        {
            float3 burn = 1 - saturate((1 - a) / max(2 * b, 1e-4));
            float3 dodge = saturate(a / max(1 - (2 * b - 1), 1e-4));
            return lerp(burn, dodge, step(0.5, b));
        }
        if (mode == 20) return saturate(a + 2 * b - 1);                      // Linear light
        if (mode == 21) return lerp(min(a, 2 * b), max(a, 2 * b - 1), step(0.5, b)); // Pin light
        if (mode == 22) return step(1, a + b);                               // Hard mix
        if (mode == 23) return abs(a - b);                                   // Difference
        if (mode == 24) return a + b - 2 * a * b;                            // Exclusion
        if (mode == 25) return saturate(a / max(b, 1e-4));                   // Divide
        if (mode == 26) return Lum(a) <= Lum(b) ? a : b;                     // Darker color
        if (mode == 27) return Lum(a) >= Lum(b) ? a : b;                     // Lighter color
        return b;                                                            // Normal
    }

    float3 RGBtoHSL(float3 c)
    {
        float mx = max(c.r, max(c.g, c.b));
        float mn = min(c.r, min(c.g, c.b));
        float l = (mx + mn) * 0.5;
        float d = mx - mn;
        float h = 0, s = 0;
        if (d > 1e-6)
        {
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == c.r) h = (c.g - c.b) / d + (c.g < c.b ? 6 : 0);
            else if (mx == c.g) h = (c.b - c.r) / d + 2;
            else h = (c.r - c.g) / d + 4;
            h /= 6;
        }
        return float3(h, s, l);
    }

    float Hue2RGB(float p, float q, float t)
    {
        t = frac(t);
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    float3 HSLtoRGB(float3 hsl)
    {
        float h = hsl.x, s = hsl.y, l = hsl.z;
        if (s <= 1e-6) return float3(l, l, l);
        float q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        return float3(Hue2RGB(p, q, h + 1.0 / 3), Hue2RGB(p, q, h), Hue2RGB(p, q, h - 1.0 / 3));
    }

    // TextureCraft shift_unit: push toward 1 for positive amounts, toward 0 for negative.
    float ShiftUnit(float x, float a)
    {
        a = clamp(a, -1, 1);
        return a >= 0 ? x + (1 - x) * a : x * (1 + a);
    }

    float4 SampleLUT(float t)
    {
        t = saturate(t);
        return tex2D(_LUT, float2(t * (255.0 / 256.0) + 0.5 / 256.0, 0.5));
    }

    // OKLab; colour values are gamma/sRGB.
    float3 MToLinear(float3 c) { c = saturate(c); return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
    float3 MToGamma(float3 c) { c = saturate(c); return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }
    float3 ToLab(float3 g)
    {
        float3 c = MToLinear(g);
        float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
        float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
        float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
        l = pow(max(l, 0), 1.0 / 3); m = pow(max(m, 0), 1.0 / 3); s = pow(max(s, 0), 1.0 / 3);
        return float3(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                      1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                      0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }
    // OKLab -> linear sRGB, not clamped (also used to test whether a colour is inside the sRGB gamut).
    float3 FromLabLinear(float3 lab)
    {
        float l = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
        float m = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
        float s = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;
        l = l * l * l; m = m * m * m; s = s * s * s;
        return float3(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                      -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                      -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }
    float3 FromLab(float3 lab) { return MToGamma(FromLabLinear(lab)); }

    float4 Finish(float2 uv, float4 src, float3 layerRGB, float layerAlpha)
    {
        float m = tex2D(_MaskTex, uv).r;
        if (_InvertMask > 0.5) m = 1 - m;
        if (_HideMode > 0.5)
        {
            float c = saturate(tex2D(_HideTex, uv).a);
            m *= _HideMode > 1.5 ? c : 1 - c;
        }
        m *= tex2D(_ClipTex, uv).r;
        float w = saturate(_Opacity * m * layerAlpha);
        if (_CoverageOut > 0.5) return float4(w, w, w, 1);
        float3 a = saturate(src.rgb);
        float3 blended = BlendRGB(a, saturate(layerRGB), (int)round(_Blend));
        return float4(lerp(src.rgb, blended, w * _ChannelMask.rgb), lerp(src.a, Lum(saturate(layerRGB)), w * _ChannelMask.a));
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Hue / Saturation / Lightness
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                if (_Params1.x < 0.5)
                {
                    float3 hsl = RGBtoHSL(saturate(src.rgb));
                    bool grey = hsl.y < 1e-4;
                    hsl.x = frac(hsl.x + _Params0.x + 1);
                    if (!grey) hsl.y = ShiftUnit(hsl.y, _Params0.y);
                    hsl.z = ShiftUnit(hsl.z, _Params0.z);
                    return Finish(i.uv, src, HSLtoRGB(hsl), 1);
                }
                // Add / Set colour (OKLCh): greys can be given a hue; the result is kept inside the sRGB gamut.
                // _Params1 = (mode 1 add / 2 set, chroma, colour hue 0..1, use the hue for every pixel)
                float3 lab = ToLab(src.rgb);
                float C = length(lab.yz);
                float h = atan2(lab.z, lab.y);
                float own = smoothstep(0.01, 0.05, C);
                float tH = _Params1.z * 6.28318530718;
                float2 dir = float2(cos(tH), sin(tH));
                if (_Params1.w < 0.5)
                {
                    float2 mixd = lerp(dir, float2(cos(h), sin(h)), own);
                    dir = dot(mixd, mixd) > 1e-6 ? normalize(mixd) : dir;
                }
                float hn = atan2(dir.y, dir.x) + _Params0.x * 6.28318530718;
                float2 hd = float2(cos(hn), sin(hn));
                float C2 = _Params1.x < 1.5 ? C + _Params1.y : _Params1.y;
                float L = ShiftUnit(saturate(lab.x), _Params0.z);
                float lo = 0, hi = max(C2, 0);
                for (int k = 0; k < 10; k++)
                {
                    float3 t = FromLabLinear(float3(L, hd * hi));
                    if (!(any(t < 0) || any(t > 1))) break;      // already inside the gamut
                    float mid = (lo + hi) * 0.5;
                    t = FromLabLinear(float3(L, hd * mid));
                    if (any(t < 0) || any(t > 1)) hi = mid; else lo = mid;
                }
                if (any(FromLabLinear(float3(L, hd * hi)) < 0) || any(FromLabLinear(float3(L, hd * hi)) > 1)) hi = lo;
                return Finish(i.uv, src, FromLab(float3(L, hd * hi)), 1);
            }
            ENDCG
        }

        // 1: Levels
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 t = saturate((saturate(src.rgb) - _Params0.x) / max(_Params0.y - _Params0.x, 1e-5));
                t = pow(t, 1.0 / max(_Params0.z, 1e-3));
                float3 o = _Params1.x + (_Params1.y - _Params1.x) * t;
                return Finish(i.uv, src, o, 1);
            }
            ENDCG
        }

        // 2: Gradient map (luminance -> gradient)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float lum = dot(saturate(src.rgb), float3(0.2126, 0.7152, 0.0722));
                float4 g = SampleLUT(lum);
                return Finish(i.uv, src, g.rgb, g.a);
            }
            ENDCG
        }

        // 3: Solid colour
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                return Finish(i.uv, src, _Color0.rgb, _Color0.a);
            }
            ENDCG
        }

        // 4: Root -> tip gradient. _Params0 = (start, end, reverse, useRawV)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float t = _Params0.w > 0.5 ? 1 - i.uv.y : tex2D(_GradTex, i.uv).r;
                if (_Params0.z > 0.5) t = 1 - t;
                t = saturate((t - _Params0.x) / max(_Params0.y - _Params0.x, 1e-5));
                float4 g = SampleLUT(t);
                return Finish(i.uv, src, g.rgb, g.a);
            }
            ENDCG
        }

        // 5: Paint layer (straight-alpha brush strokes rendered by the compositor)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float4 p = tex2D(_PaintTex, i.uv);
                return Finish(i.uv, src, p.rgb, p.a);
            }
            ENDCG
        }

        // 6: Brightness / Contrast. _Params0 = (brightness -1..1, contrast -1..1)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float k = _Params0.y >= 0 ? 1 / max(1 - _Params0.y, 1e-3) : 1 + _Params0.y;
                float3 c = (saturate(src.rgb) - 0.5) * k + 0.5 + _Params0.x;
                return Finish(i.uv, src, saturate(c), 1);
            }
            ENDCG
        }

        // 7: Color Balance. _Params0.xyz shadows, _Params1.xyz midtones, _Color0.xyz highlights
        //    (x = Cyan-Red, y = Magenta-Green, z = Yellow-Blue, -1..1); _Params0.w = keep brightness
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 a = saturate(src.rgb);
                float l = Lum(a);
                float ws = 1 - smoothstep(0.0, 0.5, l);       // dark tones
                float wh = smoothstep(0.5, 1.0, l);           // bright tones
                float wm = saturate(1 - ws - wh);             // middle tones
                float3 shift = (_Params0.xyz * ws + _Params1.xyz * wm + _Color0.xyz * wh) * 0.5;
                float3 c = saturate(a + shift);
                if (_Params0.w > 0.5) c = SetLum(c, l);
                return Finish(i.uv, src, c, 1);
            }
            ENDCG
        }

        // 8: Posterization. _Params0.x = levels (2..20)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float n = max(_Params0.x, 2) - 1;
                return Finish(i.uv, src, round(saturate(src.rgb) * n) / n, 1);
            }
            ENDCG
        }

        // 9: Reverse Gradient (invert)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                return Finish(i.uv, src, 1 - saturate(src.rgb), 1);
            }
            ENDCG
        }

        // 10: Binarization. _Params0.x = threshold
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float v = step(_Params0.x, Lum(saturate(src.rgb)));
                return Finish(i.uv, src, float3(v, v, v), 1);
            }
            ENDCG
        }

        // 11: Tone Curve. _LUT = 256x1 per-channel lookup (RGB curve then channel curves)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 a = saturate(src.rgb);
                float3 c = float3(SampleLUT(a.r).r, SampleLUT(a.g).g, SampleLUT(a.b).b);
                return Finish(i.uv, src, c, 1);
            }
            ENDCG
        }

        // 12: Drawn gradient (CSP Gradient tool). _Params0 = (start.xy, end.xy) in UV,
        //     _Params1 = (shape 0 linear / 1 circle / 2 ellipse, edge 0 clamp / 1 repeat / 2 reverse repeat / 3 don't draw,
        //                 ellipse flatness, dither)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float2 asp = float2(max(_Aspect, 1e-4), 1);
                float2 s = _Params0.xy * asp;
                float2 d = _Params0.zw * asp - s;
                float len = max(length(d), 1e-5);
                float2 dir = d / len;
                float2 q = i.uv * asp - s;
                int shape = (int)round(_Params1.x);
                float t;
                if (shape == 0) t = dot(q, dir) / len;
                else if (shape == 1) t = length(q) / len;
                else t = length(float2(dot(q, dir) / len, dot(q, float2(-dir.y, dir.x)) / (len * max(_Params1.z, 0.01))));
                int edge = (int)round(_Params1.y);
                float keep = 1;
                if (edge == 1) t = frac(t);
                else if (edge == 2) t = 1 - abs(frac(t * 0.5) * 2 - 1);
                else if (edge == 3) keep = (t >= 0 && t <= 1) ? 1 : 0;
                float4 g = SampleLUT(saturate(t));
                if (_Params1.w > 0.5)
                {
                    // Interleaved gradient noise, +-0.5 of an 8-bit step: hides banding once saved as a PNG.
                    float2 px = floor(i.uv * _MainTex_TexelSize.zw);
                    float n = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))) - 0.5;
                    g.rgb += n / 255.0;
                    g.a = saturate(g.a + n / 255.0);
                }
                return Finish(i.uv, src, g.rgb, g.a * keep);
            }
            ENDCG
        }

        // 13: Image layer (a picture placed on the texture; also flattened shader decals).
        //     _Params0 = (centre.xy, size.xy) in texture UV, _Params1 = (rotation in radians, tiled, symmetry, mask channel or -1).
        //     Same maths as Poiyomi's decals: rotate the UV around the centre, then map the rectangle to 0..1.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float3 ImgLinearToSRGB(float3 c)
            {
                c = saturate(c);
                return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
            }
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float2 uv = i.uv;
                int sym = (int)round(_Params1.z);
                if (sym == 1 || sym == 3) uv.x = abs(uv.x - 0.5) + 0.5;      // copy to the other half (mirrored)
                if (sym == 2 && uv.x < 0.5) uv.x += 0.5;                     // copy to the other half (not mirrored)
                float2 d = uv - _Params0.xy;
                float cs = cos(_Params1.x), sn = sin(_Params1.x);
                d = float2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
                float2 size = _Params0.zw;
                size = float2(abs(size.x) < 1e-5 ? 1e-5 : size.x, abs(size.y) < 1e-5 ? 1e-5 : size.y);
                d = d / size + 0.5;
                if (sym == 3 && i.uv.x < 0.5) d.x = 1 - d.x;                 // lilToon "flip copy"
                float inside = 1;
                if (_Params1.y < 0.5) inside = (d.x >= 0 && d.x <= 1 && d.y >= 0 && d.y <= 1) ? 1 : 0;
                float2 suv = d * _ImageST.xy + _ImageST.zw;
                float4 c = tex2Dgrad(_ImageTex, suv, ddx(i.uv) / size * _ImageST.xy, ddy(i.uv) / size * _ImageST.xy);
                if (_ImageConvert > 0.5) c.rgb = ImgLinearToSRGB(c.rgb);
                c *= _Color0;
                float a = c.a * inside;
                int ch = (int)round(_Params1.w);
                if (ch >= 0)
                {
                    float4 m = tex2D(_ImageMaskTex, i.uv);
                    if (_MaskConvert > 0.5) m.rgb = ImgLinearToSRGB(m.rgb);
                    a *= ch == 0 ? m.r : ch == 1 ? m.g : ch == 2 ? m.b : m.a;
                }
                return Finish(i.uv, src, c.rgb, a);
            }
            ENDCG
        }

        // 14: Colour Match. Moves each colour by the shift of the matched palette colours near it, in OKLab, so the
        //     shading and fine detail stay and only the colours change. _MatchFrom/_MatchTo = palette pairs (OKLab),
        //     _Params0 = (pair count, softness, brightness amount, colour amount), _Params1.x = 1: leave far colours alone,
        //     _Params1.y = 1: keep whites and blacks.
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 _MatchFrom[8];
            float4 _MatchTo[8];
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 lab = ToLab(src.rgb);
                int n = (int)round(_Params0.x);
                float sig = max(_Params0.y, 1e-3);
                float3 shift = 0;
                float wsum = 0;
                for (int k = 0; k < 8; k++)
                {
                    if (k >= n) break;
                    float3 d = lab - _MatchFrom[k].xyz;
                    float wk = exp(-dot(d, d) / (2 * sig * sig));
                    // "Keep whites and blacks": each pair's shift fades to 0 at lightness 0 and 1 (a curve through black, the colour, white).
                    float fL = _MatchFrom[k].x;
                    float factor = saturate(lab.x <= fL ? lab.x / max(fL, 1e-4) : (1 - lab.x) / max(1 - fL, 1e-4));
                    shift += wk * (_MatchTo[k].xyz - _MatchFrom[k].xyz) * lerp(1, factor, _Params1.y);
                    wsum += wk;
                }
                // "Leave other colours alone": a zero shift that wins for colours far from every matched colour.
                float wNone = _Params1.x > 0.5 ? exp(-4.5) : 0;   // as heavy as a palette colour 3 softness away
                shift /= max(wsum + wNone, 1e-6);
                float3 o = lab + float3(shift.x * _Params0.z, shift.yz * _Params0.w);
                return Finish(i.uv, src, FromLab(o), 1);
            }
            ENDCG
        }

        // 15: Recolour part (2 · Colour › By part). The same maths as Recolour.Shift (HSV) / Recolour.ShiftOklab, so the
        //     texture matches the Colour tab's preview. _Params0 = (hue in turns -0.5..0.5, saturation -1..1,
        //     brightness -1..1, 1 = turn the hue in OKLab). _Params1.x = 1 means Tint instead: _Params1 = (1, tint chroma x
        //     strength, tint hue 0..1 turns, 0). The tint's hue and chroma go on the pixel's own lightness (+ brightness),
        //     with chroma faded near black and white and kept inside the sRGB gamut (like pass 0's Add / Set colour).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float3 RgbToHsv(float3 c)
            {
                float4 K = float4(0, -1.0 / 3, 2.0 / 3, -1);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6 * d + 1e-10)), d / (q.x + 1e-10), q.x);
            }
            float3 HsvToRgb(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(1, 2.0 / 3, 1.0 / 3)) * 6 - 3);
                return c.z * lerp(1, saturate(p - 1), c.y);
            }
            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 c = saturate(src.rgb);
                float3 o;
                if (_Params1.x > 0.5)
                {
                    float L = ShiftUnit(saturate(ToLab(c).x), _Params0.z);
                    float fade = smoothstep(0.03, 0.2, L) * (1 - smoothstep(0.93, 1.0, L));
                    float tH = _Params1.z * 6.28318530718;
                    float2 hd = float2(cos(tH), sin(tH));
                    float lo = 0, hi = max(_Params1.y * fade, 0);
                    for (int k = 0; k < 10; k++)
                    {
                        float3 t = FromLabLinear(float3(L, hd * hi));
                        if (!(any(t < 0) || any(t > 1))) break;
                        float mid = (lo + hi) * 0.5;
                        t = FromLabLinear(float3(L, hd * mid));
                        if (any(t < 0) || any(t > 1)) hi = mid; else lo = mid;
                    }
                    if (any(FromLabLinear(float3(L, hd * hi)) < 0) || any(FromLabLinear(float3(L, hd * hi)) > 1)) hi = lo;
                    return Finish(i.uv, src, FromLab(float3(L, hd * hi)), 1);
                }
                if (_Params0.w > 0.5)
                {
                    float3 lab = ToLab(c);
                    float ang = _Params0.x * 6.28318530718;
                    float ca = cos(ang), sa = sin(ang);
                    float k = max(0, 1 + _Params0.y);
                    float2 ab = float2(lab.y * ca - lab.z * sa, lab.y * sa + lab.z * ca) * k;
                    float3 lin = max(FromLabLinear(float3(lab.x, ab)), 0);
                    o = lin <= 0.0031308 ? lin * 12.92 : 1.055 * pow(lin, 1.0 / 2.4) - 0.055;
                    o *= max(0, 1 + _Params0.z);
                }
                else
                {
                    float3 hsv = RgbToHsv(c);
                    hsv.x = frac(hsv.x + _Params0.x + 1);
                    hsv.y = saturate(hsv.y * (1 + _Params0.y));
                    hsv.z = max(0, hsv.z * (1 + _Params0.z));
                    o = HsvToRgb(hsv);
                }
                return Finish(i.uv, src, o, 1);
            }
            ENDCG
        }
    }
}
