Shader "Hidden/Avistry/Highlight"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.65, 0.18, 1)
        _Fill ("Fill", Float) = 1
        _PixelsPerPoint ("Pixels per GUI point", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _Color;
        float _Fill, _PixelsPerPoint;
        float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
        fixed4 frag() : SV_Target { return _Color; }

        struct HighlightInput
        {
            float4 position : POSITION;
            float3 normal : NORMAL;
            float4 corner : COLOR;
            float3 outgoing : TEXCOORD0;
            float3 incoming : TEXCOORD1;
        };
        struct HighlightVaryings
        {
            float4 position : SV_POSITION;
            float3 boundary : TEXCOORD0;
            float3 seam : TEXCOORD1;
            float facing : TEXCOORD2;
        };
        float2 edgeKinds(float3 neighbour, float3 view, float facing)
        {
            float size = dot(neighbour, neighbour);
            float silhouette = (dot(neighbour, view) >= 0) != (facing >= 0) ? 1 : 0;
            return float2(max(1 - step(0.25, size), silhouette), step(2.25, size));
        }
        HighlightVaryings highlightVert(HighlightInput v)
        {
            HighlightVaryings o;
            o.position = UnityObjectToClipPos(v.position);
            float3 world = mul(unity_ObjectToWorld, v.position).xyz;
            float3 view = lerp(_WorldSpaceCameraPos - world, UNITY_MATRIX_V[2].xyz, unity_OrthoParams.w);
            float facing = dot(v.normal, view);
            float2 outgoing = edgeKinds(v.outgoing, view, facing);
            float2 incoming = edgeKinds(v.incoming, view, facing);
            float3 smoothNormal = 3 * v.normal
                + v.outgoing * rsqrt(max(1, dot(v.outgoing, v.outgoing)))
                + v.incoming * rsqrt(max(1, dot(v.incoming, v.incoming)));
            o.facing = dot(normalize(smoothNormal), normalize(view));
            float3 c = v.corner.rgb;
            o.boundary = c + c.yzx * (1 - outgoing.x) + c.zxy * (1 - incoming.x);
            o.seam = c + c.yzx * (1 - outgoing.y) + c.zxy * (1 - incoming.y);
            return o;
        }
        float edgeDistance(float3 coords)
        {
            float3 dx = ddx(coords), dy = ddy(coords);
            float3 distanceInPixels = coords * rsqrt(max(dx * dx + dy * dy, 1e-12));
            return min(distanceInPixels.x, min(distanceInPixels.y, distanceInPixels.z)) / _PixelsPerPoint;
        }
        float silhouetteGlow(float facing)
        {
            float slope = length(float2(ddx(facing), ddy(facing))) * _PixelsPerPoint;
            float distance = abs(facing) / max(slope, 1e-5);
            return step(1e-5, slope) * (1 - smoothstep(0.5, 5.0, distance));
        }
        fixed4 visibleHighlight(HighlightVaryings i) : SV_Target
        {
            float edge = edgeDistance(i.boundary), seam = edgeDistance(i.seam);
            float rim = 1 - smoothstep(0.65, 1.8, edge);
            float keyline = 1 - smoothstep(1.65, 3.0, edge);
            float softEdge = 1 - smoothstep(1.0, 5.0, edge);
            float divider = 1 - smoothstep(0.10, 1.05, seam);
            float3 colour = lerp(_Color.rgb, _Color.rgb * 0.19, keyline);
            colour = lerp(colour, lerp(_Color.rgb, 1.0, 0.22), rim);
            float alpha = max(_Fill * 0.045, max(0.13 * softEdge, 0.48 * divider));
            alpha = max(alpha, _Fill * 0.25 * silhouetteGlow(i.facing));
            alpha = lerp(lerp(alpha, 0.63, keyline), 0.98, rim);
            return fixed4(colour, alpha);
        }
        fixed4 hiddenHighlight(HighlightVaryings i) : SV_Target
        {
            float edge = edgeDistance(i.boundary);
            float rim = 1 - smoothstep(0.45, 1.65, edge);
            float halo = 1 - smoothstep(0.0, 4.5, edge);
            float alpha = max(0.13 * silhouetteGlow(i.facing), 0.40 * rim + 0.16 * halo * halo);
            return fixed4(lerp(_Color.rgb, 1.0, 0.16 * rim), alpha);
        }
        ENDCG

        Pass
        {
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex highlightVert
            #pragma fragment visibleHighlight
            ENDCG
        }
        Pass
        {
            ZWrite Off ZTest Greater Cull Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex highlightVert
            #pragma fragment hiddenHighlight
            ENDCG
        }
    }
}
