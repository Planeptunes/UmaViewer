using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// Timeline adapter for the Vortex effect. Port of the JP LiveTimelineControl.AlterUpdate_Vortex
    /// (0x7ffd592b8a30): IsEnable comes from the current key; Area, RotVolume and DepthClip are only
    /// interpolated when the NEXT key IsInterpolateKey, with the shared CalculateInterpolationValue ratio.
    /// Native clamps the ratio to [0, 1] for Area and does NOT clamp it for RotVolume and DepthClip.
    /// The list is gated by the Disable attribute and by the player mode.
    /// </summary>
    public static class LiveVortexTimeline
    {
        public static VortexPass.Parameter Evaluate(LiveTimelineKeyVortexDataList keys,
            float frame, TimelinePlayerMode playMode)
        {
            var p = new VortexPass.Parameter();
            if (keys == null || keys.Count == 0 ||
                keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || frame < keys.thisList[0].frame)
                return p;

            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyVortexData;
            if (a == null) return p;

            p.IsEnable = a.IsEnable;
            var b = next as LiveTimelineKeyVortexData;
            if (b == null || !b.IsInterpolateKey())
            {
                p.Area = a.Area;
                p.RotVolume = a.RotVolume;
                p.DepthClip = a.DepthClip;
                return p;
            }

            float t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
            float tc = Mathf.Clamp01(t);
            p.Area = new Vector4(
                Mathf.LerpUnclamped(a.Area.x, b.Area.x, tc), Mathf.LerpUnclamped(a.Area.y, b.Area.y, tc),
                Mathf.LerpUnclamped(a.Area.z, b.Area.z, tc), Mathf.LerpUnclamped(a.Area.w, b.Area.w, tc));
            p.RotVolume = Mathf.LerpUnclamped(a.RotVolume, b.RotVolume, t);
            p.DepthClip = Mathf.LerpUnclamped(a.DepthClip, b.DepthClip, t);
            return p;
        }
    }
}
