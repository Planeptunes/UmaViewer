using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Model.Component
{
    /// <summary>
    /// Port of the material side of Gallop.Model.Component.EyeHighlightController (dump_jp TypeDefIndex 33586298,
    /// code_JP Gallop.Model.Component.EyeHighlightController.c) and its nested EyeHighlightPart (33586303).
    ///
    /// Ported:
    ///  - OnInitialize @0x7ffd59580570: per-material arrays _mainParamFinalPose[2] (+0x40), _highParam1FinalPose[3]
    ///    (+0x48), _highParam2FinalPose[2] (+0x50), _pupliScale (+0x58, ctor @0x7ffd595815b0 = 1.0).
    ///  - InitializeHighlighPartsSettings @0x7ffd5957ff10: 7 parts, part k reads the head AssetHolder object
    ///    names[n] and writes element w of one array:
    ///      part0 names[0] Main[0], part1 names[1] High1[0], part2 names[2] High1[1], part3 names[6] High1[2],
    ///      part4 names[3] Main[1], part5 names[4] High2[0], part6 names[5] High2[1].
    ///  - EyeHighlightPart.Initialize @0x7ffd59581990, BeforeApplyDrivenKey @0x7ffd59581720 (not-interpolating
    ///    branch), AfterApplyDrivenKey part loop of the controller @0x7ffd5957f220, MakeMaterialParameter
    ///    @0x7ffd59581a90: (localPosition.x * 100, -localPosition.y * 100, localPosition.z * 1.7453293, localScale.x).
    ///  - The material write of AfterApplyDrivenKey / ApplyMaterial / UpdateEyeMaterial: ShaderManager ids
    ///    0x82 _MainParam, 0x80 _HighParam1, 0x81 _HighParam2 (SetVectorArray) and 0x40 _EyePupliScale.
    ///
    /// DATA GAP (AGENTS.md 8.1): the static names array (EyeHighlightController statics +0x8) is made of il2cpp
    /// string literals that cannot be read offline. The order below is identified by data, not read from the binary:
    /// the head AssetHolder holds exactly these 7 keys, and the game (live 1001, chara 1013) has
    /// _HighParam1[0] == _HighParam2[0] == (0, 0.2452, 0, 1) at f3919, which is the Eye_2 (EyeCloseA) driven-key
    /// offset of Eye_big_info_L / _R (y -0.0025) -> High1 = [big_l, small_l, kira], High2 = [big_r, small_r],
    /// Main = [base_l, base_r].
    ///
    /// Highlight animation (PlayAnimation @0x7ffd59580be0, Update @0x7ffd59581420, EyeHighlightPlayableAnimator):
    /// triggered in a live by the facial EFFECT key (LiveFaceController.AlterUpdateFacialNew @0x7ffd592420e0: attribute
    /// kAttrTeary 0x20000 -> FacePartsSetInfo.EYE_HIGHLIGHT_ANIMATION_ARRAY[tearyType] (cctor @0x7ffd59c78180:
    /// {NoAnim, Teary, Twinkle}) -> ModelController.PlayEyeHighlightAnimation @0x7ffd59678420). HighlightAnimationParam
    /// table (EyeHighlightController..cctor @0x7ffd59c5f8e0): Teary = (State 1, attribute 1, asset key 1),
    /// Twinkle = (State 0, attribute 2, asset key 0). The clip is loaded only when the head AssetHolder value
    /// "anm_eye0N" > 0.5 (GetValueFromHeadAssetHolder, DAT_7ffd5a640d60 = 0.5); path (ResourcePath.GetEyeHighlightAnimPath,
    /// format literal not readable) identified from the asset index:
    /// 3d/chara/head/chrXXXX_YY/facial/anm_chrXXXX_YY_facial_eye0N. The clip animates position/scale of the
    /// Eye_*_info nodes relative to the "Eyes" node. Per frame: WriteDefault (rest pose of the parts), the clip is
    /// sampled at the animator time (dt * 1.0, clamped / looped like an AnimationClipPlayable), then the parts take
    /// their animated snapshot. Update: attribute bit 1 keeps the state; otherwise it stops once the clip ends.
    /// Cross-fade: InterpolateParam (target/current time, comparison type) fed by MasterHighlightInterpolate
    /// (highlight_interpolate: Teary in 0.3 s / out 0.5 s), value-level lerp in EyeHighlightPart.BeforeApplyDrivenKey.
    /// HighlightKeyLocator: "Cheek_Ctrl" localPosition.z * 100 selects the animation (AfterDrivenKey).
    /// Viewer-only: the clip is evaluated with its own PlayableGraph. A clip-less state never ends (see UpdateAnimation).
    /// </summary>
    public class EyeHighlightController
    {
        private const float UNIT_SCALE = 100f;
        private const float UNIT_SCALE_DEG2RAD = 1.7453293f;

        // Data-identified order of the static names array (see the class header).
        private static readonly string[] PART_NAMES =
        {
            "eye_base_info_l", "eye_big_info_l", "eye_small_info_l",
            "eye_base_info_r", "eye_big_info_r", "eye_small_info_r",
            "eye_kira_info",
        };

        private static readonly int ID_MainParam = Shader.PropertyToID("_MainParam");
        private static readonly int ID_HighParam1 = Shader.PropertyToID("_HighParam1");
        private static readonly int ID_HighParam2 = Shader.PropertyToID("_HighParam2");
        private static readonly int ID_EyePupliScale = Shader.PropertyToID("_EyePupliScale");

        private readonly Vector4[] _mainParamFinalPose = new Vector4[2];
        private readonly Vector4[] _highParam1FinalPose = new Vector4[3];
        private readonly Vector4[] _highParam2FinalPose = new Vector4[2];
        private float _pupliScale = 1f;
        // _eyeMaterial (+0x60). Viewer: resolved from the face renderer slot on every write, because the viewer may
        // instance the renderer's materials after load.
        private Renderer _eyeRenderer;
        private int _eyeMaterialIndex = -1;
        private EyeHighlightPart[] _eyeHighLightParts;
        private bool _isShaderEnable;

        public float PupliScale
        {
            get => _pupliScale;
            set => _pupliScale = value;
        }

        // HighlightAnimationParam (statics +0x28): index = HighlightAnimationId.
        private static readonly int[] PARAM_ATTRIBUTE = { 0, 1, 2 };
        private static readonly int[] PARAM_ASSET_KEY_ID = { -1, 1, 0 };
        private static readonly string[] ASSET_KEYS = { "anm_eye00", "anm_eye01" };

        private Transform _eyeRoot;
        // EyeHighlightPlayableAnimator stand-in: a manual PlayableGraph (AnimationClipPlayable -> Animator on the
        // parts' parent), which only writes the properties the clip binds, like the game's playable animator.
        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;
        private AnimationClip _graphClip;
        private Animator _graphAnimator;
        private readonly AnimationClip[] _clips = new AnimationClip[2];
        private int _currentState;           // _currentState (+0x78)
        private float _playTime;
        private bool _isAnimationEnable;     // _isAnimationEnable (+0x89)

        private sealed class EyeHighlightPart
        {
            private int _finalPoseWriteIndex;
            private Vector4[] _finalPose;
            private Transform _partTransform;
            private Vector3 _positionByAnimationOnly;
            private Vector3 _scaleByAnimationOnly;
            private Vector4 _animatedPose = new Vector4(0f, 0f, 0f, 1f);
            private bool _isNeedInterpolate;
            public Transform PartTransform => _partTransform;
            private Vector3 _defaultPosition;
            private Vector3 _defaultScale;

            // EyeHighlightPlayableAnimator.WriteDefault: the animated nodes return to their default pose before the
            // clip is evaluated.
            public void WriteDefault()
            {
                if (_partTransform == null) return;
                _partTransform.localPosition = _defaultPosition;
                _partTransform.localScale = _defaultScale;
            }

            // EyeHighlightPart.Initialize @0x7ffd59581990
            public void Initialize(GameObject partObject, Vector4[] finalPose, int writeIndex)
            {
                if (partObject == null) return;
                _finalPose = finalPose;
                _finalPoseWriteIndex = writeIndex;
                _partTransform = partObject.transform;
                _positionByAnimationOnly = _partTransform.localPosition;
                _scaleByAnimationOnly = _partTransform.localScale;
                _defaultPosition = _positionByAnimationOnly;
                _defaultScale = _scaleByAnimationOnly;
            }

            // EyeHighlightPart.BeforeApplyDrivenKey(isNeedInterpolate, ratio) @0x7ffd59581720.
            //  - false: snapshot the animation-only pose (+0x30 position, +0x3c scale) and its material parameter.
            //  - true: the snapshot stays (the pose before the fade); the transform takes
            //    lerp(snapshot, current, clamp01(ratio)) for localPosition and localScale, then the material parameter
            //    (+0x48) is rebuilt from the blended pose.
            public void BeforeApplyDrivenKey(bool needInterpolate, float ratio)
            {
                _isNeedInterpolate = needInterpolate;
                if (_partTransform == null) return;
                if (!needInterpolate)
                {
                    _positionByAnimationOnly = _partTransform.localPosition;
                    _scaleByAnimationOnly = _partTransform.localScale;
                    MakeMaterialParameter(ref _animatedPose);
                    return;
                }
                float r = ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio);
                Vector3 curPos = _partTransform.localPosition;
                Vector3 curScale = _partTransform.localScale;
                _partTransform.localPosition = new Vector3(
                    (curPos.x - _positionByAnimationOnly.x) * r + _positionByAnimationOnly.x,
                    (curPos.y - _positionByAnimationOnly.y) * r + _positionByAnimationOnly.y,
                    (curPos.z - _positionByAnimationOnly.z) * r + _positionByAnimationOnly.z);
                _partTransform.localScale = new Vector3(
                    (curScale.x - _scaleByAnimationOnly.x) * r + _scaleByAnimationOnly.x,
                    (curScale.y - _scaleByAnimationOnly.y) * r + _scaleByAnimationOnly.y,
                    (curScale.z - _scaleByAnimationOnly.z) * r + _scaleByAnimationOnly.z);
                MakeMaterialParameter(ref _animatedPose);
            }

            // Part loop of EyeHighlightController.AfterApplyDrivenKey @0x7ffd5957f220.
            public void AfterApplyDrivenKey()
            {
                if (_partTransform == null || _finalPose == null) return;
                if (!_isNeedInterpolate)
                {
                    Vector3 scale = _partTransform.localScale;
                    if (_scaleByAnimationOnly.x < scale.x)
                    {
                        _scaleByAnimationOnly = scale;
                        MakeMaterialParameter(ref _animatedPose);
                    }
                }
                Vector4 final = Vector4.zero;
                MakeMaterialParameter(ref final);
                final.z = _animatedPose.z;
                final.w = _animatedPose.w;
                _finalPose[_finalPoseWriteIndex] = final;
            }

            // EyeHighlightPart.MakeMaterialParameter @0x7ffd59581a90
            private void MakeMaterialParameter(ref Vector4 param)
            {
                Vector3 p = _partTransform.localPosition;
                param.x = p.x * UNIT_SCALE;
                param.y = -p.y * UNIT_SCALE;
                param.z = p.z * UNIT_SCALE_DEG2RAD;
                param.w = _partTransform.localScale.x;
            }
        }

        /// <summary>OnInitialize @0x7ffd59580570 + InitializeHighlighPartsSettings @0x7ffd5957ff10.</summary>
        public bool Initialize(Renderer eyeRenderer, int eyeMaterialIndex, AssetHolder headHolder)
        {
            _eyeRenderer = eyeRenderer;
            _eyeMaterialIndex = eyeMaterialIndex;
            if (_eyeRenderer == null || _eyeMaterialIndex < 0 || headHolder == null || headHolder._assetTable == null) return false;

            GameObject Get(int i) => headHolder._assetTable[PART_NAMES[i]] as GameObject;
            if (Get(0) == null) return false;

            _eyeHighLightParts = new EyeHighlightPart[7];
            for (int i = 0; i < _eyeHighLightParts.Length; i++)
                _eyeHighLightParts[i] = new EyeHighlightPart();

            _eyeHighLightParts[0].Initialize(Get(0), _mainParamFinalPose, 0);
            _eyeHighLightParts[1].Initialize(Get(1), _highParam1FinalPose, 0);
            _eyeHighLightParts[2].Initialize(Get(2), _highParam1FinalPose, 1);
            _eyeHighLightParts[4].Initialize(Get(3), _mainParamFinalPose, 1);
            _eyeHighLightParts[5].Initialize(Get(4), _highParam2FinalPose, 0);
            _eyeHighLightParts[6].Initialize(Get(5), _highParam2FinalPose, 1);
            _eyeHighLightParts[3].Initialize(Get(6), _highParam1FinalPose, 2);
            _isShaderEnable = true;
            return true;
        }

        /// <summary>BeforeApplyDrivenKey @0x7ffd5957f750 (no highlight animator: parts take the snapshot branch).</summary>
        public void BeforeApplyDrivenKey()
        {
            if (!_isShaderEnable) return;
            // playable.WriteDefault(): the animated nodes return to their default pose every frame.
            if (_isAnimationEnable)
                for (int i = 0; i < _eyeHighLightParts.Length; i++)
                    _eyeHighLightParts[i].WriteDefault();
            if (_isAnimationEnable && _currentState != 0)
            {
                // Animator evaluation of the current clip. The game's WriteDefault is covered here by the viewer's eye
                // morph reset (FacialResetEye, run just before) for base/big/small, and the clip rewrites every
                // property it binds each evaluation; WriteDefault proper only runs when the animation stops.
                var clip = GetClip(_currentState);
                if (clip != null)
                {
                    float t = _playTime;
                    float length = clip.length;
                    if (length > 0f)
                        t = clip.isLooping ? Mathf.Repeat(t, length) : Mathf.Min(t, length);
                    Evaluate(clip, t);
                }
            }
            // Interpolation ratio (game BeforeApplyDrivenKey @0x7ffd5957f750): currentTime / targetTime, 1.0 without a
            // target. The time itself is advanced once per frame in UpdateAnimation (this method also runs for the
            // extra eye passes of the viewer).
            float ratio = _interpTargetTime > 0f ? _interpCurrentTime / _interpTargetTime : 1f;
            for (int i = 0; i < _eyeHighLightParts.Length; i++)
                _eyeHighLightParts[i].BeforeApplyDrivenKey(_interpNeed, ratio);
        }

        // The clip paths are the part names, relative to the parts' parent (the "Eyes" node once the head is merged).
        private Transform ResolveEyeRoot()
        {
            for (int i = 0; i < _eyeHighLightParts.Length; i++)
            {
                Transform t = _eyeHighLightParts[i].PartTransform;
                if (t != null && t.parent != null) return t.parent;
            }
            return _eyeRoot;
        }

        private void Evaluate(AnimationClip clip, float time)
        {
            Transform root = ResolveEyeRoot();
            if (root == null) return;
            if (_graphAnimator == null || _graphAnimator.transform != root)
            {
                DestroyGraph();
                _graphAnimator = root.GetComponent<Animator>();
                if (_graphAnimator == null) _graphAnimator = root.gameObject.AddComponent<Animator>();
            }
            if (!_graph.IsValid())
            {
                _graph = PlayableGraph.Create("EyeHighlight");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(_graph, "EyeHighlight", _graphAnimator);
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                output.SetSourcePlayable(_clipPlayable);
                _graphClip = clip;
            }
            else if (_graphClip != clip)
            {
                var output = (AnimationPlayableOutput)_graph.GetOutput(0);
                _clipPlayable.Destroy();
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                output.SetSourcePlayable(_clipPlayable);
                _graphClip = clip;
            }
            _clipPlayable.SetTime(time);
            _graph.Evaluate(0f);
        }

        public void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graphClip = null;
        }

        /// <summary>InitializeAnimator / OnInitialize clip setup: clips of the head that has them.</summary>
        public void InitializeAnimator(Transform eyeRoot, AssetHolder headHolder, string headName, System.Func<string, AnimationClip> loader)
        {
            _eyeRoot = eyeRoot;
            _isAnimationEnable = false;
            if (_eyeRoot == null || headHolder == null || headHolder._assetTableValue == null || loader == null) return;
            for (int k = 0; k < ASSET_KEYS.Length; k++)
            {
                float flag = 0f;
                foreach (var e in headHolder._assetTableValue.list)
                    if (e.Key == ASSET_KEYS[k]) { flag = e.Value; break; }
                if (flag > 0.5f)
                    _clips[k] = loader("3d/chara/head/" + headName + "/facial/anm_" + headName + "_facial_eye" + k.ToString("00"));
            }
            _isAnimationEnable = true;
        }

        private AnimationClip GetClip(int state)
        {
            if (state <= 0 || state >= PARAM_ASSET_KEY_ID.Length) return null;
            int key = PARAM_ASSET_KEY_ID[state];
            return key >= 0 && key < _clips.Length ? _clips[key] : null;
        }

        // InterpolateParam (+0x80): isNeedInterpolate +0x10, targetTime +0x14, currentTime +0x18; ComparisonType +0x98
        // (TimeComparisonType: GreaterThan = 0, ApproximatelyGreaterThan = 1; the controller default is not recoverable
        // offline, GreaterThan used).
        private bool _interpNeed;
        private float _interpTargetTime;
        private float _interpCurrentTime;
        public int ComparisonType;

        private static void GetInterpolateTime(int id, out float inSec, out float outSec)
        {
            var master = UmaDatabaseController.Instance != null ? UmaDatabaseController.Instance.HighlightInterpolateData : null;
            if (master == null) { inSec = 0f; outSec = 0f; return; }
            master.GetInterpolateTime(id, out inSec, out outSec);
        }

        /// <summary>
        /// PlayAnimation(id, noInterp, callback, speed) @0x7ffd59580be0. From NoAnim the state fades in over
        /// max(in_time(new), out_time(current)) (master highlight_interpolate) unless noInterp or the state has the
        /// IgnoreInterpolate attribute (bit 4); switching from a running state never fades.
        /// </summary>
        public void PlayAnimation(int animationId, bool noInterp = false)
        {
            if (!_isAnimationEnable) return;
            if (animationId == _currentState) return;
            if (animationId == 0)
            {
                StopAnimation();
                return;
            }
            bool skipInterp = _currentState == 0 ? noInterp : true;
            if (!skipInterp && animationId < PARAM_ATTRIBUTE.Length && (PARAM_ATTRIBUTE[animationId] & 4) == 0)
            {
                GetInterpolateTime(animationId, out float inNew, out _);
                GetInterpolateTime(_currentState, out _, out float outCur);
                _interpNeed = true;
                _interpCurrentTime = 0f;
                _interpTargetTime = Mathf.Max(inNew, outCur);
            }
            else
            {
                _interpNeed = false;
                _interpCurrentTime = 0f;
                _interpTargetTime = 0f;
            }
            _currentState = animationId;
            _playTime = 0f;
        }

        /// <summary>
        /// StopAnimation @0x52010D0: back to NoAnim. Without a fade running it fades out over
        /// max(in_time(NoAnim), out_time(current)); with one running it cancels the fade. The parts' default pose is
        /// written every frame in BeforeApplyDrivenKey (WriteDefault), not here.
        /// </summary>
        public void StopAnimation()
        {
            if (_currentState == 0) return;
            GetInterpolateTime(0, out float in0, out _);
            GetInterpolateTime(_currentState, out _, out float outCur);
            if (!_interpNeed)
            {
                _interpNeed = true;
                _interpCurrentTime = 0f;
                _interpTargetTime = Mathf.Max(in0, outCur);
            }
            else
            {
                _interpNeed = false;
                _interpCurrentTime = 0f;
                _interpTargetTime = 0f;
            }
            _currentState = 0;
            _playTime = 0f;
        }

        public bool IsPlayAnimation() { return _currentState != 0; }

        /// <summary>True while a state plays or a fade is still running (the eye pass has to keep re-running).</summary>
        public bool IsBusy => _currentState != 0 || _interpNeed;

        /// <summary>
        /// Per-frame animator time, the interpolation time of BeforeApplyDrivenKey @0x7ffd5957f750 (currentTime += dt;
        /// done when ratio exceeds 1.0, or 0.999999 for ApproximatelyGreaterThan) and Update @0x7ffd59581420
        /// (non-looping states stop at the clip end; NoAnim just waits for a running fade).
        /// </summary>
        public void UpdateAnimation(float deltaTime)
        {
            if (_interpNeed)
            {
                _interpCurrentTime += deltaTime;
                float ratio = _interpTargetTime > 0f ? _interpCurrentTime / _interpTargetTime : 1f;
                float threshold = ComparisonType == 1 ? 0.99999899f : 1f;
                if (threshold < ratio)
                {
                    _interpNeed = false;
                    _interpTargetTime = 0f;
                }
            }
            if (!_isAnimationEnable || _currentState == 0) return;
            _playTime += deltaTime;
            // EyeHighlightPlayableAnimator.UpdateAnimation @0x7ffd576dab10: the slot (+0x10) takes the entry's clip only
            // when the entry has one, and keeps its previous binding otherwise; get_CurrentNormalizePlayTime @0x7ffd576db260
            // is slot.time / bound clip length, or 0 with nothing ever bound. A state with no clip therefore never ends
            // (the game has no guard that stops it), and a stale binding ends it at the previous clip's length.
            var clip = GetClip(_currentState);
            if (clip != null) _boundClip = clip;
            // Controller.Update @0x7ffd59581420: looping states (attribute bit 0) never end; otherwise StopAnimation once
            // the normalized time reaches 1.0.
            if ((PARAM_ATTRIBUTE[_currentState] & 1) != 0) return;
            float normalized = _boundClip != null && _boundClip.length > 0f ? _playTime / _boundClip.length : 0f;
            if (normalized >= 1f) StopAnimation();
        }

        // playable slot binding (+0x10): last non-null clip of a played state.
        private AnimationClip _boundClip;

        /// <summary>AfterApplyDrivenKey @0x7ffd5957f220 (part loop + material write; key-locator trigger not ported).</summary>
        public void AfterApplyDrivenKey()
        {
            if (!_isShaderEnable) return;
            for (int i = 0; i < _eyeHighLightParts.Length; i++)
                _eyeHighLightParts[i].AfterApplyDrivenKey();
            UpdateEyeMaterial();
            KeyLocatorAfterDrivenKey();
        }

        // HighlightKeyLocator (controller +0x90; ctor @RVA 0x520AB00, AfterDrivenKey @RVA 0x520A990). The ctor takes the
        // DrivenKeyLocator root and finds LOCATOR_CONTROL_NAME = "Cheek_Ctrl" (const read from the metadata default-value
        // table); IsEnable (+0x20) = true.
        private const string LOCATOR_CONTROL_NAME = "Cheek_Ctrl";
        private Transform _keyLocator;        // _locator (+0x18)
        private int _highlightType;           // _highlightType (+0x10)
        public bool KeyLocatorEnable = true;  // IsEnable (+0x20)

        public void InitializeKeyLocator(Transform drivenKeyLocator)
        {
            KeyLocatorEnable = true;
            _keyLocator = drivenKeyLocator != null ? drivenKeyLocator.Find(LOCATOR_CONTROL_NAME) : null;
        }

        // HighlightKeyLocator.AfterDrivenKey(controller): the locator's localPosition.z * 100, clamped at 0 and rounded
        // to even, selects the highlight animation; on a change below 3 PlayAnimation(id, noInterp = false, speed 1).
        private void KeyLocatorAfterDrivenKey()
        {
            if (!KeyLocatorEnable || _keyLocator == null) return;
            float z = _keyLocator.localPosition.z * 100f;
            if (z < 0f) z = 0f;
            int type = (int)System.Math.Round((double)z, System.MidpointRounding.ToEven);
            if (_highlightType == type) return;
            _highlightType = type;
            if (type < 3) PlayAnimation(type, false);
        }

        /// <summary>UpdateEyeMaterial @0x7ffd59581350.</summary>
        public void UpdateEyeMaterial()
        {
            if (_eyeRenderer == null) return;
            var materials = _eyeRenderer.sharedMaterials;
            if (_eyeMaterialIndex < materials.Length) ApplyMaterial(materials[_eyeMaterialIndex]);
        }

        /// <summary>ApplyMaterial @0x7ffd5957f680.</summary>
        public void ApplyMaterial(Material material)
        {
            if (material == null) return;
            material.SetVectorArray(ID_MainParam, _mainParamFinalPose);
            material.SetVectorArray(ID_HighParam1, _highParam1FinalPose);
            material.SetVectorArray(ID_HighParam2, _highParam2FinalPose);
            material.SetFloat(ID_EyePupliScale, _pupliScale);
        }
    }
}
