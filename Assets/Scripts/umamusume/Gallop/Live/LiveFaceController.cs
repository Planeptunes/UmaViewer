using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop
{
    // Port of Gallop.FaceGroupType (dump_jp, TypeDefIndex 33580433). Declaration order = value order, verified
    // against the ModelController.SetFaceParts call sites in LiveFaceController.SetEye / SetEyebrow / SetMouth
    // (EyeL = 3, EyeR = 2, EyebrowR = 4, EyebrowL = 5, Mouth = 6).
    public enum FaceGroupType
    {
        EarR = 0,
        EarL = 1,
        EyeR = 2,
        EyeL = 3,
        EyebrowR = 4,
        EyebrowL = 5,
        Mouth = 6,
        Max = 7,
    }

    // Port of Gallop.FaceGroupSet (dump_jp, TypeDefIndex 33580434). The dump lists the members in the order
    // None, Eye, Eyebrow, Mouth, Max, but the switch in LiveFaceController.UpdateFacePartsSetFromFacialId
    // (RVA 0x4EC54A0) and every call site prove the values used by the game: 0 = Eye (copies _eyeL/_eyeR),
    // 1 = Eyebrow (_eyebrowL/_eyebrowR), 2 = Mouth (_mouth).
    public enum FaceGroupSet
    {
        Eye = 0,
        Eyebrow = 1,
        Mouth = 2,
    }

    // Sink of the face-parts state machine: LiveFaceController resolves WHAT each face group should show
    // (the game's FaceParts arrays) and hands the resulting weights to FaceDrivenKeyTarget, which applies them
    // through the viewer's additive morphs. Morph index = FaceParts._faceParts - 1 because the
    // FacePartsSetInfo.*Dic maps are identity (FacePartsSetInfo..cctor RVA 0x58F8180).
    public interface IFacialPartsApplier
    {
        int GetPartsCount(FaceGroupType type);
        void SetPartsWeights(FaceGroupType type, float[] weights, int count);
        void ApplyParts(FaceGroupType type);

        // Eye blink hooks (Gallop.Model.Component.EyeBlinkController): EyeTraceController delayed tracking off while the
        // eyes open, EyeHighlightController.IsSuppressEyeBlink, and FaceOverrideData.IsDisableEyeBlink (copied by
        // ModelController.InitEyeBlinkController @0x7ffd59674b00 into EyeBlinkController.IsDisableByOverride).
        void DisableDelayedEyeTracking();
        bool IsEyeHighlightSuppressingBlink();
        bool IsEyeBlinkDisabledByOverride { get; }
    }

    // Port of the DrivenKeyComponent face-parts weight group (Gallop.FaceWeightGroup : WeightGroupBase,
    // dump_jp TypeDefIndex 33580412) restricted to what the Live facial path needs:
    //   DrivenKeyComponent.SetFace (per-group)          RVA 0x4AF1DD0 (@0x7ffd58e71dd0)
    //   DrivenKeyComponent.BlendProgrammable (face)     RVA 0x4AEA160 (@0x7ffd58e6a160)
    //   DrivenKeyComponent.SetInterpolateType           RVA 0x4AF2450 (per-group slot)
    //   DrivenKeyComponent.GetInterpolateRate           RVA 0x4AEBC60 (same formula as EarWeightGroup)
    // WeightGroupBase stores Current/Target/Prev weights (NativeArray regions of CommonWeightArray, one slot
    // per FaceGroupType inside the group) plus DurationTimeArray / CurrentTimeArray. SetFace with
    // duration > 0 writes the TARGET region and captures Prev (SetPrevWeight, force = false); with
    // duration <= 0 it writes CURRENT (and TARGET too when the target is replaced). BlendProgrammable ramps
    // Current = Prev + (Target - Prev) * rate while CurrentTime < DurationTime. The weight -> TRS job
    // (WeightCalcJob.CalcTRS / AddTRS) is ported in WeightCalcJob.cs but not wired for the eye / eyebrow / mouth groups
    // (the ear group runs EarWeightJob through EarWeightJobRunner): the viewer applies these weights through the
    // additive facial morphs.
    public class FacePartsWeightRamp
    {
        public float[] Current;
        public float[] Target;
        public float[] Prev;
        public float DurationTime;
        public float CurrentTime;
        public DrivenKeyComponent.InterpolateType InterpolateType;
        public bool HasBlendList; // set by SetFace, mirrors the game's AddBlendFace gate on the ramp

        public FacePartsWeightRamp(int size)
        {
            Current = new float[size];
            Target = new float[size];
            Prev = new float[size];
        }

        // FaceWeightGroup.SetPrevWeight(int, bool) RVA 0x4AF73B0 (same quirk as EarWeightGroup.SetPrevWeight:
        // Prev is captured only when the previous ramp finished, unless forced).
        public void SetPrevWeight(bool force)
        {
            bool finished = CurrentTime == 0f || DurationTime <= CurrentTime;
            if (!force && !finished) return;
            System.Array.Copy(Current, Prev, Current.Length);
        }

        // DrivenKeyComponent.SetFace (per-group) RVA 0x4AF1DD0. `scale` is the weight the game multiplies each
        // FaceParts._weight with (1.0 from SetFaceEyeType / SetFaceEyebrowType / SetFaceMouthType on this path).
        // ids outside the ramp are skipped (the game would throw via the indexers).
        public void SetFace(IReadOnlyList<Gallop.FaceParts> parts, float scale, float duration)
        {
            float[] writeArray;
            bool alsoTarget = false;
            if (0f < duration)
            {
                writeArray = Target;
                SetPrevWeight(false);
                DurationTime = duration;
                CurrentTime = 0f;
            }
            else
            {
                writeArray = Current;
                alsoTarget = true;
            }

            // !isKeepTarget: every face id except the base (id 0) is cleared before the listed parts are set.
            for (int i = 1; i < writeArray.Length; i++)
            {
                writeArray[i] = 0f;
                if (alsoTarget)
                {
                    Target[i] = 0f;
                }
            }

            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    int id = parts[i]._faceParts;
                    if (id <= 0 || id >= writeArray.Length) continue;
                    float weight = parts[i]._weight * scale;
                    writeArray[id] = weight;
                    if (alsoTarget)
                    {
                        Target[id] = weight;
                    }
                }
            }

            HasBlendList = true;
        }

        // DrivenKeyComponent.BlendProgrammable (face groups overload @0x7ffd58e6a160) with the live delta.
        public void BlendProgrammable(float deltaTime)
        {
            if (HasBlendList && CurrentTime < DurationTime)
            {
                CurrentTime += deltaTime;
                float rate = EarWeightGroup.GetInterpolateRate(InterpolateType, CurrentTime, DurationTime);
                float clamped = rate < 0f ? 0f : (1f < rate ? 1f : rate);
                for (int i = 0; i < Current.Length; i++)
                {
                    float prev = Prev[i];
                    float target = Target[i];
                    if (prev != target)
                    {
                        Current[i] = (target - prev) * clamped + prev;
                    }
                }
            }
        }

        public bool IsRamping => HasBlendList && CurrentTime < DurationTime;

        // Viewer-only: back to a freshly created group, used when the viewer seeks the live backwards.
        public void Reset()
        {
            System.Array.Clear(Current, 0, Current.Length);
            System.Array.Clear(Target, 0, Target.Length);
            System.Array.Clear(Prev, 0, Prev.Length);
            DurationTime = 0f;
            CurrentTime = 0f;
            InterpolateType = DrivenKeyComponent.InterpolateType.Linear;
            HasBlendList = false;
        }
    }
}

namespace Gallop.Live
{
    // Port of Gallop.Live.LiveFaceController (dump_jp TypeDefIndex 33583192) restricted to the Live cutt facial
    // path (code_JP Gallop.Live.LiveFaceController.c). The game class is a MonoBehaviour owned by the character
    // (its ModelController at +0x98 forwards to DrivenKeyComponent); here it is a plain object owned by
    // FaceDrivenKeyTarget (one per character) and the ModelController apply calls map onto the viewer's additive
    // morphs through IFacialPartsApplier. Ported methods and RVAs:
    //   AlterUpdateFacialNew                        0x4EC20E0 (@0x7ffd592420e0)
    //   UpdateEye                                   0x4EC5030 (@0x7ffd59245030)
    //   SetEye                                      0x4EC3FF0 / SetEyebrow 0x4EC42F0 / SetMouth 0x4EC4570
    //   SetEar                                      0x4EC3DC0
    //   UpdateFacePartsSetFromFacialId              0x4EC54A0 (@0x7ffd592454a0)
    //   UpdateFacePartsSetFromFacialPartsDataArray  0x4EC58E0 (@0x7ffd592458e0)
    //   GetFacePartsSetFromFacialId                 0x4EC32D0 (@0x7ffd592432d0)
    //   IsEqualFacePartsArray                       0x4EC39C0 / CopyFacePartsArray 0x4EC2E50
    //   UpdateWeight                                0x4EC6220 / UpdateDurationTime 0x4EC4E90
    // The eye blink goes through Gallop.Model.Component.EyeBlinkController (ModelController +0x3B8): UpdateEye asks it
    // for the key blinks (RequestBlink), SetEye waits while it is mid blink (IsEyeBlinking) and UpdateEyeBlink runs its
    // per-frame BeforeApplyDrivenKey. NOT ported (ModelController side, reported as pending): EnableEyePupli,
    // ModelController.GetDurationTimeEye (zero duration for some eye faces), SetFacePartsScaleEnabled (eyebrow
    // usePartsScale), the teardrop and mangame model calls of the effect key block, and the SystemText facial branch
    // (_isSystemTextFace, 0x4EC20E0 head). The eye-highlight animation of the effect key (PlayEyeHighlightAnimation) is
    // applied in FaceDrivenKeyTarget through EyeHighlightController.
    public class LiveFaceController : Gallop.Model.Component.EyeBlinkController.IOwner
    {
        public float _earWeight = 1f;                    // 0x20 (ctor: 1.0)
        public float _earDurationTime;                   // 0x24
        public float _eyebrowDurationTime;               // 0x28
        public float _eyeDurationTime;                   // 0x2C
        public float _mouthDurationTime;                 // 0x30
        public DrivenKeyComponent.InterpolateType _eyeInterpolateType;      // 0x34
        public DrivenKeyComponent.InterpolateType _eyebrowInterpolateType;  // 0x38
        public DrivenKeyComponent.InterpolateType _mouthInterpolateType;    // 0x3C
        public DrivenKeyComponent.InterpolateType _earInterpolateType;      // 0x40
        public bool _earRandomMotion;                    // 0x44
        public DrivenKeyComponent.InterpolateType _prevEyeInterpolateType;      // 0x48
        public DrivenKeyComponent.InterpolateType _prevEyebrowInterpolateType;  // 0x4C
        public DrivenKeyComponent.InterpolateType _prevMouthInterpolateType;    // 0x50
        public DrivenKeyComponent.InterpolateType _prevEarInterpolateType;      // 0x54
        public bool _prevEarRandomMotion;                // 0x58
        public EarType _earTypeL;                        // 0x5C
        public EarType _earTypeR;                        // 0x60
        public bool _isLipSync;                          // 0xC5
        public float _prevEarWeight = -1f;               // 0xA8 (ctor: -1.0)
        public float _prevEarDurationTime = -1f;         // 0xAC (ctor: -1.0)
        public float _prevEyebrowDurationTime;           // 0xB0
        public float _prevEyeDurationTime;               // 0xB4
        public float _prevMouthDurationTime;             // 0xB8
        public EarType _prevEarTypeL;                    // 0xBC
        public EarType _prevEarTypeR;                    // 0xC0
        public bool _forceUpdateEye;                     // 0x121
        public bool _forceUpdateMouth;                   // 0x122
        public bool _forceUpdateEyebrow;                 // 0x123
        public bool _forceUpdateEar;                     // 0x124
        public int _currentStartFrameOfEye = -1;         // 0x160 (blink state machine input, see UpdateEye; ctor: -1)

        // LiveFaceController.BlinkStep (dump_jp TypeDefIndex 33583196): the controller's own auto blink timer.
        public enum BlinkStep
        {
            WaitStart = 0,
            SyncBlink = 1,
        }

        public BlinkStep _autoBlinkStep;                 // 0x80
        public float _autoBlinkTimeCount;                // 0x84
        public float _autoBlinkTimeInterval;             // 0x88 (Initialize: Random.Range(3, 5))
        public bool _isRepressAutoBlink;                 // 0xC4
        public int _blinkRno;                            // 0x15C
        public bool IsUpdateAutoBlink = true;            // 0x15A (ctor: true; LiveModelController.Pause / Resume toggle it)

        // AutoBlinkIntervalMin / Max: DAT_7ffd5a640d7c / DAT_7ffd5a640fc8 (3.0 / 5.0).
        private const float kAutoBlinkIntervalMin = 3f;
        private const float kAutoBlinkIntervalMax = 5f;

        // ModelController +0x380 (StoredFacePartsSet): the last parts handed to ModelController.SetFaceEyeType /
        // SetFaceEyebrowType / SetFaceMouthType, kept BY REFERENCE (StoreFacePatsSet @0x7ffd59681380). The eye blink
        // restores these parts when it ends.
        private readonly FacePartsSet _storedFacePartsSet = new FacePartsSet
        {
            _eyeR = new FaceParts[0],
            _eyeL = new FaceParts[0],
            _eyebrowR = new FaceParts[0],
            _eyebrowL = new FaceParts[0],
            _mouth = new FaceParts[0],
        };

        private readonly Gallop.Model.Component.EyeBlinkController _eyeBlinkController;  // ModelController +0x3B8

        // LiveFaceController.FacePartsSetInfo (dump_jp TypeDefIndex 33583195): the parts state resolved from the
        // timeline keys. FacePartsSetArray is a level stack: level k holds arrays of capacity k + 1 (Initialize
        // creates the levels with CreateFacePartsSet(1..6)) and a group's entries live in the level indexed by
        // (group count - 1) - the appliers read sets[max(count - 1, 0)].
        public class FacePartsSetInfo
        {
            public FacePartsSet[] FacePartsSetArray; // 0x10
            public int EyeRCount; // 0x18
            public int EyeLCount; // 0x1C
            public int EyebrowRCount; // 0x20
            public int EyebrowLCount; // 0x24
            public int MouthCount; // 0x28
        }

        private const int kFacePartsSetTempItemNum = 1; // FACE_PARTS_SET_TEMP_ITEM_NUM
        private const int kFacePartsSetLevelNum = 6;    // Initialize: FacePartsSetArray length

        private FacePartsSetInfo _facePartsSet;          // 0x68
        private FacePartsSet _facePartsSetTemp;          // 0x70
        private FacePartsSetInfo _prevFacePartsSetInfo;  // 0x78

        private readonly EarWeightGroup _earWeightGroup;
        private readonly IFacialPartsApplier _applier;

        // Per-FaceGroupType weight ramps (the game's EyeWeightGroup / EyebrowWeightGroup / MouthWeightGroup at
        // DrivenKeyComponent +0x70 / +0x78 / +0x80).
        private readonly Dictionary<FaceGroupType, FacePartsWeightRamp> _ramps
            = new Dictionary<FaceGroupType, FacePartsWeightRamp>();

        public LiveFaceController(EarWeightGroup earWeightGroup, IFacialPartsApplier applier)
        {
            _earWeightGroup = earWeightGroup;
            _applier = applier;

            // Initialize (0x4EC3710) parts section: _prevFacePartsSetInfo (0x78) and _facePartsSet (0x68) get
            // FacePartsSetArray[6], level k = CreateFacePartsSet(k + 1); _facePartsSetTemp (0x70) =
            // CreateFacePartsSet(1).
            _facePartsSet = new FacePartsSetInfo();
            _prevFacePartsSetInfo = new FacePartsSetInfo();
            _facePartsSet.FacePartsSetArray = new FacePartsSet[kFacePartsSetLevelNum];
            _prevFacePartsSetInfo.FacePartsSetArray = new FacePartsSet[kFacePartsSetLevelNum];
            for (int i = 0; i < kFacePartsSetLevelNum; i++)
            {
                _facePartsSet.FacePartsSetArray[i] = CreateFacePartsSet(i + 1);
                _prevFacePartsSetInfo.FacePartsSetArray[i] = CreateFacePartsSet(i + 1);
            }
            _facePartsSetTemp = CreateFacePartsSet(kFacePartsSetTempItemNum);

            foreach (FaceGroupType type in new[]
                { FaceGroupType.EyeR, FaceGroupType.EyeL, FaceGroupType.EyebrowR, FaceGroupType.EyebrowL, FaceGroupType.Mouth })
            {
                int size = _applier != null ? _applier.GetPartsCount(type) + 1 : 1;
                _ramps[type] = new FacePartsWeightRamp(size);
            }

            // Initialize (0x4EC3710): _autoBlinkTimeInterval = Random.Range(3, 5).
            _autoBlinkTimeInterval = UnityEngine.Random.Range(kAutoBlinkIntervalMin, kAutoBlinkIntervalMax);

            // ModelController.InitEyeBlinkController @0x7ffd59674b00: MakeEyeBlinkController(Context{StoredFacePartsSet})
            // and IsDisableByOverride = FaceOverrideData.IsDisableEyeBlink.
            _eyeBlinkController = new Gallop.Model.Component.EyeBlinkController(this, _storedFacePartsSet);
            // LiveModelController.Init @0x7ffd59b36b70 -> ModelController.SetAutoBlinkEnable(false, true): in a live the
            // random auto blink is OFF (IsAutoBlink false, IsEnabledAutoDoubleBlink true). The characters only blink when
            // a facial key asks for it (IsBlink -> RequestBlink in UpdateEye); game capture of live 1001, slot 0: 35 key
            // blinks, 0 random, 0 double over frames 1249-7712.
            _eyeBlinkController.IsAutoBlink = false;
            if (_applier != null)
            {
                _eyeBlinkController.IsDisableByOverride = _applier.IsEyeBlinkDisabledByOverride;
            }
        }

        // LiveFaceController.CreateFacePartsSet(int) RVA 0x4EC30A0: a FacePartsSet whose five FaceParts arrays
        // have `num` zeroed entries.
        private static FacePartsSet CreateFacePartsSet(int num)
        {
            var set = new FacePartsSet();
            set._eyeR = new FaceParts[num];
            set._eyeL = new FaceParts[num];
            set._eyebrowR = new FaceParts[num];
            set._eyebrowL = new FaceParts[num];
            set._mouth = new FaceParts[num];
            return set;
        }

        // Gallop.Math.IsFloatEqualLight RVA 0x204AE80 (@0x7ffd563cae80): |a - b| <= 1e-6
        // (DAT_7ffd5a640d34 = 1e-6).
        private static bool IsFloatEqualLight(float a, float b)
        {
            return a >= b - 1e-6f && a <= b + 1e-6f;
        }

        // LiveFaceController.UpdateWeight RVA 0x4EC6220: clamp01(weight * 0.01).
        // DAT_7ffd5a640d4c = 0.01, DAT_7ffd5a640d6c = 1.0.
        public static void UpdateWeight(int weight, ref float outWeight)
        {
            float w = weight * 0.01f;
            if (0f <= w && w <= 1f)
            {
                outWeight = w;
                return;
            }
            outWeight = (0f <= w) ? 1f : 0f;
        }

        // LiveFaceController.UpdateDurationTime RVA 0x4EC4E90.
        // speed != 0: frames = FACIAL_SPEED_FRAME_DIC[speed]; when the speed is not in the dictionary the out value is
        // left untouched. speed == 0: frames = time. Result = frames * DAT_7ffd5a640e78 (0x3C888889 = 1/60).
        public static void UpdateDurationTime(int speed, int time, ref float outDuration)
        {
            int frames = time;
            if (speed != 0)
            {
                if (!LiveTimelineDefine.FACIAL_SPEED_FRAME_DIC.TryGetValue(speed, out frames))
                {
                    return;
                }
            }
            outDuration = frames * 0.016666668f;
        }

        // LiveFaceController.GetFacePartsSetFromFacialId RVA 0x4EC32D0: fills _facePartsSetTemp (capacity 1) from
        // LiveTimelineDefine.FACIAL_PARTS_SET_DIC[facialId]. The dictionary's FacialPartsSet layout
        // (Ear, EyebrowL, EyebrowR, EyeL, EyeR, Mouth, MouthWeight) matches the game's value object offsets
        // (+0x10 Ear, +0x14 EyebrowL, +0x18 EyebrowR, +0x1c EyeL, +0x20 EyeR, +0x24 Mouth, +0x28 MouthWeight);
        // the mouth entry uses MouthWeight as its _weight, every other entry 1.0.
        private bool GetFacePartsSetFromFacialId(int facialId)
        {
            if (!LiveTimelineDefine.FACIAL_PARTS_SET_DIC.TryGetValue(facialId, out var entry))
            {
                return false;
            }

            SetTempEntry(_facePartsSetTemp._eyebrowL, entry.EyebrowL, 1f);
            SetTempEntry(_facePartsSetTemp._eyebrowR, entry.EyebrowR, 1f);
            SetTempEntry(_facePartsSetTemp._eyeL, entry.EyeL, 1f);
            SetTempEntry(_facePartsSetTemp._eyeR, entry.EyeR, 1f);
            SetTempEntry(_facePartsSetTemp._mouth, entry.Mouth, entry.MouthWeight);
            return true;
        }

        private static void SetTempEntry(FaceParts[] array, int facePartsId, float weight)
        {
            if (array == null || array.Length == 0) return;
            array[0]._faceParts = facePartsId;
            array[0]._weight = weight;
        }

        // LiveFaceController.UpdateFacePartsSetFromFacialId RVA 0x4EC54A0: resolves a facialId into the parts
        // state of one FaceGroupSet at the key's weight. Level 0 (temp capacity 1 -> one entry per group).
        private void UpdateFacePartsSetFromFacialId(int facialId, int weight, FaceGroupSet group)
        {
            if (!GetFacePartsSetFromFacialId(facialId)) return;

            float w = 0f;
            UpdateWeight(weight, ref w);

            var set = _facePartsSet;
            FacePartsSet top = set.FacePartsSetArray[0];
            FacePartsSet temp = _facePartsSetTemp;
            switch (group)
            {
                case FaceGroupSet.Eye:
                    set.EyeLCount = temp._eyeL.Length;
                    set.EyeRCount = temp._eyeR.Length;
                    temp._eyeL.CopyTo(top._eyeL, 0);
                    temp._eyeR.CopyTo(top._eyeR, 0);
                    ScaleWeights(top._eyeL, set.EyeLCount, w);
                    ScaleWeights(top._eyeR, set.EyeRCount, w);
                    break;
                case FaceGroupSet.Eyebrow:
                    set.EyebrowLCount = temp._eyebrowL.Length;
                    set.EyebrowRCount = temp._eyebrowR.Length;
                    temp._eyebrowL.CopyTo(top._eyebrowL, 0);
                    temp._eyebrowR.CopyTo(top._eyebrowR, 0);
                    ScaleWeights(top._eyebrowL, set.EyebrowLCount, w);
                    ScaleWeights(top._eyebrowR, set.EyebrowRCount, w);
                    break;
                case FaceGroupSet.Mouth:
                    set.MouthCount = temp._mouth.Length;
                    temp._mouth.CopyTo(top._mouth, 0);
                    ScaleWeights(top._mouth, set.MouthCount, w);
                    break;
            }
        }

        private static void ScaleWeights(FaceParts[] array, int count, float scale)
        {
            for (int i = 0; i < count && i < array.Length; i++)
            {
                array[i]._weight *= scale;
            }
        }

        // LiveFaceController.UpdateFacePartsSetFromFacialPartsDataArray RVA 0x4EC58E0: resolves a timeline key's
        // FacialPartsData[] into one face group of the parts state. The count selects the level
        // (FacePartsSetArray[max(count - 1, 0)]), the ids go through the identity FacePartsSetInfo.*Dic maps and
        // the weights are WeightPer * 0.01 (the game does NOT apply the key's own weight on this path).
        private void UpdateFacePartsSetFromFacialPartsDataArray(FacialPartsData[] partsArray, FaceGroupType type)
        {
            if (partsArray == null) return; // the game raises here; the viewer skips an empty key field

            int count = partsArray.Length;
            var set = _facePartsSet;
            FacePartsSet top = set.FacePartsSetArray[Mathf.Max(count - 1, 0)];

            switch (type)
            {
                case FaceGroupType.EyeR:
                    set.EyeRCount = count;
                    FillParts(top._eyeR, partsArray, count, Gallop.FacePartsSetInfo.FacialEyeDic);
                    break;
                case FaceGroupType.EyeL:
                    set.EyeLCount = count;
                    FillParts(top._eyeL, partsArray, count, Gallop.FacePartsSetInfo.FacialEyeDic);
                    break;
                case FaceGroupType.EyebrowR:
                    set.EyebrowRCount = count;
                    FillParts(top._eyebrowR, partsArray, count, Gallop.FacePartsSetInfo.FacialEyebrowDic);
                    break;
                case FaceGroupType.EyebrowL:
                    set.EyebrowLCount = count;
                    FillParts(top._eyebrowL, partsArray, count, Gallop.FacePartsSetInfo.FacialEyebrowDic);
                    break;
                case FaceGroupType.Mouth:
                    set.MouthCount = count;
                    FillParts(top._mouth, partsArray, count, Gallop.FacePartsSetInfo.FacialMouthDic);
                    break;
            }
        }

        private static void FillParts(FaceParts[] target, FacialPartsData[] partsArray, int count,
            Dictionary<int, int> idDic)
        {
            for (int i = 0; i < count && i < target.Length; i++)
            {
                // The game uses the Dictionary indexer (throws on a missing id); ids outside the ported
                // dictionaries are skipped here (same adaptation as the FacialEarDic lookup).
                if (idDic.TryGetValue(partsArray[i].FacialPartsId, out int faceParts))
                {
                    target[i]._faceParts = faceParts;
                }
                target[i]._weight = partsArray[i].WeightPer * 0.01f;
            }
        }

        // Gallop.FacePartsUtil.IsEqualFacePartsArray(FaceParts[], int, FaceParts[], int) RVA 0x4B01F40
        // (@0x7ffd58e81f40): same count, same _faceParts ids and IsFloatEqualLight weights.
        private static bool IsEqualFacePartsArray(FaceParts[] a, int aCount, FaceParts[] b, int bCount)
        {
            if (a == null) return b == null;
            if (b == null || aCount != bCount) return false;
            for (int i = 0; i < aCount && i < a.Length && i < b.Length; i++)
            {
                if (a[i]._faceParts != b[i]._faceParts) return false;
                if (!IsFloatEqualLight(a[i]._weight, b[i]._weight)) return false;
            }
            return true;
        }

        // LiveFaceController.IsEqualFacePartsArray(FaceGroupSet, FacePartsSetInfo, FacePartsSetInfo) RVA 0x4EC39C0:
        // compares the group's arrays at the level selected by each side's counts.
        private bool IsEqualFacePartsArray(FaceGroupSet group, FacePartsSetInfo a, FacePartsSetInfo b)
        {
            if (a == null || b == null) return false;
            switch (group)
            {
                case FaceGroupSet.Eye:
                    return IsEqualFacePartsArray(GroupArray(a, a.EyeLCount, FaceGroupType.EyeL), a.EyeLCount,
                                                 GroupArray(b, b.EyeLCount, FaceGroupType.EyeL), b.EyeLCount)
                        && IsEqualFacePartsArray(GroupArray(a, a.EyeRCount, FaceGroupType.EyeR), a.EyeRCount,
                                                 GroupArray(b, b.EyeRCount, FaceGroupType.EyeR), b.EyeRCount);
                case FaceGroupSet.Eyebrow:
                    return IsEqualFacePartsArray(GroupArray(a, a.EyebrowLCount, FaceGroupType.EyebrowL), a.EyebrowLCount,
                                                 GroupArray(b, b.EyebrowLCount, FaceGroupType.EyebrowL), b.EyebrowLCount)
                        && IsEqualFacePartsArray(GroupArray(a, a.EyebrowRCount, FaceGroupType.EyebrowR), a.EyebrowRCount,
                                                 GroupArray(b, b.EyebrowRCount, FaceGroupType.EyebrowR), b.EyebrowRCount);
                case FaceGroupSet.Mouth:
                    return IsEqualFacePartsArray(GroupArray(a, a.MouthCount, FaceGroupType.Mouth), a.MouthCount,
                                                 GroupArray(b, b.MouthCount, FaceGroupType.Mouth), b.MouthCount);
            }
            return true;
        }

        // LiveFaceController.CopyFacePartsArray(FaceGroupSet, FacePartsSetInfo, FacePartsSetInfo) RVA 0x4EC2E50:
        // copies the group's counts and arrays (at the level selected by each count) into the destination.
        private void CopyFacePartsArray(FaceGroupSet group, FacePartsSetInfo src, FacePartsSetInfo dst)
        {
            if (src == null || dst == null) return;
            switch (group)
            {
                case FaceGroupSet.Eye:
                    dst.EyeRCount = src.EyeRCount;
                    dst.EyeLCount = src.EyeLCount;
                    CopyGroup(src, src.EyeLCount, dst, src.EyeLCount, FaceGroupType.EyeL);
                    CopyGroup(src, src.EyeRCount, dst, src.EyeRCount, FaceGroupType.EyeR);
                    break;
                case FaceGroupSet.Eyebrow:
                    dst.EyebrowLCount = src.EyebrowLCount;
                    dst.EyebrowRCount = src.EyebrowRCount;
                    CopyGroup(src, src.EyebrowLCount, dst, src.EyebrowLCount, FaceGroupType.EyebrowL);
                    CopyGroup(src, src.EyebrowRCount, dst, src.EyebrowRCount, FaceGroupType.EyebrowR);
                    break;
                case FaceGroupSet.Mouth:
                    dst.MouthCount = src.MouthCount;
                    CopyGroup(src, src.MouthCount, dst, src.MouthCount, FaceGroupType.Mouth);
                    break;
            }
        }

        private void CopyGroup(FacePartsSetInfo src, int srcLevel, FacePartsSetInfo dst, int dstLevel, FaceGroupType type)
        {
            FaceParts[] srcArray = GroupArray(src, srcLevel, type);
            FaceParts[] dstArray = GroupArray(dst, dstLevel, type);
            if (srcArray == null || dstArray == null) return;
            System.Array.Copy(srcArray, dstArray, Mathf.Min(srcArray.Length, dstArray.Length));
        }

        // The group's entries live in the level indexed by (count - 1); level sizes are 1..6, counts beyond the
        // stack use the top level (the game would throw an index out of range).
        private static FaceParts[] GroupArray(FacePartsSetInfo info, int count, FaceGroupType type)
        {
            FacePartsSet[] sets = info.FacePartsSetArray;
            int level = Mathf.Clamp(count - 1, 0, sets.Length - 1);
            FacePartsSet set = sets[level];
            switch (type)
            {
                case FaceGroupType.EyeR: return set._eyeR;
                case FaceGroupType.EyeL: return set._eyeL;
                case FaceGroupType.EyebrowR: return set._eyebrowR;
                case FaceGroupType.EyebrowL: return set._eyebrowL;
                case FaceGroupType.Mouth: return set._mouth;
            }
            return null;
        }

        // ModelController.SetFaceParts(FaceParts[], FaceGroupType, float duration, bool, bool) -> the group's
        // weight ramp (SetFace above). The scale on this path is always 1.0 (SetFaceEyeType / SetFaceEyebrowType /
        // SetFaceMouthType pass DAT_7ffd5a640d6c = 1.0).
        private void SetFaceParts(FaceGroupType type, FaceParts[] parts, float duration)
        {
            if (_applier == null) return;
            StoreFacePartsSet(type, parts);
            ApplyFaceParts(type, parts, duration);
        }

        // DrivenKeyComponent.SetFaceEyeType / SetFaceEyebrowType / SetFaceMouthType -> SetFace on the group's ramp,
        // without storing the parts (the eye blink uses this path directly).
        private void ApplyFaceParts(FaceGroupType type, FaceParts[] parts, float duration)
        {
            if (_applier == null) return;
            if (_ramps.TryGetValue(type, out var ramp))
            {
                ramp.SetFace(parts, 1f, duration);
            }
        }

        // ModelController.StoreFacePatsSet @0x7ffd59681380: the parts array of each group is stored by reference
        // (group 2 EyeR +0x10, 3 EyeL +0x18, 4 EyebrowR +0x20, 5 EyebrowL +0x28, 6 Mouth +0x30).
        private void StoreFacePartsSet(FaceGroupType type, FaceParts[] parts)
        {
            switch (type)
            {
                case FaceGroupType.EyeR: _storedFacePartsSet._eyeR = parts; break;
                case FaceGroupType.EyeL: _storedFacePartsSet._eyeL = parts; break;
                case FaceGroupType.EyebrowR: _storedFacePartsSet._eyebrowR = parts; break;
                case FaceGroupType.EyebrowL: _storedFacePartsSet._eyebrowL = parts; break;
                case FaceGroupType.Mouth: _storedFacePartsSet._mouth = parts; break;
            }
        }

        // ---- Gallop.Model.Component.EyeBlinkController.IOwner (DrivenKeyComponent side of the blink) ----

        // DrivenKeyComponent.SetFaceEyeBlink: EYE_R_ARRAY writes only the right eye group and EYE_L_ARRAY only the left
        // one (SetFace is called for every group, but only the groups listed in the array take the parts).
        void Gallop.Model.Component.EyeBlinkController.IOwner.SetFaceEyeBlink(FaceParts[] eyeR, FaceParts[] eyeL, float duration)
        {
            ApplyFaceParts(FaceGroupType.EyeR, eyeR, duration);
            ApplyFaceParts(FaceGroupType.EyeL, eyeL, duration);
        }

        void Gallop.Model.Component.EyeBlinkController.IOwner.SetFaceEyeType(FaceParts[] parts, FaceGroupType group, float duration)
        {
            ApplyFaceParts(group, parts, duration);
        }

        void Gallop.Model.Component.EyeBlinkController.IOwner.DisableDelayedTracking()
        {
            _applier?.DisableDelayedEyeTracking();
        }

        bool Gallop.Model.Component.EyeBlinkController.IOwner.IsSuppressEyeBlink()
        {
            return _applier != null && _applier.IsEyeHighlightSuppressingBlink();
        }

        // ModelController.IsEyeBlinking @0x7ffd59677cb0.
        private bool IsEyeBlinking()
        {
            return _eyeBlinkController != null && _eyeBlinkController.IsEyeBlinking;
        }

        // EyeBlinkController.BeforeApplyDrivenKey: runs once per frame, after the facial keys of the frame were applied
        // and before the weight ramps advance (the game runs it in the model component update, before the driven keys).
        //
        // Viewer-only (paused): the game never seeks or scrubs a paused show (its pause stops the component with
        // _isPause, freezing a blink in flight). The viewer shows the key faces while paused or dragging the slider and
        // SetEye waits during a blink, so a blink in flight is cancelled instead (Clear) and the eye group is forced to
        // re-apply the key parts, which the blink may have left half closed.
        public void UpdateEyeBlink(float deltaTime, bool paused)
        {
            if (_eyeBlinkController == null) return;
            if (paused)
            {
                if (_eyeBlinkController.IsBlinking)
                {
                    _eyeBlinkController.Clear();
                    _forceUpdateEye = true;
                }
                return;
            }
            _eyeBlinkController.BeforeApplyDrivenKey(deltaTime);
        }

        // ModelController.ResetEyeCurrentTime @0x7ffd59678c00 -> DrivenKeyComponent.ResetEyeCurrentTime @0x7ffd58e70a10:
        // the duration and current time of both eye groups are zeroed (an in flight eye ramp stops where it is).
        private void ResetEyeCurrentTime()
        {
            foreach (FaceGroupType type in new[] { FaceGroupType.EyeR, FaceGroupType.EyeL })
            {
                if (_ramps.TryGetValue(type, out var ramp))
                {
                    ramp.DurationTime = 0f;
                    ramp.CurrentTime = 0f;
                }
            }
        }

        // ModelController.SetFaceInterpolateType(FaceGroupType, InterpolateType) -> DrivenKeyComponent.
        // SetInterpolateType (per-group slot; the ear port stores the ear slot at +0x248).
        private void SetFaceInterpolateType(FaceGroupType type, DrivenKeyComponent.InterpolateType interpolateType)
        {
            if (_ramps.TryGetValue(type, out var ramp))
            {
                ramp.InterpolateType = interpolateType;
            }
        }

        // LiveFaceController.SetEye RVA 0x4EC3FF0. The game skips the eye update while the character is mid blink
        // (ModelController.IsEyeBlinking); the key is applied on a later frame once the blink ended. Not ported: the
        // DrivenKeyTarget enabled flag (+0xf0).
        public void SetEye()
        {
            if (IsEyeBlinking()) return;
            if (_applier == null || _facePartsSet == null) return;
            if (!(0 < _facePartsSet.EyeLCount && 0 < _facePartsSet.EyeRCount)) return;

            if (IsEqualFacePartsArray(FaceGroupSet.Eye, _facePartsSet, _prevFacePartsSetInfo)
                && IsFloatEqualLight(_eyeDurationTime, _prevEyeDurationTime)
                && _eyeInterpolateType == _prevEyeInterpolateType
                && !_forceUpdateEye)
            {
                return;
            }

            FaceParts[] eyeL = GroupArray(_facePartsSet, _facePartsSet.EyeLCount, FaceGroupType.EyeL);
            FaceParts[] eyeR = GroupArray(_facePartsSet, _facePartsSet.EyeRCount, FaceGroupType.EyeR);
            SetFaceParts(FaceGroupType.EyeL, eyeL, _eyeDurationTime);
            SetFaceParts(FaceGroupType.EyeR, eyeR, _eyeDurationTime);
            SetFaceInterpolateType(FaceGroupType.EyeL, _eyeInterpolateType);
            SetFaceInterpolateType(FaceGroupType.EyeR, _eyeInterpolateType);
            CopyFacePartsArray(FaceGroupSet.Eye, _facePartsSet, _prevFacePartsSetInfo);
            _prevEyeDurationTime = _eyeDurationTime;
            _prevEyeInterpolateType = _eyeInterpolateType;
            _forceUpdateEye = false;
        }

        // LiveFaceController.SetEyebrow RVA 0x4EC42F0. Game extra not ported: ModelController.
        // SetFacePartsScaleEnabled(usePartsScale) (the eyebrow key's usePartsScale flag, +0x3c).
        public void SetEyebrow()
        {
            if (_applier == null || _facePartsSet == null) return;
            if (!(0 < _facePartsSet.EyebrowLCount && 0 < _facePartsSet.EyebrowRCount)) return;

            if (IsEqualFacePartsArray(FaceGroupSet.Eyebrow, _facePartsSet, _prevFacePartsSetInfo)
                && IsFloatEqualLight(_eyebrowDurationTime, _prevEyebrowDurationTime)
                && _eyebrowInterpolateType == _prevEyebrowInterpolateType
                && !_forceUpdateEyebrow)
            {
                return;
            }

            FaceParts[] eyebrowL = GroupArray(_facePartsSet, _facePartsSet.EyebrowLCount, FaceGroupType.EyebrowL);
            FaceParts[] eyebrowR = GroupArray(_facePartsSet, _facePartsSet.EyebrowRCount, FaceGroupType.EyebrowR);
            SetFaceParts(FaceGroupType.EyebrowL, eyebrowL, _eyebrowDurationTime);
            SetFaceParts(FaceGroupType.EyebrowR, eyebrowR, _eyebrowDurationTime);
            SetFaceInterpolateType(FaceGroupType.EyebrowL, _eyebrowInterpolateType);
            SetFaceInterpolateType(FaceGroupType.EyebrowR, _eyebrowInterpolateType);
            CopyFacePartsArray(FaceGroupSet.Eyebrow, _facePartsSet, _prevFacePartsSetInfo);
            _prevEyebrowDurationTime = _eyebrowDurationTime;
            _prevEyebrowInterpolateType = _eyebrowInterpolateType;
            _forceUpdateEyebrow = false;
        }

        // LiveFaceController.SetMouth RVA 0x4EC4570. The game compares the mouth arrays with the 4-arg
        // Gallop.FacePartsUtil.IsEqualFacePartsArray (level selected by each side's count) and copies the parts
        // into the prev info inline at the level selected by the CURRENT count.
        public void SetMouth()
        {
            if (_applier == null || _facePartsSet == null) return;
            if (!(0 < _facePartsSet.MouthCount)) return;
            if (_prevFacePartsSetInfo == null) return;

            FaceParts[] cur = GroupArray(_facePartsSet, _facePartsSet.MouthCount, FaceGroupType.Mouth);
            FaceParts[] prev = GroupArray(_prevFacePartsSetInfo, _prevFacePartsSetInfo.MouthCount, FaceGroupType.Mouth);
            if (IsEqualFacePartsArray(cur, _facePartsSet.MouthCount, prev, _prevFacePartsSetInfo.MouthCount)
                && IsFloatEqualLight(_mouthDurationTime, _prevMouthDurationTime)
                && _mouthInterpolateType == _prevMouthInterpolateType
                && !_forceUpdateMouth)
            {
                return;
            }

            FaceParts[] mouth = GroupArray(_facePartsSet, _facePartsSet.MouthCount, FaceGroupType.Mouth);
            SetFaceParts(FaceGroupType.Mouth, mouth, _mouthDurationTime);
            SetFaceInterpolateType(FaceGroupType.Mouth, _mouthInterpolateType);

            _prevFacePartsSetInfo.MouthCount = _facePartsSet.MouthCount;
            FaceParts[] prevTop = GroupArray(_prevFacePartsSetInfo, _facePartsSet.MouthCount, FaceGroupType.Mouth);
            if (mouth != null && prevTop != null)
            {
                System.Array.Copy(mouth, prevTop, Mathf.Min(mouth.Length, prevTop.Length));
            }
            _prevMouthDurationTime = _mouthDurationTime;
            _prevMouthInterpolateType = _mouthInterpolateType;
            _forceUpdateMouth = false;
        }

        // LiveFaceController.UpdateEye RVA 0x4EC5030 (called between SetMouth and the eyebrow key block). The
        // game picks the newer of the face key and the eye key for the EYE group (frame comparison when both are
        // active), resolves the parts, runs the key blink request and hands the parts to SetEye. NOT ported
        // (ModelController side): the pupil flag (EnableEyePupli(!IsDisableEyePupli)). The "next key starts within one
        // second" pre-repression check is unreachable in the game (see the blink request below).
        public void UpdateEye(float time, int frameRate, ref FacialDataUpdateInfo info)
        {
            bool faceActive = info.faceCur != null && !info.faceCur.IsNop;
            bool eyeActive = info.eyeCur != null && !info.eyeCur.IsNop;

            int phase;
            if (faceActive && eyeActive)
            {
                phase = info.faceCur.frame <= info.eyeCur.frame ? 2 : 1;
            }
            else if (eyeActive)
            {
                phase = 2;
            }
            else if (faceActive)
            {
                phase = 1;
            }
            else
            {
                return;
            }

            // keyFrame / isBlink: the chosen key's frame (+0x10) and IsBlink (attribute 0x20000, get_IsBlink 0x4F70650),
            // the inputs of the blink request below.
            int keyFrame;
            bool isBlink;
            if (phase == 1)
            {
                LiveTimelineKeyFacialFaceData key = info.faceCur;
                keyFrame = key.frame;
                isBlink = key.IsBlink;
                UpdateFacePartsSetFromFacialId(key.facialId, key.weight, FaceGroupSet.Eye);
                UpdateDurationTime(key.speed, key.time, ref _eyeDurationTime);
                _eyeInterpolateType = key.interpolateType;
            }
            else
            {
                LiveTimelineKeyFacialEyeData key = info.eyeCur;
                keyFrame = key.frame;
                isBlink = key.IsBlink;
                if (!key.IsEnabledFaceParts)
                {
                    UpdateFacePartsSetFromFacialId(key.facialId, key.weight, FaceGroupSet.Eye);
                }
                else
                {
                    UpdateFacePartsSetFromFacialPartsDataArray(key.facialPartsDataArrayL, FaceGroupType.EyeL);
                    UpdateFacePartsSetFromFacialPartsDataArray(key.facialPartsDataArrayR, FaceGroupType.EyeR);
                }
                UpdateDurationTime(key.speed, key.time, ref _eyeDurationTime);
                _eyeInterpolateType = key.interpolateType;
            }

            // Blink request of the key (UpdateEye @0x7ffd59245030, after EnableEyePupli). _isRepressAutoBlink (0xC4) is
            // written true unconditionally here, so the "next key starts within one second" pre-check the game has
            // behind `if (!_isRepressAutoBlink)` is unreachable and the controller's own timer below never counts.
            _isRepressAutoBlink = true;
            if (_currentStartFrameOfEye == keyFrame)
            {
                if (isBlink)
                {
                    RequestKeyBlink();
                }
            }
            else
            {
                // A new key: _blinkRno restarts, and a key with IsBlink also stops the eye ramp in flight and blinks.
                _blinkRno = 0;
                if (isBlink)
                {
                    ResetEyeCurrentTime();
                    RequestKeyBlink();
                }
            }

            if (IsUpdateAutoBlink)
            {
                if (_autoBlinkStep == BlinkStep.WaitStart)
                {
                    if (!_isRepressAutoBlink)
                    {
                        _autoBlinkTimeCount += Time.deltaTime;
                        if (_autoBlinkTimeCount > _autoBlinkTimeInterval)
                        {
                            _eyeBlinkController?.RequestBlink(false);
                            _autoBlinkStep = BlinkStep.SyncBlink;
                        }
                    }
                    else
                    {
                        _autoBlinkTimeCount = 0f;
                    }
                }
                else if (_autoBlinkStep == BlinkStep.SyncBlink)
                {
                    if (!IsEyeBlinking())
                    {
                        _autoBlinkStep = BlinkStep.WaitStart;
                        _autoBlinkTimeInterval = UnityEngine.Random.Range(kAutoBlinkIntervalMin, kAutoBlinkIntervalMax);
                    }
                }
            }

            _currentStartFrameOfEye = keyFrame;
            SetEye();
        }

        // The _blinkRno state machine of UpdateEye (0x15C): 0 -> RequestBlink(false), 1 waits for the blink to end
        // (IsEyeBlinking), 2 = done for this key.
        private void RequestKeyBlink()
        {
            if (_blinkRno == 0)
            {
                _blinkRno = 1;
                _eyeBlinkController?.RequestBlink(false);
            }
            else if (_blinkRno == 1)
            {
                if (!IsEyeBlinking())
                {
                    _blinkRno = 2;
                }
            }

            if (_blinkRno <= 1)
            {
                _isRepressAutoBlink = true;
            }
        }

        // LiveFaceController.AlterUpdateAutoLip @0x7ffd59241fd0. Called by the lip-sync delegate
        // (Director.<InitializeTimeline>b__476_2 @0x7ffd59204110) with the current lip key, the character index and the
        // duration UpdateDurationTime(key.speed, key.time). Only while _isLipSync (0xC5, set by the face / mouth key
        // blocks of AlterUpdateFacialNew) and when the key's character flag has this character: the mouth parts come
        // from the key (facialId/weight, or facialPartsDataArray when IsEnabledFaceParts 0x20000), then the mouth
        // duration (+0x30) / interpolate type (+0x3C) and SetMouth - the same state machine as the facial mouth key.
        // The flag is read through GetCharacterFlag(Live3DSettings.SongPartVariationId, +0xF4), as in the game. The viewer
        // has no source for that id yet (the game's writer of +0xF4 is not traced), so it stays 0 = the key's own flag.
        public static int SongPartVariationId;
        public void AlterUpdateAutoLip(Gallop.Live.Cutt.LiveTimelineKeyLipSyncData key, int charaIndex, float duration)
        {
            if (!_isLipSync || key == null) return;
            if ((((long)key.GetCharacterFlag(SongPartVariationId) >> charaIndex) & 1) == 0) return;

            if (((int)key.attribute & 0x20000) == 0)
            {
                UpdateFacePartsSetFromFacialId(key.facialId, key.weight, FaceGroupSet.Mouth);
            }
            else
            {
                UpdateFacePartsSetFromFacialPartsDataArray(key.facialPartsDataArray, FaceGroupType.Mouth);
            }
            _mouthDurationTime = duration;
            _mouthInterpolateType = key.interpolateType;
            SetMouth();
        }

        // LiveFaceController.SetRandomEarState RVA 0x2008DA0 (writes +0x44). Called by Director.UpdateRandomEarMotion.
        public void SetRandomEarState(bool isRandom)
        {
            _earRandomMotion = isRandom;
        }

        // Face / mouth / eye / eyebrow sections of LiveFaceController.AlterUpdateFacialNew (RVA 0x4EC20E0), in
        // the game's order: face key block -> mouth key block -> SetMouth -> UpdateEye -> eyebrow key block ->
        // SetEyebrow. The ear key block + SetEar run next (AlterUpdateFacialNew_Ear) and the effect key block
        // last (FaceDrivenKeyTarget.AlterUpdateFacialNew), completing the game's order. Not ported here: the
        // SystemText facial branch at the head of the game function and the cheek-noise time argument (the game
        // passes GetFacialNoiseCurrentTime as `time`).
        public void AlterUpdateFacialNew_FaceParts(ref FacialDataUpdateInfo updateInfo, float time, int frameRate)
        {
            var faceKey = updateInfo.faceCur;
            _isLipSync = true; // game writes +0xC5 = 1 before the face key block

            int faceFrame = -1;
            if (faceKey != null && !faceKey.IsNop)
            {
                // Face key block (0x4EC20E0): the EYEBROW group state comes from the face key (the game calls
                // UpdateFacePartsSetFromFacialId(facialId, weight, FaceGroupSet.Eyebrow) here) plus the face's
                // duration / interpolate type (this+0x28 / this+0x38). The ear part of the same block
                // (FACIAL_PARTS_SET_DIC[facialId].Ear to both ears) is in AlterUpdateFacialNew_Ear.
                UpdateFacePartsSetFromFacialId(faceKey.facialId, faceKey.weight, FaceGroupSet.Eyebrow);
                float faceDuration = 0f;
                UpdateDurationTime(faceKey.speed, faceKey.time, ref faceDuration);
                _eyebrowDurationTime = faceDuration;
                _eyebrowInterpolateType = faceKey.interpolateType;
                faceFrame = faceKey.frame;
            }

            // Mouth key block: no face-frame gate in the game. kAttrEnableFaceParts (0x20000) selects the
            // facialPartsDataArray path (the game ignores the key weight there).
            var mouthKey = updateInfo.mouth;
            if (mouthKey != null && !mouthKey.IsNop)
            {
                _isLipSync = false;
                if (!mouthKey.IsEnabledFaceParts)
                {
                    UpdateFacePartsSetFromFacialId(mouthKey.facialId, mouthKey.weight, FaceGroupSet.Mouth);
                }
                else
                {
                    UpdateFacePartsSetFromFacialPartsDataArray(mouthKey.facialPartsDataArray, FaceGroupType.Mouth);
                }
                UpdateDurationTime(mouthKey.speed, mouthKey.time, ref _mouthDurationTime);
                _mouthInterpolateType = mouthKey.interpolateType;
            }
            SetMouth();

            UpdateEye(time, frameRate, ref updateInfo);

            // Eyebrow key block: gated by faceFrame <= eyebrow.frame (the face key wins when newer). The parts
            // arrays go to EyebrowL (FacialPartsDataArrayL) and EyebrowR (FacialPartsDataArrayR). The game then
            // calls ModelController.SetFacePartsScaleEnabled(usePartsScale) - not ported.
            var eyebrowKey = updateInfo.eyebrow;
            if (eyebrowKey != null && !eyebrowKey.IsNop && faceFrame <= eyebrowKey.frame)
            {
                if (!eyebrowKey.IsEnabledFaceParts)
                {
                    UpdateFacePartsSetFromFacialId(eyebrowKey.facialId, eyebrowKey.weight, FaceGroupSet.Eyebrow);
                }
                else
                {
                    UpdateFacePartsSetFromFacialPartsDataArray(eyebrowKey.facialPartsDataArrayL, FaceGroupType.EyebrowL);
                    UpdateFacePartsSetFromFacialPartsDataArray(eyebrowKey.facialPartsDataArrayR, FaceGroupType.EyebrowR);
                }
                UpdateDurationTime(eyebrowKey.speed, eyebrowKey.time, ref _eyebrowDurationTime);
                _eyebrowInterpolateType = eyebrowKey.interpolateType;
            }
            SetEyebrow();
        }

        // Ear-related parts of LiveFaceController.AlterUpdateFacialNew RVA 0x4EC20E0, in the game's order:
        //  1) face key (FacialDataUpdateInfo.faceCur): when not Nop, the eyebrow group state and
        //     FACIAL_PARTS_SET_DIC[face.facialId].Ear go to both ears with the face weight / duration /
        //     interpolate type, unless the ear key uses random motion; its frame gates the ear key below
        //     (faceFrame <= earFrame).
        //  2) ear key (FacialDataUpdateInfo.ear): skipped when useEarRandomMotion or Nop or older than the face key.
        //     IsEnabledFacialEarId set   -> FacePartsSetInfo.FacialEarDic[facialEarIdL / facialEarIdR];
        //     IsEnabledFacialEarId clear -> FACIAL_PARTS_SET_DIC[faceCur.facialId].Ear for both ears (faceCur may be a
        //                                   Nop key; only its presence is checked). Nothing changes when faceCur is
        //                                   null or its facialId is not in the dictionary.
        //     The ear key's own interpolateType is not read by the game here.
        //  3) SetEar().
        public void AlterUpdateFacialNew_Ear(ref FacialDataUpdateInfo updateInfo)
        {
            var faceKey = updateInfo.faceCur;
            var earKey = updateInfo.ear;

            int faceFrame = -1;
            if (faceKey != null && !faceKey.IsNop)
            {
                float faceWeight = 0f;
                UpdateWeight(faceKey.weight, ref faceWeight);
                float faceDuration = 0f;
                UpdateDurationTime(faceKey.speed, faceKey.time, ref faceDuration);

                if (LiveTimelineDefine.FACIAL_PARTS_SET_DIC.TryGetValue(faceKey.facialId, out var partsSet)
                    && (earKey == null || !earKey.useEarRandomMotion))
                {
                    _earWeight = faceWeight;
                    _earDurationTime = faceDuration;
                    _earTypeL = partsSet.Ear;
                    _earTypeR = partsSet.Ear;
                    _earInterpolateType = faceKey.interpolateType;
                }
                faceFrame = faceKey.frame;
            }

            if (earKey != null && !earKey.useEarRandomMotion && !earKey.IsNop && faceFrame <= earKey.frame)
            {
                bool resolved = false;
                if (!earKey.IsEnabledFacialEarId)
                {
                    if (faceKey != null
                        && LiveTimelineDefine.FACIAL_PARTS_SET_DIC.TryGetValue(faceKey.facialId, out var partsSet))
                    {
                        _earTypeL = partsSet.Ear;
                        _earTypeR = partsSet.Ear;
                        resolved = true;
                    }
                }
                else
                {
                    // The game uses the Dictionary indexer (throws on a missing id); ids outside 0..16 are skipped here.
                    if (Gallop.FacePartsSetInfo.FacialEarDic.TryGetValue(earKey.facialEarIdL, out var earL)
                        && Gallop.FacePartsSetInfo.FacialEarDic.TryGetValue(earKey.facialEarIdR, out var earR))
                    {
                        _earTypeL = earL;
                        _earTypeR = earR;
                        resolved = true;
                    }
                }

                if (resolved)
                {
                    UpdateWeight(earKey.weight, ref _earWeight);
                    UpdateDurationTime(earKey.speed, earKey.time, ref _earDurationTime);
                }
            }

            SetEar();
        }

        // LiveFaceController.SetEar RVA 0x4EC3DC0.
        // ModelController.SetEar (0x52FAF70) forwards to DrivenKeyComponent.SetEar (0x4AF1120) with
        // (type, side, weight, duration, false, false, true): left ear = side 1, right ear = side 0.
        // ModelController.SetFaceInterpolateType(1 / 0, _earInterpolateType) -> DrivenKeyComponent.SetInterpolateType
        // writes the ear slot (+0x248) for both.
        public void SetEar()
        {
            if (_earWeightGroup == null) return;

            if (_earTypeL == _prevEarTypeL && _earTypeR == _prevEarTypeR
                && _earWeight == _prevEarWeight && _earDurationTime == _prevEarDurationTime
                && _earInterpolateType == _prevEarInterpolateType && _earRandomMotion == _prevEarRandomMotion
                && !_forceUpdateEar)
            {
                return;
            }

            if (!_earRandomMotion)
            {
                _earWeightGroup.DrivenKeyComponent_SetEar(_earTypeL, 1, _earWeight, _earDurationTime, false, false, true);
                _earWeightGroup.DrivenKeyComponent_SetEar(_earTypeR, 0, _earWeight, _earDurationTime, false, false, true);
                _earWeightGroup.EarInterpolateType = _earInterpolateType;
            }

            _prevEarTypeL = _earTypeL;
            _prevEarTypeR = _earTypeR;
            _prevEarWeight = _earWeight;
            _prevEarDurationTime = _earDurationTime;
            _prevEarInterpolateType = _earInterpolateType;
            _prevEarRandomMotion = _earRandomMotion;
            _forceUpdateEar = false;
        }

        // Advances the three face-group ramps with the live delta (the game runs them from
        // DrivenKeyComponent.PostJob) and writes the resulting weights into the additive morphs. Like PostJob it
        // runs every frame for groups whose blend list is non-empty (SetFace has targeted them at least once):
        // the weights must be re-applied while a ramp is in flight even when no setter retargeted the group this
        // frame. Groups never targeted are left untouched (their morphs may be driven by other systems, e.g. the
        // lip-sync path).
        public void AdvanceFaceRamps(float deltaTime)
        {
            if (_applier == null) return;
            foreach (var pair in _ramps)
            {
                FacePartsWeightRamp ramp = pair.Value;
                if (!ramp.HasBlendList) continue;
                if (deltaTime > 0f)
                {
                    ramp.BlendProgrammable(deltaTime);
                }
                _applier.SetPartsWeights(pair.Key, ramp.Current, ramp.Current.Length);
                _applier.ApplyParts(pair.Key);
            }
        }

        // Viewer-only: return to the constructor state (LiveFaceController..ctor RVA 0x4EC6250) when the viewer
        // seeks the live backwards.
        public void ResetEar()
        {
            _earWeight = 1f;
            _earDurationTime = 0f;
            _earInterpolateType = DrivenKeyComponent.InterpolateType.Linear;
            _earRandomMotion = false;
            _prevEarInterpolateType = DrivenKeyComponent.InterpolateType.Linear;
            _prevEarRandomMotion = false;
            _earTypeL = EarType.Base;
            _earTypeR = EarType.Base;
            _prevEarWeight = -1f;
            _prevEarDurationTime = -1f;
            _prevEarTypeL = EarType.Base;
            _prevEarTypeR = EarType.Base;
            _forceUpdateEar = false;
        }
    }
}
