using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// Port of the JP LiveTimelineControl.AlterUpdate_LensDistortion (0x7ffd592a6220): current key values, and a
    /// linear interpolation (unclamped ratio from CalculateInterpolationValue) towards the next key only when the
    /// NEXT key IsInterpolateKey. The list is gated by the Disable attribute and the player mode.
    /// </summary>
    public static class LiveLensDistortionTimeline
    {
        public static LensDistortionPass.Parameter Evaluate(LiveTimelineKeyLensDistortionDataList keys,
            float frame, TimelinePlayerMode playMode)
        {
            var p = new LensDistortionPass.Parameter { IntensityX = 1f, IntensityY = 1f, Scale = 1f };
            if (keys == null || keys.Count == 0 ||
                keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || frame < keys.thisList[0].frame)
                return p;

            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyLensDistortionData;
            if (a == null) return p;

            p.Intensity = a.Intensity; p.IntensityX = a.IntensityX; p.IntensityY = a.IntensityY;
            p.CenterX = a.CenterX; p.CenterY = a.CenterY; p.Scale = a.Scale;

            var b = next as LiveTimelineKeyLensDistortionData;
            if (b != null && b.IsInterpolateKey())
            {
                float t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
                p.Intensity = Mathf.LerpUnclamped(a.Intensity, b.Intensity, t);
                p.IntensityX = Mathf.LerpUnclamped(a.IntensityX, b.IntensityX, t);
                p.IntensityY = Mathf.LerpUnclamped(a.IntensityY, b.IntensityY, t);
                p.CenterX = Mathf.LerpUnclamped(a.CenterX, b.CenterX, t);
                p.CenterY = Mathf.LerpUnclamped(a.CenterY, b.CenterY, t);
                p.Scale = Mathf.LerpUnclamped(a.Scale, b.Scale, t);
            }
            return p;
        }
    }
}
