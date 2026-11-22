using UnityEngine;

namespace Gallop.Model.Component
{
    /// <summary>
    /// Port of Gallop.Model.Component.EyeBlinkController (dump_jp TypeDefIndex 33586292, code_JP
    /// Gallop.Model.Component.EyeBlinkController.c). One per character (ModelController +0x3B8); it owns the random
    /// auto blink and the key requested blink. Ported methods (RVA / address):
    ///   ctor 0x51FF190, OnInitialize 0x51FE180, Clear 0x51FDA00, SetIntervalTime 0x51FEA20,
    ///   BeforeApplyDrivenKey 0x51FD350 (@0x7ffd5957d350), UpdateBlink 0x51FEA50 (@0x7ffd5957ea50),
    ///   UpdateFaceEye 0x51FEFB0, RequestBlink 0x51FE410, RequestBlinkInternal 0x51FE3C0,
    ///   GetNextBlinkStep / GetWaitTime / GetCloseEyeKeepTime / GetOpenEyeKeepTime, get_IsBlinking 0x51FF1E0,
    ///   IsSuppressEyeBlink 0x51FDFE0, CheckIgnore 0x51FD6A0 / 0x51FD830 / 0x51FD890.
    ///
    /// Sequence (BlinkStep): OpenWait (waiting the random interval) -> CloseCall -> CloseUpdate -> CloseWait ->
    /// OpenCall -> OpenUpdate -> OpenWait. A step ends when _timeCount reaches _timeMax; the transition runs
    /// UpdateFaceEye with the step being LEFT: CloseCall and OpenCall apply FaceType.EyeHalfA, CloseUpdate applies
    /// FaceType.EyeClose, OpenUpdate restores the stored eye parts of the facial key, CloseWait / OpenWait apply nothing.
    /// Every step lasts GetDurationTime() = 0.03 s (DAT_7ffd5a8b6a34); CloseUpdate adds the same 0.03 s keep time
    /// (+ EyeBlinkSettingData.ForceEyeCloseTime when the blink was forced) and OpenUpdate adds
    /// Random.Range(_intervalTimeMin, _intervalTimeMax) = (3, 5) s (ctor 0x40400000 / 0x40a00000) before the next blink.
    /// With IsEnabledAutoDoubleBlink (default true) the pattern alternates one single blink and one double blink.
    ///
    /// Data read from the game binary (JP): GetDurationTime 0.03 (0x3cf5c28f), DefaultProbability 100 (0x42c80000),
    /// ForceEyeCloseTime 0x3d888880 (EyeBlinkSettingData ctor 0x569CF20), IgnoreFaceEyeTypeArray
    /// {5 WaraiA, 8 WaraiD, 2 CloseA, 16 KusyoA, 22 EyeHideA} (JP game value),
    /// EYE_R_ARRAY {FaceGroupType.EyeR}, EYE_L_ARRAY {FaceGroupType.EyeL} (.cctor 0x58DF790).
    ///
    /// Not ported (reported): the PlayAnim branch (LastFacePlayType == PlayAnim, face animation clips: not used by the
    /// Live facial path), RequestEyeChangeBlink, StoryEyeBlinkController, the per-character EyeBlinkSettingData asset
    /// arrays (IgnoreEyeBlinkType / IgnoreFaceEyeTypeOnFaceChangeArray: asset contents not read, treated as empty; they
    /// are only consulted by conditional blink requests and by the "ignored face while a blink is requested" abort) and
    /// the +0xC1 flag the game sets on both EyeTraceObjects together with DisableDelayedTracking.
    /// </summary>
    public class EyeBlinkController
    {
        public enum BlinkStep
        {
            CloseCall = 0,
            CloseUpdate = 1,
            CloseWait = 2,
            OpenCall = 3,
            OpenUpdate = 4,
            OpenWait = 5,
        }

        public enum DoubleBlinkStep
        {
            None = 0,
            First = 1,
            Second = 2,
        }

        public enum FacePlayType
        {
            SetFace = 0,
            PlayAnim = 1,
        }

        // What the controller needs from its character: the game reaches these through DrivenKeyComponent,
        // EyeHighlightController and EyeTraceController (RefreshUpdaterComponent 0x51FE330).
        public interface IOwner
        {
            // DrivenKeyComponent.SetFaceEyeBlink(faceType, 1.0, EYE_R_ARRAY, duration) +
            // SetFaceEyeBlink(faceType, 1.0, EYE_L_ARRAY, duration): the right eye group takes the FaceType's right eye
            // parts and the left eye group its left eye parts; every other group is left untouched.
            void SetFaceEyeBlink(FaceParts[] eyeR, FaceParts[] eyeL, float duration);

            // DrivenKeyComponent.SetFaceEyeType(parts, group, duration, ...) (restore of the stored facial parts).
            void SetFaceEyeType(FaceParts[] parts, FaceGroupType group, float duration);

            // EyeTraceController delayed tracking switched off while the eyes open (BeforeApplyDrivenKey / UpdateBlink).
            void DisableDelayedTracking();

            // EyeHighlightController.IsSuppressEyeBlink: the running highlight animation has attribute bit 2.
            bool IsSuppressEyeBlink();
        }

        // EyeBlinkSettingData defaults (ctor 0x569CF20). The per-character asset is not read (see class header).
        public sealed class SettingData
        {
            public float ProbabilityOfOccurrence = 100f;                                  // +0x18 (0x42c80000)
            public float ForceEyeCloseTime = System.BitConverter.Int32BitsToSingle(0x3d888880); // +0x1C
            public int[] IgnoreEyeBlinkType;                                              // +0x20
            public int[] IgnoreFaceEyeTypeOnFaceChangeArray;                              // +0x30
        }

        // StaticVariableDefine.CG3D.EyeBlinkController.IgnoreFaceEyeTypeArray: FaceEyeType ids (WaraiA, WaraiD,
        // CloseA, KusyoA, EyeHideA).
        private static readonly int[] IgnoreFaceEyeTypeArray = { 5, 8, 2, 16, 22 };

        // Gallop.FaceType.EyeHalfA / EyeClose parts sets (face_type_data rows 'EyeHalfA': eye_l = eye_r = HalfA and
        // 'EyeClose': eye_l = eye_r = CloseA, every other group Base). FaceParts._faceParts is the FaceEyeType id
        // (HalfA 1, CloseA 2). The weight is 1.0: the master row carries no weight and the FacePartsSet asset the game
        // indexes by FaceType (DrivenKeyComponent +0x90) is not read (data gap).
        private static readonly FaceParts[] EyeHalfAParts = { new FaceParts { _faceParts = 1, _weight = 1f, _originalWeight = 1f } };
        private static readonly FaceParts[] EyeCloseParts = { new FaceParts { _faceParts = 2, _weight = 1f, _originalWeight = 1f } };

        private const float kEpsilon = 1e-6f;                // DAT_7ffd5a640d34
        private const float kDurationTime = 0.03f;           // DAT_7ffd5a8b6a34 (GetDurationTime / close keep time)
        private const float kDefaultIntervalMin = 3f;        // ctor 0x40400000
        private const float kDefaultIntervalMax = 5f;        // ctor 0x40a00000

        // ModelComponentBase
        public bool IsEnable = true;                         // 0x10
        public bool IsPause;                                 // 0x24 (_isPause)

        public bool IsAutoBlink = true;                      // 0x28 (OnInitialize writes 0x101: auto + double blink)
        public bool IsEnabledAutoDoubleBlink = true;         // 0x29
        private float _timeCount;                            // 0x2C
        private float _timeMax;                              // 0x30
        private int _singleCount;                            // 0x34
        private BlinkStep _step = BlinkStep.OpenWait;        // 0x38
        private DoubleBlinkStep _doubleStep;                 // 0x3C
        private bool _isRequestForceBlink;                   // 0x40
        private float _intervalTimeMin = kDefaultIntervalMin; // 0x44
        private float _intervalTimeMax = kDefaultIntervalMax; // 0x48
        private readonly IOwner _owner;
        private readonly FacePartsSet _storedFacePartsSet;   // 0x68 (ModelController +0x380, shared by reference)
        public FacePlayType LastFacePlayType;                // 0x70
        public bool IsRequestBlinkChangeEye;                 // 0x74
        private bool _isUpdatedEyeBlink;                     // 0x75
        public bool WaitOpenEye;                             // 0x76
        public bool IsDisableByOverride;                     // 0x77 (FaceOverrideData.IsDisableEyeBlink, InitEyeBlinkController)
        public bool IsRequestClear;                          // 0x78
        private SettingData _settingData = new SettingData(); // 0x80

        public bool IsBlinking => _step != BlinkStep.OpenWait;

        // ModelController.IsEyeBlinking RVA (@0x7ffd59677cb0): the controller ran this frame, is enabled and is mid blink.
        public bool IsEyeBlinking => _isUpdatedEyeBlink && IsEnable && IsBlinking;

        // ctor (@0x7ffd5957f190) + OnInitialize (@0x7ffd5957e180).
        public EyeBlinkController(IOwner owner, FacePartsSet storedFacePartsSet)
        {
            _owner = owner;
            _storedFacePartsSet = storedFacePartsSet;
            IsAutoBlink = true;
            IsEnabledAutoDoubleBlink = true;
            _timeMax = UnityEngine.Random.Range(_intervalTimeMin, _intervalTimeMax);
            _step = BlinkStep.OpenWait;
            _isRequestForceBlink = false;
            WaitOpenEye = false;
            IsRequestClear = false;
        }

        // Clear @0x7ffd5957da00.
        public void Clear()
        {
            _timeMax = UnityEngine.Random.Range(_intervalTimeMin, _intervalTimeMax);
            _step = BlinkStep.OpenWait;
            _isRequestForceBlink = false;
            WaitOpenEye = false;
            IsRequestClear = false;
        }

        // SetIntervalTime @0x7ffd5957ea20.
        public void SetIntervalTime(float min, float max)
        {
            _intervalTimeMax = max;
            _intervalTimeMin = min;
            _timeMax = UnityEngine.Random.Range(min, max);
        }

        // GetDurationTime @0x7ffd579df150 (virtual; StoryEyeBlinkController overrides it).
        private static float GetDurationTime()
        {
            return kDurationTime;
        }

        // GetCloseEyeKeepTime @0x7ffd5957db90.
        private float GetCloseEyeKeepTime()
        {
            float time = kDurationTime;
            if (_isRequestForceBlink && _settingData != null)
            {
                time = _settingData.ForceEyeCloseTime + time;
            }
            return time;
        }

        // IsSuppressEyeBlink @0x7ffd5957dfe0.
        private bool IsSuppressEyeBlink()
        {
            return _owner != null && _owner.IsSuppressEyeBlink();
        }

        // CheckIgnore(ref FacePartsSet, FaceEyeType[]) @0x7ffd5957d890: any eye part with a weight above epsilon whose
        // id is in the list. The whole parts arrays are scanned (the game does not use the group counts).
        private static bool CheckIgnore(FacePartsSet set, int[] ignore)
        {
            if (set == null || ignore == null) return false;
            return CheckIgnoreParts(set._eyeL, ignore) || CheckIgnoreParts(set._eyeR, ignore);
        }

        private static bool CheckIgnoreParts(FaceParts[] parts, int[] ignore)
        {
            if (parts == null) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i]._weight > kEpsilon)
                {
                    for (int j = 0; j < ignore.Length; j++)
                    {
                        if (ignore[j] == parts[i]._faceParts) return true;
                    }
                }
            }
            return false;
        }

        // CheckIgnore(ref FacePartsSet) @0x7ffd5957d830: against the static IgnoreFaceEyeTypeArray.
        private static bool CheckIgnore(FacePartsSet set)
        {
            return CheckIgnore(set, IgnoreFaceEyeTypeArray);
        }

        // RequestBlinkInternal @0x7ffd5957e3c0 (virtual).
        private void RequestBlinkInternal(bool isForce)
        {
            _isRequestForceBlink = isForce;
            _doubleStep = DoubleBlinkStep.Second;
            _singleCount = 0;
            _step = BlinkStep.CloseCall;
            _timeCount = 0f;
            _timeMax = GetDurationTime() + 0f;
        }

        // RequestBlink @0x7ffd5957e410 (ModelController.RequestBlink @0x7ffd59678790 forwards here).
        public void RequestBlink(bool isConditional)
        {
            if (!IsEnable || IsPause || IsDisableByOverride) return;

            if (_step == BlinkStep.OpenWait)
            {
                if (isConditional && _settingData != null)
                {
                    float random = UnityEngine.Random.Range(0f, 100f); // DAT_7ffd5a640e94 = 100
                    if (random > _settingData.ProbabilityOfOccurrence) return;
                    if (CheckIgnore(_storedFacePartsSet, _settingData.IgnoreEyeBlinkType)) return;
                }
                RequestBlinkInternal(isConditional);
                return;
            }

            if (isConditional)
            {
                if (_step != BlinkStep.OpenUpdate && _step != BlinkStep.CloseUpdate)
                {
                    Clear();
                }
                _isRequestForceBlink = true;
            }
        }

        // BeforeApplyDrivenKey @0x7ffd5957d350: runs once per frame before the driven keys are applied. deltaTime is
        // the owner's delta time (ModelControllerBehaviour vcall +0x1c8).
        public void BeforeApplyDrivenKey(float deltaTime)
        {
            if (!IsEnable) return;
            if (IsPause) return;
            if (IsDisableByOverride) return;
            // DrivenKeyComponent.get_CanEyeBlink is true unless a face animation clip is playing (never on this path).

            if (IsRequestClear)
            {
                Clear();
            }
            _isUpdatedEyeBlink = false;

            if (LastFacePlayType != FacePlayType.SetFace)
            {
                // PlayAnim branch (face animation clips): not ported.
                return;
            }

            if (!CheckIgnore(_storedFacePartsSet))
            {
                UpdateBlink(deltaTime);
                _isUpdatedEyeBlink = true;
                return;
            }

            // The stored facial parts are an ignored eye type: a blink that is only requested (CloseCall) is cancelled,
            // a blink in flight is simply not advanced.
            if (_step != BlinkStep.CloseCall) return;

            // setting.IgnoreFaceEyeTypeOnFaceChangeArray check (asset arrays not read) and the repeated static check
            // (true here) both lead to the same cancel.
            Clear();
            _timeCount = 0f;
        }

        // UpdateBlink @0x7ffd5957ea50.
        private void UpdateBlink(float deltaTime)
        {
            if (_step == BlinkStep.OpenCall)
            {
                if (WaitOpenEye) return;
            }
            else if (_step == BlinkStep.OpenWait)
            {
                if (IsSuppressEyeBlink()) return;
                if (!IsAutoBlink) return;
            }

            _timeCount += deltaTime;
            if (_timeCount < _timeMax && !_isRequestForceBlink) return;

            if ((_step == BlinkStep.OpenCall || _step == BlinkStep.OpenUpdate) && _owner != null)
            {
                _owner.DisableDelayedTracking();
            }

            if (_step == BlinkStep.CloseWait)
            {
                _isRequestForceBlink = false;
            }

            bool continueDoubleBlink = false;
            if (IsEnabledAutoDoubleBlink)
            {
                continueDoubleBlink = _doubleStep == DoubleBlinkStep.First;
            }

            BlinkStep nextStep = BlinkStep.CloseCall;
            float duration = GetDurationTime();
            float extraWait = 0f;
            switch (_step)
            {
                case BlinkStep.CloseCall:
                    nextStep = BlinkStep.CloseUpdate;
                    break;
                case BlinkStep.CloseUpdate:
                    nextStep = BlinkStep.CloseWait;
                    extraWait = GetCloseEyeKeepTime();
                    break;
                case BlinkStep.CloseWait:
                    nextStep = BlinkStep.OpenCall;
                    break;
                case BlinkStep.OpenCall:
                    nextStep = BlinkStep.OpenUpdate;
                    break;
                case BlinkStep.OpenUpdate:
                    if (!continueDoubleBlink)
                    {
                        nextStep = BlinkStep.OpenWait;
                        extraWait = UnityEngine.Random.Range(_intervalTimeMin, _intervalTimeMax);
                    }
                    break;
                case BlinkStep.OpenWait:
                    break;
            }

            _timeCount = 0f;
            _timeMax = extraWait + duration;

            if (IsEnabledAutoDoubleBlink)
            {
                if (_step == BlinkStep.CloseCall)
                {
                    // UpdateDoubleBlink_CloseCall @0x7ffd5957ef20
                    if (_doubleStep >= DoubleBlinkStep.Second)
                    {
                        _doubleStep = DoubleBlinkStep.None;
                    }
                    else if (_doubleStep == DoubleBlinkStep.None)
                    {
                        if (_singleCount < 1)
                        {
                            _singleCount++;
                        }
                        else
                        {
                            _doubleStep = DoubleBlinkStep.First;
                            _singleCount = 0;
                        }
                    }
                }
                else if (_step == BlinkStep.OpenUpdate && _doubleStep == DoubleBlinkStep.First)
                {
                    // UpdateDoubleBlink_OpenUpdate @0x7ffd5957ef50
                    _doubleStep = DoubleBlinkStep.Second;
                }
            }

            UpdateFaceEye(duration);
            _step = nextStep;
        }

        // UpdateFaceEye @0x7ffd5957efb0: applies the eye state of the step being left.
        private void UpdateFaceEye(float duration)
        {
            if (_owner == null) return;
            switch (_step)
            {
                case BlinkStep.CloseCall:
                case BlinkStep.OpenCall:
                    _owner.SetFaceEyeBlink(EyeHalfAParts, EyeHalfAParts, duration);
                    break;
                case BlinkStep.CloseUpdate:
                    _owner.SetFaceEyeBlink(EyeCloseParts, EyeCloseParts, duration);
                    break;
                case BlinkStep.OpenUpdate:
                    if (LastFacePlayType == FacePlayType.SetFace && _storedFacePartsSet != null)
                    {
                        if (_storedFacePartsSet._eyeR != null)
                        {
                            _owner.SetFaceEyeType(_storedFacePartsSet._eyeR, FaceGroupType.EyeR, duration);
                        }
                        if (_storedFacePartsSet._eyeL != null)
                        {
                            _owner.SetFaceEyeType(_storedFacePartsSet._eyeL, FaceGroupType.EyeL, duration);
                        }
                    }
                    break;
            }
        }
    }
}
