using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// Port of the JP AlterUpdate_Hatching / SetupHatchingUpdateInfo (0x7ffd592a4030 / 0x7ffd592c7140).
    /// BlendMode and NoiseTexture come from the current key; the floats are interpolated (unclamped ratio) only
    /// when the NEXT key IsInterpolateKey. Native quirk kept on purpose: the interpolated CircleMaskAngle starts
    /// from the current key's CircleMaskSize (offset 0x58) instead of its CircleMaskAngle (0x64).
    /// </summary>
    public static class LiveHatchingTimeline
    {
        public static HatchingPass.Parameter Evaluate(LiveTimelineKeyHatchingDataList keys,
            float frame, TimelinePlayerMode playMode)
        {
            var p = new HatchingPass.Parameter();
            if (keys == null || keys.Count == 0 ||
                keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || frame < keys.thisList[0].frame)
                return p;

            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyHatchingData;
            if (a == null) return p;

            p.BlendMode = a.BlendMode;
            p.NoiseTexture = a.NoiseTexture;
            p.BlendAlpha = a.BlendAlpha;
            p.StrokeTiling = a.StrokeTiling; p.StrokeHeight = a.StrokeHeight;
            p.StrokeAngle = a.StrokeAngle; p.StrokeLength = a.StrokeLength;
            p.CircleMaskOffset = a.CircleMaskOffset; p.CircleMaskSize = a.CircleMaskSize;
            p.CircleMaskStretchRatio = a.CircleMaskStretchRatio;
            p.CircleMaskAngle = a.CircleMaskAngle;
            p.CircleMaskInnerRatio = a.CircleMaskInnerRatio; p.CircleMaskInnerEffect = a.CircleMaskInnerEffect;

            var b = next as LiveTimelineKeyHatchingData;
            if (b != null && b.IsInterpolateKey())
            {
                float t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
                p.BlendAlpha = Mathf.LerpUnclamped(a.BlendAlpha, b.BlendAlpha, t);
                p.StrokeTiling = Mathf.LerpUnclamped(a.StrokeTiling, b.StrokeTiling, t);
                p.StrokeHeight = Mathf.LerpUnclamped(a.StrokeHeight, b.StrokeHeight, t);
                p.StrokeAngle = Mathf.LerpUnclamped(a.StrokeAngle, b.StrokeAngle, t);
                p.StrokeLength = Mathf.LerpUnclamped(a.StrokeLength, b.StrokeLength, t);
                p.CircleMaskOffset = new Vector2(
                    Mathf.LerpUnclamped(a.CircleMaskOffset.x, b.CircleMaskOffset.x, t),
                    Mathf.LerpUnclamped(a.CircleMaskOffset.y, b.CircleMaskOffset.y, t));
                p.CircleMaskSize = Mathf.LerpUnclamped(a.CircleMaskSize, b.CircleMaskSize, t);
                p.CircleMaskStretchRatio = new Vector2(
                    Mathf.LerpUnclamped(a.CircleMaskStretchRatio.x, b.CircleMaskStretchRatio.x, t),
                    Mathf.LerpUnclamped(a.CircleMaskStretchRatio.y, b.CircleMaskStretchRatio.y, t));
                // Native quirk: (next.angle - cur.size) * t + cur.size.
                p.CircleMaskAngle = (b.CircleMaskAngle - a.CircleMaskSize) * t + a.CircleMaskSize;
                p.CircleMaskInnerRatio = Mathf.LerpUnclamped(a.CircleMaskInnerRatio, b.CircleMaskInnerRatio, t);
                p.CircleMaskInnerEffect = Mathf.LerpUnclamped(a.CircleMaskInnerEffect, b.CircleMaskInnerEffect, t);
            }
            return p;
        }
    }
}
