// Matériau du campus (murs, sols, tables...) : texture plaquée en coordonnées monde, pour que le motif garde
// la même taille quelle que soit la taille du bloc (un sol de 50 m n'étire pas ses carreaux). Créé par CampusMapBuilder.
Shader "Campus/Triplanar"
{
    Properties
    {
        _Color ("Couleur", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _Tiling ("Taille du motif (m)", Float) = 1
        _Glossiness ("Brillance", Range(0, 1)) = 0.3
        [HDR] _EmissionColor ("Émission", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _Tiling;
        half _Glossiness;
        fixed4 _EmissionColor;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 w = abs(IN.worldNormal);
            w /= (w.x + w.y + w.z);
            float3 p = IN.worldPos / max(_Tiling, 0.01);
            fixed4 c = tex2D(_MainTex, p.zy) * w.x + tex2D(_MainTex, p.xz) * w.y + tex2D(_MainTex, p.xy) * w.z;
            o.Albedo = c.rgb * _Color.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Emission = _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
