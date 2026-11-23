// See-through sand block for "hold to see inside": same soft lighting as SandSurface, faded by _Alpha with a
// brighter rim so the outer shape still reads while the inner layers show through. Built-in pipeline.
Shader "GameKit/SandGhost"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0,1)) = 0.2
        _Ambient ("Ambient", Range(0,1)) = 0.62
        _Wrap ("Light Wrap", Range(0,1)) = 0.45
        _Saturation ("Saturation Boost", Range(0.5,2)) = 1.25
        _RimStrength ("Rim Light", Range(0,1)) = 0.35
        _RimPower ("Rim Power", Range(1,8)) = 2.5
        _RimAlpha ("Extra Alpha At Rim", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            half _Alpha, _Ambient, _Wrap, _Saturation, _RimStrength, _RimPower, _RimAlpha;

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
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.normal);
                half3 v = normalize(i.viewDir);
                half3 l = normalize(_WorldSpaceLightPos0.xyz);
                half wrapped = saturate((dot(n, l) + _Wrap) / (1.0 + _Wrap));
                half light = lerp(_Ambient, 1.0, wrapped);

                half3 c = _Color.rgb;
                half lum = dot(c, half3(0.299, 0.587, 0.114));
                c = saturate(lerp(lum.xxx, c, _Saturation));

                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                half3 rgb = c * light * _LightColor0.rgb + rim * _RimStrength;
                half alpha = saturate(_Alpha + rim * _RimAlpha * (1.0 - _Alpha));
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
