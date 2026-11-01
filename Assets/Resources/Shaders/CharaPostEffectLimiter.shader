// =====================================================================================================
// Limitadores de post efectos sobre los cantantes del live (ver CharaPostEffectLimiter.cs).
//
// Pass 0 (mascara): dibuja los renderers de personaje (capa CHAR) en una RT ARGBFloat:
//                   R = profundidad lineal (eye depth; 0 = no hay personaje),
//                   G = peso (silueta por _CharaColor -> 0, mob -> mobStrength),
//                   B = id de cantante (1..8; 0 = mob).
// Pass 1 (limitador): "Diffusion Limit": en los cantantes visibles se sustituye la imagen por
//                   _CharaNoDiffusion (la pasada DOF/Bloom/PostFilm ejecutada sin Diffusion).
//                   "Saturation Limit": compara la imagen DESPUES (_MainTex) con el MAXIMO LOCAL de la
//                   imagen ANTES de SunShafts/LightShafts/Bloom/PostFilm (_CharaLimitOrig) y comprime con
//                   una curva por tramos ese exceso de luz (luminancia y "blancura" = componente gris).
//                   El desenfoque y lo que los efectos oscurecen no se tocan. Cada cantante se gradua con
//                   SU cobertura de pantalla. Fuera de la mascara la imagen queda identica.
// Pass 2 (cobertura): 1 en el canal del cantante visible y no silueteado de cada pixel; se escribe en
//                   2 RT 256x256 RGBA con mipmaps (cantantes 1-4 / 5-8): el ultimo mip es la fraccion de
//                   pantalla de cada cantante.
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

    // Muestra de la mascara si el personaje es visible (sin escenario delante); 0 en otro caso.
    // x = peso, y = id de cantante (1..8; 0 = mob).
    float2 VisibleChara(float2 uv)
    {
        float3 chara = tex2D(_CharaLimitMask, uv).rgb;
        if (chara.r <= 0.0) return 0.0;
        float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        return chara.r <= scene + max(0.05, scene * 0.02) ? float2(saturate(chara.g), chara.b) : 0.0;
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
            sampler2D _CharaLimitOrig;
            // Cobertura POR CANTANTE (ultimo mip = fraccion de pantalla): RT0 = cantantes 1-4, RT1 = 5-8.
            sampler2D _CharaLimitCoverage0;
            sampler2D _CharaLimitCoverage1;
            // Ultimo mip de las RT de cobertura (256x256 -> 8): su unico texel es la media.
            float _CharaLimitCoverageMip;

            // x = radio (px) del vecindario de referencia, yz = tamaño de texel de la imagen de render.
            float4 _CharaLimitRef;
            // x = limite de luminancia añadida, y = limite de blancura añadida, z = fuerza (0..1).
            float4 _CharaLimitParams;
            // x = ratioAt2L, y = ratioAt4L, z = coverageStart, w = coverageEnd.
            float4 _CharaLimitCurve;
            // x = whiteRatioAt2W, y = whiteRatioAt4W (porcentajes propios de la curva de blancura).
            float4 _CharaLimitWhite;
            // Diffusion Limit: version del frame sin Diffusion.
            // x = activo, y = atenuar en siluetas (peso _CharaColor), z = atenuar en planos abiertos.
            sampler2D _CharaNoDiffusion;
            float4 _CharaLimitDiff;

            // Referencia "antes" = MAXIMO por canal de la imagen original en un vecindario del pixel. El
            // desenfoque (DOF / Diffusion) solo redistribuye la luz que ya habia alrededor: nunca supera
            // ese maximo, asi que no cuenta como luz añadida (si no, las lineas oscuras finas de un
            // personaje desenfocado recuperaban su nitidez y resaltaban). El brillo real de
            // Bloom/PostFilm/SunShafts si lo supera. 17 muestras: centro + 8 direcciones a r/2 y a r.
            float3 ReferenceMax(float2 uv)
            {
                float3 m = tex2D(_CharaLimitOrig, uv).rgb;
                float r = max(_CharaLimitRef.x, 0.0);
                if (r < 0.5) return m;
                float2 t = _CharaLimitRef.yz;
                static const float2 dirs[8] =
                {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(0.7071, 0.7071), float2(-0.7071, 0.7071),
                    float2(0.7071, -0.7071), float2(-0.7071, -0.7071)
                };
                [unroll] for (int k = 0; k < 8; k++)
                {
                    m = max(m, tex2D(_CharaLimitOrig, uv + dirs[k] * t * (r * 0.5)).rgb);
                    m = max(m, tex2D(_CharaLimitOrig, uv + dirs[k] * t * r).rgb);
                }
                return m;
            }

            // Fraccion de pantalla visible del cantante 'id' (1..8). 0 si id = 0 (mob).
            float CoverageOf(float id)
            {
                int k = (int)round(id) - 1;
                if (k < 0 || k > 7) return 0.0;
                float4 lod = float4(0.5, 0.5, 0, _CharaLimitCoverageMip);
                float4 c = k < 4 ? tex2Dlod(_CharaLimitCoverage0, lod) : tex2Dlod(_CharaLimitCoverage1, lod);
                int ch = k - (k < 4 ? 0 : 4);
                return ch == 0 ? c.r : ch == 1 ? c.g : ch == 2 ? c.b : c.a;
            }

            // Fundido POR CANTANTE: 0 si ocupa menos de coverageStart, 1 desde coverageEnd.
            float CoverageFadeOf(float id)
            {
                return smoothstep(_CharaLimitCurve.z, max(_CharaLimitCurve.w, _CharaLimitCurve.z + 1e-5), CoverageOf(id));
            }

            // Saturation Limit: peso del pixel por el fundido de SU cantante. Los mobs (id 0) no tienen
            // cobertura propia y no se gradúan (solo cuenta mobStrength, por defecto 0).
            float SaturationVisible(float2 uv)
            {
                float2 c = VisibleChara(uv);
                if (c.x <= 0.0) return 0.0;
                return c.y > 0.5 ? c.x * CoverageFadeOf(c.y) : c.x;
            }

            // Diffusion Limit: 1 donde hay un CANTANTE visible, con las atenuaciones opcionales.
            float SingerVisible(float2 uv)
            {
                float2 c = VisibleChara(uv);
                if (c.y <= 0.5) return 0.0;
                float v = _CharaLimitDiff.y > 0.5 ? c.x : 1.0;
                if (_CharaLimitDiff.z > 0.5) v *= CoverageFadeOf(c.y);
                return v;
            }

            // Curva por tramos en multiplos del limite (ver CharaPostEffectLimiterSettings):
            //   x <= 0.5L : sin cambio | 0.5L..1.2L : compresion suave | 1.2L..2L : hasta ratio2 |
            //   2L..4L : hasta ratio4 | > 4L : ratio4. Continua y creciente.
            float SoftCap(float x, float limit, float ratio2, float ratio4)
            {
                limit = max(limit, 1e-4);
                float knee = limit * 0.5;
                if (x <= knee) return x;
                float x1 = limit * 1.2;
                float soft = knee + knee * (1.0 - exp(-(min(x, x1) - knee) / knee));
                if (x <= x1) return soft;
                float r1 = soft / x1;
                float x2 = limit * 2.0;
                float x3 = limit * 4.0;
                float r2 = min(ratio2, r1);
                float r3 = min(ratio4, r2);
                if (x <= x2) return x * lerp(r1, r2, (x - x1) / (x2 - x1));
                if (x <= x3) return x * lerp(r2, r3, (x - x2) / (x3 - x2));
                return x * r3;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 d = _CharaLimitRef.yz * 1.5;

                // Peso del Saturation Limit (graduado por la cobertura de cada cantante), con el borde
                // de la silueta suavizado con 5 muestras.
                float mask = (SaturationVisible(uv) * 2.0
                    + SaturationVisible(uv + float2(d.x, 0)) + SaturationVisible(uv - float2(d.x, 0))
                    + SaturationVisible(uv + float2(0, d.y)) + SaturationVisible(uv - float2(0, d.y))) / 6.0;

                float4 fxSample = tex2D(_MainTex, uv);
                float3 fx = fxSample.rgb;

                // Diffusion Limit: neutralizacion completa (no reduccion) en los cantantes visibles.
                if (_CharaLimitDiff.x > 0.5)
                {
                    float singerMask = (SingerVisible(uv) * 2.0
                        + SingerVisible(uv + float2(d.x, 0)) + SingerVisible(uv - float2(d.x, 0))
                        + SingerVisible(uv + float2(0, d.y)) + SingerVisible(uv - float2(0, d.y))) / 6.0;
                    fx = lerp(fx, tex2D(_CharaNoDiffusion, uv).rgb, singerMask);
                }

                // Exceso sobre el maximo local del original: solo la luz que de verdad AÑADEN los efectos.
                float3 excess = max(fx - ReferenceMax(uv), 0.0);
                float3 added = excess;

                // 1) Luminancia añadida
                float lum = dot(added, float3(0.2126, 0.7152, 0.0722));
                if (lum > 1e-5) added *= SoftCap(lum, _CharaLimitParams.x, _CharaLimitCurve.x, _CharaLimitCurve.y) / lum;

                // 2) Blancura añadida (componente gris comun a R,G,B), con sus propios porcentajes.
                float grey = min(added.r, min(added.g, added.b));
                added += SoftCap(grey, _CharaLimitParams.y, _CharaLimitWhite.x, _CharaLimitWhite.y) - grey;

                float3 limited = fx - excess + added;
                float3 result = lerp(fx, limited, saturate(mask * _CharaLimitParams.z));
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
                float2 c = VisibleChara(i.uv);
                if (c.y <= 0.5 || c.x <= 0.0) return 0.0;
                int ch = (int)round(c.y) - 1 - (int)round(_CharaLimitCoverageGroup) * 4;
                return float4(ch == 0 ? 1.0 : 0.0, ch == 1 ? 1.0 : 0.0, ch == 2 ? 1.0 : 0.0, ch == 3 ? 1.0 : 0.0);
            }
            ENDCG
        }
    }
}
