using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    

    [System.Serializable]
    public class LiveTimelineKeyCharaMotionData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType
        {
            get
            {
                return LiveTimelineKeyDataType.CharaMotionSequecne;
            }
        }
        public string motionName;
        public string motionName2;
        public string motionName3;
        public AnimationClip clip;
        public AnimationClip clip2;
        public AnimationClip clip3;
        public int overrideMotionSysTextId;
        public bool useOverrideMotionFacial;
        public int motionHeadFrame;
        public int playFrameLength;
        public float playSpeed;
        public bool loop;
        [SerializeField] private bool _isTimescaleDisabled;
        public bool IsTimescaleDisabled => _isTimescaleDisabled;
        public bool isMotionHeadFrameAll;
        public int[] motionHeadFrameSeparetes;

        // Run mode / transform canceller fields of the native key (JP dump LiveTimelineKeyCharaMotionData, offsets in
        // the comments). Read by UpdateOverrideRaceMotion @0x7ffd59303260 (run mode: the character runs in place, the
        // root motion is cancelled) and get_UseOverrideAnimation @0x7ffd592da770 (override sys text id > 0 or run mode).
        // Not used by the viewer yet: they are deserialized only to be inspected.
        [SerializeField] private bool _useRandomMotionSystemText;   // 0x6C
        [SerializeField] private int _randomMotionGroupIndex;       // 0x70
        public bool UseSecondMotion;                                // 0x79
        [SerializeField] private bool _isUseRunMode;                // 0xB0
        [SerializeField] private float _runModeMotionSpeed;         // 0xB4
        [SerializeField] private float _runModeBlendRate;           // 0xB8
        [SerializeField] private bool _isUsePrevAnimationTime;      // 0xBC
        [SerializeField] private bool _isCancelX;                   // 0xBD
        [SerializeField] private bool _isCancelY;                   // 0xBE
        [SerializeField] private bool _isCancelZ;                   // 0xBF
        [SerializeField] private float _cancelRate;                 // 0xC0
        [SerializeField] private bool _isCancelScaleAveragePos;     // 0xC4
        [SerializeField] private int _cancelOffsetCheckTarget;      // 0xC8
        [SerializeField] private Vector3 _cancelFixPos;             // 0xCC
        public bool IsUseRunMode => _isUseRunMode;
        public float RunModeMotionSpeed => _runModeMotionSpeed;
        public float RunModeBlendRate => _runModeBlendRate;
        public bool IsCancelX => _isCancelX;
        public bool IsCancelY => _isCancelY;
        public bool IsCancelZ => _isCancelZ;
        public float CancelRate => _cancelRate;
        public bool IsCancelScaleAveragePos => _isCancelScaleAveragePos;
        public int CancelOffsetCheckTarget => _cancelOffsetCheckTarget;
        public Vector3 CancelFixPos => _cancelFixPos;
        public bool IsUsePrevAnimationTime => _isUsePrevAnimationTime;
        public bool UseRandomMotionSystemText => _useRandomMotionSystemText;
        public int RandomMotionGroupIndex => _randomMotionGroupIndex;
    }

    [System.Serializable]
    public class LiveTimelineKeyCharaMotionSeqDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaMotionData>
    {

    }

    [System.Serializable]
    public class LiveTimelineCharaMotSeqData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyCharaMotionSeqDataList keys;
        [SerializeField]
        private bool _existsOverrideMotionSet;
    }
}
