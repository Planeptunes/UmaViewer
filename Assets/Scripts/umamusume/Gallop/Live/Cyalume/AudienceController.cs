using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live.Cutt;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Cyalume
{
    // Port of Gallop.Cyalume.AudienceUpdateInfo (struct, TypeDefIndex 33584051). Field order follows the native layout
    // (offsets in comments are the native ones minus the 0x10 object header).
    public struct AudienceUpdateInfo
    {
        public float progressTime;                          // 0x00
        public int timelineIndex;                           // 0x04
        public Vector3 position;                            // 0x08
        public Quaternion rotation;                         // 0x14
        public Vector3 scale;                               // 0x24
        public bool isEnabledRender;                        // 0x30
        public Color cyalumeBodyColor;                      // 0x34
        public Color cyalumeGlowColor;                      // 0x44
        public float cyalumeGlowColorPower;                 // 0x54
        public float cyalumeMaskRadius;                     // 0x58
        public AudienceController.AnimationSetting AnimationSetting;       // 0x5C
        public int AnimationRootIndex;                      // 0x60
        public AudienceController.AnimationBodyRegion AnimationBodyRegion; // 0x64
        public AudienceController.AnimationCategory AnimationCategory;     // 0x68
        public int AnimationIndex;                          // 0x6C
        public WrapMode AnimationWrapMode;                  // 0x70
        public float AnimationSpeed;                        // 0x74
        public float AnimationOffsetTime;                   // 0x78
        public float AnimationTime;                         // 0x7C
        public bool UseAnimationTime;                       // 0x80
        public Color AudienceColor;                         // 0x84
        public float AudienceColorPower;                    // 0x94
        public Color AudienceToonBrightColor;               // 0x98
        public Color AudienceToonDarkColor;                 // 0xA8
        public float AudienceVertexColorToonPower;          // 0xB8
        public float AudienceOutlineWidthPower;             // 0xBC
        public Color AudienceOutlineColor;                  // 0xC0
        public OutlineColorBlend AudienceOutlineColorBlend; // 0xD0
        public Color AudienceRimColor;                      // 0xD4
        public float AudienceRimStep;                       // 0xE4
        public float AudienceRimFeather;                    // 0xE8
        public float AudienceRimSpecRate;                   // 0xEC
        public float AudienceRimShadowRate;                 // 0xF0
        public float AudienceRimHorizonOffset;              // 0xF4
        public float AudienceRimVerticalOffset;             // 0xF8
        public Color AudienceRimColor2;                     // 0xFC
        public float AudienceRimStep2;                      // 0x10C
        public float AudienceRimFeather2;                   // 0x110
        public float AudienceRimSpecRate2;                  // 0x114
        public float AudienceRimShadowRate2;                // 0x118
        public float AudienceRimHorizonOffset2;             // 0x11C
        public float AudienceRimVerticalOffset2;            // 0x120
        public bool AudienceUseDirectionalLight;            // 0x124
        public Quaternion AudienceDirectionalLightRotation; // 0x128
    }

    // Port of Gallop.Cyalume.AudienceController (TypeDefIndex 33584052): one audience object of the stage (an instance
    // of a pfb_env_live_cmn_audience*_toon prefab) driven by the cutt's Audience track.
    public class AudienceController
    {
        public enum AnimationSetting { Default = 0, SyncCyalume = 1, Direct = 2, Max = 3 }
        public enum AnimationBodyRegion { None = 0, RightHand = 1, LeftHand = 2, BothHands = 3, Max = 4 }
        public enum AnimationCategory { None = 0, Common = 1, Unique = 2, Max = 3 }

        // Material name tokens (string literals of the metadata: "_audience", "_cyalume", "_cyalume_glow").
        private const string AUDIENCE_MATERIAL_NAME_TOKEN = "_audience";
        private const string CYALUME_MATERIAL_NAME_TOKEN = "_cyalume";
        private const string CYALUME_GLOW_MATERIAL_NAME_TOKEN = "_cyalume_glow";

        // AudienceController static BODY_REGION_TOKEN_ARRAY / CATEGORY_TOKEN_ARRAY (.cctor 0x58F2D70). The literals of the
        // name format are "_r", "_l", "_common", "_unique" (metadata); the body-region slots None / BothHands are empty
        // here: their literal could not be recovered (only used by the Default / SyncCyalume animation settings).
        private static readonly string[] BODY_REGION_TOKEN_ARRAY = { string.Empty, "_r", "_l", string.Empty };
        private static readonly string[] CATEGORY_TOKEN_ARRAY = { string.Empty, "_common", "_unique" };
        // ResourcePath.LIVE_AUDIENCE_ANIMATION_NAME.
        private const string LIVE_AUDIENCE_ANIMATION_NAME = "anm_env_live_cmn_audience{0:000}{1}{2}_{3:000}";

        private static readonly int ID_AmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int ID_MulColor0 = Shader.PropertyToID("_MulColor0");
        private static readonly int ID_ColorPower = Shader.PropertyToID("_ColorPower");
        private static readonly int ID_CharaColor = Shader.PropertyToID("_CharaColor");
        private static readonly int ID_ToonDarkColor = Shader.PropertyToID("_ToonDarkColor");
        private static readonly int ID_ToonBrightColor = Shader.PropertyToID("_ToonBrightColor");
        private static readonly int ID_VertexColorToonPower = Shader.PropertyToID("_VertexColorToonPower");
        private static readonly int ID_OutlineWidth = Shader.PropertyToID("_OutlineWidth");
        private static readonly int ID_OutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int ID_RimColor = Shader.PropertyToID("_RimColor");
        private static readonly int ID_RimStep = Shader.PropertyToID("_RimStep");
        private static readonly int ID_RimFeather = Shader.PropertyToID("_RimFeather");
        private static readonly int ID_RimSpecRate = Shader.PropertyToID("_RimSpecRate");
        private static readonly int ID_RimShadowRate = Shader.PropertyToID("_RimShadowRate");
        private static readonly int ID_RimShadowRate2 = Shader.PropertyToID("_RimShadowRate2");
        private static readonly int ID_RimHorizonOffset = Shader.PropertyToID("_RimHorizonOffset");
        private static readonly int ID_RimVerticalOffset = Shader.PropertyToID("_RimVerticalOffset");
        private static readonly int ID_RimColor2 = Shader.PropertyToID("_RimColor2");
        private static readonly int ID_RimStep2 = Shader.PropertyToID("_RimStep2");
        private static readonly int ID_RimFeather2 = Shader.PropertyToID("_RimFeather2");
        private static readonly int ID_RimSpecRate2 = Shader.PropertyToID("_RimSpecRate2");
        private static readonly int ID_RimHorizonOffset2 = Shader.PropertyToID("_RimHorizonOffset2");
        private static readonly int ID_RimVerticalOffset2 = Shader.PropertyToID("_RimVerticalOffset2");
        private static readonly int ID_UseOriginalDirectionalLight = Shader.PropertyToID("_UseOriginalDirectionalLight");
        private static readonly int ID_OriginalDirectionalLightDir = Shader.PropertyToID("_OriginalDirectionalLightDir");

        // Static AudienceController.CyalumeController3D (get 0x4F93920 / set 0x4F93960): the cyalume controller whose
        // choreography drives the SyncCyalume animation setting.
        public static CyalumeControllerBase CyalumeController3D { get; set; }

        private GameObject _instance;                          // 0x10
        private Transform _transform;                          // 0x18
        private Material _audienceMaterial;                    // 0x20
        private bool _existAudienceMaterial;                   // 0x28
        private Material _cyalumeBodyMaterial;                 // 0x30
        private bool _existCyalumeBodyMaterial;                // 0x38
        private Material[] _cyalumeGlowMaterialArray;          // 0x40
        private bool _existCyalumeGlowMaterial;                // 0x48
        private Renderer[] _rendererArray;                     // 0x50
        private Material[][] _materialArray;                   // 0x58
        private Animation _animation;                          // 0x60
        private string[] _animationClipNameArray;              // 0x68
        private Vector4 _audienceWorldPosition;                // 0x70
        private bool _isInitialized;                           // 0x80
        private int _finalAnimationIndex;                      // 0x84
        private int _playAnimationIndex = -1;                  // 0x88
        private float _playAnimationTime;                      // 0x8C
        private Vector3 _position;                             // 0x90
        private Quaternion _rotation;                          // 0x9C
        private Vector3 _scale;                                // 0xAC
        private bool _isEnabledRender;                         // 0xB8
        private Color _cyalumeBodyColor;                       // 0xBC
        private Color _cyalumeGlowColor;                       // 0xCC
        private float _cyalumeGlowColorPower;                  // 0xDC
        private float _cyalumeMaskRadius;                      // 0xE0
        private AnimationSetting _animationSetting;            // 0xE4
        private int _animationRootIndex;                       // 0xE8
        private AnimationBodyRegion _animationBodyRegion;      // 0xEC
        private AnimationCategory _animationCategory;          // 0xF0
        private int _animationIndex;                           // 0xF4
        private WrapMode _animationWrapMode;                   // 0xF8
        private float _animationSpeed;                         // 0xFC
        private float _animationOffsetTime;                    // 0x100

        public GameObject GameObject => _instance;             // get_GameObject 0x7ffd548111b0

        // Initialize (0x7ffd593119c0).
        public void Initialize(GameObject prefab, Transform parent, AnimationClip[] clips, bool isSetLayer)
        {
            _instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (_instance == null)
                return;

            _transform = _instance.transform;
            _existAudienceMaterial = false;
            _existCyalumeBodyMaterial = false;
            _existCyalumeGlowMaterial = false;

            // Renderers and their (instanced) materials; the three materials the controller drives are picked by name.
            var glowList = new List<Material>();
            _rendererArray = _instance.GetComponentsInChildren<Renderer>();
            _materialArray = new Material[_rendererArray.Length][];
            for (int r = 0; r < _rendererArray.Length; r++)
            {
                _materialArray[r] = _rendererArray[r].materials;
                foreach (var material in _materialArray[r])
                {
                    if (material == null)
                        continue;
                    string name = material.name;
                    if (_audienceMaterial == null && name.Contains(AUDIENCE_MATERIAL_NAME_TOKEN))
                    {
                        _audienceMaterial = material;
                        _existAudienceMaterial = true;
                    }
                    if (name.Contains(CYALUME_GLOW_MATERIAL_NAME_TOKEN))
                    {
                        glowList.Add(material);
                    }
                    else if (name.Contains(CYALUME_MATERIAL_NAME_TOKEN) && _cyalumeBodyMaterial == null)
                    {
                        _cyalumeBodyMaterial = material;
                        _existCyalumeBodyMaterial = true;
                    }
                }
            }
            if (glowList.Count > 0)
            {
                _cyalumeGlowMaterialArray = glowList.ToArray();
                _existCyalumeGlowMaterial = true;
            }

            var existing = _instance.GetComponent<Animation>();
            if (clips == null)
            {
                // No clip list: the prefab's own Animation is kept, but its clips are replaced by per-instance clones.
                _animation = existing;
                if (_animation != null)
                {
                    var clones = new List<AnimationClip>();
                    var originals = new List<AnimationClip>();
                    foreach (AnimationState state in _animation)
                    {
                        var clone = UnityEngine.Object.Instantiate(state.clip);
                        clone.name = state.clip.name;
                        clones.Add(clone);
                        originals.Add(state.clip);
                    }
                    foreach (var original in originals)
                        _animation.RemoveClip(original.name);
                    foreach (var clone in clones)
                        _animation.AddClip(clone, clone.name);
                }
            }
            else
            {
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);
                _animation = _instance.AddComponent<Animation>();
                foreach (var clip in clips)
                {
                    if (clip != null)
                        _animation.AddClip(clip, clip.name);
                }
            }

            if (_animation != null)
            {
                var names = new List<string>();
                foreach (AnimationState state in _animation)
                    names.Add(state.clip.name.Replace("(Clone)", string.Empty));   // GallopGameObjectExtensions.DeleteCloneToken
                _animationClipNameArray = names.ToArray();
                _animation.clip = null;
                _animation.Stop();
            }

            if (isSetLayer)
            {
                // SetLayerRecursively(instance, GetLayer(2), GetLayer(0)): every Default-layer object moves to the audience
                // layer. The game scene dump (f4349) shows these objects on layer 25 = Background3D_NotReflect.
                int newLayer = LayerMask.NameToLayer("Background3D_NotReflect");
                if (newLayer >= 0)
                    SetLayerRecursively(_instance, newLayer, 0);
            }

            _isInitialized = true;
        }

        private static void SetLayerRecursively(GameObject go, int newLayer, int oldLayer)
        {
            if (go.layer == oldLayer)
                go.layer = newLayer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i).gameObject, newLayer, oldLayer);
        }

        // Destroy (0x7ffd593118d0): RenderUtils.Destroy(_materialArray) destroys the instanced materials.
        public void Destroy()
        {
            if (_materialArray != null)
            {
                foreach (var materials in _materialArray)
                {
                    if (materials == null)
                        continue;
                    foreach (var material in materials)
                    {
                        if (material != null)
                            UnityEngine.Object.Destroy(material);
                    }
                }
            }
            _isInitialized = false;
        }

        // GetWorldPosition (0x7ffd59311930): xyz = world position, w = 1 / mask radius (0 when the radius is 0).
        public Vector4 GetWorldPosition()
        {
            if (_isInitialized && _isEnabledRender)
                return _audienceWorldPosition;
            return Vector4.zero;
        }

        // UpdateInfo (0x7ffd59312f00).
        public void UpdateInfo(ref AudienceUpdateInfo info)
        {
            if (!_isInitialized)
                return;

            _position = info.position;
            _rotation = info.rotation;
            _scale = info.scale;
            _isEnabledRender = info.isEnabledRender;
            _cyalumeBodyColor = info.cyalumeBodyColor;
            _cyalumeGlowColor = info.cyalumeGlowColor;
            _cyalumeGlowColorPower = info.cyalumeGlowColorPower;
            _cyalumeMaskRadius = info.cyalumeMaskRadius;

            if (_instance.activeSelf != _isEnabledRender)     // SetActiveWithCheck
                _instance.SetActive(_isEnabledRender);

            _transform.localPosition = _position;
            _transform.localRotation = _rotation;
            _transform.localScale = _scale;

            Vector3 localScale = _transform.localScale;
            float radiusScale = localScale.x <= localScale.y ? localScale.z : localScale.x;
            float radius = _cyalumeMaskRadius * radiusScale;
            Vector3 worldPosition = _transform.position;
            _audienceWorldPosition = new Vector4(worldPosition.x, worldPosition.y, worldPosition.z,
                radius <= 0f ? 0f : 1f / radius);

            if (_existCyalumeBodyMaterial)
                _cyalumeBodyMaterial.SetColor(ID_AmbientColor, _cyalumeBodyColor);

            if (_existCyalumeGlowMaterial)
            {
                for (int i = 0; i < _cyalumeGlowMaterialArray.Length; i++)
                {
                    var glow = _cyalumeGlowMaterialArray[i];
                    glow.SetColor(ID_MulColor0, _cyalumeGlowColor);
                    glow.SetFloat(ID_ColorPower, _cyalumeGlowColorPower);
                }
            }

            if (_existAudienceMaterial)
            {
                var m = _audienceMaterial;
                float p = info.AudienceColorPower;
                m.SetColor(ID_CharaColor, new Color(info.AudienceColor.r * p, info.AudienceColor.g * p, info.AudienceColor.b * p, 1f));
                m.SetColor(ID_ToonBrightColor, info.AudienceToonBrightColor);
                m.SetColor(ID_ToonDarkColor, info.AudienceToonDarkColor);
                m.SetFloat(ID_VertexColorToonPower, info.AudienceVertexColorToonPower);
                m.SetFloat(ID_OutlineWidth, info.AudienceOutlineWidthPower * 0.325f);
                m.SetColor(ID_OutlineColor, GetOutlineColor(info.AudienceOutlineColor, info.AudienceOutlineColorBlend));
                m.SetColor(ID_RimColor, info.AudienceRimColor);
                m.SetFloat(ID_RimStep, info.AudienceRimStep);
                m.SetFloat(ID_RimFeather, info.AudienceRimFeather);
                m.SetFloat(ID_RimSpecRate, info.AudienceRimSpecRate);
                m.SetFloat(ID_RimShadowRate, info.AudienceRimShadowRate);
                m.SetFloat(ID_RimHorizonOffset, info.AudienceRimHorizonOffset);
                m.SetFloat(ID_RimVerticalOffset, info.AudienceRimVerticalOffset);
                m.SetColor(ID_RimColor2, info.AudienceRimColor2);
                m.SetFloat(ID_RimStep2, info.AudienceRimStep2);
                m.SetFloat(ID_RimFeather2, info.AudienceRimFeather2);
                m.SetFloat(ID_RimSpecRate2, info.AudienceRimSpecRate2);
                m.SetFloat(ID_RimShadowRate2, info.AudienceRimShadowRate2);
                m.SetFloat(ID_RimHorizonOffset2, info.AudienceRimHorizonOffset2);
                m.SetFloat(ID_RimVerticalOffset2, info.AudienceRimVerticalOffset2);
                m.SetInt(ID_UseOriginalDirectionalLight, info.AudienceUseDirectionalLight ? 1 : 0);
                Vector3 lightDir = info.AudienceDirectionalLightRotation * Vector3.forward;
                m.SetVector(ID_OriginalDirectionalLightDir, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            }

            UpdateAnimation(ref info);
        }

        // ModelController.GetOutlineColor (0x7ffd59673e90): the blend mode rewrites the alpha channel only.
        private static Color GetOutlineColor(Color color, OutlineColorBlend blend)
        {
            Color result = color;
            if (blend == OutlineColorBlend.Blend)
                result.a = (1f - color.a) * 0.5f;
            else if (blend == OutlineColorBlend.Multiply)
                result.a = color.a * 0.5f + 0.5f;
            else
                result.a = 0.5f;
            return result;
        }

        // UpdateAnimation (0x7ffd593127e0).
        public void UpdateAnimation(ref AudienceUpdateInfo info)
        {
            _animationSetting = info.AnimationSetting;
            _animationRootIndex = info.AnimationRootIndex;
            _animationBodyRegion = info.AnimationBodyRegion;
            _animationCategory = info.AnimationCategory;
            _animationIndex = info.AnimationIndex;
            _animationWrapMode = info.AnimationWrapMode;
            _animationSpeed = info.AnimationSpeed;
            _animationOffsetTime = info.AnimationOffsetTime;
            _finalAnimationIndex = -1;

            if (_animationSetting == AnimationSetting.Default)
            {
                string name = GetAnimationName(_animationRootIndex, _animationBodyRegion, _animationCategory, _animationIndex);
                FindFinalIndex(name);
                if (_finalAnimationIndex < 0)
                    return;
            }
            else if (_animationSetting == AnimationSetting.SyncCyalume)
            {
                var cyalume = CyalumeController3D;
                if (cyalume == null)
                    return;
                if (!cyalume.TryGetCurrentChoreographyType(out int choreographyType) || choreographyType > 7)
                    return;
                _animationIndex = choreographyType;
                // Category token slot 1 ("_common") with the body-region token of the key.
                string name = GetAnimationName(_animationRootIndex, _animationBodyRegion, AnimationCategory.Common, _animationIndex);
                FindFinalIndex(name);
                if (_finalAnimationIndex < 0)
                    return;
            }
            else if (_animationSetting == AnimationSetting.Direct)
            {
                _finalAnimationIndex = _animationIndex;
            }
            else
            {
                return;
            }

            bool changed = false;
            if (_playAnimationIndex != _finalAnimationIndex)
            {
                if (_playAnimationIndex >= 0 && _animationClipNameArray != null && _playAnimationIndex < _animationClipNameArray.Length)
                {
                    string playing = _animationClipNameArray[_playAnimationIndex];
                    if (_animation.IsPlaying(playing))
                        _animation.Stop(playing);
                }
                _playAnimationTime = 0f;
                changed = true;
                _playAnimationIndex = _finalAnimationIndex;
            }

            if (_playAnimationIndex >= 0 && _animationClipNameArray != null && _playAnimationIndex < _animationClipNameArray.Length)
            {
                string clipName = _animationClipNameArray[_playAnimationIndex];
                AnimationState state = _animation[clipName];
                if (state != null)
                {
                    float time = _animationSpeed * info.progressTime + _animationOffsetTime;
                    if (info.UseAnimationTime)
                        time = info.AnimationTime;
                    if (!Mathf.Approximately(_playAnimationTime, time) || changed)    // Gallop.Math.IsFloatEqual
                    {
                        _playAnimationTime = time;
                        state.time = time;
                        _animation.Play(clipName);
                        state.enabled = true;
                        _animation.wrapMode = _animationWrapMode;
                        _animation.Sample();
                        state.enabled = false;
                    }
                }
            }
        }

        private void FindFinalIndex(string name)
        {
            if (_animationClipNameArray == null)
                return;
            for (int i = 0; i < _animationClipNameArray.Length; i++)
            {
                if (name == _animationClipNameArray[i])
                {
                    _finalAnimationIndex = i;
                    break;
                }
            }
        }

        // ResourcePath.GetLiveStageAudienceAnimationName (0x7ffd577cec60) fed with the static token tables.
        private static string GetAnimationName(int rootIndex, AnimationBodyRegion region, AnimationCategory category, int index)
        {
            string regionToken = (int)region >= 0 && (int)region < BODY_REGION_TOKEN_ARRAY.Length ? BODY_REGION_TOKEN_ARRAY[(int)region] : string.Empty;
            string categoryToken = (int)category >= 0 && (int)category < CATEGORY_TOKEN_ARRAY.Length ? CATEGORY_TOKEN_ARRAY[(int)category] : string.Empty;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, LIVE_AUDIENCE_ANIMATION_NAME,
                rootIndex, regionToken, categoryToken, index);
        }
    }
}
