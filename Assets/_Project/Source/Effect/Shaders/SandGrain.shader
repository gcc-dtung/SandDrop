// Sand grain drawn with GPU instancing: one colour per instance (set from SandModelRenderer).
Shader "GameKit/SandGrain"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        CGPROGRAM
        #pragma surface surf SandLambert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling
        #pragma target 3.0

        half _Ambient;

        struct Input
        {
            float3 worldNormal;
        };

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)

        half4 LightingSandLambert(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = saturate(dot(s.Normal, lightDir));
            half light = lerp(_Ambient, 1.0, ndl * atten);
            return half4(s.Albedo * light * _LightColor0.rgb, 1);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = UNITY_ACCESS_INSTANCED_PROP(Props, _Color).rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
