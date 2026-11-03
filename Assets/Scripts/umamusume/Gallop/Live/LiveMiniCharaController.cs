using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Gallop.Live
{
    // Mini characters of a live (MiniCharaTransform / MiniCharaMotion tracks, plus the
    // ParentConstraint tracks with _objectType 1). Each mini index is a chibi copy of the live
    // character in the same position, e.g. the plush that is handed over in son1156.
    //
    // Open points (not recovered from the game): the mini's dress (the summer uniform is used,
    // as seen in game captures), the facial/ear DrivenKeyAnimation keys, the colour track and
    // the layer / 2D layout flags.
    internal sealed class LiveMiniCharaController : IDisposable
    {
        private const string MiniCostumeId = "0002_00_00";

        private sealed class Mini
        {
            public int Index;
            public UmaContainerCharacter Container;
            public Renderer[] Renderers;
            public bool[] RendererEnabled;
            public Animator Animator;
            public PlayableGraph Graph;
            public AnimationClipPlayable ClipPlayable;
            public AnimationClip PlayingClip;
            public bool Visible = true;
        }

        private readonly Transform owner;
        private readonly IList<UmaContainerCharacter> characters;
        private readonly LiveParentConstraint.NodeResolver resolveNode;
        private readonly int musicId;
        private readonly List<Mini> minis = new List<Mini>();
        private readonly Dictionary<string, AnimationClip> fallbackClips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        private bool disposed;

        public LiveMiniCharaController(Transform owner, IList<LiveTimelineWorkSheet> sheets,
            IList<UmaContainerCharacter> characters, LiveParentConstraint.NodeResolver resolveNode, int musicId)
        {
            this.owner = owner;
            this.characters = characters;
            this.resolveNode = resolveNode;
            this.musicId = musicId;

            var indices = new SortedSet<int>();
            if (sheets != null)
                foreach (var sheet in sheets)
                {
                    if (sheet?.MiniCharaTransformDataList != null)
                        foreach (var group in sheet.MiniCharaTransformDataList)
                            if (group != null && group.keys != null && group.keys.Count > 0) indices.Add(group._index);
                    if (sheet?.MiniCharaMotionDataList != null)
                        foreach (var group in sheet.MiniCharaMotionDataList)
                            if (group != null && group.keys != null && group.keys.Count > 0) indices.Add(group._index);
                }
            foreach (int index in indices) CreateMini(index);
        }

        public bool HasMinis => minis.Count > 0;

        private void CreateMini(int index)
        {
            if (characters == null || index < 0 || index >= characters.Count || characters[index] == null)
            {
                Warn("Mini character " + index + " has no live character in that position.");
                return;
            }
            var source = characters[index];
            if (source.CharaEntry == null || source.CharaEntry.IsMob)
            {
                Warn("Mini character " + index + ": mob characters have no mini model.");
                return;
            }
            var builder = UmaViewerBuilder.Instance;
            var container = builder != null ? builder.LoadLiveMiniUma(source.CharaEntry, MiniCostumeId, owner) : null;
            if (container == null)
            {
                Warn("Mini character " + index + ": mini model could not be loaded.");
                return;
            }
            var mini = new Mini
            {
                Index = index,
                Container = container,
                Animator = container.UmaAnimator != null ? container.UmaAnimator : container.GetComponentInChildren<Animator>(true),
                Renderers = container.GetComponentsInChildren<Renderer>(true)
            };
            if (mini.Animator != null)
            {
                mini.Animator.runtimeAnimatorController = null;
                mini.Animator.applyRootMotion = false;
            }
            container.isAnimatorControl = false;
            mini.RendererEnabled = new bool[mini.Renderers.Length];
            for (int i = 0; i < mini.Renderers.Length; i++)
                mini.RendererEnabled[i] = mini.Renderers[i] != null && mini.Renderers[i].enabled;
            SetVisible(mini, false);
            minis.Add(mini);
        }

        public void Evaluate(IList<LiveTimelineWorkSheet> sheets, float frame, TimelinePlayerMode mode)
        {
            if (disposed || minis.Count == 0) return;
            foreach (var mini in minis)
            {
                if (mini.Container == null) continue;
                var transformKey = FindTransformKey(sheets, mini.Index, frame, mode, out var nextKey, out float t);
                bool visible = transformKey != null && transformKey.Visible;
                SetVisible(mini, visible);
                if (!visible) continue;

                ApplyTransform(mini, transformKey, nextKey, t);
                ApplyMotion(mini, FindMotionKey(sheets, mini.Index, frame, mode), frame);
                ApplyColor(mini, sheets, frame, mode);
                ApplyConstraints(mini, transformKey, nextKey, t, sheets, frame, mode);
            }
        }

        private void ApplyTransform(Mini mini, LiveTimelineKeyMiniCharaTransformData a,
            LiveTimelineKeyMiniCharaTransformData b, float t)
        {
            var root = mini.Container.transform;
            Vector3 position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            Quaternion rotation = Quaternion.Euler(Vector3.LerpUnclamped(a.Rotate, b.Rotate, t));
            float scale = Mathf.LerpUnclamped(a.BodyScale, b.BodyScale, t);
            if (scale <= 0f) scale = 1f;

            Transform parent = owner;
            if (a.IsAttachChara)
            {
                var node = resolveNode(a.AttachIndex, a.AttachNodeName);
                if (node != null) parent = node;
                else Warn("Mini character attach node missing: " + a.AttachIndex + "/" + a.AttachNodeName);
            }
            if (parent == owner)
            {
                if (root.parent != owner) root.SetParent(owner, false);
                root.localPosition = position;
                root.localRotation = rotation;
            }
            else
            {
                if (root.parent != owner) root.SetParent(owner, false);
                root.SetPositionAndRotation(parent.TransformPoint(position), parent.rotation * rotation);
            }
            if (a.ShouldInheritParentScale && parent != owner)
                Warn("Mini character ShouldInheritParentScale is not supported; BodyScale only.");
            root.localScale = Vector3.one * scale;
            if (a.IsLayout2d) Warn("Mini character 2D layout is not supported.");
        }

        private void ApplyConstraints(Mini mini, LiveTimelineKeyMiniCharaTransformData a,
            LiveTimelineKeyMiniCharaTransformData b, float t, IList<LiveTimelineWorkSheet> sheets,
            float frame, TimelinePlayerMode mode)
        {
            Vector3 position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            Quaternion rotation = Quaternion.Euler(Vector3.LerpUnclamped(a.Rotate, b.Rotate, t));
            foreach (var group in LiveParentConstraint.Groups(sheets, LiveTimelineParentConstraintData.ObjectTypeMiniChara, frame, mode))
            {
                if (group._charaIndex != mini.Index) continue;
                var result = LiveParentConstraint.Evaluate(group, frame, position, rotation, resolveNode, Warn);
                LiveParentConstraint.Apply(mini.Container.transform, result);
            }
        }

        private void ApplyMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float frame)
        {
            if (key == null || mini.Animator == null) return;
            var clip = key.BodyAnimationClip != null ? key.BodyAnimationClip : FallbackBodyClip(mini.Index);
            if (clip == null)
            {
                Warn("Mini character body clip could not be resolved.");
                return;
            }
            if (key.FacialDrivenKeyAnimation != null || key.EarDrivenKeyAnimation != null)
                Warn("Mini character facial/ear DrivenKeyAnimation keys are not supported.");
            if (mini.PlayingClip != clip)
            {
                if (mini.Graph.IsValid()) mini.Graph.Destroy();
                mini.PlayingClip = clip;
                mini.Graph = PlayableGraph.Create("LiveMiniCharaAnimation");
                mini.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                mini.ClipPlayable = AnimationClipPlayable.Create(mini.Graph, clip);
                mini.ClipPlayable.SetApplyFootIK(false);
                mini.ClipPlayable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(mini.Graph, "MiniChara", mini.Animator);
                output.SetSourcePlayable(mini.ClipPlayable);
                mini.Graph.Play();
            }
            float speed = key.BodyPlaySpeed > 0f ? key.BodyPlaySpeed : 1f;
            float time = Mathf.Max(0f, (frame - key.frame + key.BodyHeadFrame) / 60f * speed);
            time = key.Loop && clip.length > 0f ? Mathf.Repeat(time, clip.length) : Mathf.Min(time, clip.length);
            mini.ClipPlayable.SetTime(time);
            mini.Graph.Evaluate(0f);
        }

        // MiniModelController (game): SetOutlineWidthForPower(p) -> _OutlineWidth = p * 0.325;
        // SetOutlineColor(c, blend) encodes the blend mode in the alpha:
        // Blend (1 - a) * 0.5, Multiply a * 0.5 + 0.5, Max 0.5. The mini material default
        // (_OutlineWidth 2) is the out-of-live value and draws a far too thick silhouette.
        private const float DefaultOutlineWidth = 0.325f;
        private static readonly int CharaColorId = Shader.PropertyToID("_CharaColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private MaterialPropertyBlock colorBlock;

        private void ApplyColor(Mini mini, IList<LiveTimelineWorkSheet> sheets, float frame, TimelinePlayerMode mode)
        {
            LiveTimelineKeyMiniCharaColorData a = null, b = null;
            float t = 0f;
            if (sheets != null)
                foreach (var sheet in sheets)
                {
                    if (a != null || sheet == null || !sheet.enableAtRuntime || sheet.MiniCharaColorDataList == null) continue;
                    foreach (var group in sheet.MiniCharaColorDataList)
                    {
                        if (group == null || !Usable(group.keys, frame, mode)) continue;
                        LiveTimelineControl.FindTimelineKey(out var current, out var next, group.keys, frame);
                        var key = current as LiveTimelineKeyMiniCharaColorData;
                        if (key == null || (key.TargetFlags != 0 && (key.TargetFlags & (1 << mini.Index)) == 0)) continue;
                        a = key;
                        b = next as LiveTimelineKeyMiniCharaColorData;
                        if (b != null && b != a && b.IsInterpolateKey())
                            t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
                        else b = a;
                        break;
                    }
                }

            float widthPower = a != null ? Mathf.LerpUnclamped(a.OutlineWidthPower, b.OutlineWidthPower, t) : 1f;
            if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
            foreach (var renderer in mini.Renderers)
            {
                if (renderer == null || renderer.sharedMaterial == null) continue;
                var material = renderer.sharedMaterial;
                renderer.GetPropertyBlock(colorBlock);
                if (material.HasProperty(OutlineWidthId))
                    colorBlock.SetFloat(OutlineWidthId, DefaultOutlineWidth * widthPower);
                if (a != null)
                {
                    if (material.HasProperty(CharaColorId))
                        colorBlock.SetColor(CharaColorId, Color.LerpUnclamped(a.Color, b.Color, t)
                            * Mathf.LerpUnclamped(a.ColorPower, b.ColorPower, t));
                    if (material.HasProperty(OutlineColorId))
                    {
                        var outline = Color.LerpUnclamped(a.OutlineColor, b.OutlineColor, t);
                        outline.a = a.OutlineColorBlend == 0 ? (1f - outline.a) * 0.5f
                            : a.OutlineColorBlend == 1 ? outline.a * 0.5f + 0.5f : 0.5f;
                        colorBlock.SetColor(OutlineColorId, outline);
                    }
                }
                renderer.SetPropertyBlock(colorBlock);
            }
        }

        // The cutt references the clip from another bundle; when that reference is not resolved,
        // the live's own mini body clip (anm_liv_son####_*_mini) is used.
        private AnimationClip FallbackBodyClip(int index)
        {
            string prefix = "3d/motion/live/body/son" + musicId + "/";
            var names = new List<string>();
            foreach (var entry in UmaViewerMain.Instance.AbList)
                if (entry.Key.StartsWith(prefix, StringComparison.Ordinal) && entry.Key.EndsWith("_mini", StringComparison.Ordinal))
                    names.Add(entry.Key);
            if (names.Count == 0) return null;
            names.Sort(StringComparer.Ordinal);
            string name = names[Mathf.Clamp(index, 0, names.Count - 1)];
            if (names.Count > 1) Warn("Several mini body clips in the live; picked " + name + " by mini index.");
            if (fallbackClips.TryGetValue(name, out var clip)) return clip;
            clip = UmaViewerMain.Instance.AbList[name].Get<AnimationClip>();
            fallbackClips[name] = clip;
            return clip;
        }

        private static LiveTimelineKeyMiniCharaTransformData FindTransformKey(IList<LiveTimelineWorkSheet> sheets,
            int index, float frame, TimelinePlayerMode mode, out LiveTimelineKeyMiniCharaTransformData next, out float t)
        {
            next = null;
            t = 0f;
            if (sheets == null) return null;
            foreach (var sheet in sheets)
            {
                if (sheet == null || !sheet.enableAtRuntime || sheet.MiniCharaTransformDataList == null) continue;
                foreach (var group in sheet.MiniCharaTransformDataList)
                {
                    if (group == null || group._index != index || !Usable(group.keys, frame, mode)) continue;
                    LiveTimelineControl.FindTimelineKey(out var current, out var nextKey, group.keys, frame);
                    var a = current as LiveTimelineKeyMiniCharaTransformData;
                    if (a == null) continue;
                    var b = nextKey as LiveTimelineKeyMiniCharaTransformData;
                    if (b != null && b != a && b.IsInterpolateKey())
                        t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
                    else b = a;
                    next = b;
                    return a;
                }
            }
            return null;
        }

        private static LiveTimelineKeyMiniCharaMotionData FindMotionKey(IList<LiveTimelineWorkSheet> sheets,
            int index, float frame, TimelinePlayerMode mode)
        {
            if (sheets == null) return null;
            foreach (var sheet in sheets)
            {
                if (sheet == null || !sheet.enableAtRuntime || sheet.MiniCharaMotionDataList == null) continue;
                foreach (var group in sheet.MiniCharaMotionDataList)
                {
                    if (group == null || group._index != index || !Usable(group.keys, frame, mode)) continue;
                    LiveTimelineControl.FindTimelineKey(out var current, out _, group.keys, frame);
                    if (current is LiveTimelineKeyMiniCharaMotionData key) return key;
                }
            }
            return null;
        }

        private static bool Usable(ILiveTimelineKeyDataList keys, float frame, TimelinePlayerMode mode)
        {
            return keys != null && keys.Count > 0 && !keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)
                && keys.EnablePlayModeTimeline(mode) && keys[0] != null && frame >= keys[0].frame;
        }

        private static void SetVisible(Mini mini, bool visible)
        {
            if (mini.Visible == visible) return;
            mini.Visible = visible;
            for (int i = 0; i < mini.Renderers.Length; i++)
                if (mini.Renderers[i] != null) mini.Renderers[i].enabled = visible && mini.RendererEnabled[i];
        }

        private void Warn(string message)
        {
            if (warnings.Add(message)) Debug.LogWarning("[LiveMiniCharaController] " + message);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var mini in minis)
            {
                if (mini.Graph.IsValid()) mini.Graph.Destroy();
                if (mini.Container != null) Object.Destroy(mini.Container.gameObject);
            }
            minis.Clear();
            fallbackClips.Clear();
            warnings.Clear();
        }
    }
}
