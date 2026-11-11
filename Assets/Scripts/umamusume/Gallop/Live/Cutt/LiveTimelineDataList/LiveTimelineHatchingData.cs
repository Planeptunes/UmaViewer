using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Native layout (JP LiveTimelineKeyHatchingData). BlendMode is HatchingParam.HatchingBlendMode
    // (None 0, Alpha 1, Multiply 2, Screen 3, Additive 4, Overlay 5); None keeps the effect off.
    [Serializable]
    public class LiveTimelineKeyHatchingData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Hatching;

        public int BlendMode;
        public float BlendAlpha;
        public Texture2D NoiseTexture;
        public float StrokeTiling;
        public float StrokeHeight;
        public float StrokeAngle;
        public float StrokeLength;
        public Vector2 CircleMaskOffset;
        public float CircleMaskSize;
        public Vector2 CircleMaskStretchRatio;
        public float CircleMaskAngle;
        public float CircleMaskInnerRatio;
        public float CircleMaskInnerEffect;
    }

    [Serializable]
    public class LiveTimelineKeyHatchingDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyHatchingData> { }
}
