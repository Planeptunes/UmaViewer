using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Native layout (JP LiveTimelineKeyEdgeStyleData). Only the data is bound here (names match the bundle
    // TypeTree); the EdgeStyle renderer (LiveImageEffect.SetupEdgeStyleController) is not ported yet.
    [Serializable]
    public class LiveTimelineKeyEdgeStyleData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.EdgeStyle;

        public bool IsEnable;
        public Vector2 OffsetDirectionBase;
        public float OffsetScaleBase;
        public Vector2 OffsetDirectionExpand;
        public float OffsetScaleExpand;
        public bool IsCharacterListMode;
        public int DownscaleFactor;
        public float MinDepthValue;
        public float MaxDepthValue;
        public Color MistColor;
        public float MistDensity;
        public float MistIntensity;
        public float MistPower;
        public Vector2 MistScrollSpeed;
        public float MistBlurSize;
        public Vector2 SplashScrollSpeed;
        public Color SplashColor;
        public float SplashThreshold;
        public float SplashBaseRatio;
        public float SplashDensity;
        public float SplashIntensity;
        public float SplashPower;
        public bool IsSplashOuter;
        public bool IsSplashInner;
    }

    [Serializable]
    public class LiveTimelineKeyEdgeStyleDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyEdgeStyleData> { }
}
