using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyMirrorReflectionData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.MirrorReflection;}
        }
        // Names below intentionally cover both official dummy names and the JSON field names seen in exported timeline data.
        public bool EnableKey;
        public bool EnableMirror;
        public bool EnableBgLayer;
        public bool Enable3dLayer;
        public bool IsToonMirror;
        public float MirrorReflectionRate = 1f;
        public LiveCharaPositionFlag TargetChara = LiveCharaPositionFlag.All;
        public bool EnableCharaHead;
        public LiveCharaPositionFlag TargetCharaHead = LiveCharaPositionFlag.All;

        // Exported JSON names
        public bool isValidMirror;
        public bool isMirror;
        public bool isBgMirror;
        public bool IsMirrorBg3d;
        public bool EnableCharacterMirrorExpandFaceBounds;
        public LiveCharaPositionFlag characterMirror = LiveCharaPositionFlag.All;
        public LiveCharaPositionFlag CharacterMirrorHead = LiveCharaPositionFlag.All;
        public LiveCharaPositionFlag CharacterMirrorExpandFaceBounds;
        public float mirrorReflectionRate = 1f;

        // Backing/export helper fields sometimes present in JSON
        public bool _isValidMirror;
        public int _mirror;
        public int _bgMirror;
        public LiveCharaPositionFlag _characterMirror;
        public float _mirrorReflectionRate = -1f;

        // Native LiveTimelineKeyStageEnvironmentData.OnLoad: a non-negative `_mirrorReflectionRate`
        // marks legacy data, whose `_` fields replace isValidMirror/isMirror/isBgMirror/
        // characterMirror/mirrorReflectionRate. Otherwise the authored fields are used as they are.
        private bool UsesLegacyMirrorFields => _mirrorReflectionRate >= 0f;

        public bool GetIsValidMirror()
        {
            return UsesLegacyMirrorFields ? _isValidMirror : isValidMirror;
        }

        public bool GetMirrorEnabled()
        {
            return UsesLegacyMirrorFields ? _mirror != 0 : isMirror;
        }

        public bool GetBgMirrorEnabled()
        {
            return UsesLegacyMirrorFields ? _bgMirror != 0 : isBgMirror;
        }

        public bool GetBg3dMirrorEnabled()
        {
            return IsMirrorBg3d;
        }

        public float GetMirrorReflectionRate()
        {
            return UsesLegacyMirrorFields ? _mirrorReflectionRate : mirrorReflectionRate;
        }

        public LiveCharaPositionFlag GetCharacterMirrorFlag()
        {
            return UsesLegacyMirrorFields ? _characterMirror : characterMirror;
        }

        public LiveCharaPositionFlag GetCharacterMirrorHeadFlag()
        {
            return CharacterMirrorHead;
        }

        // Native IsEnabledMirrorCharacterHead: attribute bit 0x10000.
        public bool GetEnableCharacterMirrorHead()
        {
            return ((int)attribute & 0x10000) != 0;
        }
    }

    [Serializable]
    public class LiveTimelineKeyMirrorReflectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMirrorReflectionData>
    {
    }

    [Serializable]
    public class LiveTimelineStageEnvironmentData : ILiveTimelineGroupDataWithName
    {
        private const string DEFAULT_NAME = "Environment";
        public LiveTimelineKeyMirrorReflectionDataList keys = new LiveTimelineKeyMirrorReflectionDataList();

        public LiveTimelineStageEnvironmentData()
        {
            if (string.IsNullOrEmpty(name))
                name = DEFAULT_NAME;
        }
    }
}
