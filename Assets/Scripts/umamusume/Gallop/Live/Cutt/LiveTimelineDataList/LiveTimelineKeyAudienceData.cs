using System;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    // Port of Gallop.Live.Cutt.LiveTimelineKeyAudienceData (track type 34 = Audience, native key offsets in comments).
    // Field names are case-sensitive and must match the cutt TypeTree (dump_jp.cs, class LiveTimelineKeyAudienceData).
    // The key attribute word (+0x14) carries three flags: 0x10000 IsEnabledRender, 0x20000 AudienceUseDirectionalLight,
    // 0x40000 AudienceDirectionalLightCameraFollow.
    [Serializable]
    public class LiveTimelineKeyAudienceData : LiveTimelineKeyWithInterpolate
    {
        public const int ATTR_ENABLE_RENDER = 0x10000;
        public const int ATTR_AUDIENCE_USE_DIRECTIONAL_LIGHT = 0x20000;
        public const int ATTR_AUDIENCE_DIRECTIONAL_LIGHT_CAMERA_FOLLOW = 0x40000;

        // Defaults follow the native constructor (0x7ffd592d71a0); the cutt data overrides every serialized field.
        public Vector3 position = Vector3.zero;                  // 0x30
        public Vector3 rotate = Vector3.zero;                    // 0x3C (euler degrees)
        public Vector3 scale = Vector3.one;                      // 0x48
        public Color cyalumeColor = Color.white;                 // 0x54
        public Color cyalumeGlowColor = Color.white;             // 0x64
        public float cyalumeGlowColorPower = 1f;                 // 0x74
        public float cyalumeMaskRadius = 1f;                     // 0x78
        public Gallop.Cyalume.AudienceController.AnimationSetting animationSetting = Gallop.Cyalume.AudienceController.AnimationSetting.Direct; // 0x7C
        public int animationRootIndex;                           // 0x80
        public Gallop.Cyalume.AudienceController.AnimationBodyRegion animationBodyRegion = Gallop.Cyalume.AudienceController.AnimationBodyRegion.RightHand; // 0x84
        public Gallop.Cyalume.AudienceController.AnimationCategory animationCategory = Gallop.Cyalume.AudienceController.AnimationCategory.Common;       // 0x88
        public int animationIndex = -1;                          // 0x8C
        public WrapMode animationWrapMode;                       // 0x90
        public float animationSpeed = 1f;                        // 0x94
        public float animationOffsetTime;                        // 0x98
        public float AnimationTime;                              // 0x9C
        public bool UseAnimationTime;                            // 0xA0
        public Color AudienceColor = Color.white;                // 0xA4
        public float AudienceColorPower = 1f;                    // 0xB4
        public Color AudienceToonBrightColor = Color.white;      // 0xB8
        public Color AudienceToonDarkColor = Color.white;        // 0xC8
        public float AudienceVertexColorToonPower = 1f;          // 0xD8
        public float AudienceOutlineWidthPower = 1f;             // 0xDC
        public Color AudienceOutlineColor = Color.white;         // 0xE0
        public OutlineColorBlend AudienceOutlineColorBlend;      // 0xF0
        public Color AudienceRimColor = Color.white;             // 0xF4
        public float AudienceRimStep = 0.15f;                    // 0x104
        public float AudienceRimFeather = 0.001f;                // 0x108
        public float AudienceRimSpecRate = 1f;                   // 0x10C
        public float AudienceRimShadowRate;                      // 0x110
        public float AudienceRimHorizonOffset;                   // 0x114
        public float AudienceRimVerticalOffset;                  // 0x118
        public Color AudienceRimColor2 = Color.white;            // 0x11C
        public float AudienceRimStep2 = 0.1f;                    // 0x12C
        public float AudienceRimFeather2 = 0.01f;                // 0x130
        public float AudienceRimSpecRate2 = 0.5f;                // 0x134
        public float AudienceRimShadowRate2;                     // 0x138
        public float AudienceRimHorizonOffset2;                  // 0x13C
        public float AudienceRimVerticalOffset2;                 // 0x140
        public Vector3 AudienceDirectionalLightAngle;            // 0x144 (euler degrees)

        // <Rotation>k__BackingField (0x150) / <AudienceDirectionalLightRotation>k__BackingField (0x160): runtime, set in OnLoad.
        [NonSerialized] public Quaternion Rotation = Quaternion.identity;
        [NonSerialized] public Quaternion AudienceDirectionalLightRotation = Quaternion.identity;

        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Audience;

        public bool IsEnabledRender => ((int)attribute & ATTR_ENABLE_RENDER) != 0;                                   // 0x7ffd592d7490
        public bool AudienceUseDirectionalLight => ((int)attribute & ATTR_AUDIENCE_USE_DIRECTIONAL_LIGHT) != 0;       // 0x7ffd592d7480
        public bool AudienceDirectionalLightCameraFollow => ((int)attribute & ATTR_AUDIENCE_DIRECTIONAL_LIGHT_CAMERA_FOLLOW) != 0; // 0x7ffd592d7460

        // OnLoad (0x7ffd592d70d0): both quaternions come from the authored euler angles (degrees * 0.0174533 -> FromEulerRad).
        public override void OnLoad(LiveTimelineControl timelineControl)
        {
            Rotation = Quaternion.Euler(rotate);
            AudienceDirectionalLightRotation = Quaternion.Euler(AudienceDirectionalLightAngle);
        }
    }

    [Serializable]
    public class LiveTimelineKeyAudienceDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyAudienceData> { }

    // Port of Gallop.Live.Cutt.LiveTimelineAudienceData: one group per audience object (keys + index into the stage's
    // _audienceObjects prefab array, _objectIndex at 0x28).
    [Serializable]
    public class LiveTimelineAudienceData : ILiveTimelineGroupDataWithName
    {
        private const string DEFAULT_NAME = "Audience";

        public LiveTimelineKeyAudienceDataList keys = new LiveTimelineKeyAudienceDataList();
        public int _objectIndex;

        public int ObjectIndex
        {
            get => _objectIndex;
            set => _objectIndex = value;
        }

        public override ILiveTimelineKeyDataList GetKeyList() => keys;

        public LiveTimelineAudienceData() : base(DEFAULT_NAME)
        {
        }
    }

    // Gallop.Live.Cutt.AudienceUpdateInfoDelegate: the info struct is passed by reference.
    public delegate void AudienceUpdateInfoDelegate(ref Gallop.Cyalume.AudienceUpdateInfo updateInfo);
}
