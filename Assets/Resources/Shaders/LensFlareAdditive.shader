// Aditivo para los lens flares del escenario del live. Cada elemento del asset de flare se dibuja
// como un quad que mira a la cámara y que muestrea su trozo del atlas (las UV se hornean en la
// malla, una por elemento), así que el shader sólo compone la textura con el tinte del elemento.
//
// Blend One One: es un flare aditivo, el fondo del atlas es negro y la forma la da el propio atlas.
// ZTest Always (propiedad _ZTest, por defecto 8): es lo que declara el shader nativo del flare; el
// juego no recorta el flare contra el buffer de profundidad. Con LEqual el halo de las esferas del
// escenario salía cortado por la mitad contra la geometría cercana.
Shader "Hidden/UmaViewer/LensFlareAdditive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // Depth test of the flare quads (UnityEngine.Rendering.CompareFunction): 8 = Always (default,
        // what the native Gallop/3D/Bg/LensFlare shader declares; verified in the game binary),
        // 4 = LEqual (old behaviour, clipped the halo of the balls against neighbouring geometry).
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend One One
        ZWrite Off
        // The native flare shader is `ZTest Always`: the game does not clip a flare against the depth
        // buffer (occlusion is a CPU test, see CustomLensFlare.RuntimeOcclusion / IgnoreLayers). With
        // LEqual and the small depth bias of CustomLensFlare.LateUpdate the halo of the stage balls
        // was cut in half against neighbouring geometry.
        ZTest [_ZTest]
        Cull Off
        Lighting Off

        Pass
        {
            Name "LensFlareAdditive"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            // Sin CBUFFER a propósito: el tinte se escribe por renderer con un
            // MaterialPropertyBlock, y las propiedades declaradas dentro de UnityPerMaterial no
            // admiten sobreescritura por bloque (el SRP Batcher las lee del material).
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = UnityObjectToClipPos(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, input.uv);
                // El atlas guarda las imágenes de flare sobre negro; el alfa es la opacidad de la
                // imagen dentro del atlas. El brillo y el color por elemento van en el tinte.
                fixed3 rgb = tex.rgb * tex.a * input.color.rgb;
                return fixed4(rgb, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
