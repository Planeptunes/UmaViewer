using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyGlobalLightData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.GlobalLight;}
        }
        // Static field
        public static readonly Color DefaultRimColor;

        // Public fields
        public Vector3 lightDir;
        public Color rimColor;
        public float rimStep;
        public float rimFeather;
        public float rimSpecRate;
        public float globalRimShadowRate;
        public bool cameraFollow;
        public float RimHorizonOffset;
        public float RimVerticalOffset;
        public float RimHorizonOffset2;
        public float RimVerticalOffset2;
        public Color rimColor2;
        public float rimStep2;
        public float rimFeather2;
        public float rimSpecRate2;
        public float globalRimShadowRate2;
        public LiveCharaPositionFlag flags;
        public string BlinkLightName;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public LiveTimelineKeyLoopType loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public bool isPasteLoopUnit;
        public bool isChangeLoopInterpolate;

        // Properties
        // (BLINK LIGHT) Native get_IsSyncBlinkLightToRimColor / ...RimColor2 (JP 0x7ffd592f0610 /
        // 0x7ffd592f0650) read attribute bits of the key (FlagUtil.HasAnyFlag(attribute, 0x10000 /
        // 0x20000)) and the setters write them back (LiveTimelineKeyAttribute.Ext.addFlag). They
        // were plain auto-properties here, so the sync never ran and the authored rim colours stood.
        private const int ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR = 0x10000;
        private const int ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR2 = 0x20000;

        public bool IsSyncBlinkLightToRimColor
        {
            get => ((int)attribute & ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR) != 0;
            set => attribute = (LiveTimelineKeyAttribute)(value
                ? (int)attribute | ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR
                : (int)attribute & ~ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR);
        }

        public bool IsSyncBlinkLightToRimColor2
        {
            get => ((int)attribute & ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR2) != 0;
            set => attribute = (LiveTimelineKeyAttribute)(value
                ? (int)attribute | ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR2
                : (int)attribute & ~ATTR_SYNC_BLINKLIGHT_TO_RIMCOLOR2);
        }
    }

    [Serializable]
    public class LiveTimelineKeyGlobalLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyGlobalLightData>
    {

    }

    [Serializable]
    public class LiveTimelineGlobalLightData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "GlobalLight";
        public LiveTimelineKeyGlobalLightDataList keys;
    }
}
