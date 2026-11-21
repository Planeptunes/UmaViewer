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
    // character in the same position, e.g. the plush that is handed over in son1156
    // (in the game the big models are chara_1013_02 and the mini is chara_1013_02_mini).
    //
    // Motion key playback follows LiveTimelineControl.AlterUpdate_MiniCharaMotion
    // @0x7ffd592a84f0 (time = elapsed * PlaySpeed + GetTimeFromFrame(HeadFrame), Loop flag):
    //   body     -> MiniAnimationController.PlayBodyMotion     @0x7ffd59598f10
    //   facial   -> MiniAnimationController.PlayFacialMotion   @0x7ffd59599090
    //   ear      -> MiniAnimationController.PlayEarMotion      @0x7ffd59598f40
    //   position -> MiniAnimationController.PlayPositionMotion @0x7ffd59599220
    //
    // Open points (not recovered from the game):
    // - The ear DrivenKeyAnimation chain is ported (DrivenKeyComponent.PlayEar / SetEarTime /
    //   UpdateEarWeight) but cannot be wired: the mini's driven-key ear locator is a
    //   DrivenKeyTarget from the head AssetHolder (MiniModelController.InitDrivenKey
    //   @0x7ffd57843a20) whose asset key is an unresolved metadata string literal, and the ear
    //   root node names live in DrivenKeyComponent.LOCATOR_CONTROL_FOR_EAR_ARRAY (a static
    //   readonly string[] whose values are only assigned at runtime). Additionally the final
    //   weight -> ear bone transform step (DrivenKeyComponent.EarWeightJob) is not ported.
    // - The colour track apply path and the LayerIndex / 2D layout flags.
    // The facial DrivenKeyAnimation keys are ported (MiniFacialController + GridTexture).
    internal sealed class LiveMiniCharaController : IDisposable
    {
        // ResourcePath.MINI_FACIAL_LOCATOR_PATH = "3d/Animator/pfb_mini_facial_locator" (global dump.cs).
        private const string MiniFacialLocatorPath = "3d/animator/pfb_mini_facial_locator";
        private const string MiniFacialLocatorName = "pfb_mini_facial_locator";
        // LiveTimelineControl.AlterUpdate_MiniCharaMotion: GetTimeFromFrame(frame, DAT_7ffd5a8b6b40 = 60f).
        private const float TimelineFps = 60f;

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
            public MiniFacialController Facial;
            // MiniAnimationController._positionPlayableAnimator (+0x40): a
            // MinimumPlayableAnimator over the Animator of the Position node
            // (MiniModelController.PositionNode +0xb0 -> its game object's Animator).
            public Animator PositionAnimator;
            public PlayableGraph PositionGraph;
            public AnimationClipPlayable PositionClipPlayable;
            public AnimationClip PositionClip;
            // MiniAnimationController._drivenKeyComponent (+0x28), ear driven-key chain.
            public DrivenKeyComponent Ear;
            public EarWeightGroup EarWeights;
            public bool Visible = true;
        }

        private readonly Transform owner;
        private readonly IList<UmaContainerCharacter> characters;
        private readonly LiveParentConstraint.NodeResolver resolveNode;
        private readonly List<Mini> minis = new List<Mini>();
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        private bool disposed;

        public LiveMiniCharaController(Transform owner, IList<LiveTimelineWorkSheet> sheets,
            IList<UmaContainerCharacter> characters, LiveParentConstraint.NodeResolver resolveNode, int musicId)
        {
            this.owner = owner;
            this.characters = characters;
            this.resolveNode = resolveNode;

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
            // The game builds the mini as the chibi of the same chara + costume as the live
            // character (live 1156: big models chara_1013_02, mini
            // chara_1013_02_mini). The costume is the one the live character was loaded with
            // (UmaContainerCharacter.VarCostumeIdLong); nothing is hardcoded.
            string costumeId = source.VarCostumeIdLong;
            if (string.IsNullOrEmpty(costumeId))
            {
                Warn("Mini character " + index + ": the costume id of the live character is unknown.");
                return;
            }
            var builder = UmaViewerBuilder.Instance;
            var container = builder != null ? builder.LoadLiveMiniUma(source.CharaEntry, costumeId, owner) : null;
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
            mini.Facial = InitFacial(container);
            mini.PositionAnimator = InitPositionAnimator(container);
            mini.Ear = new DrivenKeyComponent();
            mini.EarWeights = new EarWeightGroup();
            mini.Ear.InitializeEar(mini.EarWeights, null);
            mini.RendererEnabled = new bool[mini.Renderers.Length];
            for (int i = 0; i < mini.Renderers.Length; i++)
                mini.RendererEnabled[i] = mini.Renderers[i] != null && mini.Renderers[i].enabled;
            SetVisible(mini, false);
            minis.Add(mini);
        }

        // MiniAnimationController.OnInitialize @0x7ffd59598d00 builds
        // _positionPlayableAnimator (+0x40) as a MinimumPlayableAnimator over the Animator of the
        // Position node's game object (MiniModelController.PositionNode +0xb0 -> gameObject ->
        // GetComponent; MinimumPlayableAnimator..ctor(Animator, string)). The viewer finds the
        // node as container.Position ("Position" node of the model, the name used by
        // UmaViewerBuilder.LoadNormalUma/LoadMiniUma for the same game node).
        private static Animator InitPositionAnimator(UmaContainerCharacter container)
        {
            var positionNode = container.Position != null ? container.Position : container.transform.Find("Position");
            if (positionNode == null) return null;
            return positionNode.GetComponent<Animator>();
        }

        // MiniModelController.InitFacial @0x7ffd57843fd0: instantiates pfb_mini_facial_locator under
        // the model root (keeping the prefab name), takes its Animator and builds the
        // MiniFacialController with the eyebrow L/R, eye and mouth materials.
        // Differences: the mini animator controller + AnimatorOverrideController are not set up
        // (see MiniFacialController header); the materials are the instanced materials of the
        // M_Mayu_L / M_Mayu_R / M_Eye / M_Mouth renderers (the game takes them from its
        // CharaPartsHolder, +0x220/+0x238/+0x248/+0x278).
        private MiniFacialController InitFacial(UmaContainerCharacter container)
        {
            var main = UmaViewerMain.Instance;
            if (main == null || !main.AbList.TryGetValue(MiniFacialLocatorPath, out var entry))
            {
                Warn("Mini facial locator bundle not found: " + MiniFacialLocatorPath);
                return null;
            }
            var bundle = UmaAssetManager.LoadAssetBundle(entry);
            var prefab = bundle != null ? bundle.LoadAsset<GameObject>(MiniFacialLocatorName) : null;
            if (prefab == null)
            {
                Warn("Mini facial locator prefab could not be loaded.");
                return null;
            }
            var locator = Object.Instantiate(prefab, container.transform, false);
            locator.name = prefab.name;
            var animator = locator.GetComponent<Animator>();
            if (animator == null) Warn("Mini facial locator has no Animator.");

            Material eyebrowL = null, eyebrowR = null, eye = null, mouth = null;
            foreach (var renderer in container.GetComponentsInChildren<MeshRenderer>(true))
            {
                switch (renderer.gameObject.name)
                {
                    case "M_Mayu_L": eyebrowL = renderer.material; break;
                    case "M_Mayu_R": eyebrowR = renderer.material; break;
                    case "M_Eye": eye = renderer.material; break;
                    case "M_Mouth": mouth = renderer.material; break;
                }
            }
            var facial = new MiniFacialController();
            facial.Init(eyebrowL, eyebrowR, eye, mouth, locator.transform, animator);
            if (!facial.HasAllLocators) Warn("Mini facial locator is missing Eye/Mayu/Mouth Ctrl nodes.");
            return facial;
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
                // DrivenKeyComponent.PostJob @0x7ffd58e702a0, ear section: pose the ear layer at
                // its current play time and turn the ear nodes into EarWeightGroup weights.
                if (mini.Ear != null && mini.Ear.IsPlayingEarAnim)
                {
                    mini.Ear.EvaluateEarLayer();
                    mini.Ear.UpdateEarWeight();
                }
                // MiniFacialController.AfterApplyDrivenKey @0x7ffd59907ee0 runs after the
                // facial locator has been posed for this frame.
                mini.Facial?.AfterApplyDrivenKey();
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

        // LiveTimelineControl.AlterUpdate_MiniCharaMotion @0x7ffd592a84f0: for the current key,
        // elapsed = time - GetTimeFromFrame(key.frame); each clip is played at
        // elapsed * PlaySpeed + GetTimeFromFrame(HeadFrame) with the key's Loop flag. The game
        // calls the four MiniAnimationController.Play*Motion methods in this order (body,
        // facial, ear, position) and skips each one whose asset reference is null.
        private void ApplyMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float frame)
        {
            if (key == null) return;
            float elapsed = frame / TimelineFps - key.frame / TimelineFps;
            ApplyBodyMotion(mini, key, elapsed);
            ApplyFacialMotion(mini, key, elapsed);
            ApplyEarMotion(mini, key, elapsed);
            ApplyPositionMotion(mini, key, elapsed);
        }

        // MiniAnimationController.PlayFacialMotion @0x7ffd59599090.
        private void ApplyFacialMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float elapsed)
        {
            if (key.FacialDrivenKeyAnimation == null) return;
            var animation = key.FacialDrivenKeyAnimation as DrivenKeyAnimation;
            if (animation == null)
            {
                Warn("Mini character FacialDrivenKeyAnimation is not a Gallop.DrivenKeyAnimation: "
                    + key.FacialDrivenKeyAnimation.GetType().Name);
                return;
            }
            var clip = animation.clip;
            if (clip == null || mini.Facial == null) return;
            float time = elapsed * key.FacialPlaySpeed + key.FacialHeadFrame / TimelineFps;
            float length = clip.length;
            if (time <= 0f) time = 0f;
            float normalizedTime;
            if (length < time)
                // DAT_7ffd5a640d6c = 1.0f: a non-looping clip holds its last pose.
                normalizedTime = key.Loop ? (time % length) / length : 1f;
            else
                normalizedTime = length > 0f ? time / length : 0f;
            mini.Facial.PlayClip(clip, normalizedTime);
            mini.Facial.IsUseFaceType = false;
        }

        // MiniAnimationController.PlayEarMotion @0x7ffd59598f40: the ear play time is clamped to
        // [0, clip.length] (fmod(time, length) when Loop, clamp to length otherwise), then
        // DrivenKeyComponent.PlayEar(animation, 0, 0, 0) @0x7ffd58e6fa20,
        // DrivenKeyComponent.SetEarTime(time) @0x7ffd58e70e00 and
        // DrivenKeyComponent.SetDrivenKeySpeed(0) @0x7ffd58e70d30.
        private void ApplyEarMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float elapsed)
        {
            if (key.EarDrivenKeyAnimation == null) return;
            var animation = key.EarDrivenKeyAnimation as DrivenKeyAnimation;
            if (animation == null || animation.clip == null) return;
            if (mini.Ear == null || !mini.Ear.HasEarNodes)
            {
                Warn("Mini character ear DrivenKeyAnimation keys: DrivenKeyComponent.PlayEar is ported, but the mini's driven-key ear locator is not available (the head AssetHolder's DrivenKeyTarget key and the ear root node names are runtime string literals), so the ear nodes cannot be posed.");
                return;
            }
            float length = animation.clip.length;
            float time = elapsed * key.EarPlaySpeed + key.EarHeadFrame / TimelineFps;
            if (time <= 0f) time = 0f;
            if (length < time) time = key.Loop ? time % length : length;
            mini.Ear.PlayEar(animation);
            mini.Ear.SetEarTime(time);
            mini.Ear.SetDrivenKeySpeed(0f);
        }

        // MiniAnimationController.PlayPositionMotion @0x7ffd59599220 ->
        // MinimumPlayableAnimator (_positionPlayableAnimator +0x40): IsLoop (+0x70) = key.Loop,
        // PlayAnimationClip(clip, time) @0x7ffd576e1110 (recreates the AnimationClipPlayable when
        // the clip changes) and SetPlayTime @0x7ffd576e1480 (fmod(time, length) when IsLoop).
        private void ApplyPositionMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float elapsed)
        {
            if (key.PositionAnimationClip == null) return;
            if (mini.PositionAnimator == null)
            {
                Warn("Mini character position clips: the Position node of the mini model has no Animator (the MinimumPlayableAnimator target).");
                return;
            }
            var clip = key.PositionAnimationClip;
            if (mini.PositionClip != clip)
            {
                if (mini.PositionGraph.IsValid()) mini.PositionGraph.Destroy();
                mini.PositionClip = clip;
                mini.PositionGraph = PlayableGraph.Create("LiveMiniCharaPosition");
                mini.PositionGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                mini.PositionClipPlayable = AnimationClipPlayable.Create(mini.PositionGraph, clip);
                mini.PositionClipPlayable.SetApplyFootIK(false);
                mini.PositionClipPlayable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(mini.PositionGraph, "MiniCharaPosition", mini.PositionAnimator);
                output.SetSourcePlayable(mini.PositionClipPlayable);
                mini.PositionGraph.Play();
            }
            float time = elapsed * key.PositionPlaySpeed + key.PositionHeadFrame / TimelineFps;
            if (key.Loop && clip.length > 0f) time %= clip.length;
            mini.PositionClipPlayable.SetTime(time);
            mini.PositionGraph.Evaluate(0f);
        }

        // MiniAnimationController.PlayBodyMotion @0x7ffd59598f10 ->
        // MinimumPlayableAnimator.PlayAnimationClip / SetPlayTime (loop: fmod(time, length)).
        // The clip comes straight from the cutt key (AlterUpdate_MiniCharaMotion passes the key's
        // BodyAnimationClip field, +0x18); when the reference does not resolve the game skips the
        // body motion, and so does the viewer.
        private void ApplyBodyMotion(Mini mini, LiveTimelineKeyMiniCharaMotionData key, float elapsed)
        {
            if (mini.Animator == null) return;
            var clip = key.BodyAnimationClip;
            if (clip == null)
            {
                Warn("Mini character body clip is null in the cutt key data (the cross-bundle AnimationClip reference did not resolve); the game skips the body motion in that case.");
                return;
            }
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
            float time = elapsed * key.BodyPlaySpeed + key.BodyHeadFrame / TimelineFps;
            if (key.Loop && clip.length > 0f) time %= clip.length;
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
                if (mini.PositionGraph.IsValid()) mini.PositionGraph.Destroy();
                mini.Facial?.Dispose();
                mini.Ear?.Dispose();
                if (mini.Container != null) Object.Destroy(mini.Container.gameObject);
            }
            minis.Clear();
            warnings.Clear();
        }
    }
}
