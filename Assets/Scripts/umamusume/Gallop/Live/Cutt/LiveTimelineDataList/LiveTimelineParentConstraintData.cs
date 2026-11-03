using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Field names and types follow the cutt TypeTree (son1156_camera). The track blends the
    // object (a character prop or a mini character) towards weighted source nodes.
    [Serializable]
    public class LiveTimelineParentConstraintSourceData
    {
        // 0 = character node (_targetIndex = character position, _nodeName = bone).
        public int _sourceType;
        public int _targetIndex;
        public string _nodeName;
        public float _weight;
        public int _propId;
    }

    [Serializable]
    public class LiveTimelineKeyParentConstraintData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.ParentConstraint;

        public bool _enable;
        public float _weight;
        public LiveTimelineParentConstraintSourceData[] _sourceDataArray;
        public bool _influenceOfSourceScale;
    }

    [Serializable]
    public class LiveTimelineKeyParentConstraintDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyParentConstraintData> { }

    [Serializable]
    public class LiveTimelineParentConstraintData : ILiveTimelineGroupData
    {
        public const int ObjectTypeProps = 0;
        public const int ObjectTypeMiniChara = 1;

        public LiveTimelineKeyParentConstraintDataList keys = new LiveTimelineKeyParentConstraintDataList();
        public int _objectType;
        public int _charaIndex;
        public int _dressIndex;
        public int _propsIndex;
        public string _stageObjectName;
        public int _stageObjectNameHash;

        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
}
