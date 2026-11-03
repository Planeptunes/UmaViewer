// =====================================================================================================
// Limitadores de post efectos sobre los cantantes del live (ver CharaPostEffectLimiter.cs).
//
// Pass 0 (mascara): dibuja los renderers de personaje (capa CHAR) en una RT ARGBFloat:
//                   R = profundidad lineal (eye depth; 0 = no hay personaje),
//                   G = peso (silueta por _CharaColor -> 0, mob -> mobStrength),
//                   B = id de cantante (1..8; 0 = mob).
// Pass 1 (limitador): "Diffusion Limit": en los cantantes visibles se sustituye la imagen por
//                   _CharaNoDiffusion (la pasada DOF/Bloom/PostFilm ejecutada sin Diffusion).
//                   "Saturation Limit" (por capas): con las capas Base / B / BPF / Normal y la medida
//                   media de cada transicion sobre el cantante (pass 3), decide un factor por capa y
//                   cantante (Bloom -> PostFilm -> SunShafts/LightShafts, acumulado; solo se reduce,
//                   hasta maxReduction, nunca se apaga) y recompone:
//                     out = Base + kB (B - Base) + kPF (BPF - B) + kS (Normal - BPF)
//                   Fuera de la mascara la imagen queda identica.
// Pass 2 (cobertura): 1 en el canal del cantante visible y no silueteado de cada pixel; se escribe en
//                   2 RT 256x256 RGBA con mipmaps (cantantes 1-4 / 5-8): el ultimo mip es la fraccion de
//                   pantalla de cada cantante.
// Pass 3 (medida de capa): como el pass 2, pero en el canal del cantante va el CAMBIO del pixel entre
//                   _CharaLayerA y _CharaLayerB (luz añadida: luminancia + whiteWeight * gris). El ultimo
//                   mip dividido por la cobertura es el cambio medio sobre ese cantante.
// La capa PostFilm ademas se deja sin limitar en la proporcion en que es un fundido de color uniforme
// de toda la pantalla (dato del timeline, _CharaLimitThresholds.w).
// =====================================================================================================
Shader "Hidden/UmaViewer/CharaPostEffectLimiter"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D_float _CharaLimitMask;
    sampler2D_float _CameraDepthTexture;
    // z = whiteWeight, w = 1 si esta activa la puerta de blancura del resultado (ver TEMPORAL abajo)
    float4 _CharaLimitReduce;
    // TEMPORAL (puerta de blancura; borrar junto con el pass 4 y los usos de _CharaLimitReduce.w si se
    // decide quedarse con el metodo anterior, que solo mide la luz añadida por capa).
    // x/y = blancura de pixel (min(r,g,b)) donde empieza / termina de contar como "quemado",
    // z/w = fraccion de pixeles quemados del cantante a partir de la cual empieza / alcanza el 100 % la puerta.
    float4 _CharaLimitWhite;

    // TEMPORAL: 0 = pixel con color, 1 = pixel lavado a blanco.
    float PixelWhite(float3 c)
    {
        float w = min(saturate(c.r), min(saturate(c.g), saturate(c.b)));
        return smoothstep(_CharaLimitWhite.x, max(_CharaLimitWhite.y, _CharaLimitWhite.x + 1e-4), w);
    }

    // Muestra de la mascara si el personaje es visible (sin escenario delante); 0 en otro caso.
    // x = peso, y = id de cantante (1..8; 0 = mob).
    float2 VisibleChara(float2 uv)
    {
        float3 chara = tex2D(_CharaLimitMask, uv).rgb;
        if (chara.r <= 0.0) return 0.0;
        float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        return chara.r <= scene + max(0.05, scene * 0.02) ? float2(saturate(chara.g), chara.b) : 0.0;
    }

    // Canal 'ch' (0..3) de un RGBA.
    float Channel(float4 c, int ch)
    {
        return ch == 0 ? c.r : ch == 1 ? c.g : ch == 2 ? c.b : c.a;
    }

    // Valor por canal de cantante para los pass 2 y 3: 'v' en el canal del cantante visible y no
    // silueteado del pixel (grupo 0 = cantantes 1-4, grupo 1 = 5-8); 0 en los demas.
    float4 SingerChannel(float2 uv, float group, float v)
    {
        float2 c = VisibleChara(uv);
        if (c.y <= 0.5 || c.x <= 0.0) return 0.0;
        int ch = (int)round(c.y) - 1 - (int)round(group) * 4;
        return float4(ch == 0 ? v : 0.0, ch == 1 ? v : 0.0, ch == 2 ? v : 0.0, ch == 3 ? v : 0.0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        // Pass 0: mascara de personajes
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Por renderer (los pone el C#): peso del limitador e id de cantante.
            float _CharaLimitWeight;
            float _CharaLimitSinger;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float eye : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.eye = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.eye, _CharaLimitWeight, _CharaLimitSinger, 1);
            }
            ENDCG
        }

        // Pass 1: limitador
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            sampler2D _MainTex;
            // Cobertura POR CANTANTE (ultimo mip = fraccion de pantalla): RT0 = cantantes 1-4, RT1 = 5-8.
            sampler2D _CharaLimitCoverage0;
            sampler2D _CharaLimitCoverage1;
            // Ultimo mip de las RT de cobertura/medida (256x256 -> 8): su unico texel es la media.
            float _CharaLimitCoverageMip;

            // yz = tamaño de texel de la imagen de render.
            float4 _CharaLimitRef;
            // z = fuerza (0..1).
            float4 _CharaLimitParams;
            // z = coverageStart, w = coverageEnd.
            float4 _CharaLimitCurve;
            // x = notable, y = excessive, z = maxReduction
            float4 _CharaLimitThresholds;
            // x = 1 si hay capas este frame.
            float4 _CharaLimitLayers;
            // Diffusion Limit: version del frame sin Diffusion.
            // x = activo, y = atenuar en siluetas (peso _CharaColor), z = atenuar en planos abiertos.
            sampler2D _CharaNoDiffusion;
            float4 _CharaLimitDiff;

            // Capas (ver CharaPostEffectLimiter.cs) y medidas de cada transicion (grupo 0 / 1).
            sampler2D _CharaLayerBase;
            sampler2D _CharaLayerBloom;
            sampler2D _CharaLayerBPF;
            sampler2D _CharaLimitMeasureB0;
            sampler2D _CharaLimitMeasureB1;
            sampler2D _CharaLimitMeasurePF0;
            sampler2D _CharaLimitMeasurePF1;
            sampler2D _CharaLimitMeasureS0;
            sampler2D _CharaLimitMeasureS1;
            // TEMPORAL: fraccion de pixeles quemados del resultado final (pass 4), grupo 0 / 1.
            // Texture2D + sampler en linea (solo D3D11): el pass ya agota los 16 samplers con sampler2D.
            Texture2D _CharaLimitMeasureW0;
            Texture2D _CharaLimitMeasureW1;
            SamplerState sampler_linear_clamp;

            // Media (ultimo mip) del canal del cantante k (0..7) en el par de RT (grupo 0 / grupo 1).
            #define SINGER_MEAN(T0, T1, k) Channel((k) < 4 \
                ? tex2Dlod(T0, float4(0.5, 0.5, 0, _CharaLimitCoverageMip)) \
                : tex2Dlod(T1, float4(0.5, 0.5, 0, _CharaLimitCoverageMip)), (k) - ((k) < 4 ? 0 : 4))

            // Fraccion de pantalla visible del cantante 'id' (1..8). 0 si id = 0 (mob).
            float CoverageOf(float id)
            {
                int k = (int)round(id) - 1;
                if (k < 0 || k > 7) return 0.0;
                return SINGER_MEAN(_CharaLimitCoverage0, _CharaLimitCoverage1, k);
            }

            // Fundido POR CANTANTE: 0 si ocupa menos de coverageStart, 1 desde coverageEnd.
            float CoverageFadeOf(float id)
            {
                return smoothstep(_CharaLimitCurve.z, max(_CharaLimitCurve.w, _CharaLimitCurve.z + 1e-5), CoverageOf(id));
            }

            // Saturation Limit: peso de una muestra de la mascara por el fundido de SU cantante. Las capas
            // se miden por cantante: los mobs (id 0) no tienen medida propia y no se limitan.
            float SaturationWeight(float2 c)
            {
                if (c.x <= 0.0 || c.y <= 0.5) return 0.0;
                return c.x * CoverageFadeOf(c.y);
            }

            // Diffusion Limit: 1 donde hay un CANTANTE visible, con las atenuaciones opcionales.
            float SingerVisible(float2 c)
            {
                if (c.y <= 0.5) return 0.0;
                float v = _CharaLimitDiff.y > 0.5 ? c.x : 1.0;
                if (_CharaLimitDiff.z > 0.5) v *= CoverageFadeOf(c.y);
                return v;
            }

            // Factor de una capa segun el cambio ACUMULADO: 1 hasta 'notable'; baja con una rampa suave
            // hasta 1 - maxReduction en 'excessive' y se queda ahi (nunca se apaga).
            float LayerKeep(float acc)
            {
                float N = max(_CharaLimitThresholds.x, 1e-4);
                float E = max(_CharaLimitThresholds.y, N + 1e-4);
                return 1.0 - saturate(_CharaLimitThresholds.z) * smoothstep(N, E, acc);
            }

            // Factores (kB, kPF, kS) del cantante 'id' a partir del cambio medio de cada transicion.
            // Orden: 1) Bloom, 2) PostFilm (sobre el Bloom ya reducido), 3) SunShafts/LightShafts (sobre
            // ambos ya reducidos).
            // TEMPORAL: puerta de blancura del cantante 'id' (1 si esta desactivada): fraccion de sus pixeles ya
            // quemados en el resultado final, de gateStart a gateEnd. La comparten el Saturation Limit y la
            // parte de Diffusion (ambos solo actuan si el cantante esta de verdad lavado a blanco).
            float BurnGate(float id)
            {
                if (_CharaLimitReduce.w <= 0.5) return 1.0;
                int k = (int)round(id) - 1;
                float cov = max(CoverageOf(id), 1e-4);
                float4 w4 = k < 4
                    ? _CharaLimitMeasureW0.SampleLevel(sampler_linear_clamp, float2(0.5, 0.5), _CharaLimitCoverageMip)
                    : _CharaLimitMeasureW1.SampleLevel(sampler_linear_clamp, float2(0.5, 0.5), _CharaLimitCoverageMip);
                float burnt = max(Channel(w4, k - (k < 4 ? 0 : 4)), 0.0) / cov;
                return smoothstep(_CharaLimitWhite.z, max(_CharaLimitWhite.w, _CharaLimitWhite.z + 1e-4), burnt);
            }

            float3 LayerFactors(float id)
            {
                int k = (int)round(id) - 1;
                float cov = max(CoverageOf(id), 1e-4);
                float mB = max(SINGER_MEAN(_CharaLimitMeasureB0, _CharaLimitMeasureB1, k), 0.0) / cov;
                float mPF = max(SINGER_MEAN(_CharaLimitMeasurePF0, _CharaLimitMeasurePF1, k), 0.0) / cov;
                float mS = max(SINGER_MEAN(_CharaLimitMeasureS0, _CharaLimitMeasureS1, k), 0.0) / cov;

                float kB = LayerKeep(mB);
                float acc = kB * mB;
                // PostFilm de color uniforme (fundido de toda la escena, dato del timeline en w): esa
                // parte de la capa no se limita ni cuenta como luz acumulada para la siguiente capa.
                float uniformFilm = saturate(_CharaLimitThresholds.w);
                float kPF = lerp(LayerKeep(acc + mPF), 1.0, uniformFilm);
                acc += kPF * mPF * (1.0 - uniformFilm);
                float kS = LayerKeep(acc + mS);
                return float3(kB, kPF, kS);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 d = _CharaLimitRef.yz * 1.5;

                float4 fxSample = tex2D(_MainTex, uv);
                float3 fx = fxSample.rgb;

                // Muestras de la mascara (centro y 4 vecinas para suavizar el borde). El cantante del pixel
                // es el del centro o, en el borde, el de la muestra vecina que lo tenga.
                float2 c0 = VisibleChara(uv);
                float2 c1 = VisibleChara(uv + float2(d.x, 0));
                float2 c2 = VisibleChara(uv - float2(d.x, 0));
                float2 c3 = VisibleChara(uv + float2(0, d.y));
                float2 c4 = VisibleChara(uv - float2(0, d.y));
                float id = c0.y > 0.5 ? c0.y : max(max(c1.y, c2.y), max(c3.y, c4.y));

                // ETAPA 1 (Diffusion): neutralizacion completa en los cantantes visibles, como cuando era un
                // limitador aparte. Sin puerta de blancura.
                if (_CharaLimitDiff.x > 0.5)
                {
                    float singerMask = (SingerVisible(c0) * 2.0 + SingerVisible(c1) + SingerVisible(c2)
                        + SingerVisible(c3) + SingerVisible(c4)) / 6.0;
                    fx = lerp(fx, tex2D(_CharaNoDiffusion, uv).rgb, singerMask);
                }

                if (_CharaLimitLayers.x < 0.5 || _CharaLimitParams.z <= 0.0)
                    return float4(fx, fxSample.a);

                // ETAPA 2 (Saturation, sobre el resultado de la etapa 1).
                // TEMPORAL (puerta de blancura): actua donde el resultado esta de verdad lavado a blanco
                // (por pixel) y si el cantante tiene una fraccion apreciable de pixeles quemados. Con la
                // puerta desactivada (_CharaLimitReduce.w = 0) vale 1 (comportamiento anterior).
                float burn = 1.0;
                if (_CharaLimitReduce.w > 0.5)
                    burn = id > 0.5 ? BurnGate(id) * PixelWhite(fx) : 0.0;

                // Mascara del Saturation Limit (graduada por la cobertura de cada cantante).
                float mask = (SaturationWeight(c0) * 2.0 + SaturationWeight(c1) + SaturationWeight(c2)
                    + SaturationWeight(c3) + SaturationWeight(c4)) / 6.0;
                if (mask <= 0.0 || id <= 0.5)
                    return float4(fx, fxSample.a);

                float3 k = LayerFactors(id);
                float3 base = tex2D(_CharaLayerBase, uv).rgb;
                float3 bloom = tex2D(_CharaLayerBloom, uv).rgb;
                float3 bpf = tex2D(_CharaLayerBPF, uv).rgb;
                float3 composed = base + k.x * (bloom - base) + k.y * (bpf - bloom) + k.z * (fx - bpf);

                // TEMPORAL (restaurar color): reducir cada capa quita tambien el color que aportaban (bloom azul,
                // tinte del PostFilm) y deja el pelo grisaceo. Se conserva el brillo de 'composed' pero con la
                // saturacion de la capa Base (el color propio del personaje, antes de los efectos).
                // _CharaLimitReduce.x = cuanto (0 = como antes). Para quitarlo: borrar este bloque y el campo
                // chromaRestore del C#.
                const float3 lw = float3(0.2126, 0.7152, 0.0722);
                float Lb = dot(base, lw);
                float Lc = dot(composed, lw);
                float3 chromaTarget = (base - Lb) * min(Lc / max(Lb, 0.05), 3.0);
                composed = max(Lc + lerp(composed - Lc, chromaTarget, saturate(_CharaLimitReduce.x)), 0.0);

                float3 result = lerp(fx, composed, saturate(mask * _CharaLimitParams.z * burn));
                return float4(result, fxSample.a);
            }
            ENDCG
        }

        // Pass 2: cobertura POR CANTANTE. _CharaLimitCoverageGroup = 0 -> canales RGBA = cantantes 1-4;
        // 1 -> cantantes 5-8.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float _CharaLimitCoverageGroup;

            float4 frag(v2f_img i) : SV_Target
            {
                return SingerChannel(i.uv, _CharaLimitCoverageGroup, 1.0);
            }
            ENDCG
        }

        // Pass 3: medida de capa POR CANTANTE (cambio _CharaLayerA -> _CharaLayerB). Mismos canales y
        // grupos que el pass 2.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float _CharaLimitCoverageGroup;
            sampler2D _CharaLayerA;
            sampler2D _CharaLayerB;
            float4 frag(v2f_img i) : SV_Target
            {
                // Solo luz AÑADIDA; el gris comun a R,G,B (lo que lava el color) pesa whiteWeight extra.
                float3 delta = max(tex2D(_CharaLayerB, i.uv).rgb - tex2D(_CharaLayerA, i.uv).rgb, 0.0);
                float m = dot(delta, float3(0.2126, 0.7152, 0.0722))
                    + _CharaLimitReduce.z * min(delta.r, min(delta.g, delta.b));
                return SingerChannel(i.uv, _CharaLimitCoverageGroup, m);
            }
            ENDCG
        }

        // Pass 4 (TEMPORAL, puerta de blancura): como el pass 3, pero mide el RESULTADO (_CharaLayerB =
        // imagen final) en vez del cambio entre capas: en el canal del cantante va 1 si el pixel esta
        // quemado (PixelWhite). El ultimo mip dividido por la cobertura es la fraccion de pixeles quemados.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float _CharaLimitCoverageGroup;
            sampler2D _CharaLayerB;
            float4 frag(v2f_img i) : SV_Target
            {
                return SingerChannel(i.uv, _CharaLimitCoverageGroup, PixelWhite(tex2D(_CharaLayerB, i.uv).rgb));
            }
            ENDCG
        }
    }
}
