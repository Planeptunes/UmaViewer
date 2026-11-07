using System;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyData_AdditionalLight : LiveTimelineKeyWithInterpolate
    {
        public enum MyLightType
        {
            Directional = 0,
            Spot = 1,
            Point = 2
        }

        public override LiveTimelineKeyDataType dataType
        {
            get { return LiveTimelineKeyDataType.AdditionalLight; }
        }

        // Public fields (names are case-sensitive and match the bundle TypeTree)
        public Vector3 Position;
        public Vector3 Rotate;
        public bool IsEnable;
        public MyLightType Type;
        public float Range;
        public int SpotAngle;
        public int IndirectMultiplier;
        public LightShadows ShadowType;
        public float Strength;
        public float Bias;
        public float NormalBias;
        public float NearPlane;
    }

    [Serializable]
    public class LiveTimelineKeyAdditionalLightList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyData_AdditionalLight>
    {
    }

    [Serializable]
    public class LiveTimelineAdditionalLight : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "AdditionalLight";

        public LiveTimelineKeyAdditionalLightList keys;

        public override ILiveTimelineKeyDataList GetKeyList()
        {
            return keys;
        }

        public LiveTimelineAdditionalLight()
            : base(default_name)
        {
            keys = new LiveTimelineKeyAdditionalLightList();
        }
    }
}
