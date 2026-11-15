using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Port of Gallop.Live.Cutt.LiveTimelineKeyCharaMotionNoiseData (track type 27, CharaMotionNoise).
    // Field names are case-sensitive and must match the cutt TypeTree. Native offsets in comments.
    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseData : LiveTimelineKey
    {
        public enum NoiseType
        {
            None = 0,
            Direct = 1,
            Side = 2,
            Back = 3
        }

        public enum GravityType
        {
            None = 0,
            Scale = 1,
            AddValue = 2,
            SetValue = 3
        }

        // Number of per-character slots (native array length 0x14).
        public const int CharaMax = 20;

        // Attribute bits (IsEnableAttribute reads the attribute word at +0x14 of the key).
        public const int kAttrDisableAll = 0x10000;
        public const int kAttrEnableLeft = 0x20000;
        public const int kAttrEnableRight = 0x40000;
        public const int kAttrEnable4th = 0x80000;
        public const int kAttrEnable5th = 0x100000;

        public float sideChrMotNoiseBaseBias;                       // 0x18
        public float sideChrMotNoiseRange = 0.07f;                  // 0x1C (ctor 0x3d8f5c29)
        public float sideChrMotNoiseFrequency = 10f;                // 0x20 (ctor 0x41200000)
        public float backChrMotNoiseBaseBias;                       // 0x24
        public float backChrMotNoiseRange = 0.0875f;                // 0x28 (ctor 0x3db33333)
        public float backChrMotNoiseFrequency = 10f;                // 0x2C (ctor 0x41200000)
        public NoiseType[] NoiseTypeArray = new NoiseType[CharaMax];            // 0x30
        public float[] NoiseBaseBiasArray = new float[CharaMax];                // 0x38
        public float[] NoiseRangeArray = new float[CharaMax];                   // 0x40
        public float[] NoiseFrequencyArray = new float[CharaMax];               // 0x48
        public bool[] EnableCySpringTailArray = new bool[CharaMax];             // 0x50
        public bool[] EnableCySpringSkirtArray = new bool[CharaMax];            // 0x58
        public bool isNegativeCheck;                                            // 0x60
        public GravityType[] cySpringGravityScaleType = new GravityType[CharaMax]; // 0x68
        public float[] cySpringGravityScale = new float[CharaMax];              // 0x70
        public string[] cySpringGravityScaleBoneName = new string[CharaMax];    // 0x78
        public float[] cySpringExpressionKneeCollisionRadius = new float[CharaMax];  // 0x80
        public float[] cySpringExpressionAnkleCollisionRadius = new float[CharaMax]; // 0x88
        public float[] cySpringExpressionInfluenceAngle = new float[CharaMax];       // 0x90
        public float[] cySpringExpressionInfluenceMaxAngle = new float[CharaMax];    // 0x98

        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaMotionNoise;

        public bool IsEnableAttribute(int mask)
        {
            return ((int)attribute & mask) != 0;
        }

        // OnLoad (0x7ffd592da930): every per-character array is resized to CharaMax; new elements get the default
        // (true for the CySpring enable arrays, 1.0 for the gravity scale, "" for the bone names).
        public override void OnLoad(LiveTimelineControl timelineControl)
        {
            Resize(ref NoiseTypeArray, default(NoiseType));
            Resize(ref NoiseBaseBiasArray, 0f);
            Resize(ref NoiseRangeArray, 0f);
            Resize(ref NoiseFrequencyArray, 0f);
            Resize(ref EnableCySpringTailArray, true);
            Resize(ref EnableCySpringSkirtArray, true);
            Resize(ref cySpringGravityScaleType, default(GravityType));
            Resize(ref cySpringGravityScale, 1f);
            Resize(ref cySpringGravityScaleBoneName, string.Empty);
            Resize(ref cySpringExpressionKneeCollisionRadius, 0f);
            Resize(ref cySpringExpressionAnkleCollisionRadius, 0f);
            Resize(ref cySpringExpressionInfluenceAngle, 0f);
            Resize(ref cySpringExpressionInfluenceMaxAngle, 0f);
        }

        private static void Resize<T>(ref T[] array, T fill)
        {
            if (array != null && array.Length == CharaMax) return;
            var resized = new T[CharaMax];
            int keep = array != null ? System.Math.Min(array.Length, CharaMax) : 0;
            for (int i = 0; i < CharaMax; i++)
                resized[i] = i < keep ? array[i] : fill;
            array = resized;
        }

        // GetEnableCySpringTail (0x7ffd592da870) / GetEnableCySpringSkirt (0x7ffd592da7c0): with per-item settings the
        // array decides; otherwise slots 0..4 use a "disable" attribute bit each (set bit = disabled), the rest stay on.
        public bool GetEnableCySpringTail(int index, bool usePerItem)
        {
            return GetEnableCySpring(index, usePerItem, EnableCySpringTailArray,
                0x200000, 0x400000, 0x800000, 0x1000000, 0x2000000);
        }

        public bool GetEnableCySpringSkirt(int index, bool usePerItem)
        {
            return GetEnableCySpring(index, usePerItem, EnableCySpringSkirtArray,
                0x4000000, 0x8000000, 0x10000000, 0x20000000, 0x40000000);
        }

        private bool GetEnableCySpring(int index, bool usePerItem, bool[] array, int bit0, int bit1, int bit2, int bit3, int bit4)
        {
            if ((uint)index >= CharaMax)
                return false;
            if (usePerItem)
                return array[index];
            switch (index)
            {
                case 0: return !IsEnableAttribute(bit0);
                case 1: return !IsEnableAttribute(bit1);
                case 2: return !IsEnableAttribute(bit2);
                case 3: return !IsEnableAttribute(bit3);
                case 4: return !IsEnableAttribute(bit4);
                default: return true;
            }
        }

        // TryGetNoiseParam (0x7ffd592daaf0). The bias is clamped to >= 0; the result is true only when both the range
        // and the frequency are above the native epsilon (1e-6).
        public bool TryGetNoiseParam(int index, out float bias, out float range, out float frequency)
        {
            bias = 0f;
            range = 0f;
            frequency = 0f;
            if ((uint)index >= CharaMax)
                return false;

            switch (NoiseTypeArray[index])
            {
                case NoiseType.Direct:
                    bias = NoiseBaseBiasArray[index];
                    range = NoiseRangeArray[index];
                    frequency = NoiseFrequencyArray[index];
                    break;
                case NoiseType.Side:
                    bias = sideChrMotNoiseBaseBias;
                    range = sideChrMotNoiseRange;
                    frequency = sideChrMotNoiseFrequency;
                    break;
                case NoiseType.Back:
                    bias = backChrMotNoiseBaseBias;
                    range = backChrMotNoiseRange;
                    frequency = backChrMotNoiseFrequency;
                    break;
            }

            if (bias <= 0f)
                bias = 0f;
            const float epsilon = 1e-6f;
            return epsilon < range && epsilon < frequency;
        }
    }

    // Port of Gallop.Live.Cutt.LiveTimelineKeyCharaMotionNoiseDataList.
    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaMotionNoiseData>
    {
        [SerializeField] private bool _usePerItemSettingsForAll;   // 0x40
        [SerializeField] private float _offsetTimeFactor;          // 0x44

        // <IsInitializedOffsetTime>k__BackingField (0x48): runtime flag, not serialized.
        [NonSerialized] private bool _isInitializedOffsetTime;

        public bool UsePerItemSettingsForAll
        {
            get => _usePerItemSettingsForAll;
            set => _usePerItemSettingsForAll = value;
        }

        public float OffsetTimeFactor
        {
            get => _offsetTimeFactor;
            set => _offsetTimeFactor = value;
        }

        public bool IsInitializedOffsetTime
        {
            get => _isInitializedOffsetTime;
            set => _isInitializedOffsetTime = value;
        }
    }
}
