using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop
{
    // Gallop.DrivenKeyComponent (dump_jp.cs TypeDefIndex 33580406). Only the pieces the viewer
    // needs are ported: the enum plus the ear driven-key playback chain used by the mini
    // characters (the Live MiniCharaMotion ear keys).
    //
    // Ear chain, ported from code_JP (class/method @ runtime address):
    //   MiniAnimationController.PlayEarMotion          @0x7ffd59598f40  (LiveMiniCharaController.ApplyEarMotion)
    //     -> DrivenKeyComponent.PlayEar                @0x7ffd58e6fa20
    //     -> DrivenKeyComponent.SetEarTime             @0x7ffd58e70e00
    //     -> DrivenKeyComponent.SetDrivenKeySpeed(0)   @0x7ffd58e70d30
    //   DrivenKeyComponent.UpdateEarWeight             @0x7ffd58e730c0  (per frame, from PostJob @0x7ffd58e702a0)
    //   DrivenKeyComponent.InitializeEarDrivenKeyInfo  @0x7ffd58e6bef0
    //
    // How the ear clip drives the ears (recovered from code_JP + dumps): the clip is played on
    // layer 1 (the "ear" layer) of the DrivenKeyPlayableAnimator, which poses the driven-key
    // locator; each ear side (DrivenKeys, _drivenKeyForEarArray +0xd8) has one root transform
    // (+0x18) whose children are one node per EarType (ObjectTransInfo +0x20: isEnable +0x10,
    // objectTrans +0x18). The clip animates those nodes' localPosition; UpdateEarWeight reads
    // them and writes three weights per (EarType, side) into the EarWeightGroup
    // (CommonWeightArray via the WeightGroup offset table):
    //   weight0 = localPosition.x * TRANSLATE_TO_UNITY_POSITION      (-100, DAT_7ffd5a8baff8)
    //   weight1 = localPosition.y * TRANSLATE_TO_UNITY_POSITION_VALUE( 100, DAT_7ffd5a640e94)
    //   weight2 = localPosition.z * TRANSLATE_TO_UNITY_POSITION_VALUE( 100)
    // (both constants verified in GameAssembly_jp_dumped.dll; the class constants are
    // DrivenKeyComponent.TRANSLATE_TO_UNITY_POSITION = -100 and ..._VALUE = 100, global dump.cs.)
    // The game then turns the weights into ear bone transforms with DrivenKeyComponent.EarWeightJob
    // (SetTRS/CalcTRS/AddTRS/BlendTRS); that job is ported in EarWeightJob.cs and driven by
    // EarWeightJobRunner for the characters' facial update (FaceDrivenKeyTarget). The mini
    // characters do not run it: their weights stop at EarWeightGroup.WeightArray.
    //
    // NOT ported (unused by the mini timeline path or not decodable):
    //   - PlayEar's weight > 0 blend-list branch (EarWeightGroup.GetEarTypes bookkeeping): the
    //     only caller in the mini path passes weight 0.
    //   - DrivenKeyPlayableAnimator as its own class: only the ear layer state the mini path
    //     touches is modelled here (layer 1: clip, play time, speed). On the timeline path
    //     PlayEarMotion always calls SetDrivenKeySpeed(0), so the layer time only advances via
    //     SetEarTime.
    //   - The face/eye/mouth weight groups and PostJob.
    public class DrivenKeyComponent
    {
        public enum InterpolateType
        {
            Linear = 0,
            EaseIn = 1,
            EaseOut = 2,
            EaseInOut = 3
        }

        // Gallop.DrivenKeyComponent.ObjectTransInfo (dump_jp.cs TypeDefIndex 33580415):
        // one driven-key locator node.
        public sealed class ObjectTransInfo
        {
            public bool isEnable;          // 0x10
            public Transform objectTrans;  // 0x18
        }

        // Gallop.DrivenKeyComponent.DrivenKeys (per ear side; _drivenKeyForEarArray +0xd8):
        // the ear root transform (+0x18) and its per-EarType nodes (+0x20).
        public sealed class DrivenKeys
        {
            public Transform root;              // 0x18
            public ObjectTransInfo[] objects;   // 0x20, indexed by EarType
        }

        // DrivenKeyComponent.FACE_TYPE_NAME_DIVIDER = "__" (global dump.cs): when a locator node
        // name is not in the EarType name table, the game parses the integer after "__" as the
        // EarType index (InitializeEarDrivenKeyInfo fallback).
        private const string FaceTypeNameDivider = "__";

        // DrivenKeyComponent.EarWeightGroup.WEIGHT_SIZE = 3 (three weights per side).
        private const int WeightSize = 3;

        // Ear state (DrivenKeyComponent offsets per dump_jp.cs):
        private EarWeightGroup _earWeight;                  // +0x88
        private readonly DrivenKeys[] _drivenKeyForEarArray // +0xd8
            = new DrivenKeys[DrivenKeyAnimation.EarNum];
        private DrivenKeyAnimation _earKeyAnimation;        // +0x128
        private EarTypeSet[] _animationEarTypeArray;        // +0x130
        private bool _isPlayingEarAnim;                     // +0xf4
        private bool _isEarPlayFrame;                       // +0xf5

        // DrivenKeyPlayableAnimator ear layer (layer index 1 of +0x58): clip (+0x20 of the layer
        // entry), play time (+0x10) and speed (+0x18). The playable itself is kept as a manual
        // graph, the same way MiniFacialController.PlayClip replaces the game's
        // AnimatorOverrideController playback.
        private Animator _locatorAnimator;                  // DrivenKeyPlayableAnimator +0x10
        private AnimationClip _earClip;
        private float _earPlayTime;
        private PlayableGraph _earGraph;
        private AnimationClipPlayable _earClipPlayable;

        public EarWeightGroup EarWeight => _earWeight;
        public bool IsPlayingEarAnim => _isPlayingEarAnim;
        public bool IsEarPlayFrame => _isEarPlayFrame;

        // True when the per-EarType nodes of at least one ear side are known.
        public bool HasEarNodes
        {
            get
            {
                foreach (var side in _drivenKeyForEarArray)
                    if (side?.objects != null) return true;
                return false;
            }
        }

        // Viewer-side wiring (the game builds this in MiniModelController.InitDrivenKey
        // @0x7ffd57843a20 from the head AssetHolder's DrivenKeyTarget and the driven-key
        // locator; see LiveMiniCharaController header for the missing asset identity).
        public void InitializeEar(EarWeightGroup weight, Animator locatorAnimator)
        {
            _earWeight = weight;
            _locatorAnimator = locatorAnimator;
        }

        // Gallop.DrivenKeyComponent.InitializeEarDrivenKeyInfo @0x7ffd58e6bef0 (one side):
        // builds the per-EarType node array by walking the ear root's children. A child resolves
        // to an EarType through the EarType name table (_earTypeTableDic; modelled with
        // Enum.TryParse) or, failing that, the integer after "__" in its name. The game raises a
        // null reference for unresolvable names; here they are skipped.
        public void InitializeEarDrivenKeyInfo(int side, Transform earRoot)
        {
            if (side < 0 || side >= _drivenKeyForEarArray.Length || earRoot == null) return;
            int earCount = (int)EarType.Roll + 1;
            var objects = new ObjectTransInfo[earCount];
            for (int i = 0; i < earCount; i++) objects[i] = new ObjectTransInfo();
            _drivenKeyForEarArray[side] = new DrivenKeys { root = earRoot, objects = objects };
            for (int i = 0; i < earRoot.childCount; i++)
            {
                Transform child = earRoot.GetChild(i);
                if (TryParseEarType(child.name, out int earType) && earType >= 0 && earType < earCount)
                {
                    objects[earType].objectTrans = child;
                    objects[earType].isEnable = true;
                }
            }
        }

        private static bool TryParseEarType(string nodeName, out int earType)
        {
            earType = 0;
            if (string.IsNullOrEmpty(nodeName)) return false;
            if (Enum.TryParse(nodeName, true, out EarType parsed))
            {
                earType = (int)parsed;
                return true;
            }
            int divider = nodeName.LastIndexOf(FaceTypeNameDivider, StringComparison.Ordinal);
            if (divider >= 0
                && int.TryParse(nodeName.Substring(divider + FaceTypeNameDivider.Length), out earType))
            {
                return true;
            }
            // Viewer adaptation: the viewer's driven-key locator names its ear nodes after the EarType name
            // ("...__Kanasi"; FaceDrivenKeyTarget.SetupLocator matches them with EndsWith(EarType name)), while the
            // game resolves the full node name through its EarType name table (_earTypeTableDic, not recovered).
            foreach (EarType candidate in Enum.GetValues(typeof(EarType)))
            {
                string candidateName = candidate.ToString();
                if (nodeName.EndsWith(FaceTypeNameDivider + candidateName, StringComparison.OrdinalIgnoreCase)
                    || nodeName.EndsWith("_" + candidateName, StringComparison.OrdinalIgnoreCase))
                {
                    earType = (int)candidate;
                    return true;
                }
            }
            return false;
        }

        // Gallop.DrivenKeyComponent.PlayEar @0x7ffd58e6fa20. The mini timeline path calls it as
        // PlayEar(animation, 0, 0, 0) (MiniAnimationController.PlayEarMotion @0x7ffd59598f40), so
        // only the weight = 0 branch runs: store the animation and its EarTypeSet array, set the
        // clip on the DrivenKeyPlayableAnimator ear layer (SetClip(1, clip)), reset the layer
        // play time, enable the animator and set the flags _isPlayingEarAnim/_isEarPlayFrame
        // (+0xf4 = 0x101). The weight > 0 branch only maintains the EarWeightGroup blend-face
        // list (GetEarTypes) and is not used here.
        public void PlayEar(DrivenKeyAnimation animation)
        {
            if (animation == null || animation.clip == null) return;
            _earKeyAnimation = animation;
            _animationEarTypeArray = animation.animationEarType;

            // DrivenKeyPlayableAnimator.SetClip(1, animation.clip) @0x7ffd58e75b50. The game
            // requires the driven-key locator to be active (GameObject.get_activeSelf), which
            // the caller mirrors by only driving this when the ear nodes exist.
            if (_earClip != animation.clip)
            {
                _earClip = animation.clip;
                CreateEarLayerPlayable(animation.clip);
            }
            // PlayEar writes the layer play time as clip.length * weight = 0 (weight is 0 on the
            // mini path); PlayEarMotion's SetEarTime call overwrites it right after.
            _earPlayTime = 0f;
            // The game also sets the clip's wrapMode from Motion.isLooping (SetClip) and the
            // layer weight to 1 (PlayEar tail); the manual graph needs neither.

            // PlayEar tail: *(ushort*)(this + 0xf4) = 0x101 -> _isPlayingEarAnim = true,
            // _isEarPlayFrame = true; EarWeightGroup.IsCalcWeight (+0x80) = false.
            _isPlayingEarAnim = true;
            _isEarPlayFrame = true;
            if (_earWeight != null) _earWeight.IsCalcWeight = false;
        }

        // Gallop.DrivenKeyComponent.SetEarTime @0x7ffd58e70e00: sets the ear layer play time
        // (seconds) and the _isPlayingEarAnim/_isEarPlayFrame flags.
        public void SetEarTime(float time)
        {
            if (_earClip == null) return;
            _isPlayingEarAnim = true;
            _isEarPlayFrame = true;
            _earPlayTime = time;
        }

        // Gallop.DrivenKeyComponent.SetDrivenKeySpeed @0x7ffd58e70d30: writes the speed of every
        // DrivenKeyPlayableAnimator layer. MiniAnimationController.PlayEarMotion always passes 0
        // (the timeline drives the ear layer time through SetEarTime), so only the ear layer is
        // modelled and the speed is not stored.
        public void SetDrivenKeySpeed(float speed)
        {
            // Intentionally empty: on the mini timeline path speed is always 0 (see comment).
        }

        // Gallop.DrivenKeyComponent.StopLayer(1) @0x7ffd58e72a90 (the ear layer): _isPlayingEarAnim = false and the
        // layer speed = 0. The weights already written into the EarWeightGroup are NOT cleared.
        public void StopEarLayer()
        {
            _isPlayingEarAnim = false;
        }

        // Ear section of Gallop.DrivenKeyComponent.PostJob @0x7ffd58e702a0 for the Live path (the clip plays on its
        // own at layer speed 1 instead of being driven through SetEarTime like the mini path): while
        // _isPlayingEarAnim, pose the ear layer, UpdateEarWeight (locator nodes -> EarWeightGroup), and when the
        // ear clip is a non-looping clip, it is not the frame PlayEar ran (_isEarPlayFrame, +0xF5) and its
        // normalized time reached 1, clear _isPlayingEarAnim (the last weights stay). Then _isEarPlayFrame = false.
        // Viewer adaptation: the game advances the layer time inside the Animator update (scaled game time); here the
        // caller passes the live timeline delta (0 while paused / seeking), so the layer time is advanced by hand.
        // The virtual call on the EarWeightGroup before UpdateEarWeight (vtable +0x188) is not recovered and not
        // ported.
        public void UpdateEarLive(float deltaTime)
        {
            if (_isPlayingEarAnim)
            {
                if (!_isEarPlayFrame && deltaTime > 0f) _earPlayTime += deltaTime;
                EvaluateEarLayer();
                UpdateEarWeight();
                if (_earClip != null && !_isEarPlayFrame && !_earClip.isLooping
                    && _earClip.length > 0f && _earPlayTime / _earClip.length >= 1f)
                {
                    _isPlayingEarAnim = false;
                }
            }
            _isEarPlayFrame = false;
        }

        // Gallop.DrivenKeyComponent.UpdateEarWeight @0x7ffd58e730c0: reads each enabled ear
        // node's localPosition and writes the three per-side weights of that EarType into the
        // weight group. While a side is still ramping (CurrentTimeArray < DurationTimeArray, the
        // SetEar path) the game writes into the target region of CommonWeightArray; on the clip
        // path there is no ramp and the weights go straight to WeightArray.
        // The trailing AddBlendEar call (@0x7ffd58e68e70, blend-face list bookkeeping) is not
        // ported: the viewer's EarWeightGroup model does not use the list (see its header).
        public void UpdateEarWeight()
        {
            if (_earWeight == null || _earKeyAnimation == null) return;
            EarTypeSet[] earTypeSets = _animationEarTypeArray;
            if (earTypeSets == null) return;

            for (int side = 0; side < _drivenKeyForEarArray.Length && side < earTypeSets.Length; side++)
            {
                var drivenKeys = _drivenKeyForEarArray[side];
                var typeSet = earTypeSets[side];
                if (drivenKeys?.objects == null || typeSet?.faceType == null) continue;

                int timeIndex = side * WeightSize;
                // The game writes into the target region while a side is ramping
                // (CurrentTimeArray < DurationTimeArray; SetEar path), into the weight region
                // otherwise (the clip path used by the mini timeline).
                bool isRamping = _earWeight.CurrentTimeArray[timeIndex] < _earWeight.DurationTimeArray[timeIndex];

                foreach (EarType earType in typeSet.faceType)
                {
                    int index = (int)earType;
                    if (index < 0 || index >= drivenKeys.objects.Length) continue;
                    ObjectTransInfo node = drivenKeys.objects[index];
                    if (node == null || !node.isEnable || node.objectTrans == null) continue;

                    Vector3 position = node.objectTrans.localPosition;
                    for (int seg = 0; seg < WeightSize; seg++)
                    {
                        float value = seg == 0 ? position.x * -100f : (seg == 1 ? position.y : position.z) * 100f;
                        if (isRamping)
                            _earWeight.TargetWeightArray[index, side * WeightSize + seg] = value;
                        else
                            _earWeight.WeightArray[index, side * WeightSize + seg] = value;
                    }
                }
            }
        }

        // Poses the ear layer at its current play time (the DrivenKeyPlayableAnimator section of
        // PostJob @0x7ffd58e702a0: UpdateMotion/UpdateEarAnimation -> UpdateAnimation). The game
        // plays the clip through a playable on the driven-key locator's Animator; the viewer
        // keeps a manual graph (same approach as MiniFacialController.PlayClip).
        public void EvaluateEarLayer()
        {
            if (_earClip == null || _locatorAnimator == null) return;
            if (!_earGraph.IsValid()) CreateEarLayerPlayable(_earClip);
            if (!_earGraph.IsValid()) return;
            _earClipPlayable.SetTime(_earPlayTime);
            _earGraph.Evaluate(0f);
        }

        private void CreateEarLayerPlayable(AnimationClip clip)
        {
            if (_locatorAnimator == null) return;
            if (_earGraph.IsValid()) _earGraph.Destroy();
            _earGraph = PlayableGraph.Create("DrivenKeyEarLayer");
            _earGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _earClipPlayable = AnimationClipPlayable.Create(_earGraph, clip);
            _earClipPlayable.SetApplyFootIK(false);
            _earClipPlayable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(_earGraph, "DrivenKeyEar", _locatorAnimator);
            output.SetSourcePlayable(_earClipPlayable);
            _earGraph.Play();
        }

        // Viewer-only: back to the state before any ear clip was played (the game never seeks a
        // live backwards).
        public void ResetEar()
        {
            _earKeyAnimation = null;
            _animationEarTypeArray = null;
            _isPlayingEarAnim = false;
            _isEarPlayFrame = false;
            _earClip = null;
            _earPlayTime = 0f;
            if (_earGraph.IsValid()) _earGraph.Destroy();
            if (_earWeight != null) _earWeight.Reset();
        }

        public void Dispose()
        {
            ResetEar();
            for (int i = 0; i < _drivenKeyForEarArray.Length; i++) _drivenKeyForEarArray[i] = null;
            _locatorAnimator = null;
        }
    }
}
