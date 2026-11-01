using System;
using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// Key of the cutt "CharaNode" track (CharaNodeDataList). For the characters in
    /// <see cref="PositionFlag"/> it switches whether the CySpring result is applied, per part
    /// (head, ears, body, skirt, tail) and per root bone, so the motion clip drives those bones
    /// instead (LiveTimelineControl.AlterUpdate_CharaNode).
    /// </summary>
    [Serializable]
    public class LiveTimelineKeyCharaNodeData : LiveTimelineKeyWithInterpolate
    {
        public LiveCharaPositionFlag PositionFlag;
        public bool EnableHeadCySpring = true;
        public bool EnableEarCySpring = true;
        public bool EnableBodyCySpring = true;
        public bool EnableSkirtCySpring = true;
        public bool EnableTailCySpring = true;
        public List<string> TargetCySpringBornNameList = new List<string>();
        public List<bool> TargetCySpringBornEnableList = new List<bool>();

        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaNode;
    }

    [Serializable]
    public class LiveTimelineKeyCharaNodeDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaNodeData> { }

    [Serializable]
    public class LiveTimelineCharaNodeData : ILiveTimelineGroupDataWithName
    {
        private const string DEFAULT_NAME = "CharaNode";

        public LiveTimelineKeyCharaNodeDataList keys = new LiveTimelineKeyCharaNodeDataList();

        public override ILiveTimelineKeyDataList GetKeyList() => keys;

        public LiveTimelineCharaNodeData() : base(DEFAULT_NAME)
        {
        }
    }
}
