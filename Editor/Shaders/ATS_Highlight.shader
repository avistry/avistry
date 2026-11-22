// Scene-view highlight for selected / hovered UV islands (world-space mesh, drawn with DrawMeshNow).
// Pass 0: visible parts. Pass 1: parts hidden behind other geometry (drawn faintly so buried strands still show).
Shader "Hidden/Avistry/Highlight"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.55, 0.1, 0.35)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _Color;
        float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
        fixed4 frag() : SV_Target { return _Color; }
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
            ZWrite Off ZTest Greater Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
