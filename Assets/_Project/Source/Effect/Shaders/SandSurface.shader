// Smooth sand block: solid colour (from a MaterialPropertyBlock) broken up by fine grain noise and dark specks,
// computed in object space so the pattern turns with the model. Built-in pipeline, no textures.
Shader "GameKit/SandSurface"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1)) = 0.62
        _Wrap ("Light Wrap (soft shading)", Range(0,1)) = 0.45
        _Saturation ("Saturation Boost", Range(0.5,2)) = 1.25
        _RimStrength ("Rim Light", Range(0,1)) = 0.22
        _RimPower ("Rim Power", Range(1,8)) = 3
        _GrainScale ("Grain Scale (per object unit)", Float) = 45
        _GrainStrength ("Grain Strength", Range(0,0.5)) = 0.08
        _SpeckDensity ("Speck Density", Range(0,0.3)) = 0.05
        _SpeckStrength ("Speck Strength", Range(0,0.6)) = 0.14
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf SandLambert vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0

        fixed4 _Color;
        half _Ambient, _Wrap, _Saturation, _RimStrength, _RimPower, _GrainScale, _GrainStrength, _SpeckDensity, _SpeckStrength;

        struct Input
        {
            float3 objPos;
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(lerp(Hash31(i + float3(0,0,0)), Hash31(i + float3(1,0,0)), f.x),
                     lerp(Hash31(i + float3(0,1,0)), Hash31(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(Hash31(i + float3(0,0,1)), Hash31(i + float3(1,0,1)), f.x),
                     lerp(Hash31(i + float3(0,1,1)), Hash31(i + float3(1,1,1)), f.x), f.y), f.z);
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.objPos = v.vertex.xyz;
        }

        // Soft "toy" shading: wrapped diffuse keeps the dark side bright, a light rim outlines the shape.
        half4 LightingSandLambert(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));
            half light = lerp(_Ambient, 1.0, wrapped * atten);
            half rim = pow(1.0 - saturate(dot(s.Normal, viewDir)), _RimPower) * _RimStrength;
            return half4(s.Albedo * light * _LightColor0.rgb + rim, 1);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 p = IN.objPos * _GrainScale;
            // Two octaves: grains + slightly larger clumps.
            float grain = ValueNoise(p) * 0.65 + ValueNoise(p * 0.37 + 11.0) * 0.35;
            float speck = step(1.0 - _SpeckDensity, Hash31(floor(p * 1.7)));
            half shade = 1.0 + (grain - 0.5) * 2.0 * _GrainStrength - speck * _SpeckStrength;
            half3 c = _Color.rgb;
            half lum = dot(c, half3(0.299, 0.587, 0.114));
            c = saturate(lerp(lum.xxx, c, _Saturation));
            o.Albedo = c * shade;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
