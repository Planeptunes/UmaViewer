using UnityEngine;

namespace Gallop.Live
{
    // Foot shadow (one per ankle). Field spelling/defaults are the shadow000 prefab TypeTree
    // (_maxAlpha 1, _shadowPower 0.75, _crossHeight 2). Intensity and colour are driven by the
    // BgColor1 "Shadow*" tracks through UpdateShadowFactor; the shadow fades out as the ankle
    // rises above its bind height and is quantised to 1/64 steps (also used as sortingOrder).
    [DisallowMultipleComponent]
    public sealed class FakeShadowController : MonoBehaviour
    {
        public enum Type
        {
            Normal = 0,
            Projection = 1,
        }

        private const float AlphaSteps = 64f;
        private const float AlphaStepRate = 1f / 64f;
        private const float ColorEpsilon = 1e-10f;
        private const int SortingOrderBase = 100;
        // Draw order (user-confirmed): the ground light panels must render BEFORE the foot shadow,
        // otherwise they cover the X. The shadow is therefore kept in the transparent range and
        // drawn after them (the game's shader tag is "Transparent-1", under its own additive
        // panels; ours cover it, so the X goes after).
        public const int ShadowRenderQueue = 3005;

        private static readonly int ShadowIntensityId = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");

        [SerializeField] private float _maxAlpha = 1f;
        [SerializeField] private float _shadowPower = 0.75f;
        [SerializeField] private Color _color = Color.black;
        [SerializeField] private Transform _rootTransform;
        [SerializeField] private Transform _locationTransform;
        [SerializeField] private float _crossHeight = 2f;

        private Renderer _renderer;
        private Material _instanceMaterial;
        private MaterialPropertyBlock _materialPropertyBlock;
        private Color _prevColor = Color.black;
        private Vector3 _position;
        private float _y;
        private float _alpha = -1f;
        private bool _isProjection;
        private LightProjection _projection;
        private Transform _cacheTransform;
        private bool _forceUpdate;
        private Type _shadowType = Type.Normal;
        private Vector3 _offsetPos = Vector3.zero;
        private Vector3 _scale = Vector3.one;
        private bool _initialized;

        public float MaxAlpha { get => _maxAlpha; set => _maxAlpha = value; }
        public float ShadowPower { get => _shadowPower; set => _shadowPower = value; }
        public Color Color { get => _color; set => _color = value; }
        public Vector3 OffsetPos { get => _offsetPos; set => _offsetPos = value; }
        public Vector3 Scale { get => _scale; set => _scale = value; }
        public bool IsLightProjection => _isProjection;
        public LightProjection LightProjection => _projection;
        public float CurrentAlpha => _alpha;

        // Ankle bone. The bind-time height is the reference "foot on the ground" height.
        // Stored relative to the location node so a character that is not at y = 0 when it is
        // bound does not get its floor counted twice (identical to native when bound at y = 0).
        public Transform RootTransform
        {
            set
            {
                _rootTransform = value;
                if (value == null) { _y = 0f; return; }
                _y = value.position.y - (_locationTransform != null ? _locationTransform.position.y : 0f);
            }
        }

        // Model position node: provides the floor height under the character.
        public Transform LocationTransform { set => _locationTransform = value; }

        public void Bind(Transform ankle, Transform positionNode)
        {
            LocationTransform = positionNode;
            RootTransform = ankle;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _cacheTransform = transform;
            _projection = GetComponentInChildren<LightProjection>(true);
            _isProjection = _projection != null && _projection.Initialize();
            if (_isProjection) _projection.IsEnable = false;
            _alpha = -1f;
            _materialPropertyBlock = new MaterialPropertyBlock();
            _renderer = GetComponentInChildren<Renderer>(true);
            if (_renderer == null)
            {
                Debug.LogError("[FakeShadow] Authored shadow prefab has no Renderer.");
                return;
            }
            // Native CreateInstance(doInstanceMaterial: true) + CharaEffectInfo renderQueue 3005.
            Material shared = _renderer.sharedMaterial;
            if (shared != null)
            {
                _instanceMaterial = new Material(shared) { name = shared.name, hideFlags = HideFlags.DontSave };
                _instanceMaterial.renderQueue = ShadowRenderQueue;
                _renderer.sharedMaterial = _instanceMaterial;
            }
            _materialPropertyBlock.SetFloat(ShadowIntensityId, 1f);
        }

        // CharacterObject.UpdateShadowFactor: power, colour (alpha = max alpha) and type from
        // the BgColor1 Shadow* key.
        public void UpdateShadowFactor(float shadowPower, Color color, Type shadowType)
        {
            _shadowPower = shadowPower;
            _maxAlpha = color.a;
            _color = color;
            ShadowType = shadowType;
        }

        public Type ShadowType
        {
            get => _shadowType;
            set
            {
                Type next = _isProjection && value == Type.Projection ? Type.Projection : Type.Normal;
                bool changed = _shadowType != next;
                _shadowType = next;
                _forceUpdate = changed;
                if (!changed || _renderer == null) return;
                if (next == Type.Projection)
                {
                    _projection.IsEnable = _renderer.enabled;
                    _renderer.enabled = false;
                }
                else
                {
                    if (_projection != null)
                    {
                        _renderer.enabled = _projection.IsEnable;
                        _projection.IsEnable = false;
                    }
                }
            }
        }

        public bool IsVisible
        {
            get
            {
                if (_shadowType == Type.Normal) return _renderer != null && _renderer.enabled;
                return _projection != null && _projection.IsEnable;
            }
            set
            {
                if (_shadowType == Type.Normal)
                {
                    if (_isProjection) _projection.IsEnable = false;
                    if (_renderer != null && _renderer.enabled != value) _renderer.enabled = value;
                }
                else
                {
                    if (_projection == null) return;
                    _projection.IsEnable = value;
                    if (_renderer != null && _renderer.enabled) _renderer.enabled = false;
                }
            }
        }

        public void AlterLateUpdate()
        {
            if (_rootTransform == null || _cacheTransform == null) return;
            Vector3 rootPosition = _rootTransform.position;
            _position.x = rootPosition.x;
            _position.z = rootPosition.z;
            float ground = _y;
            if (_locationTransform != null)
            {
                _position.y = _locationTransform.position.y;
                ground += _position.y;
            }
            Vector3 position = _position + _offsetPos;
            _cacheTransform.SetPositionAndRotation(position, Quaternion.identity);
            _cacheTransform.localScale = _scale;

            float height = Mathf.Clamp((rootPosition.y - ground) * _crossHeight, 0f, _maxAlpha);
            int step = (int)((_maxAlpha - height) * _shadowPower * AlphaSteps);
            float alpha = step * AlphaStepRate;

            if (_shadowType == Type.Normal)
            {
                if (_renderer == null || !_renderer.enabled) return;
                if (!NeedsUpdate(alpha)) return;
                Remember(alpha);
                _materialPropertyBlock.SetFloat(ShadowIntensityId, _alpha);
                _materialPropertyBlock.SetColor(ShadowColorId, _color);
                _renderer.SetPropertyBlock(_materialPropertyBlock);
                _renderer.sortingOrder = step + SortingOrderBase;
                _forceUpdate = false;
            }
            else
            {
                if (!NeedsUpdate(alpha)) return;
                Remember(alpha);
                Material material = _projection != null ? _projection.Material : null;
                if (material == null) return;
                material.SetColor(ShadowColorId, _color);
                material.SetFloat(ShadowIntensityId, alpha);
                _forceUpdate = false;
            }
        }

        private bool NeedsUpdate(float alpha)
        {
            Color d = _color - _prevColor;
            bool sameColor = d.r * d.r + d.g * d.g + d.b * d.b + d.a * d.a < ColorEpsilon;
            return !(Mathf.Approximately(_alpha, alpha) && sameColor && !_forceUpdate);
        }

        private void Remember(float alpha)
        {
            _alpha = alpha;
            _prevColor = _color;
        }

        private void OnDestroy()
        {
            if (_instanceMaterial != null)
            {
                if (Application.isPlaying) Destroy(_instanceMaterial);
                else DestroyImmediate(_instanceMaterial);
                _instanceMaterial = null;
            }
        }
    }
}
