using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // TransmittedLight track (glow of the stage lights). Field names and types follow the cutt
    // TypeTree (son1156_camera). The parameters drive FastBloom's DownSampleMask / blur / Bloom
    // passes over the mask drawn by the stage lights' "TransmittedLightMask" shader pass.
    [Serializable]
    public class LiveTimelineKeyTransmittedLightData : LiveTimelineKeyWithInterpolate
    {
        // Bit 16 of the key attribute: effect enabled (set on every authored key seen so far).
        public const int AttributeEnable = 0x10000;

        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TransmittedLight;

        public int Iterations = 1;
        public float Intensity = 1f;
        public float Threshold;
        public float BlurSpread = 2f;
        // 2 = Add; any other value is composed as Screen (BloomScreenBlendMode).
        public int BlendMode;

        public bool IsEnable => ((int)attribute & AttributeEnable) != 0;
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTransmittedLightData> { }
}
