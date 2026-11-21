using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [System.Serializable]
    public class FacialPartsData
    {
        public int FacialPartsId;
        public int WeightPer;
    }

    [System.Serializable]
    public class LiveTimelineKeyLipSyncData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.LipSync;}
        }
        // LiveTimelineKeyLipSyncData.SongPartVariationLipSyncTarget (dump TypeDefIndex 33583458): the characters that
        // lip-sync for one song part variation.
        [System.Serializable]
        public class SongPartVariationLipSyncTarget
        {
            public int SongPartVariationId;
            public LiveCharaPositionFlag PositionFlag;
        }

        public LiveCharaPositionFlag character;
        // key +0x40; empty for the keys without song part variations.
        public System.Collections.Generic.List<SongPartVariationLipSyncTarget> SongPartVariationTargetFlagList;

        public bool HasSongPartVariationTargetFlag
        {
            get { return SongPartVariationTargetFlagList != null && SongPartVariationTargetFlagList.Count > 0; }
        }

        // GetCharacterFlag @0x7ffd592f2540: song part 0 -> the key's own flag; otherwise the entry of that song part
        // variation, and the key's own flag when there is none.
        public LiveCharaPositionFlag GetCharacterFlag(int songPartVariationId)
        {
            if (songPartVariationId == 0 || SongPartVariationTargetFlagList == null)
                return character;
            for (int i = 0; i < SongPartVariationTargetFlagList.Count; i++)
            {
                if (SongPartVariationTargetFlagList[i].SongPartVariationId == songPartVariationId)
                    return SongPartVariationTargetFlagList[i].PositionFlag;
            }
            return character;
        }
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public FacialPartsData[] facialPartsDataArray; // 0x30
        public DrivenKeyComponent.InterpolateType interpolateType; // 0x38
    }

    [System.Serializable]
    public class LiveTimelineKeyLipSyncDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLipSyncData>
    {

    }
}
