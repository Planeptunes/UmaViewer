using UnityEngine;

namespace Gallop
{
    public static class GraphicSettings
    {
  
        //* 图像效果能力开关
        //* 当前项目固定使用最高画质,因此静态初始化时全部开启
        public static bool IsGlobalFog;
        public static bool IsSunShaft;
        public static bool IsTiltShift;
        public static bool IsIndirectLightShaft;

        public static bool IsBloom;
        public static bool IsDOF;
        public static bool IsScreenOverlay;

        // cy拼错单词了,字段名就是 Diffution,不要改成 Diffusion
        public static bool IsDiffution;

        public static bool IsRadialBlur;
        public static bool IsFluctuation;
        public static bool IsLensDistortion;
        public static bool IsChromaticAberration;
        public static bool IsColorCorrection;
        public static bool IsColorGrading;
        public static bool IsToneCurve;
        public static bool IsExposure;
        public static bool IsTransmittedLight;
        public static bool IsRainSplash;

        // Gallop.GraphicSettings lightmap state (instance fields +0xd8..+0x10c of the game's object), seeded by
        // SetDefaultLightmap @0x7ffd58e9ba70 from the class static block (cctor @0x7ffd58e9d360). The default values were
        // read from the JP binary: lightmap colour (1,1,1,1) @0x7ffd5a640e60, shadow colour (0.25,0.25,0.25,1)
        // @0x7ffd5a8bbc30, density colour (0.5,0.5,0.5,1) @0x7ffd5a640ed0, constants 2.0 @0x7ffd5a640d78 and 1.0 @0x7ffd5a640d6c.
        public static Color LightmapColor = new Color(1f, 1f, 1f, 1f);                  // +0xd8
        public static Color LightmapShadowColor = new Color(0.25f, 0.25f, 0.25f, 1f);   // +0xe8
        public static float LightmapDensity = 1f;                                       // +0xf8
        public static Color LightmapDensityColor = new Color(0.5f, 0.5f, 0.5f, 1f);     // +0xfc
        public static float LightmapMinDensity = 0f;                                    // +0x10c

        // GraphicSettings.SetDefaultLightmap @0x7ffd58e9ba70: instance fields from the static defaults, then UpdateLightmap.
        public static void SetDefaultLightmap()
        {
            LightmapColor = new Color(1f, 1f, 1f, 1f);
            LightmapShadowColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            LightmapDensity = 1f;
            LightmapDensityColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            LightmapMinDensity = 0f;
            UpdateLightmap();
        }

        // GraphicSettings.UpdateLightmap @0x7ffd58e9d050: publishes the four _Global_Lightmap* colours.
        public static void UpdateLightmap()
        {
            const float k = 2f; // DAT_7ffd5a640d78 (multiplies only the red channel; the other channels use x + x)
            Shader.SetGlobalColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._Global_LightmapColor), LightmapColor);
            Shader.SetGlobalColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._Global_LightmapShadowColor), LightmapShadowColor);
            float addRate = Mathf.Max(LightmapMinDensity, 1f - LightmapDensity); // DAT_7ffd5a640d6c (1.0) - density
            Color c = LightmapDensityColor;
            Shader.SetGlobalColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._Global_LightmapDensityAddColor),
                new Color(c.r * k * addRate, (c.g + c.g) * addRate, (c.b + c.b) * addRate, (c.a + c.a) * addRate));
            Shader.SetGlobalColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._Global_LightmapModulateColor),
                new Color(c.r * k * LightmapDensity, (c.g + c.g) * LightmapDensity, (c.b + c.b) * LightmapDensity, (c.a + c.a) * LightmapDensity));
        }

        static GraphicSettings()
        {
            // 项目固定使用最高图像效果画质
            SetGameQualityImageEffect(true);
        }

        public static void SetGameQualityImageEffect(bool isNormal)
        {
            // 无论isNormal是什么,都强制开启这两个
            IsScreenOverlay = true;
            IsRadialBlur = true;

            IsBloom = isNormal;
            IsDOF = isNormal;
            IsDiffution = isNormal;

            IsGlobalFog = isNormal;
            IsSunShaft = isNormal;
            IsTiltShift = isNormal;
            IsIndirectLightShaft = isNormal;

            IsFluctuation = isNormal;
            IsLensDistortion = isNormal;
            IsChromaticAberration = isNormal;

            IsColorGrading = isNormal;
            IsToneCurve = isNormal;
            IsExposure = isNormal;
            IsTransmittedLight = isNormal;

            
        }

        public enum LayerIndex
        {
            LayerDefault = 0,
            LayerCHAR = 1,
            LayerBG = 2,
            LayerEFFECT = 3,
            LayerWater = 4,
            LayerCircleProfile = 5,
            LayerIgnoreRaycast = 6,
            Layer3D = 7,
            LayerTransparentFX = 8,
            LayerTouchEffect = 9,
            LayerNO_VISIBLE = 10,
            LayerUI = 11,
            Layer3DUI = 12,
            LayerBackground3D_NotReflect = 13,
            LayerCharacter3D_NotReflect = 14,
            LayerCharacter3D_0 = 15,
            LayerCharacter3D_1 = 16
        }

        public static int GetLayer(LayerIndex layerIndex)
        {
            string layerName;
            switch (layerIndex)
            {
                case LayerIndex.LayerDefault: layerName = "Default"; break;
                case LayerIndex.LayerCHAR: layerName = "CHAR"; break;
                case LayerIndex.LayerBG: layerName = "BG"; break;
                case LayerIndex.LayerEFFECT: layerName = "EFFECT"; break;
                case LayerIndex.LayerWater: layerName = "Water"; break;
                case LayerIndex.LayerCircleProfile: layerName = "CircleProfile"; break;
                case LayerIndex.LayerIgnoreRaycast: layerName = "Ignore Raycast"; break;
                case LayerIndex.Layer3D: layerName = "3D"; break;
                case LayerIndex.LayerTransparentFX: layerName = "TransparentFX"; break;
                case LayerIndex.LayerTouchEffect: layerName = "TouchEffect"; break;
                case LayerIndex.LayerNO_VISIBLE: layerName = "NO_VISIBLE"; break;
                case LayerIndex.LayerUI: layerName = "UI"; break;
                case LayerIndex.Layer3DUI: layerName = "3D_UI"; break;
                case LayerIndex.LayerBackground3D_NotReflect: layerName = "Background3D_NotReflect"; break;
                case LayerIndex.LayerCharacter3D_NotReflect: layerName = "Character3D_NotReflect"; break;
                case LayerIndex.LayerCharacter3D_0: layerName = "Character3D_0"; break;
                case LayerIndex.LayerCharacter3D_1: layerName = "Character3D_1"; break;
                default: return -1;
            }
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) Debug.LogWarning($"[GraphicSettings] Unity Layer does not exist: {layerName}");
            return layer;
        }

        public static int GetCullingLayer(LayerIndex layerIndex)
        {
            int layer = GetLayer(layerIndex);
            return layer < 0 ? 0 : 1 << layer;
        }
    }
}