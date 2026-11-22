using UnityEngine;

namespace Gallop
{
    // Port of EarWeightGroup : WeightGroupBase (dump_jp, TypeDefIndex 33580417) restricted to what the Live facial ear
    // path needs: the current / target / previous weight arrays and the per-side duration / current time used to ramp
    // an ear pose. The game stores them in NativeArrays (CommonWeightArray indexed through WeightArray /
    // TargetWeightArray / PrevWeightArray, DurationTimeArray, CurrentTimeArray); here each one is a plain array
    // indexed [EarType, side * WEIGHT_SIZE + segment], side 0 = right, 1 = left (LiveFaceController.SetEar passes
    // _earTypeL with side 1 and _earTypeR with side 0).
    //
    // The DrivenKeyComponent methods that drive this group for the ears are ported here too (DrivenKeyComponent is
    // otherwise only an enum holder in the viewer):
    //   DrivenKeyComponent.SetEar(EarType, int side, float weight, float duration, bool, bool, bool) RVA 0x4AF1120
    //   DrivenKeyComponent.BlendProgrammable(float deltaTime) ear section                             RVA 0x4AE9C90
    //   DrivenKeyComponent.GetInterpolateRate                                                        RVA 0x4AEBC60
    //   DrivenKeyComponent.SetInterpolateType (ear slot +0x248)                                      RVA 0x4AF2450
    //
    // The EarWeightJob / WeightCalcJob TRS blend that turns these weights into bone transforms is ported in
    // EarWeightJob.cs / WeightCalcJob.cs and driven by EarWeightJobRunner. The blend-face list bookkeeping
    // (AddBlendEar / ClearEarBlendFaces / GetEarTypes) is not ported: BlendProgrammable only changes types whose
    // previous and target weights differ, which is the same set the list holds on this path, and the runner rebuilds the
    // job's face list from the weights.
    //
    // WeightGroupBase.Initialize (RVA 0x4AFA8B0) sets every segment of EarType.Base to 1.0 in the current and previous
    // weights (the Base face is the reference pose the job accumulates the other faces onto). The target weights of Base
    // are not set by the game; they start at 1.0 here as well so that BlendProgrammable, which scans every type instead of
    // the game's face list, leaves Base alone (documented adaptation).
    public class EarWeightGroup
    {
        public const int EAR_TYPE_NUM = 17;   // Gallop.EarType Base..Roll
        public const int WEIGHT_SIZE = 3;     // EarWeightGroup.WEIGHT_SIZE: three weights per side (WEIGHT_0..2)
        public const int SIDE_NUM = 2;

        public readonly float[,] WeightArray = new float[EAR_TYPE_NUM, SIDE_NUM * WEIGHT_SIZE];
        public readonly float[,] TargetWeightArray = new float[EAR_TYPE_NUM, SIDE_NUM * WEIGHT_SIZE];
        public readonly float[,] PrevWeightArray = new float[EAR_TYPE_NUM, SIDE_NUM * WEIGHT_SIZE];
        public readonly float[] DurationTimeArray = new float[SIDE_NUM * WEIGHT_SIZE];
        public readonly float[] CurrentTimeArray = new float[SIDE_NUM * WEIGHT_SIZE];
        public bool IsCalcWeight; // WeightGroupBase +0x80

        // DrivenKeyComponent +0x248: interpolate type of the ear group (SetInterpolateType cases 0/1/7).
        public DrivenKeyComponent.InterpolateType EarInterpolateType;

        // DrivenKeyComponent +0x130: blend-face list of the ear group, assigned at the end of SetEar. While it is null
        // BlendProgrammable does not ramp the ears.
        private bool _hasBlendList;

        public EarWeightGroup()
        {
            InitBaseWeights();
        }

        // WeightGroupBase.Initialize RVA 0x4AFA8B0 (see the class comment).
        private void InitBaseWeights()
        {
            for (int seg = 0; seg < SIDE_NUM * WEIGHT_SIZE; seg++)
            {
                WeightArray[0, seg] = 1f;
                PrevWeightArray[0, seg] = 1f;
                TargetWeightArray[0, seg] = 1f;
            }
        }

        // EarWeightGroup.SetPrevWeight(int, bool) RVA 0x4AF73B0.
        // Note: the game reads CurrentTimeArray/DurationTimeArray at [side] here (not [side * 3]); kept as is.
        public void SetPrevWeight(int side, bool force)
        {
            bool finished = CurrentTimeArray[side] == 0f || DurationTimeArray[side] <= CurrentTimeArray[side];
            if (!force && !finished) return;
            int offset = side * WEIGHT_SIZE;
            for (int type = 0; type < EAR_TYPE_NUM; type++)
            {
                for (int seg = 0; seg < WEIGHT_SIZE; seg++)
                {
                    PrevWeightArray[type, offset + seg] = WeightArray[type, offset + seg];
                }
            }
        }

        // EarWeightGroup.ClearTargetWeight(int, ref NativeArray<WeightGroup>) RVA 0x4AF7130.
        // Clears EarType 1..16 of that side (the loop starts at element 1: EarType.Base is never cleared).
        public static void ClearTargetWeight(float[,] array, int side)
        {
            int offset = side * WEIGHT_SIZE;
            for (int type = 1; type < EAR_TYPE_NUM; type++)
            {
                for (int seg = 0; seg < WEIGHT_SIZE; seg++)
                {
                    array[type, offset + seg] = 0f;
                }
            }
        }

        // DrivenKeyComponent.SetEar(EarType, int side, float weight, float duration, bool isKeepTarget,
        // bool isKeepBlend, bool isForcePrev) RVA 0x4AF1120. LiveFaceController.SetEar calls it with (false, false, true).
        public void DrivenKeyComponent_SetEar(EarType type, int side, float weight, float duration,
            bool isKeepTarget, bool isKeepBlend, bool isForcePrev)
        {
            if ((uint)side > 1) return;

            float[,] array = WeightArray;
            if (0f < duration)
            {
                array = TargetWeightArray;
                SetPrevWeight(side, isForcePrev);
                DurationTimeArray[side * WEIGHT_SIZE] = duration;
                CurrentTimeArray[side * WEIGHT_SIZE] = 0f;
            }

            // (IsCalcWeight && !isKeepBlend) -> ClearEarBlendFaces: blend list bookkeeping only (see class comment).
            IsCalcWeight = false;
            if (!isKeepTarget)
            {
                ClearTargetWeight(array, side);
            }

            int offset = side * WEIGHT_SIZE;
            for (int seg = 0; seg < WEIGHT_SIZE; seg++)
            {
                array[(int)type, offset + seg] = weight;
            }

            _hasBlendList = true;
        }

        // DrivenKeyComponent.BlendProgrammable(float) RVA 0x4AE9C90, ear section (the three face groups are handled by
        // the overload at 0x4AEA160 and are not ported here).
        public void BlendProgrammable(float deltaTime)
        {
            if (_hasBlendList)
            {
                for (int side = 0; side < SIDE_NUM; side++)
                {
                    int timeIndex = side * WEIGHT_SIZE;
                    float duration = DurationTimeArray[timeIndex];
                    float current = CurrentTimeArray[timeIndex];
                    if (!(current < duration)) continue;

                    CurrentTimeArray[timeIndex] = deltaTime + current;
                    float rate = GetInterpolateRate(EarInterpolateType, deltaTime + current, duration);
                    float clamped = Mathf.Clamp01(rate);

                    int offset = side * WEIGHT_SIZE;
                    for (int type = 0; type < EAR_TYPE_NUM; type++)
                    {
                        for (int seg = 0; seg < WEIGHT_SIZE; seg++)
                        {
                            float prev = PrevWeightArray[type, offset + seg];
                            float target = TargetWeightArray[type, offset + seg];
                            if (prev != target)
                            {
                                WeightArray[type, offset + seg] = (target - prev) * clamped + prev;
                            }
                        }
                    }
                }
            }
            IsCalcWeight = true;
        }

        // DrivenKeyComponent.GetInterpolateRate(InterpolateType, float time, float duration) RVA 0x4AEBC60.
        // Constants read from GameAssembly_jp_dumped.dll: DAT_7ffd5a651df0 = 1.5707964 (PI/2),
        // DAT_7ffd5a640d6c = 1.0, DAT_7ffd5a640d60 = 0.5. FUN_7ffd547dd3a0 = cos, FUN_7ffd547ddc90 = sin (the only
        // assignment that gives rate(0) = 0 for EaseIn and EaseOut).
        public static float GetInterpolateRate(DrivenKeyComponent.InterpolateType type, float time, float duration)
        {
            float rate = time / duration;
            if (rate < 0f) rate = 0f;
            else if (1f < rate) rate = 1f;

            const float HALF_PI = 1.5707964f;
            switch ((int)type)
            {
                case 1:
                    return 1f - Mathf.Cos(rate * HALF_PI);
                case 2:
                    return Mathf.Sin(rate * HALF_PI);
                case 3:
                    rate = rate + rate;
                    if (rate < 1f)
                    {
                        return (1f - Mathf.Cos(rate * HALF_PI)) * 0.5f;
                    }
                    return Mathf.Sin((rate - 1f) * HALF_PI) * 0.5f + 0.5f;
                default:
                    return rate;
            }
        }

        // Viewer-only: back to the state of a freshly created group (all weights 0, no ramp running). Used when the
        // viewer seeks the live backwards, which the game never does.
        public void Reset()
        {
            System.Array.Clear(WeightArray, 0, WeightArray.Length);
            System.Array.Clear(TargetWeightArray, 0, TargetWeightArray.Length);
            System.Array.Clear(PrevWeightArray, 0, PrevWeightArray.Length);
            System.Array.Clear(DurationTimeArray, 0, DurationTimeArray.Length);
            System.Array.Clear(CurrentTimeArray, 0, CurrentTimeArray.Length);
            InitBaseWeights();
            IsCalcWeight = false;
            EarInterpolateType = DrivenKeyComponent.InterpolateType.Linear;
            _hasBlendList = false;
        }
    }
}
