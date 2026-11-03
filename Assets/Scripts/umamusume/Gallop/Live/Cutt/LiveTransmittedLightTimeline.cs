using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Evaluates the TransmittedLight track into the parameters read by TransmittedLightPass.
    public static class LiveTransmittedLightTimeline
    {
        public struct State
        {
            public bool IsEnable;
            public int Iterations;
            public float Intensity, Threshold, BlurSpread;
            public int BlendMode;
        }

        // Current frame's value for the presentation cameras (written by LiveTimelineControl).
        public static State Current;

        public static State Evaluate(ILiveTimelineKeyDataList keys, float frame, TimelinePlayerMode playMode)
        {
            var state = new State();
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || frame < keys.At(0).frame) return state;
            LiveTimelineControl.FindTimelineKey(out var currentKey, out var nextKey, keys, frame);
            var a = currentKey as LiveTimelineKeyTransmittedLightData;
            if (a == null || !a.IsEnable) return state;
            var b = nextKey as LiveTimelineKeyTransmittedLightData;
            float t = b != null && b != a && b.IsInterpolateKey()
                ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0f;
            if (b == null) b = a;
            state.IsEnable = true;
            state.Iterations = Mathf.Clamp(a.Iterations, 1, 4);
            state.Intensity = Mathf.LerpUnclamped(a.Intensity, b.Intensity, t);
            state.Threshold = Mathf.LerpUnclamped(a.Threshold, b.Threshold, t);
            state.BlurSpread = Mathf.LerpUnclamped(a.BlurSpread, b.BlurSpread, t);
            state.BlendMode = a.BlendMode;
            return state;
        }
    }
}
