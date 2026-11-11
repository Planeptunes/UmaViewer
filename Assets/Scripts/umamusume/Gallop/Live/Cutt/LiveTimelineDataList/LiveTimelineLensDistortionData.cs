using System;

namespace Gallop.Live.Cutt
{
    // Native layout (JP LiveTimelineKeyLensDistortionData): Intensity +0x30, IntensityX +0x34,
    // IntensityY +0x38, CenterX +0x3C, CenterY +0x40, Scale +0x44. Names match the bundle TypeTree.
    [Serializable]
    public class LiveTimelineKeyLensDistortionData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.LensDistortion;

        public float Intensity;
        public float IntensityX;
        public float IntensityY;
        public float CenterX;
        public float CenterY;
        public float Scale;
    }

    [Serializable]
    public class LiveTimelineKeyLensDistortionDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLensDistortionData> { }
}
