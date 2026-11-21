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
                // Keeps the cached key state of the list up to date (other consumers read TimeKeyIndex).
                LiveTimelineControl.AlterUpdate_Key(_currentKey, currentTime);

                // LiveTimelineMotionSequence.AlterUpdate @0x7ffd59300a80: the key that plays is the one FindPlayingMot
                // finds (frame <= curFrame <= frame + playFrameLength), not simply the last key that started. Inside a
                // gap between keys the game disables the animation and samples nothing.
                int curFrame = (int)(currentTime / kFrameSec);
                LiveTimelineKeyCharaMotionData arg = FindPlayingMot(curFrame, out int playingIndex);
                _indexOfPlayingKey = playingIndex;
                if (arg == null) return;

                _curIndex = playingIndex;
                if (string.IsNullOrEmpty(arg.motionName) || arg.clip == null) return;
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
                        SampleLastPositionClip(playingIndex, timescaleKeys);

                    SampleClip(anim, arg.loop ? Mathf.Repeat(currentAnimationTime, anim.length) : currentAnimationTime);
                }
                _prevIndex = _curIndex;            
            }
        }

        // LiveTimelineMotionSequence.FindPlayingMot @0x7ffd59300df0 (the same scan is inlined in AlterUpdate
        // @0x7ffd59300a80): walks the keys in order; the first key with curFrame < key.frame ends the search (nothing
        // plays); the first key with curFrame <= key.frame + playFrameLength (+0x80) is the playing one (an earlier key
        // wins over a later one that already started).
        private LiveTimelineKeyCharaMotionData FindPlayingMot(int curFrame, out int index)
        {
            index = -1;
            var list = _currentKey != null ? _currentKey.thisList : null;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var key = list[i];
                if (key == null) return null;
                if (curFrame < key.frame) return null;
                if (curFrame <= key.frame + key.playFrameLength)
                {
                    index = i;
                    return key;
                }
            }
            return null;
        }

        private const float kFrameSec = 0.016666668f; // DAT_7ffd5a640e78 = 1/60

        // LiveTimelineControl.GetCurrentTimescale @0x7ffd592bffd0: the Timescale of the timescale key in force at the
        // frame (FindTimelineKey, no interpolation), 1.0 when there is none.
        private static float GetCurrentTimescale(LiveTimelineKeyTimescaleDataList keys, int frame)
        {
            float scale = 1f;
            if (keys == null || keys.thisList == null) return scale;
            for (int i = 0; i < keys.thisList.Count; i++)
            {
                var k = keys.thisList[i];
                if (k == null) continue;
                if (k.frame > frame) break;
                scale = k.Timescale;
            }
            return scale;
        }

        // LiveTimelineControl.IsDefaultTimescale @0x7ffd592c2a50: scanning the keys from the last one backwards, the
        // first key at or before the frame decides: a scale other than 1 -> false; scale 1 -> keep looking only while
        // that key is not before the start frame of the motion.
        private static bool IsDefaultTimescale(LiveTimelineKeyTimescaleDataList keys, int startFrame, int curFrame)
        {
            if (keys == null || keys.thisList == null) return true;
            for (int i = keys.thisList.Count - 1; i >= 0; i--)
            {
                var k = keys.thisList[i];
                if (k == null || curFrame < k.frame) continue;
                if (k.Timescale != 1f) return false;
                if (startFrame > k.frame) return true;
            }
            return true;
        }

        // Port of LiveTimelineUtil.GetAnimationTime @0x7ffd59305c30 as called by LegacyAnimationUpdate
        // @0x7ffd593022d0 (the timeline is integrated frame by frame with the timescale of each frame; the result
        // of the previous frame is cached in +0x5C/+0x60, here _prevFrame/_prevFrameAnimationTime).
        // useCache = false for one-off evaluations (seek branch, nothing stored).
        private float ComputeAnimationTime(LiveTimelineKeyCharaMotionData arg, float currentTime, LiveTimelineKeyTimescaleDataList timescaleKeys, bool useCache = true)
        {
            int headFrame = arg.isMotionHeadFrameAll ? arg.motionHeadFrame : arg.motionHeadFrameSeparetes[charaIndex];
            int keyFrame = arg.frame;
            int curFrame = (int)(currentTime / kFrameSec);
            float playSpeed = arg.playSpeed;
            bool disabled = arg.IsTimescaleDisabled;
            int prevFrame = useCache ? _prevFrame : -1;
            float prevAccum = useCache ? _prevFrameAnimationTime : 0f;

            float remainder = (currentTime - curFrame * kFrameSec) * playSpeed;
            if (remainder <= 0f) remainder = 0f;

            float accum, result;
            if (IsDefaultTimescale(timescaleKeys, keyFrame, curFrame))
            {
                int frames = curFrame - keyFrame;
                if (frames < 1) frames = 0;
                accum = (frames * playSpeed + headFrame) * kFrameSec;
                result = accum + remainder;
            }
            else
            {
                float scale = 1f;
                if (curFrame == prevFrame)
                {
                    accum = prevAccum;
                    if (!disabled) scale = GetCurrentTimescale(timescaleKeys, curFrame);
                }
                else if (prevFrame < curFrame && keyFrame < prevFrame && prevFrame >= 0)
                {
                    accum = prevAccum;
                    for (int f = prevFrame; f < curFrame; f++)
                    {
                        scale = disabled ? 1f : GetCurrentTimescale(timescaleKeys, f);
                        accum += playSpeed * kFrameSec * scale;
                    }
                }
                else
                {
                    accum = headFrame * kFrameSec;
                    for (int f = keyFrame; f < curFrame; f++)
                    {
                        scale = disabled ? 1f : GetCurrentTimescale(timescaleKeys, f);
                        accum += playSpeed * kFrameSec * scale;
                    }
                }
                result = remainder * scale + accum;
            }

            if (useCache)
            {
                _prevFrameAnimationTime = accum;
                _prevFrame = curFrame;
            }
            return result;
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
                float t = ComputeAnimationTime(prev, endTime, timescaleKeys, false);
                SampleClip(prev.clip, prev.loop ? Mathf.Repeat(t, prev.clip.length) : t);
                return;
            }
        }
    }
}
