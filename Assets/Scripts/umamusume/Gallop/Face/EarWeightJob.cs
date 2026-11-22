using Posture = Gallop.FacialDrivenKeyJobWork.Posture;

namespace Gallop
{
    // Port of EarWeightJob (dump_jp.cs TypeDefIndex 33580416): the IJob that turns the EarWeightGroup weights into the
    // ear bone TRS results (ResultTRSArray). It is the ear version of WeightCalcJob: same arrays, but each ear side
    // ("group") carries WEIGHT_SIZE (3) weights, one per bone of that side, instead of one weight per group.
    //
    // Driven by EarWeightJobRunner, which builds the arrays and applies ResultTRSArray to the ear bones.
    public class EarWeightJob
    {
        private const int WeightSize = EarWeightGroup.WEIGHT_SIZE;

        // Job fields (dump offsets).
        public int[] EarBlendFaceArray;                            // 0x10 (EarType values, -1 terminated)
        public int FacialGroupOffset;                              // 0x20
        public int GroupNum;                                       // 0x24 (weights per face = sides * WEIGHT_SIZE)
        public CalcTargetInfomation.JobWorkData CalcInfomation;    // 0x28
        public FacialDrivenKeyJobWork DrivenKeyData;               // 0x48
        public bool[] IsOverrideTarget;                            // 0x78
        public WeightGroup[] WeightArray;                          // 0x88
        public float[] CommonWeightArray;                          // 0x98
        public CalcTargetInfomation.CalcTRS[] ResultTRSArray;                           // 0xA8

        // The game reads the weight NativeArray unchecked; an index past its end (a bone beyond WEIGHT_SIZE on the last
        // group of the last face) counts as 0 here.
        private float ReadWeight(int index)
        {
            return index >= 0 && index < CommonWeightArray.Length ? CommonWeightArray[index] : 0f;
        }

        // EarWeightJob.Execute RVA 0x4AF81A0 (a direct call to CalcTRS()).
        public void Execute()
        {
            CalcTRS();
        }

        // EarWeightJob.CalcTRS() RVA 0x4AF80F0.
        public void CalcTRS()
        {
            if (EarBlendFaceArray == null) return;

            for (int i = 0; i < EarBlendFaceArray.Length; i++)
            {
                if (EarBlendFaceArray[i] == -1) break;
                CalcTRSFromFace(EarBlendFaceArray[i]);
            }

            CalcTargetInfomation.CalcTRS acc = default(CalcTargetInfomation.CalcTRS);
            for (int i = 0; i < EarBlendFaceArray.Length; i++)
            {
                if (EarBlendFaceArray[i] == -1) return;
                BlendTRSFromFace(EarBlendFaceArray[i], ref acc);
            }
        }

        // EarWeightJob.CalcTRSFromFace(int) RVA 0x4AF78B0.
        public void CalcTRSFromFace(int face)
        {
            int postureIndex = DrivenKeyData.FacialStartIndex[face];
            int groupsPerFace = GroupNum / 3;
            int weightStart = WeightArray[face].StartIndex;
            bool overrideFace = IsOverrideTarget[face];

            int group = 0;
            for (int seg = 0; seg < GroupNum; seg += 3, group++)
            {
                int count = DrivenKeyData.FacialPostureNum[groupsPerFace * face + group];
                if (0 < count)
                {
                    int calcStart = CalcInfomation.FaceGroupStartIndexArray[FacialGroupOffset + group];
                    for (int j = 0; j < count; j++)
                    {
                        // One weight per bone of the side (WEIGHT_0..2).
                        float weight = ReadWeight(weightStart + group * WeightSize + j);
                        if (!(0f < weight)) continue;

                        Posture posture = DrivenKeyData.PostureArray[postureIndex + j];
                        // Override-target face: only the postures flagged as override targets are applied
                        // (unlike WeightCalcJob there is no IsIgnoreOverrideTarget switch here).
                        if (overrideFace && !posture.IsOverrideTarget) continue;

                        bool validScale = CalcInfomation.IsValidScaleTransformArray[calcStart + j];
                        ref CalcTargetInfomation.CalcTRS result = ref ResultTRSArray[calcStart + j];
                        if (face == 0) WeightCalcJob.SetTRS(ref result, posture, weight, validScale);
                        else WeightCalcJob.CalcTRS_(ref result, posture, weight, validScale);
                        result.MayaRotation = Math.FromMayaEuler(result.Rotation);
                    }
                }
                postureIndex += count;
            }
        }

        // EarWeightJob.BlendTRSFromFace(int, ref CalcTRS) RVA 0x4AF75B0. Only runs for override-target faces.
        // Two quirks of the decompile are kept as they are:
        //  - the posture-count index is face * GroupNum + seg (seg steps by 3), unlike CalcTRSFromFace which uses
        //    (GroupNum / 3) * face + group. Out-of-range indices (the game reads past the array) count as 0 here.
        //  - the face posture index advances by 1 + count per group, the base index only by count.
        public void BlendTRSFromFace(int face, ref CalcTargetInfomation.CalcTRS acc)
        {
            if (!IsOverrideTarget[face]) return;

            int baseIndex = DrivenKeyData.FacialStartIndex[0];
            int faceIndex = DrivenKeyData.FacialStartIndex[face];
            int weightStart = WeightArray[face].StartIndex;

            int group = 0;
            for (int seg = 0; seg < GroupNum; seg += 3, group++)
            {
                int numIndex = face * GroupNum + seg;
                int count = numIndex < DrivenKeyData.FacialPostureNum.Length ? DrivenKeyData.FacialPostureNum[numIndex] : 0;
                if (0 < count)
                {
                    int calcStart = CalcInfomation.FaceGroupStartIndexArray[FacialGroupOffset + group];
                    for (int j = 0; j < count; j++)
                    {
                        float weight = ReadWeight(weightStart + group * WeightSize + j);
                        if (!(0f < weight)) continue;

                        Posture facePosture = DrivenKeyData.PostureArray[faceIndex + j];
                        if (!facePosture.IsOverrideTarget) continue;

                        Posture basePosture = DrivenKeyData.PostureArray[baseIndex + j];
                        bool validScale = CalcInfomation.IsValidScaleTransformArray[calcStart + j];
                        ref CalcTargetInfomation.CalcTRS result = ref ResultTRSArray[calcStart + j];
                        WeightCalcJob.AddTRS(ref acc, basePosture, facePosture, weight, validScale);
                        WeightCalcJob.BlendTRS(ref result, acc, validScale);
                        result.MayaRotation = Math.FromMayaEuler(result.Rotation);
                    }
                }
                baseIndex += count;
                faceIndex += 1 + count;
            }
        }
    }
}
