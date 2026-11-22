using UnityEngine;
using CalcTRS = Gallop.CalcTargetInfomation.CalcTRS;
using BonePosture = Gallop.CalcTargetInfomation.BonePosture;
using Posture = Gallop.FacialDrivenKeyJobWork.Posture;

namespace Gallop
{
    // Port of WeightCalcJob (dump_jp.cs TypeDefIndex 33580409), the IJob that blends the face / eye / mouth weights into
    // bone TRS results. EarWeightJob (EarWeightJob.cs) reuses its static helpers (the game calls them on a WeightCalcJob
    // instance whose state they never read).
    //
    // Each method notes the original and its RVA. The decompile was ported line by line, quirks included.
    public class WeightCalcJob
    {
        // Job fields (dump offsets).
        public float[] CommonWeightArray;                          // 0x10
        public WeightGroup[] WeightArray;                          // 0x20
        public int FacialGroupOffset;                              // 0x30
        public bool[] IsOverrideTarget;                            // 0x38
        public bool IsEnableAdditiveMotion;                        // 0x48
        public int[] BlendFaceArray;                               // 0x50 (-1 terminated)
        public CalcTargetInfomation.JobWorkData CalcInfomation;    // 0x60
        public FacialDrivenKeyJobWork DrivenKeyData;               // 0x80
        public BonePosture[] CurrentPostureArray;                  // 0xB0
        public BonePosture[] PrevPostureArray;                     // 0xC0
        public bool IsIgnoreOverrideTarget;                        // 0xD0
        public CalcTRS[] ResultTRSArray;                           // 0xD8

        // WeightCalcJob.Execute RVA 0x4AFA0D0.
        public void Execute()
        {
            if (BlendFaceArray != null)
            {
                for (int i = 0; i < BlendFaceArray.Length; i++)
                {
                    if (BlendFaceArray[i] == -1) break;
                    CalcTRSFromFace(BlendFaceArray[i]);
                }
            }

            CalcTRS acc = default(CalcTRS);
            if (BlendFaceArray != null)
            {
                for (int i = 0; i < BlendFaceArray.Length; i++)
                {
                    if (BlendFaceArray[i] == -1) break;
                    BlendTRSFromFace(BlendFaceArray[i], ref acc);
                }
            }

            if (IsEnableAdditiveMotion)
            {
                // The group count is the Length of WeightArray[0] (the game reads the high dword of its first qword).
                int groups = WeightArray[0].Length;
                if (groups != 0)
                {
                    for (int g = 0; g < groups; g++)
                    {
                        int count = DrivenKeyData.FacialPostureNum[g];
                        int index = CalcInfomation.FaceGroupStartIndexArray[FacialGroupOffset + g];
                        for (int k = 0; k < count; k++)
                        {
                            SetTRSAdditive(ref ResultTRSArray[index + k], CurrentPostureArray[index + k], PrevPostureArray[index + k]);
                        }
                    }
                }
            }
        }

        // WeightCalcJob.CalcTRSFromFace(int) RVA 0x4AF9870.
        public void CalcTRSFromFace(int face)
        {
            int postureIndex = DrivenKeyData.FacialStartIndex[face];
            WeightGroup weights = WeightArray[face];
            int groups = weights.Length;
            bool overrideFace = IsOverrideTarget[face];

            for (int g = 0; g < groups; g++)
            {
                int count = DrivenKeyData.FacialPostureNum[groups * face + g];
                float weight = CommonWeightArray[weights.StartIndex + g];
                if (0f < weight && 0 < count)
                {
                    int calcStart = CalcInfomation.FaceGroupStartIndexArray[FacialGroupOffset + g];
                    for (int j = 0; j < count; j++)
                    {
                        Posture posture = DrivenKeyData.PostureArray[postureIndex + j];
                        // Override-target face: only the postures not flagged as override targets are applied, unless
                        // IsIgnoreOverrideTarget is set.
                        if (overrideFace && !IsIgnoreOverrideTarget && posture.IsOverrideTarget) continue;

                        bool validScale = CalcInfomation.IsValidScaleTransformArray[calcStart + j];
                        ref CalcTRS result = ref ResultTRSArray[calcStart + j];
                        if (face == 0) SetTRS(ref result, posture, weight, validScale);
                        else CalcTRS_(ref result, posture, weight, validScale);
                        result.MayaRotation = Math.FromMayaEuler(result.Rotation);
                    }
                }
                postureIndex += count;
            }
        }

        // WeightCalcJob.BlendTRSFromFace(int, ref CalcTRS) RVA 0x4AF9390. Only runs for override-target faces.
        public void BlendTRSFromFace(int face, ref CalcTRS acc)
        {
            if (!IsOverrideTarget[face]) return;

            int baseIndex = DrivenKeyData.FacialStartIndex[0];
            int faceIndex = DrivenKeyData.FacialStartIndex[face];
            WeightGroup weights = WeightArray[face];
            int groups = weights.Length;

            for (int g = 0; g < groups; g++)
            {
                int count = DrivenKeyData.FacialPostureNum[groups * face + g];
                float weight = CommonWeightArray[weights.StartIndex + g];
                if (0f < weight && 0 < count)
                {
                    int calcStart = CalcInfomation.FaceGroupStartIndexArray[FacialGroupOffset + g];
                    for (int j = 0; j < count; j++)
                    {
                        Posture facePosture = DrivenKeyData.PostureArray[faceIndex + j];
                        if (!facePosture.IsOverrideTarget) continue;

                        Posture basePosture = DrivenKeyData.PostureArray[baseIndex + j];
                        bool validScale = CalcInfomation.IsValidScaleTransformArray[calcStart + j];
                        ref CalcTRS result = ref ResultTRSArray[calcStart + j];
                        AddTRS(ref acc, basePosture, facePosture, weight, validScale);
                        BlendTRS(ref result, acc, validScale);
                        result.MayaRotation = Math.FromMayaEuler(result.Rotation);
                    }
                }
                baseIndex += count;
                faceIndex += count;
            }
        }

        // WeightCalcJob.SetTRS(ref CalcTRS, ref Posture, float, bool) RVA 0x4AFA5D0.
        public static void SetTRS(ref CalcTRS result, Posture posture, float weight, bool validScale)
        {
            result.CurWeight = weight;
            result.Position = posture.Position * weight;
            result.Rotation = posture.Rotation * weight;
            if (validScale) result.Scale = posture.Scale * weight;
        }

        // WeightCalcJob.CalcTRS(ref CalcTRS, ref Posture, float, bool) RVA 0x4AFA020 (named CalcTRS_ here: C# does not
        // allow a method named like the CalcTRS struct imported above).
        public static void CalcTRS_(ref CalcTRS result, Posture posture, float weight, bool validScale)
        {
            result.CurWeight = weight + result.CurWeight;
            result.Position = posture.Position * weight + result.Position;
            result.Rotation = posture.Rotation * weight + result.Rotation;
            if (validScale) result.Scale = posture.Scale * weight + result.Scale;
        }

        // WeightCalcJob.AddTRS(ref CalcTRS, ref Posture, ref Posture, float, bool) RVA 0x4AF92A0:
        // acc = a + weight * b (CurWeight = weight + 1).
        public static void AddTRS(ref CalcTRS acc, Posture a, Posture b, float weight, bool validScale)
        {
            acc.CurWeight = weight + 1f;
            acc.Position = b.Position * weight + a.Position;
            acc.Rotation = b.Rotation * weight + a.Rotation;
            if (validScale) acc.Scale = b.Scale * weight + a.Scale;
        }

        // WeightCalcJob.BlendTRS(ref CalcTRS, ref CalcTRS, bool) RVA 0x4AF96C0:
        // dst is moved towards src by clamp01(src.CurWeight - 1); CurWeight becomes the mean of the two surpluses + 1.
        public static void BlendTRS(ref CalcTRS dst, CalcTRS src, bool validScale)
        {
            float w = src.CurWeight - 1f;
            dst.CurWeight = ((dst.CurWeight - 1f) + w) * 0.5f + 1f;
            float t = Mathf.Clamp01(w);
            dst.Position = (src.Position - dst.Position) * t + dst.Position;
            dst.Rotation = (src.Rotation - dst.Rotation) * t + dst.Rotation;
            if (validScale) dst.Scale = (src.Scale - dst.Scale) * t + dst.Scale;
        }

        // WeightCalcJob.SetTRSAdditive(ref CalcTRS, ref BonePosture, ref BonePosture) RVA 0x4AFA2F0:
        // adds the animation delta (current - previous bone posture) on top of the blended result.
        public static void SetTRSAdditive(ref CalcTRS result, BonePosture current, BonePosture prev)
        {
            result.Position += current.Position - prev.Position;
            Quaternion delta = Quaternion.Inverse(prev.Rotation) * current.Rotation;
            result.MayaRotation = delta * result.MayaRotation;
            result.Scale += current.Scale - prev.Scale;
        }
    }
}
