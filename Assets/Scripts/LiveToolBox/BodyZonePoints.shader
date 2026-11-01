// Herramienta local de edición (ver BodyZoneEditor.cs).
// Shader mínimo sin iluminación: dibuja los puntos/quad de la nube con color por vértice.
// _ZTest: 4 = LEqual (los puntos tras la piel quedan ocultos), 8 = Always (rayos X).
Shader "Hidden/BodyZonePoints"
{
    Properties
    {
        _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            // el relleno de caras se apoya justo sobre la superficie: evita z-fighting
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
