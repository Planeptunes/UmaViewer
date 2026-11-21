using System;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    public struct BgColor1UpdateInfo
    {
        public string TimelineName;
        public int TimelineNameHash;
        public int[] TargetCharaIdArray;
        public int[] TargetDressIdArray;
        public Color color;
        public float colorPower;
        public float scale;
        public float Saturation;
        public int flags;
        public Color toonDarkColor;
        public Color toonBrightColor;
        public float vertexColorToonPower;
        public float outlineWidthPower;
        public Color outlineColor;
        public OutlineColorBlend outlineColorBlend;
        public LightBlendMode LightBlendMode;
        public bool IsSilhouette;
        public bool IsProjector;
        public ColorType CurrentColorType;
        public Color CurrentColor;
        public int CurrentFlags;
        public ColorType NextColorType;
        public Color NextColor;
        public int NextFlags;
        public float InterpolateRatio;
        public bool IsSyncBlinkLight;
        public bool IsSyncBlinkLightNext;
        public bool IsAdjustedBlinkLightColor;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        // JP BgColor1UpdateInfo (dump_jp.cs TypeDefIndex 33583579) FakeLight tail, consumed at the end of the
        // per-character block of StageController.UpdateCharaColor1 (JP 0x7ffd5926edf0) by
        // CharacterObject.SetFakeLightReceiveSetting (JP 0x7ffd5922ce90).
        public bool IsEnabledFakeLight;      // 0xD0
        public bool IsFakeLightOutline;      // 0xD1
        public float FakeLightRate;          // 0xD4
        public Color FakeLightReceiveColor;  // 0xD8
        public GameDefine.BlendMode FakeLightBlendMode; // 0xE8
    }

    public delegate void BgColor1UpdateInfoDelegate(ref BgColor1UpdateInfo updateInfo);

    public struct BgColor2UpdateInfo
    {
        public string TimelineName;
        public int TimelineNameHash;
        public Color color1;
        public Color color2;
        public float power;
        public int randomTableIndex;

        // 兼容旧字段名
        public float value
        {
            get => power;
            set => power = value;
        }

        public int rndValueIdx
        {
            get => randomTableIndex;
            set => randomTableIndex = value;
        }
    }

    public delegate void BgColor2UpdateInfoDelegate(ref BgColor2UpdateInfo updateInfo);
}

namespace Gallop
{
    // Gallop.GameDefine.BlendMode (dump_jp.cs TypeDefIndex 33558845): None, Alpha, Multiply, Screen,
    // Additive, Overlay. The numeric values are confirmed by the game shaders' _FakeLightBlendMode
    // property (tools 09-shaders info.txt: Enum(None,0,Alpha,1,Multiply,2,Screen,3,Additive,4,Overlay,5)).
    public static class GameDefine
    {
        public enum BlendMode
        {
            None = 0,
            Alpha = 1,
            Multiply = 2,
            Screen = 3,
            Additive = 4,
            Overlay = 5,
        }
    }
}
