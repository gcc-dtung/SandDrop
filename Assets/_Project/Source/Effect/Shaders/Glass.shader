// Simple glass for the sand jars: faint tint, bright fresnel rim and a soft highlight. Built-in pipeline.
// Back faces are drawn first so the far wall of the jar shows through the near one.
Shader "GameKit/Glass"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.86, 0.95, 1.0, 0.12)
        _RimColor ("Rim", Color) = (1, 1, 1, 0.65)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Highlight ("Highlight", Range(0, 1)) = 0.6
        _HighlightPower ("Highlight Sharpness", Range(4, 128)) = 48
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"

        fixed4 _Tint, _RimColor;
        half _RimPower, _Highlight, _HighlightPower;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 viewDir : TEXCOORD1;
        };

        v2f vert(appdata_base v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.normal = UnityObjectToWorldNormal(v.normal);
            o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
            return o;
        }

        fixed4 shade(v2f i, float side)
        {
            float3 n = normalize(i.normal) * side;
            float3 v = normalize(i.viewDir);
            half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
            float3 h = normalize(_WorldSpaceLightPos0.xyz + v);
            half spec = pow(saturate(dot(n, h)), _HighlightPower) * _Highlight;
            fixed3 col = _Tint.rgb + _RimColor.rgb * rim + spec;
            half alpha = saturate(_Tint.a + _RimColor.a * rim + spec);
            return fixed4(col, alpha);
        }
        ENDCG

        Pass
        {
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target { fixed4 c = shade(i, -1); c.a *= 0.6; return c; }
            ENDCG
        }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target { return shade(i, 1); }
            ENDCG
        }
    }
}
