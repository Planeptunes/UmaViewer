using System;
using System.Collections.Generic;
using System.IO;
using Gallop.Live.Cutt;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gallop.Live
{
    // Director.LoadParticle 0x1a61710; LiveEffectController 0x1a9d2d0..0x1a9d921.
    public sealed class LiveEffectController : IDisposable
    {
        private readonly Dictionary<int, Effect> effects = new Dictionary<int, Effect>();
        private readonly List<UmaDatabaseEntry> resources = new List<UmaDatabaseEntry>();
        private readonly Transform defaultParent, worldOwner;
        private readonly Func<int, Transform> characterTransform;
        private readonly Func<string, int, Transform> stageTransform;
        private readonly int normalLayer, excludedColorCorrectionLayer, transparentLayer;

        public LiveEffectController(Transform defaultParent, Transform worldOwner,
            Func<int, Transform> characterTransform, Func<string, int, Transform> stageTransform,
            int normalLayer, int excludedColorCorrectionLayer, int transparentLayer)
        {
            this.defaultParent = defaultParent;
            this.worldOwner = worldOwner;
            this.characterTransform = characterTransform;
            this.stageTransform = stageTransform;
            this.normalLayer = normalLayer;
            this.excludedColorCorrectionLayer = excludedColorCorrectionLayer;
            this.transparentLayer = transparentLayer;
        }

        private static bool Selected(LiveTimelineEffectData group, TimelinePlayerMode mode,
            Func<int, bool> variationEnabled)
        {
            return group?.keys != null && !group.keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)
                && group.keys.EnablePlayModeTimeline(mode)
                && (!group.applyVariation || (variationEnabled != null && variationEnabled(group.variationId)));
        }

        public static string GetResourcePath(LiveTimelineEffectData group)
        {
            return string.IsNullOrEmpty(group.folder) ? "3d/Effect/Live/pfb_" + group.name
                : "3d/Effect/Live/" + group.folder + "/pfb_" + group.name;
        }

        // Pass GetWorkSheetBySheetIndex(1), NOT every worksheet. Same predicate as Load.
        public static void CollectResourcePaths(LiveTimelineWorkSheet sheetIndex1, TimelinePlayerMode mode,
            Func<int, bool> variationEnabled, ICollection<string> paths)
        {
            if (sheetIndex1?.effectList == null) return;
            foreach (var group in sheetIndex1.effectList)
                if (Selected(group, mode, variationEnabled)) paths.Add(GetResourcePath(group));
        }

        public void Load(LiveTimelineWorkSheet sheetIndex1, TimelinePlayerMode mode,
            Func<int, bool> variationEnabled)
        {
            Dispose();
            if (sheetIndex1?.effectList == null) return;
            for (int i = 0; i < sheetIndex1.effectList.Count; i++)
            {
                var group = sheetIndex1.effectList[i];
                if (!Selected(group, mode, variationEnabled)) continue;
                string path = GetResourcePath(group);
                GameObject prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    UmaDatabaseEntry entry = FindResourceEntry(path);
                    if (entry == null)
                    {
                        Debug.LogWarning("[LiveEffectController] Missing effect entry: " + path);
                        continue;
                    }
                    AssetBundle bundle = UmaAssetManager.LoadAssetBundle(entry, neverUnload: false, isRecursive: true);
                    // LoadAssetBundle acquires the dependency tree even if the root fails.
                    resources.Add(entry);
                    if (bundle != null) prefab = LoadPrefab(bundle, path);
                }
                if (prefab == null)
                {
                    Debug.LogWarning("[LiveEffectController] Missing effect prefab: " + path);
                    continue;
                }
                var instance = Object.Instantiate(prefab);
                if (instance.GetComponentInChildren<ParticleSystem>(true) == null)
                {
                    Debug.LogWarning("[LiveEffectController] Effect has no particle system: " + path);
                    Object.Destroy(instance);
                    continue;
                }
                effects.Add(i, new Effect(instance, group.keys, defaultParent, normalLayer, excludedColorCorrectionLayer, transparentLayer));
            }
        }

        public static UmaDatabaseEntry FindResourceEntry(string path)
        {
            var entries = UmaViewerMain.Instance?.AbList;
            if (entries == null) return null;
            if (entries.TryGetValue(path, out var entry)) return entry;
            foreach (var pair in entries)
                if (string.Equals(pair.Key, path, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value?.Name, path, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        private static GameObject LoadPrefab(AssetBundle bundle, string path)
        {
            var prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab != null) return prefab;
            string file = Path.GetFileName(path);
            foreach (string asset in bundle.GetAllAssetNames())
                if (string.Equals(Path.GetFileNameWithoutExtension(asset), file, StringComparison.OrdinalIgnoreCase))
                    return bundle.LoadAsset<GameObject>(asset);
            return null;
        }

        public static int CharacterIndex(int owner)
        {
            if (owner >= 1 && owner <= 17) return owner - 1;
            if (owner >= 23 && owner <= 24) return owner - 2;
            return -1;
        }

        public static bool IsOwnerEnabled(int owner, Func<int, bool> characterEnabled)
        {
            if (owner == 18 || owner == 19 || owner == 22) return true;
            int index = CharacterIndex(owner);
            return index >= 0 && characterEnabled != null && characterEnabled(index);
        }

        private Transform ResolveOwner(LiveEffectTimeline.EffectUpdateInfo info)
        {
            if (info.owner == 18) return null;
            if (info.owner == 19) return worldOwner;
            if (info.owner == 22) return stageTransform?.Invoke(info.ParentStageObjectName, info.ParentStageObjectNameHash);
            int index = CharacterIndex(info.owner);
            return index >= 0 ? characterTransform?.Invoke(index) : null;
        }

        public void Update(LiveEffectTimeline.EffectUpdateInfo info)
        {
            if (effects.TryGetValue(info.timelineIndex, out var effect)) effect.Update(info, ResolveOwner(info));
        }

        // Run AFTER character transform/scaling and Effect.Update (0x1a6a1d0).
        public void UpdateScale(LiveEffectTimeline.EffectScaleUpdateInfo info)
        {
            if (effects.TryGetValue(info.timelineIndex, out var effect)) effect.UpdateScale(info);
        }

        public void Pause(bool paused)
        {
            foreach (var effect in effects.Values) effect.Pause(paused);
        }

        // The next update of each effect rebuilds it from its key history (see Effect.CatchUp).
        public void ResetForSeek()
        {
            foreach (var effect in effects.Values)
            {
                effect.Stop(true);
                effect.RequestCatchUp();
            }
        }

        public void Dispose()
        {
            foreach (var effect in effects.Values) effect.Dispose();
            effects.Clear();
            for (int i = resources.Count - 1; i >= 0; i--) UmaAssetManager.UnloadAssetBundle(resources[i], false);
            resources.Clear();
        }

        private sealed class Effect : IDisposable
        {
            private static readonly int ColorId = Shader.PropertyToID("_Color");
            private static readonly int MulColorId = Shader.PropertyToID("_MulColor0");
            private static readonly int ColorPowerId = Shader.PropertyToID("_ColorPower");
            private readonly GameObject instance;
            private readonly Transform transform, fallbackParent;
            private readonly Vector3 initialPosition, initialAngle;
            private readonly ParticleSystem root;
            private readonly ParticleSystem[] particles;
            private readonly Renderer[] renderers;
            private readonly Animator[] animators;
            private readonly Dictionary<int, ParticleSystem> namedParticles = new Dictionary<int, ParticleSystem>();
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private Transform owner;
            private bool playing, paused;
            private float speed = 1f;
            private readonly int normalLayer, excludedColorCorrectionLayer, transparentLayer;
            private readonly LiveTimelineKeyEffectDataList keys;
            private bool catchUpPending = true;
            // Effect.Initialize 0x4FA3B60 / Update 0x4F9A6F0: colour is only driven when a
            // material has _Color or _MulColor0, and the block is only written when colour,
            // power, property or the RGB flag change. The state starts at white / 1 / _Color /
            // false, so an effect keyed white x1 keeps its material colour (e.g. the 0.51 grey
            // of the bazooka confetti, which the shader doubles).
            private readonly bool colorEnabled;
            private Color appliedColor = Color.white;
            private float appliedColorPower = 1f;
            private int appliedColorProperty;
            private bool appliedColorPowerRGB;

            public Effect(GameObject instance, LiveTimelineKeyEffectDataList keys, Transform fallbackParent,
                int normalLayer, int excludedColorCorrectionLayer, int transparentLayer)
            {
                this.instance = instance;
                this.keys = keys;
                this.fallbackParent = fallbackParent;
                this.normalLayer = normalLayer;
                this.excludedColorCorrectionLayer = excludedColorCorrectionLayer;
                this.transparentLayer = transparentLayer;
                transform = instance.transform;
                initialPosition = transform.localPosition;
                initialAngle = transform.localEulerAngles;
                root = instance.GetComponentInChildren<ParticleSystem>(true);
                particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                renderers = instance.GetComponentsInChildren<Renderer>(true);
                animators = instance.GetComponentsInChildren<Animator>(true);
                foreach (var particle in particles)
                    namedParticles[FNVHash.Generate(particle.gameObject.name)] = particle;
                foreach (var renderer in renderers)
                    if (renderer != null) KeepBehindCharacters(renderer.sharedMaterials);
                foreach (var renderer in renderers)
                {
                    if (renderer == null || colorEnabled) continue;
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && (material.HasProperty(ColorId) || material.HasProperty(MulColorId)))
                        {
                            colorEnabled = true;
                            break;
                        }
                }
                transform.SetParent(fallbackParent, false);
                SetLayer(normalLayer);
                Stop(true);
            }

            private static readonly int StencilMaskId = Shader.PropertyToID("_StencilMask");
            private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");

            // Effects authored with stencil NotEqual 100 hide where a character is drawn (the game
            // writes 100 on the whole character). The viewer gives each character's face and hair
            // its own id (200-250, UmaContainerCharacter.StencilId), which NotEqual 100 lets through,
            // so test Greater 100 instead: drawn only where nothing at or above 100 was written.
            private static void KeepBehindCharacters(Material[] materials)
            {
                foreach (var material in materials)
                {
                    if (material == null || !material.HasProperty(StencilCompId) || !material.HasProperty(StencilMaskId))
                        continue;
                    if ((int)material.GetFloat(StencilCompId) == (int)UnityEngine.Rendering.CompareFunction.NotEqual &&
                        (int)material.GetFloat(StencilMaskId) == ShaderManager.DEFAULT_CHARA_STENCIL_MASK)
                        material.SetFloat(StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Greater);
                }
            }

            public void RequestCatchUp() => catchUpPending = true;

            public void Update(LiveEffectTimeline.EffectUpdateInfo info, Transform resolvedOwner)
            {
                bool catchUp = catchUpPending;
                catchUpPending = false;
                if (!catchUp && info.isClear && info.IsUpdateClear) Stop(true);
                foreach (var renderer in renderers) if (renderer != null) renderer.enabled = info.isEnable;
                owner = resolvedOwner;
                Transform parent = owner != null ? owner : fallbackParent;
                if (transform.parent != parent) transform.SetParent(parent, false);
                Vector3 position = info.offset + (info.owner == 18 ? initialPosition : Vector3.zero);
                Vector3 angle = info.offsetAngle + (info.owner == 18 ? initialAngle : Vector3.zero);
                transform.localPosition = position;
                Vector3 linked = transform.position;
                transform.position = new Vector3(info.isLinkOwnerPositionX ? linked.x : position.x,
                    info.isLinkOwnerPositionY ? linked.y : position.y, info.isLinkOwnerPositionZ ? linked.z : position.z);
                if (info.isLinkOwnerRotate) transform.localRotation = Quaternion.Euler(angle);
                else transform.rotation = Quaternion.Euler(angle);
                transform.localScale = info.OffsetScale;
                SetLayer(info.isTransparentFX ? transparentLayer
                    : info.ExcludeColorCorrection ? excludedColorCorrectionLayer : normalLayer);
                speed = info.Speed;
                foreach (var particle in particles)
                {
                    if (particle == null) continue;
                    var main = particle.main;
                    main.simulationSpeed = speed;
                }
                foreach (var animator in animators) if (animator != null) animator.speed = paused ? 0f : speed;
                if (info.IsParticleParam) UpdateParticleParams(info.ParticleParamArray);
                if (catchUp) CatchUp(info.CurrentTime);
                else if (playing != info.isPlay)
                {
                    if (!info.isPlay) Stop(false);
                    else
                    {
                        root.Simulate(info.ProgressTime, true, true, false);
                        root.Play(true);
                        foreach (var animator in animators)
                            if (animator != null) { animator.enabled = true; animator.Update(info.ProgressTime); }
                        playing = true;
                    }
                }
                var rootMain = root.main;
                if (rootMain.loop != info.isLoop) rootMain.loop = info.isLoop;
                if (catchUp) ReplayColor(info.CurrentTime * 60f);
                if (TrackColor(info.color, info.colorPower, info.ColorProperty, info.IsColorPowerRGB) || catchUp)
                    CommitColor();
                if (paused) Pause(true);
            }

            private bool TrackColor(Color color, float power, int property, bool powerRGB)
            {
                bool changed = false;
                if (color != appliedColor) { appliedColor = color; changed = true; }
                if (power != appliedColorPower) { appliedColorPower = power; changed = true; }
                if (property != appliedColorProperty) { appliedColorProperty = property; changed = true; }
                if (powerRGB != appliedColorPowerRGB) { appliedColorPowerRGB = powerRGB; changed = true; }
                return changed;
            }

            // After a seek, rebuild the colour state the game would hold at this frame: start
            // from the initial state and pass every earlier key through the change detection.
            private bool colorWritten;

            private void ReplayColor(float frame)
            {
                appliedColor = Color.white;
                appliedColorPower = 1f;
                appliedColorProperty = 0;
                appliedColorPowerRGB = false;
                colorWritten = false;
                properties.Clear();
                if (keys == null) return;
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i] as LiveTimelineKeyEffectData;
                    if (key == null || key.frame > frame) break;
                    if (TrackColor(key.color, key.colorPower, key.ColorProperty, key.IsColorPowerRGB))
                        colorWritten = true;
                }
            }

            private void CommitColor()
            {
                if (!colorEnabled) return;
                if (!colorWritten && appliedColor == Color.white && appliedColorPower == 1f
                    && appliedColorProperty == 0 && !appliedColorPowerRGB)
                {
                    // Never changed from the initial state: the material colour stays.
                    foreach (var renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(null);
                    return;
                }
                colorWritten = true;
                if (appliedColorProperty == 0)
                {
                    Color color = appliedColor;
                    color.r *= appliedColorPower; color.g *= appliedColorPower; color.b *= appliedColorPower;
                    if (!appliedColorPowerRGB) color.a *= appliedColorPower;
                    properties.SetColor(ColorId, color);
                }
                else if (appliedColorProperty == 1)
                {
                    properties.SetColor(MulColorId, appliedColor);
                    properties.SetFloat(ColorPowerId, appliedColorPower);
                }
                else return;
                foreach (var renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(properties);
            }

            // The game never seeks, so an effect only holds what it accumulated while playing
            // (e.g. the son1156 hearts piling up on the floor plane with infinite lifetime).
            // After a seek, or when the live starts mid-way, replay the keys since the last
            // clear: play/stop, particle params (seeds included, so the result matches normal
            // playback) and loop, simulating each span up to the current time.
            private void CatchUp(float time)
            {
                Stop(true);
                if (keys == null || keys.Count == 0) return;
                float frame = time * 60f;
                int last = -1, start = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i] as LiveTimelineKeyEffectData;
                    if (key == null || key.frame > frame) break;
                    last = i;
                    if (key.IsClear) start = i;
                }
                if (last < 0) return;
                bool emitting = false;
                float playStart = 0f;
                for (int i = start; i <= last; i++)
                {
                    var key = keys[i] as LiveTimelineKeyEffectData;
                    if (key == null) continue;
                    float from = key.frame / 60f;
                    float to = i < last && keys[i + 1] != null ? Mathf.Min(keys[i + 1].frame / 60f, time) : time;
                    if (key.IsParticleParam) UpdateParticleParams(key.ParticleParamArray);
                    var main = root.main;
                    if (main.loop != key.IsLoop) main.loop = key.IsLoop;
                    if (key.IsPlay && !emitting)
                    {
                        root.Simulate(0f, true, true, false);
                        emitting = true;
                        playStart = from;
                    }
                    else if (!key.IsPlay && emitting)
                    {
                        root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                        emitting = false;
                    }
                    // Simulate after a stop keeps the already emitted particles moving. The
                    // timeline speed is already in main.simulationSpeed.
                    float span = to - from;
                    if (span > 0f && (emitting || AnyAlive()))
                        root.Simulate(span, true, false, true);
                }
                if (emitting)
                {
                    root.Play(true);
                    foreach (var animator in animators)
                        if (animator != null) { animator.enabled = true; animator.Update(Mathf.Max(0f, time - playStart)); }
                    playing = true;
                }
                else if (AnyAlive())
                {
                    // Resume the leftover particles without starting a new emission.
                    root.Play(true);
                    root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            private bool AnyAlive()
            {
                foreach (var particle in particles)
                    if (particle != null && particle.particleCount > 0) return true;
                return false;
            }

            public void UpdateScale(LiveEffectTimeline.EffectScaleUpdateInfo info)
            {
                if (!info.isEnable || owner == null) return;
                Vector3 scale = transform.localScale, parent = owner.localScale;
                if (!info.isLinkOwnerScaleX) scale.x = SafeDivide(scale.x, parent.x);
                if (!info.isLinkOwnerScaleY) scale.y = SafeDivide(scale.y, parent.y);
                if (!info.isLinkOwnerScaleZ) scale.z = SafeDivide(scale.z, parent.z);
                transform.localScale = scale;
            }

            private static float SafeDivide(float value, float scale) => Mathf.Abs(scale) < 0.000001f ? 0f : value / scale;

            private void UpdateParticleParams(LiveTimelineKeyEffectData.ParticleParam[] parameters)
            {
                if (parameters == null) return;
                foreach (var parameter in parameters)
                {
                    if (parameter == null || !namedParticles.TryGetValue(parameter.ObjectNameHash, out var particle)
                        || particle == null) continue;
                    if (particle.gameObject.activeSelf != parameter.IsEnabled)
                        particle.gameObject.SetActive(parameter.IsEnabled);
                    var main = particle.main;
                    if (!particle.isPlaying && !Mathf.Approximately(main.duration, parameter.MainDuration)) main.duration = parameter.MainDuration;
                    main.loop = parameter.MainLooping;
                    if (parameter.IsUpdateMainSimulationSpace)
                    {
                        ParticleSystemSimulationSpace space = parameter.MainSimulationSpace == 1
                            ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
                        if (parameter.IsSetAllMainSimulationSpace)
                            foreach (var item in particles) { if (item == null) continue; var module = item.main; module.simulationSpace = space; }
                        else main.simulationSpace = space;
                    }
                    var color = main.startColor;
                    if (color.mode == ParticleSystemGradientMode.TwoColors)
                    {
                        color.colorMin = parameter.MainStartColorMin;
                        color.colorMax = parameter.MainStartColorMax;
                        main.startColor = color;
                    }
                    else if (color.mode == ParticleSystemGradientMode.Color)
                    {
                        color.color = parameter.MainStartColorMax;
                        main.startColor = color;
                    }
                    var emission = particle.emission;
                    emission.rateOverTimeMultiplier = parameter.EmissionRateOverTimeMultiplier;
                    var shape = particle.shape;
                    shape.scale = parameter.ShapeScale;
                    if (parameter.IsUpdateRandomSeed)
                    {
                        if (parameter.IsSetAllRandomSeed) foreach (var item in particles) SetRandomSeed(item, parameter.RandomSeed);
                        else SetRandomSeed(particle, parameter.RandomSeed);
                    }
                }
            }

            private static void SetRandomSeed(ParticleSystem particle, int seed)
            {
                if (particle == null || particle.isPlaying) return;
                particle.useAutoRandomSeed = seed == 0;
                if (seed != 0) particle.randomSeed = unchecked((uint)seed);
            }

            private void SetLayer(int layer)
            {
                // A missing configured layer cannot be replaced by an unrelated one.
                if (layer < 0) return;
                SetLayer(instance.transform, layer);
            }

            private static void SetLayer(Transform node, int layer)
            {
                node.gameObject.layer = layer;
                for (int i = 0; i < node.childCount; i++) SetLayer(node.GetChild(i), layer);
            }

            public void Stop(bool clear)
            {
                root.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
                foreach (var animator in animators) if (animator != null) animator.enabled = false;
                playing = false;
            }

            public void Pause(bool value)
            {
                bool wasPaused = paused;
                paused = value;
                if (value) root.Pause(true);
                else if (wasPaused && playing) root.Play(true);
                foreach (var animator in animators) if (animator != null) animator.speed = value ? 0f : speed;
            }

            public void Dispose()
            {
                if (instance == null) return;
                Stop(true);
                Object.Destroy(instance);
            }
        }
    }
}
