using System.Collections.Generic;
using UnityEngine;
using Posture = Gallop.FacialDrivenKeyJobWork.Posture;

namespace Gallop
{
    // Viewer-side wiring of EarWeightJob (EarWeightJob.cs): builds the job arrays from the character's ear
    // TargetInfomation list, runs the job on the EarWeightGroup weights and writes the resulting TRS to the ear bones.
    //
    // How the game does each step (RVA / address from the JP dump):
    //   - FacialDrivenKeyJobWork.Initialize(DrivenKeyTarget) 0x4B05100: FacialStartIndex[face] = running posture count,
    //     FacialPostureNum[face * groups + group] = bones of that (face, group), PostureArray = the TRS of every bone
    //     (position, scale, Maya euler rotation, IsOverrideTarget) in face / group / bone order.
    //   - CalcTargetInfomation.SetupJobWork 0x4AFBCE0 -> JobWorkData.Initialize 0x4B10B20: FaceGroupStartIndexArray[group]
    //     = running count of the group's calc targets, IsValidScaleTransformArray[calc target] = target scale flag.
    //   - WeightGroupBase.Initialize 0x4AFA8B0: WeightArray[face] = (face * GroupNum, GroupNum) over CommonWeightArray,
    //     IsOverrideTarget[face] = the face has a TRS flagged as override target.
    //   - EarWeightGroup.ClearBlendFace 0x4AF7100 / AddBlendFace 0x4AF6EF0: EarBlendFaceArray always starts with
    //     EarType.Base (0), then the faces added by DrivenKeyComponent.AddBlendEar, then -1.
    //   - EarWeightGroup.CalcTRS 0x4AF6F50 schedules EarWeightJob; CalcTargetInfomation.ClearCalc 0x4AFBBC0 zeroes
    //     CommonCalcTRSArray (0x38 bytes per entry) before it.
    //   - DrivenKeyComponent.AfterLateUpdate 0x4AE8EF0: for every calc target with a valid transform,
    //     Transform.SetLocalPositionAndRotation(result.Position, result.MayaRotation), and localScale = result.Scale when
    //     the target's IsValidScaleTransform is set.
    //
    // Adaptations (the same ones the EarWeightGroup header already documents):
    //   - The blend face list is rebuilt every frame: Base plus every EarType with a positive weight on any side /
    //     segment. The game keeps a list that DrivenKeyComponent.SetEar / ClearEarBlendFaces maintain; faces with no
    //     positive weight are skipped by the job, so both lists produce the same result.
    //   - The game skips the bone write when the entry's weight did not change since the previous frame
    //     (AfterLateUpdate, Math.IsFloatEqualLight); here the bones are written every frame.
    //   - Viewer-only guard: a result no face wrote to (CurWeight <= 0) is not applied, so an all-zero rotation can never
    //     reach a Transform.
    public class EarWeightJobRunner
    {
        private readonly EarWeightJob _job = new EarWeightJob();
        private Transform[] _targets;     // per calc target (CalcTRS index)
        private bool[] _validScale;       // per calc target
        private int _faceCount;
        private int _groupNum;

        // ---- Virtual parent (DrivenKeyComponent.SetVirtualParentTransform 0x4AF27F0 / ApplyVirtualParentTransform 0x4AE9560) ----
        // The game makes Ear_02 / Ear_03 follow the CySpring ear chain: after the job result is written to a calc target
        // whose CalcTRSTarget.IsSetTemporary (+0x1A) is set, AfterLateUpdate (0x4AE8EF0) stores that local rotation in
        // CalcTRSTarget.TemporaryRotation (+0x1C); ApplyVirtualParentTransform then runs every frame right after
        // CySpringController.EndSimulation (CySpringUpdater.EndSimulation 0x7ffd5957a310, event 3) and sets, for every
        // registered VirtualParentInfo (Root +0x10, Parent +0x18, TargetInfo +0x20):
        //     target.localRotation = Inverse(Root.rotation) * Parent.rotation * TemporaryRotation
        // Calc targets of a group are the bones of the Base face; the group index is the facial group (1 = left, 0 = right,
        // as LiveFaceController.SetEar).
        private int[] _groupStart;
        private int[] _groupCount;
        private bool[] _isSetTemporary;          // per calc target, CalcTRSTarget.IsSetTemporary
        private Quaternion[] _temporaryRotation; // per calc target, CalcTRSTarget.TemporaryRotation
        private readonly List<VirtualParentInfo> _virtualParents = new List<VirtualParentInfo>();

        // DrivenKeyComponent.VirtualParentInfo (dump_jp TypeDefIndex 33580422).
        private sealed class VirtualParentInfo
        {
            public Transform Root;   // 0x10
            public Transform Parent; // 0x18
            public int Target;       // TargetInfo (0x20): index of the calc target
        }

        // DrivenKeyComponent.SetVirtualParentTransform(FaceGroupType group, string name, Transform root, Transform parent).
        // With a non-null parent: finds the group's calc target whose transform is called `name`, sets IsSetTemporary and
        // adds the entry. With a null parent: clears IsSetTemporary and removes the entry of that name.
        public void SetVirtualParentTransform(int group, string name, Transform root, Transform parent)
        {
            if (!IsValid || _groupStart == null || group < 0 || group >= _groupStart.Length) return;
            int start = _groupStart[group];
            int end = start + _groupCount[group];
            if (parent != null)
            {
                for (int i = start; i < end; i++)
                {
                    if (_targets[i] == null || _targets[i].name != name) continue;
                    _isSetTemporary[i] = true;
                    _virtualParents.Add(new VirtualParentInfo { Root = root, Parent = parent, Target = i });
                    return;
                }
            }
            else
            {
                for (int k = 0; k < _virtualParents.Count; k++)
                {
                    int target = _virtualParents[k].Target;
                    if (_targets[target] == null || _targets[target].name != name) continue;
                    _isSetTemporary[target] = false;
                    _virtualParents.RemoveAt(k);
                    return;
                }
            }
        }

        // DrivenKeyComponent.ApplyVirtualParentTransform (0x4AE9560).
        public void ApplyVirtualParentTransform()
        {
            for (int k = 0; k < _virtualParents.Count; k++)
            {
                VirtualParentInfo info = _virtualParents[k];
                Transform target = _targets[info.Target];
                if (info.Root == null || info.Parent == null || target == null) continue;
                Quaternion relative = Quaternion.Inverse(info.Root.rotation) * info.Parent.rotation;
                target.localRotation = relative * _temporaryRotation[info.Target];
            }
        }

        public bool IsValid { get; private set; }

        // earTarget: FaceDrivenKeyTarget._earTarget, whose TrsArray.transform fields are already resolved.
        public EarWeightJobRunner(List<TargetInfomation> earTarget)
        {
            if (earTarget == null || earTarget.Count == 0 || earTarget[0]._faceGroupInfo == null) return;

            _faceCount = earTarget.Count;
            int groups = earTarget[0]._faceGroupInfo.Count;
            if (groups == 0) return;
            _groupNum = groups * EarWeightGroup.WEIGHT_SIZE;

            var facialStart = new int[_faceCount];
            var facialNum = new int[_faceCount * groups];
            var postures = new List<Posture>();
            var overrideFace = new bool[_faceCount];
            var weightGroups = new WeightGroup[_faceCount];

            for (int face = 0; face < _faceCount; face++)
            {
                facialStart[face] = postures.Count;
                weightGroups[face] = new WeightGroup { StartIndex = face * _groupNum, Length = _groupNum };
                var faceGroups = earTarget[face]._faceGroupInfo;
                for (int g = 0; g < groups; g++)
                {
                    var trsList = g < faceGroups.Count ? faceGroups[g]._trsArray : null;
                    int count = trsList != null ? trsList.Count : 0;
                    facialNum[face * groups + g] = count;
                    for (int i = 0; i < count; i++)
                    {
                        TrsArray trs = trsList[i];
                        overrideFace[face] |= trs.IsOverrideTarget;
                        postures.Add(new Posture
                        {
                            Position = trs._position,
                            Scale = trs._scale,
                            Rotation = trs._rotation,
                            IsOverrideTarget = trs.IsOverrideTarget
                        });
                    }
                }
            }

            // The calc targets of each group are the bones of the Base face (EarType 0).
            var groupStart = new int[groups];
            var targets = new List<Transform>();
            var validScale = new List<bool>();
            for (int g = 0; g < groups; g++)
            {
                groupStart[g] = targets.Count;
                var trsList = g < earTarget[0]._faceGroupInfo.Count ? earTarget[0]._faceGroupInfo[g]._trsArray : null;
                if (trsList == null) continue;
                foreach (TrsArray trs in trsList)
                {
                    targets.Add(trs.transform);
                    validScale.Add(trs._isValidScaleTransform);
                }
            }
            _targets = targets.ToArray();
            _validScale = validScale.ToArray();
            _groupStart = groupStart;
            _groupCount = new int[groups];
            for (int g = 0; g < groups; g++)
                _groupCount[g] = (g + 1 < groups ? groupStart[g + 1] : _targets.Length) - groupStart[g];
            _isSetTemporary = new bool[_targets.Length];
            _temporaryRotation = new Quaternion[_targets.Length];
            for (int i = 0; i < _temporaryRotation.Length; i++) _temporaryRotation[i] = Quaternion.identity;

            _job.FacialGroupOffset = 0;
            _job.GroupNum = _groupNum;
            _job.CalcInfomation = new CalcTargetInfomation.JobWorkData
            {
                FaceGroupStartIndexArray = groupStart,
                IsValidScaleTransformArray = _validScale
            };
            _job.DrivenKeyData = new FacialDrivenKeyJobWork
            {
                FacialStartIndex = facialStart,
                PostureArray = postures.ToArray(),
                FacialPostureNum = facialNum
            };
            _job.IsOverrideTarget = overrideFace;
            _job.WeightArray = weightGroups;
            _job.CommonWeightArray = new float[_faceCount * _groupNum];
            _job.ResultTRSArray = new CalcTargetInfomation.CalcTRS[_targets.Length];
            _job.EarBlendFaceArray = new int[_faceCount + 1];
            IsValid = true;
        }

        // EarWeightGroup.CalcTRS + DrivenKeyComponent.AfterLateUpdate (ear groups): runs the job on the current weights
        // and writes the ear bones.
        public void Apply(EarWeightGroup weights)
        {
            if (!IsValid || weights == null) return;

            // CommonWeightArray (current plane): [face * GroupNum + side * WEIGHT_SIZE + segment].
            float[] common = _job.CommonWeightArray;
            int typeCount = Mathf.Min(_faceCount, EarWeightGroup.EAR_TYPE_NUM);
            int weightCount = Mathf.Min(_groupNum, EarWeightGroup.SIDE_NUM * EarWeightGroup.WEIGHT_SIZE);
            int blendCount = 0;
            int[] blend = _job.EarBlendFaceArray;
            blend[blendCount++] = 0; // EarType.Base
            for (int type = 0; type < typeCount; type++)
            {
                bool active = false;
                for (int w = 0; w < weightCount; w++)
                {
                    float value = weights.WeightArray[type, w];
                    common[type * _groupNum + w] = value;
                    if (type > 0 && 0f < value) active = true;
                }
                if (active) blend[blendCount++] = type;
            }
            blend[blendCount] = -1;

            // CalcTargetInfomation.ClearCalc: the whole result array is zeroed before the job.
            System.Array.Clear(_job.ResultTRSArray, 0, _job.ResultTRSArray.Length);
            _job.Execute();

            for (int i = 0; i < _targets.Length; i++)
            {
                Transform target = _targets[i];
                if (target == null) continue;
                CalcTargetInfomation.CalcTRS result = _job.ResultTRSArray[i];
                if (!(0f < result.CurWeight)) continue; // viewer-only guard, see the class comment

                target.SetLocalPositionAndRotation(result.Position, result.MayaRotation);
                if (_validScale[i]) target.localScale = result.Scale;
                // AfterLateUpdate: IsSetTemporary -> TemporaryRotation = the local rotation just written.
                if (_isSetTemporary[i]) _temporaryRotation[i] = target.localRotation;
            }
        }
    }
}
