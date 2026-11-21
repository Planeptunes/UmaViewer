using System;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    public enum LiveTimelineKeyLoopType
    {
        None = 0,
        CopyStart = 1,
        CopyEnd = 2,
        Paste = 3,
        Max = 4
    }

    [Serializable]
    public class LiveTimelineKeyBgColor1Data : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get
            {
                return LiveTimelineKeyDataType.BgColor1;
            }
        }
        // JP LiveTimelineKeyBgColor1Data getters: they read the base key `attribute` (+0x14), NOT `flags` (+0x48):
        // get_IsSilhouette 0x7ffd592d7490 (& 0x10000), get_IsSyncBlinkLight 0x7ffd592d7480 (& 0x20000),
        // get_IsEnabledFakeLight (& 0x40000).
        private const int ATTR_SILHOUETTE = 0x10000;
        private const int ATTR_SYNC_BLINKLIGHT = 0x20000;
        private const int ATTR_ENABLED_FAKE_LIGHT = 0x40000;

        public Color color;
        public float power;
        public float scale;
        public int flags;
        public ColorType ColorType;
        public float Saturation;
        public Color toonDarkColor;
        public Color toonBrightColor;
        public float vertexColorToonPower;
        public float outlineWidthPower;
        public Color outlineColor;
        public OutlineColorBlend outlineColorBlend;
        public LightBlendMode LightBlendMode;
        public bool IsProjector;
        public string BlinkLightName;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public bool IsAdjustedBlinkLightColor;
        // JP LiveTimelineKeyBgColor1Data FakeLight fields (dump_jp.cs TypeDefIndex 33583373):
        // FakeLightRate 0xB0, FakeLightReceiveColor 0xB4, FakeLightBlendMode 0xC4, IsFakeLightOutline 0xC8.
        // Field initializers = the game .ctor defaults (JP 0x7ffd592d75b0): rate 1.0, receive colour
        // (1,1,1,1), blend mode 4 (Additive).
        public float FakeLightRate = 1f;
        public Color FakeLightReceiveColor = Color.white;
        public GameDefine.BlendMode FakeLightBlendMode = GameDefine.BlendMode.Additive;
        public bool IsFakeLightOutline;
        public LiveTimelineKeyLoopType loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public bool isPasteLoopUnit;
        public bool isChangeLoopInterpolate;
        public float f32;
        public LiveTimelineKeyLoopType _loopType;
        public int _loopCount;
        public int _loopExecutedCount;
        public int _loopIntervalFrame;
        public bool _isChangeLoopInterpolate;

        public bool IsSilhouette => ((int)attribute & ATTR_SILHOUETTE) != 0;
        public bool IsSyncBlinkLight => ((int)attribute & ATTR_SYNC_BLINKLIGHT) != 0;
        // JP LiveTimelineKeyBgColor1Data.get_IsEnabledFakeLight (0x7ffd592d7460): attribute & 0x40000.
        public bool IsEnabledFakeLight => ((int)attribute & ATTR_ENABLED_FAKE_LIGHT) != 0;
    }

    [Serializable]
    public class LiveTimelineKeyBgColor1DataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyBgColor1Data>
    {
    }

    [Serializable]
    public class LiveTimelineBgColor1Data : ILiveTimelineGroupDataWithName
    {
        private const string DEFAULT_NAME = "BgColor1";
        public LiveTimelineKeyBgColor1DataList keys = new LiveTimelineKeyBgColor1DataList();

        [SerializeField] private int[] _targetCharaIdArray;
        [SerializeField] private int[] _targetDressIdArray;

        public int[] TargetCharaIdArray => _targetCharaIdArray;
        public int[] TargetDressIdArray => _targetDressIdArray;

        public LiveTimelineBgColor1Data()
        {
            if (string.IsNullOrEmpty(name))
                name = DEFAULT_NAME;
        }
    }

    
}
