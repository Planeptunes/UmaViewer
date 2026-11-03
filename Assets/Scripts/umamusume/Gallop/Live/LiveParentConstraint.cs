using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // ParentConstraint track: moves a prop or a mini character onto weighted character nodes,
    // like a Unity ParentConstraint with zero source offsets. The object's own local offset
    // (attach key offset / mini transform key) is kept relative to each source node, the
    // sources are averaged by weight, and the key weight blends between the unconstrained
    // pose and the constrained one. Interpolation runs towards the next key when that key is
    // an interpolating key (same convention as the other tracks).
    internal static class LiveParentConstraint
    {
        public const int SourceTypeCharacterNode = 0;

        // Resolves (character position, node name) to a transform; null when missing.
        public delegate Transform NodeResolver(int characterIndex, string nodeName);

        public struct Result
        {
            public bool Active;
            public float Weight;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        public static IEnumerable<LiveTimelineParentConstraintData> Groups(IList<LiveTimelineWorkSheet> sheets,
            int objectType, float frame, TimelinePlayerMode mode)
        {
            if (sheets == null) yield break;
            for (int s = 0; s < sheets.Count; s++)
            {
                var sheet = sheets[s];
                if (sheet == null || !sheet.enableAtRuntime || sheet.ParentConstraintDataList == null) continue;
                foreach (var group in sheet.ParentConstraintDataList)
                {
                    if (group == null || group._objectType != objectType) continue;
                    var keys = group.keys;
                    if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)
                        || !keys.EnablePlayModeTimeline(mode) || keys[0] == null || frame < keys[0].frame) continue;
                    yield return group;
                }
            }
        }

        // localPosition / localRotation: the object's offset relative to the node it follows.
        public static Result Evaluate(LiveTimelineParentConstraintData group, float frame,
            Vector3 localPosition, Quaternion localRotation, NodeResolver resolve, Action<string> warn)
        {
            var result = new Result();
            LiveTimelineControl.FindTimelineKey(out var currentKey, out var nextKey, group.keys, frame);
            var a = currentKey as LiveTimelineKeyParentConstraintData;
            if (a == null || !a._enable) return result;
            var b = nextKey as LiveTimelineKeyParentConstraintData;
            float t = b != null && b != a && b.IsInterpolateKey()
                ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0f;
            if (b == null) b = a;

            float weight = Mathf.Clamp01(Mathf.LerpUnclamped(a._weight, b._weight, t));
            if (weight <= 0f || a._sourceDataArray == null) return result;

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            float total = 0f;
            for (int i = 0; i < a._sourceDataArray.Length; i++)
            {
                var source = a._sourceDataArray[i];
                if (source == null) continue;
                var other = b._sourceDataArray != null && i < b._sourceDataArray.Length ? b._sourceDataArray[i] : source;
                float w = Mathf.Max(0f, Mathf.LerpUnclamped(source._weight, other != null ? other._weight : source._weight, t));
                if (w <= 0f) continue;
                if (source._sourceType != SourceTypeCharacterNode)
                {
                    warn?.Invoke("ParentConstraint source type " + source._sourceType + " is not supported; source ignored.");
                    continue;
                }
                var node = resolve(source._targetIndex, source._nodeName);
                if (node == null)
                {
                    warn?.Invoke("ParentConstraint source node missing: " + source._targetIndex + "/" + source._nodeName);
                    continue;
                }
                var sourcePosition = node.TransformPoint(localPosition);
                var sourceRotation = node.rotation * localRotation;
                total += w;
                position += sourcePosition * w;
                // Running weighted average of rotations (exact for two sources).
                rotation = total == w ? sourceRotation : Quaternion.Slerp(rotation, sourceRotation, w / total);
            }
            if (total <= 0f) return result;

            result.Active = true;
            result.Weight = weight;
            result.Position = position / total;
            result.Rotation = rotation;
            return result;
        }

        public static void Apply(Transform target, Result result)
        {
            if (!result.Active || target == null) return;
            target.SetPositionAndRotation(
                Vector3.LerpUnclamped(target.position, result.Position, result.Weight),
                Quaternion.Slerp(target.rotation, result.Rotation, result.Weight));
        }
    }
}
