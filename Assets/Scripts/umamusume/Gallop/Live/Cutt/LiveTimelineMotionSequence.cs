using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RootMotion.FinalIK;

namespace Gallop.Live.Cutt
{
    public class LiveTimelineMotionSequence
    {
        private ILiveTimelineMSQTarget _target; // 0x10
        private Animation[] _animationArray; // 0x18
        private int _targetIndex; // 0x20
        private LiveTimelineKeyCharaMotionSeqDataList[] _keyArray; // 0x28
        private LiveTimelineKeyCharaMotionSeqDataList _currentKey; // 0x30
        private int _indexOfPlayingKey; // 0x38
        private LiveTimelineControl _timelineControl; // 0x40
        private float _heightRate; // 0x48
        private LiveTimelineDefine.SheetIndex _currentSheetIndex; // 0x4C
        //private LivePlayableAnimator[] _playableAnimatorArray; // 0x50
        private int _prevSysTextId; // 0x58
        private const float OVERRIDE_ANIMATOR_FACIAL_WAIT = 1;
        private int _prevFrame; // 0x5C
        private float _prevFrameAnimationTime; // 0x60

        //Testing
        private Transform _tempTarget;
        private Animation _tempAnim;
        private BipedIK _sampleIk;
        private bool _resetIkBeforeSample;

        private bool _needChange;
        private LiveTimelineKeyCharaMotionData _prevKey;

        private AnimationClip _targetAnim = null;

        private bool _motionSetup = false;

        private int _curIndex = -1;
        private int _prevIndex = -1;

        public int charaIndex = -1;

        public void Initialize(Transform target, int targetIndex, int seqDataIndex, LiveTimelineControl timelineControl, List<AnimationClip> animclips = null)
        {
            var animations = Director.instance.charaAnims;
            if (targetIndex < 0 || targetIndex >= animations.Count || animations[targetIndex] == null)
                return;

            _tempTarget = target;
            _targetIndex = targetIndex;
            charaIndex = targetIndex;
            _tempAnim = animations[targetIndex];

            if (animclips != null)
            {
                if (targetIndex >= animclips.Count) return;
                _targetAnim = animclips[targetIndex];
                if (_targetAnim != null) _tempAnim.AddClip(_targetAnim, _targetAnim.name);
                return;
            }

            _keyArray = timelineControl._keyArray;
            if (seqDataIndex < 0 || seqDataIndex >= _keyArray.Length) return;
            _currentKey = _keyArray[seqDataIndex];
            _sampleIk = _tempAnim.GetComponent<BipedIK>();
            if (_sampleIk != null)
            {
                // SolverManager.Update otherwise restores the bind pose after our manual sample.
                _resetIkBeforeSample = _sampleIk.fixTransforms;
                if (_resetIkBeforeSample) _sampleIk.fixTransforms = false;
            }

            if (_currentKey != null)
            {
                foreach (var key in _currentKey.thisList)
                    SetupAnimationClip(key);
            }
            _tempAnim.wrapMode = WrapMode.Clamp;
            _tempAnim.enabled = false;
        }

        private void SetupAnimationClip(LiveTimelineKeyCharaMotionData key)
        {
            // Native SetupAnimationClip (0x1af9ed0) ignores unnamed main-motion keys.
            if (key == null || string.IsNullOrEmpty(key.motionName)) return;
            // Native LoadAnimationClip (0x1ae4780) uses the existing clip triplet first.
            if (key.clip == null)
            {
                key.clip = LoadLiveAnimationClip(key.motionName, key.clip);
                key.clip2 = LoadLiveAnimationClip(key.motionName2, key.clip2);
                key.clip3 = LoadLiveAnimationClip(key.motionName3, key.clip3);
            }
            if (key.clip != null) _tempAnim.AddClip(key.clip, key.clip.name);
            if (key.clip2 != null) _tempAnim.AddClip(key.clip2, key.clip2.name);
            if (key.clip3 != null) _tempAnim.AddClip(key.clip3, key.clip3.name);
        }

        private static AnimationClip LoadLiveAnimationClip(string motionName, AnimationClip existingClip)
        {
            if (existingClip != null || string.IsNullOrEmpty(motionName)) return existingClip;
            // ResourcePath.GetLiveAnimationClipPath: original body motion, not a substitute action.
            string path = "3d/motion/live/body/" + motionName;
            var entry = LiveEffectController.FindResourceEntry(path);
            if (entry == null)
            {
                Debug.LogError("[LiveTimelineMotionSequence] Motion resource not found: " + path);
                return null;
            }
            var bundle = UmaAssetManager.LoadAssetBundle(entry);
            if (bundle == null)
            {
                Debug.LogError("[LiveTimelineMotionSequence] Motion bundle load failed: " + path);
                return null;
            }
            string assetName = Path.GetFileNameWithoutExtension(entry.Name);
            var clip = bundle.LoadAsset<AnimationClip>(assetName);
            if (clip == null)
            {
                foreach (string asset in bundle.GetAllAssetNames())
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(asset), assetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    clip = bundle.LoadAsset<AnimationClip>(asset);
                    break;
                }
            }
            if (LiveRuntimeDiagnostics.Enabled)
                LiveRuntimeDiagnostics.RecordAssetLoad(entry.Name, "LoadAsset", typeof(AnimationClip),
                    new UnityEngine.Object[] { clip }, clip, assetName);
            if (clip == null)
                Debug.LogError("[LiveTimelineMotionSequence] Motion clip load failed: " + path + " (" + assetName + ")");
            return clip;
        }

        // noiseAddTime: per-character offset of the CharaMotionNoise track. Native LegacyAnimationUpdate
        // (0x7ffd593022d0) adds it to the result of LiveTimelineUtil.GetAnimationTime before the clip is played.
        public void AlterUpdate(float currentTime, LiveTimelineKeyTimescaleDataList timescaleKeys, float noiseAddTime = 0f)
        {
            var director = Director.instance;
            if (Director.instance.liveMode == 0)
            {
                if (_targetAnim != null)
                {
                    if (director.sliderControl.is_Touched || director.IsRecordVMD)
                    {
                        _tempAnim[_targetAnim.name].time = currentTime;
                        _tempAnim.Play(_targetAnim.name);
                        _motionSetup = false;
                    }
                    else if (!_motionSetup)
                    {
                        _tempAnim[_targetAnim.name].time = currentTime;
                        _tempAnim.Play(_targetAnim.name);
                        _motionSetup = true;
                    }
                }
                return;
            }

            if (_currentKey != null && _tempAnim != null)
            {
                LiveTimelineKeyIndex curKey = LiveTimelineControl.AlterUpdate_Key(_currentKey, currentTime);

                _curIndex = curKey.index;
                LiveTimelineKeyCharaMotionData arg = curKey.key as LiveTimelineKeyCharaMotionData;
                if (arg == null || string.IsNullOrEmpty(arg.motionName) || arg.clip == null) return;
                AnimationClip anim = arg.clip;

                float currentAnimationTime = ComputeAnimationTime(arg, currentTime, timescaleKeys) + noiseAddTime;

                if (anim)
                {
                    // Clips such as anm_liv_son1092_*_c25 carry no curve for the "Position" bone. A legacy Animation
                    // leaves an un-animated bone at the last value that was sampled, so after a seek (or a scrub) it kept
                    // whatever the previously visited clip left there, and the character stood somewhere different every
                    // time. In continuous playback that value is the one the last clip that does animate "Position" left
                    // at the end of its span, so it is reproduced here from the key list alone.
                    if (!ClipAnimatesPosition(anim))
                        SampleLastPositionClip(curKey.index, timescaleKeys);

                    SampleClip(anim, arg.loop ? Mathf.Repeat(currentAnimationTime, anim.length) : currentAnimationTime);
                }
                _prevIndex = _curIndex;            
            }
            _prevFrameAnimationTime = currentTime;
        }

        // CalculateAnimationInterval 0x1af9640: authored zero means normal speed.
        private float ComputeAnimationTime(LiveTimelineKeyCharaMotionData arg, float currentTime, LiveTimelineKeyTimescaleDataList timescaleKeys)
        {
            double start;
            if (arg.isMotionHeadFrameAll)
            {
                start = (double)arg.motionHeadFrame / 60;
            }
            else
            {
                start = (double)arg.motionHeadFrameSeparetes[charaIndex] / 60;
            }

            // Live 1092 probe (state_trace, slots 6-11 frames 4620..4758): the clones play anm_*_c28 keys authored with
            // playSpeed = 0 and their bones do not move at all in the game (delta 0.0000), so a 0 speed holds the clip at
            // motionHeadFrame instead of meaning "normal speed". Keys named *_pose use the same value.
            float playSpeed = arg.playSpeed;
            double interval = 0;
            double last_current_time = currentTime;
            if (!arg.IsTimescaleDisabled && timescaleKeys != null && timescaleKeys.Count > 0)
            {
                var has_key = false;
                // apply timescale keys
                for (int i = timescaleKeys.thisList.Count - 1; i >= 0; i--)
                {

                    var scaleKey = timescaleKeys.thisList[i];
                    if (scaleKey.FrameSecond <= currentTime)
                    {
                        has_key = true;
                        if (scaleKey.FrameSecond <= arg.FrameSecond)
                        {
                            interval += (last_current_time - arg.FrameSecond) * scaleKey.Timescale * playSpeed;
                            break;
                        }
                        else
                        {
                            interval += (last_current_time - scaleKey.FrameSecond) * scaleKey.Timescale * playSpeed;
                            last_current_time = scaleKey.FrameSecond;
                        }
                    }
                }
                if (!has_key)
                {
                    interval = (currentTime - arg.FrameSecond) * playSpeed;
                }
            }
            else
            {
                interval = (currentTime - arg.FrameSecond) * playSpeed;
            }
            return (float)(start + interval);
        }

        private void SampleClip(AnimationClip clip, float time)
        {
            if (_resetIkBeforeSample && _sampleIk != null && _sampleIk.isActiveAndEnabled)
            {
                _sampleIk.solvers.lookAt.FixTransforms();
                foreach (var limb in _sampleIk.solvers.limbs) limb.FixTransforms();
            }
            var state = _tempAnim[clip.name];
            state.enabled = true;
            state.weight = 1;
            state.time = time;
            _tempAnim.Sample();
            state.enabled = false;
        }

        // Whether the clip has a translation curve on the "Position" bone. Detected at runtime (the curve bindings are
        // editor-only): the bone is parked far away, the clip is sampled once and the bone is checked.
        private static readonly Dictionary<AnimationClip, bool> _clipAnimatesPosition = new Dictionary<AnimationClip, bool>();
        private Transform _positionBone;

        private bool ClipAnimatesPosition(AnimationClip clip)
        {
            if (_clipAnimatesPosition.TryGetValue(clip, out bool has))
                return has;

            if (_positionBone == null)
                _positionBone = _tempAnim.transform.Find("Position");
            if (_positionBone == null)
                return true; // cannot probe: leave the behavior as it was

            Vector3 saved = _positionBone.localPosition;
            _positionBone.localPosition = new Vector3(1e6f, 1e6f, 1e6f);
            SampleClip(clip, 0f);
            has = _positionBone.localPosition.x < 1e5f;
            _positionBone.localPosition = saved;
            _clipAnimatesPosition[clip] = has;
            return has;
        }

        // Samples, at the last time it played, the nearest earlier clip that animates "Position".
        private void SampleLastPositionClip(int currentIndex, LiveTimelineKeyTimescaleDataList timescaleKeys)
        {
            for (int k = currentIndex - 1; k >= 0; k--)
            {
                var prev = _currentKey[k] as LiveTimelineKeyCharaMotionData;
                var next = _currentKey[k + 1];
                if (prev == null || prev.clip == null || next == null || !ClipAnimatesPosition(prev.clip))
                    continue;

                float endTime = (float)System.Math.Max(prev.FrameSecond, next.FrameSecond - 1.0 / 60.0);
                float t = ComputeAnimationTime(prev, endTime, timescaleKeys);
                SampleClip(prev.clip, prev.loop ? Mathf.Repeat(t, prev.clip.length) : t);
                return;
            }
        }
    }
}
