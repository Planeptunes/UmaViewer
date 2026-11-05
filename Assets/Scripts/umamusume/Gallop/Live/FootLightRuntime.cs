using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gallop.Live
{
    // Spotlight3d. Resource presence is independent of illumination: FollowSpot BgColor1
    // tracks select characters and drive circle power; defaults start dark.
    public sealed class FootLightRuntime : IDisposable
    {
        public const string DefaultSpotlightPath = "3d/env/live/common/spotlight/pfb_env_live_cmn_spotlight000";
        public const string DefaultShadowPath = "3d/env/live/common/shadow/pfb_env_live_cmn_shadow000";
        public const int Layer = 13;

        private sealed class Binding
        {
            public int characterIndex;
            public Transform character;
            public FollowSpotLightController spotlight;
            public Renderer[] shadowCasters;
            public UmaContainerCharacter container;
            public FakeShadowController leftShadow;
            public FakeShadowController rightShadow;
        }

        private readonly List<Binding> _bindings = new List<Binding>();
        private Func<int, Vector3> _groundPositionResolver;
        private Action<Gallop.RenderPipeline.CustomProjector, bool> _projectorRegistrar;

        // Optional path resolvers allow an explicitly known per-character resource selection.
        // Null/empty selections always use the default; do not guess how a root names array
        // maps to characters. Resolve/load bundles through the existing Live resource owner.
        public void Initialize(Transform stageRoot, IList<Transform> characterRoots,
            Func<int, Vector3> groundPositionResolver, Func<string, GameObject> prefabResolver,
            Func<int, bool, Transform> footResolver,
            Func<int, string> spotlightPathResolver = null, Func<int, string> shadowPathResolver = null,
            Func<int, Transform> positionNodeResolver = null,
            Action<Gallop.RenderPipeline.CustomProjector, bool> projectorRegistrar = null)
        {
            Dispose();
            if (stageRoot == null) throw new ArgumentNullException(nameof(stageRoot));
            if (characterRoots == null) throw new ArgumentNullException(nameof(characterRoots));
            if (groundPositionResolver == null) throw new ArgumentNullException(nameof(groundPositionResolver));
            if (prefabResolver == null) throw new ArgumentNullException(nameof(prefabResolver));
            if (footResolver == null) throw new ArgumentNullException(nameof(footResolver));
            _groundPositionResolver = groundPositionResolver;
            _projectorRegistrar = projectorRegistrar;
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < characterRoots.Count; i++)
                {
                    Transform character = characterRoots[i];
                    if (character == null) continue;
                    var binding = new Binding { characterIndex = i, character = character };
                    // Renderers del personaje: su sombra (shadow map) se enciende/apaga con las
                    // ventanas BgColor1 en LightBlendMode.Multiply (ver AlterLateUpdate).
                    binding.shadowCasters = character.GetComponentsInChildren<Renderer>(true);
                    binding.container = character.GetComponentInParent<UmaContainerCharacter>();
                    if (binding.container == null)
                        binding.container = character.GetComponentInChildren<UmaContainerCharacter>(true);
                    _bindings.Add(binding);
                    GameObject spotlight = InstantiateResource(spotlightPathResolver?.Invoke(i),
                        DefaultSpotlightPath, stageRoot, prefabResolver, prefabs);
                    if (spotlight != null)
                    {
                        binding.spotlight = spotlight.GetComponent<FollowSpotLightController>();
                        if (binding.spotlight == null)
                        {
                            DestroyInstance(spotlight);
                            Debug.LogError("[FootLight] Authored spotlight prefab has no FollowSpotLightController.");
                        }
                        else binding.spotlight.Bind(character, character);
                    }
                    // Foot shadows (CharacterObject._shadowL/_shadowR): root = ankle, location =
                    // model position node. Driven by the BgColor1 Shadow* tracks (ApplyShadow).
                    Transform positionNode = positionNodeResolver?.Invoke(i);
                    binding.leftShadow = CreateFootShadow(i, true, footResolver(i, true), positionNode,
                        shadowPathResolver?.Invoke(i), stageRoot, prefabResolver, prefabs);
                    binding.rightShadow = CreateFootShadow(i, false, footResolver(i, false), positionNode,
                        shadowPathResolver?.Invoke(i), stageRoot, prefabResolver, prefabs);
                    RegisterProjector(binding.leftShadow, true);
                    RegisterProjector(binding.rightShadow, true);
                }
                AlterLateUpdate();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static FakeShadowController CreateFootShadow(int index, bool left, Transform foot,
            Transform positionNode, string path, Transform stageRoot, Func<string, GameObject> resolver,
            Dictionary<string, GameObject> prefabs)
        {
            if (foot == null)
            {
                Debug.LogError("[FootLight] Missing " + (left ? "left" : "right") + " ankle for character " + index);
                return null;
            }
            var instance = InstantiateResource(path, DefaultShadowPath, stageRoot, resolver, prefabs);
            if (instance == null) return null;
            var controller = instance.GetComponent<FakeShadowController>();
            if (controller == null)
            {
                DestroyInstance(instance);
                Debug.LogError("[FootLight] Authored shadow prefab has no FakeShadowController.");
                return null;
            }
            instance.name += left ? "_LeftFoot" : "_RightFoot";
            controller.Initialize();
            controller.Bind(foot, positionNode);
            return controller;
        }

        private static GameObject InstantiateResource(string path, string defaultPath, Transform parent,
            Func<string, GameObject> resolver, Dictionary<string, GameObject> prefabs)
        {
            if (string.IsNullOrEmpty(path)) path = defaultPath;
            if (!prefabs.TryGetValue(path, out GameObject prefab))
            {
                prefab = resolver(path);
                prefabs.Add(path, prefab);
                if (prefab == null) Debug.LogError("[FootLight] Missing authored prefab " + path);
            }
            if (prefab == null) return null;
            GameObject instance = Object.Instantiate(prefab, parent, false);
            SetLayer(instance.transform);
            return instance;
        }

        private void RegisterProjector(FakeShadowController shadow, bool add)
        {
            if (_projectorRegistrar == null || shadow == null || !shadow.IsLightProjection) return;
            var projector = shadow.LightProjection.Projector;
            if (projector != null) _projectorRegistrar(projector, add);
        }

        // StageController.UpdateShadowColor1: BgColor1 Shadow* tracks select characters and set
        // power / colour (alpha = max alpha) / projection on both foot shadows.
        // Returns true when the track is a shadow track (handled, spotlight not evaluated).
        public bool ApplyShadow(ref BgColor1UpdateInfo info)
        {
            int mask;
            switch (info.TimelineName)
            {
                case "ShadowCenter": mask = 1; break;
                case "ShadowLeft": mask = 2; break;
                case "ShadowRight": mask = 4; break;
                case "ShadowBack": mask = 0xFFFF8; break;
                case "ShadowOther": mask = 0xFFFFE; break;
                case "ShadowColor": mask = info.flags == 0 ? 0xFFFFF : info.flags; break;
                case "Shadow": mask = info.flags; break;
                default: return false;
            }
            if (mask <= 0) return true;
            var type = info.IsProjector ? FakeShadowController.Type.Projection : FakeShadowController.Type.Normal;
            foreach (var binding in _bindings)
            {
                if (binding.characterIndex >= 20 || (mask & (1 << binding.characterIndex)) == 0) continue;
                binding.leftShadow?.UpdateShadowFactor(info.colorPower, info.color, type);
                binding.rightShadow?.UpdateShadowFactor(info.colorPower, info.color, type);
            }
            return true;
        }

        public void ApplySpotlight(ref BgColor1UpdateInfo info)
        {
            if (ApplyShadow(ref info)) return;
            LiveCharaPositionFlag group;
            switch (info.TimelineName)
            {
                case "FollowSpotCenter": group = LiveCharaPositionFlag.Center; break;
                case "FollowSpotLeft": group = LiveCharaPositionFlag.Left; break;
                case "FollowSpotRight": group = LiveCharaPositionFlag.Right; break;
                case "FollowSpotBack": group = LiveCharaPositionFlag.Back; break;
                // StageController.UpdateCharaFollowSpotColor1: "Other" is every character but
                // the centre; the colour track takes the key's flags, or every character.
                case "FollowSpotOther": group = (LiveCharaPositionFlag)0xFFFFE; break;
                case "FollowSpotColor":
                    if (info.flags < 0) return;
                    group = (LiveCharaPositionFlag)0xFFFFF;
                    break;
                default: return;
            }
            foreach (var binding in _bindings)
            {
                int mask = info.flags != 0 ? info.flags : (int)group;
                bool selected = (mask & (1 << binding.characterIndex)) != 0;
                if (!selected || binding.spotlight == null) continue;
                // The authored key selects which face of the spot is drawn: an additive pool or
                // the multiply fake shadow. Native applies it every frame, independently of power.
                binding.spotlight.SetBlendMode(info.LightBlendMode);
                binding.spotlight.ApplyLight(info.color, info.colorPower, info.scale);
            }
        }

        // Call after timeline formation placement, before rendering any presentation,
        // auxiliary, monitor or reflection camera. No autonomous Unity LateUpdate ordering.
        public void AlterLateUpdate()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                Binding binding = _bindings[i];
                bool active = binding.character != null && binding.character.gameObject.activeInHierarchy;
                if (binding.spotlight != null && binding.spotlight.gameObject.activeSelf != active)
                    binding.spotlight.gameObject.SetActive(active);
                // OnLiveCharaVisible: foot shadows follow the character visibility.
                if (binding.leftShadow != null && binding.leftShadow.IsVisible != active)
                    binding.leftShadow.IsVisible = active;
                if (binding.rightShadow != null && binding.rightShadow.IsVisible != active)
                    binding.rightShadow.IsVisible = active;
                if (!active) continue;
                binding.leftShadow?.AlterLateUpdate();
                binding.rightShadow?.AlterLateUpdate();
                Vector3 position = _groundPositionResolver(binding.characterIndex);
                if (binding.spotlight != null) binding.spotlight.AlterLateUpdate(position);
                if (binding.spotlight != null) binding.spotlight.SetCharacterVisible(active);
                // La sombra del personaje (shadow map real) solo se dibuja en las ventanas
                // BgColor1 con LightBlendMode.Multiply: es cuando el juego la muestra. Durante el
                // fogonazo (los haces barren y el spot se apaga) la sombra desaparece.
                // Además, la FormationOffset del cutt la apaga por personaje (IsCastShadow).
                var locator = binding.container != null ? binding.container.LiveLocator : null;
                bool castByTimeline = locator == null || locator.IsCastShadow;
                UnityEngine.Rendering.ShadowCastingMode castMode =
                    castByTimeline && binding.spotlight != null && binding.spotlight.IsShadowMode
                        ? UnityEngine.Rendering.ShadowCastingMode.On
                        : UnityEngine.Rendering.ShadowCastingMode.Off;
                Renderer[] casters = binding.shadowCasters;
                if (casters != null)
                {
                    for (int c = 0; c < casters.Length; c++)
                    {
                        Renderer caster = casters[c];
                        if (caster != null && caster.shadowCastingMode != castMode)
                            caster.shadowCastingMode = castMode;
                    }
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                Binding binding = _bindings[i];
                if (binding.spotlight != null) DestroyInstance(binding.spotlight.gameObject);
                RegisterProjector(binding.leftShadow, false);
                RegisterProjector(binding.rightShadow, false);
                if (binding.leftShadow != null) DestroyInstance(binding.leftShadow.gameObject);
                if (binding.rightShadow != null) DestroyInstance(binding.rightShadow.gameObject);
            }
            _bindings.Clear();
            _groundPositionResolver = null;
            _projectorRegistrar = null;
        }

        private static void DestroyInstance(GameObject instance)
        {
            instance.SetActive(false);
            Object.Destroy(instance);
        }

        private static void SetLayer(Transform root)
        {
            root.gameObject.layer = Layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i));
        }
    }
}
