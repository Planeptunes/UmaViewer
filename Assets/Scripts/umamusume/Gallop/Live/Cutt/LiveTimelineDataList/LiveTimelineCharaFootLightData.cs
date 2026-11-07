using System;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaFootLightData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get { return LiveTimelineKeyDataType.CharaFootLight; }
        }

        // Public fields (names are case-sensitive and match the bundle TypeTree). Each array is
        // indexed by character position.
        public LiveCharaPositionFlag positionFlag;
        public float[] hightMax;
        public Color[] lightColor;
        public LiveDefine.HightLightBlendMode[] LightBlendModeArray;
        public LiveDefine.Easing[] EasingArray;
    }

    [Serializable]
    public class LiveTimelineKeyCharaFootLightDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaFootLightData>
    {
    }
}
