using UnityEngine;

namespace Gallop
{
    // Data holders of the facial TRS jobs (EarWeightJob / WeightCalcJob). Ported from dump_jp.cs; the game keeps them
    // in Unity NativeArrays of blittable structs, the viewer uses plain managed arrays with the same element layout
    // and indexing. Field offsets in the comments are the dump offsets (the boxed value type header 0x10 included).

    // WeightGroup (dump_jp.cs TypeDefIndex 33580439): a [StartIndex, StartIndex + Length) window of CommonWeightArray.
    public struct WeightGroup
    {
        public int StartIndex; // 0x10
        public int Length;     // 0x14
    }

    // FacialDrivenKeyJobWork (TypeDefIndex 33580448): the driven-key postures of every face, flattened.
    public class FacialDrivenKeyJobWork
    {
        // FacialDrivenKeyJobWork.Posture (TypeDefIndex 33580449), 0x28 bytes.
        public struct Posture
        {
            public Vector3 Position;       // 0x10
            public Vector3 Scale;          // 0x1C
            public Vector3 Rotation;       // 0x28 (Maya euler)
            public bool IsOverrideTarget;  // 0x34
        }

        public int[] FacialStartIndex;   // 0x10: first PostureArray index of each face
        public Posture[] PostureArray;   // 0x20
        public int[] FacialPostureNum;   // 0x30: bones per (face, group), indexed face * groupsPerFace + group
    }

    // CalcTargetInfomation (TypeDefIndex 33580442): the TRS result / pose side of the job.
    public class CalcTargetInfomation
    {
        // CalcTargetInfomation.CalcTRS (TypeDefIndex 33580443), 0x38 bytes. In the native float view used by the jobs:
        // [0] CurWeight, [1..3] Position, [4..6] Scale, [7..9] Rotation (Maya euler), [10..13] MayaRotation.
        public struct CalcTRS
        {
            public float CurWeight;            // 0x10
            public Vector3 Position;           // 0x14
            public Vector3 Scale;              // 0x20
            public Vector3 Rotation;           // 0x2C
            public Quaternion MayaRotation;    // 0x38
        }

        // CalcTargetInfomation.BonePosture (TypeDefIndex 33580444), 0x28 bytes.
        public struct BonePosture
        {
            public Vector3 Position;    // 0x10
            public Vector3 Scale;       // 0x1C
            public Quaternion Rotation; // 0x28
        }

        // CalcTargetInfomation.JobWorkData (TypeDefIndex 33580447).
        public class JobWorkData
        {
            public int[] FaceGroupStartIndexArray;      // 0x10: first CalcTRS index of each group
            public bool[] IsValidScaleTransformArray;   // 0x20: per CalcTRS entry
        }
    }
}
