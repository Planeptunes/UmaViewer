using UnityEngine;

namespace Gallop.Live
{
    // Serialized identity and fields come from the authored common spotlight prefab.
    // This is a ground-following mesh, not Spotlight3d or a generated Light cookie.
    [DisallowMultipleComponent]
    public sealed class FollowSpotLightController : MonoBehaviour
    {
        [SerializeField] private Transform _rootTransform;
        [SerializeField] private Transform _locationTransform;
        private static readonly int ColorId = Shader.PropertyToID("_MulColor0");
        private static readonly int PowerId = Shader.PropertyToID("_ColorPower");
        private Renderer[] _renderers;
        // Native _additionMaterial / _multiplyMaterial: the authored LightAdd1 material and a copy
        // on LightBlend set to the Multiply light blend (DstColor / One).
        private Material[] _additionMaterials;
        private Material[] _multiplyMaterials;
        private MaterialPropertyBlock _block;
        // StaticVariableDefine.Live.FollowSpotLightScale: Initialize puts it on the renderer's
        // transform; the authored disc (radius 1) becomes a pool 1.7 times wider.
        private static readonly Vector3 FollowSpotLightScale = new Vector3(1.7f, 1f, 1.7f);
        private bool _illuminated;
        private float _power;
        private Cutt.LiveTimelineDefine.LightBlendMode _blendMode = Cutt.LiveTimelineDefine.LightBlendMode.Addition;
        // La sombra solo se dibuja cuando el foco esta encendido (no en el arranque de la rampa):
        // en los fogonazos el exceso de luz la tapa y el juego no la muestra. Con 0,5 las claves de
        // intensidad 0,5 (FollowSpotCenter/Left) siguen mostrando sombra y la rampa 0->1 de f1175 no.
        private const float ShadowPowerThreshold = 0.5f;

        public void Bind(Transform root, Transform location)
        {
            _rootTransform = root;
            _locationTransform = location;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            Renderer pool = GetComponentInChildren<Renderer>();
            if (pool != null) pool.transform.localScale = FollowSpotLightScale;
            CreateMaterials();
            ApplyLight(Color.white, 0f, 1f);
        }

        private void CreateMaterials()
        {
            _additionMaterials = new Material[_renderers.Length];
            _multiplyMaterials = new Material[_renderers.Length];
            Shader blendShader = ShaderManager.GetShader(ShaderManager.ShaderKinds.StageLightBlend);
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material addition = _renderers[i].sharedMaterial;
                _additionMaterials[i] = addition;
                if (addition == null || blendShader == null) continue;
                var multiply = new Material(addition) { shader = blendShader, renderQueue = addition.renderQueue };
                LiveDefine.TrySetLightBlendModeMaterialProperty(LiveDefine.LightBlendMode.Multiply, multiply);
                _multiplyMaterials[i] = multiply;
            }
        }

        // The BgColor1 key picks the material: Multiply brightens the floor under the character
        // with its own colour (DstColor / One), every other mode keeps the additive pool.
        public void SetBlendMode(Cutt.LiveTimelineDefine.LightBlendMode mode)
        {
            if (_blendMode == mode && _renderers != null) return;
            _blendMode = mode;
            if (_renderers == null || _additionMaterials == null) return;
            bool multiply = mode == Cutt.LiveTimelineDefine.LightBlendMode.Multiply;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = multiply && _multiplyMaterials[i] != null ? _multiplyMaterials[i] : _additionMaterials[i];
                if (_renderers[i] != null && _renderers[i].sharedMaterial != material)
                    _renderers[i].sharedMaterial = material;
            }
        }

        private bool DrawsLight => _illuminated;

        private void OnDestroy()
        {
            if (_multiplyMaterials == null) return;
            foreach (var material in _multiplyMaterials)
                if (material != null) Destroy(material);
        }

        // Verdadero cuando el foco esta encendido de verdad (no en la rampa de subida) y la pista
        // pide LightBlendMode.Multiply: es cuando el juego muestra la sombra del personaje.
        public bool IsShadowMode =>
            _power >= ShadowPowerThreshold && _blendMode == Cutt.LiveTimelineDefine.LightBlendMode.Multiply;

        public void ApplyLight(Color color, float power, float scale)
        {
            _illuminated = power != 0f;
            _power = power;
            // SetScale: the key scale is absolute on X/Z, Y is kept.
            transform.localScale = new Vector3(scale, transform.localScale.y, scale);
            foreach (var target in _renderers)
            {
                target.enabled = DrawsLight;
                target.GetPropertyBlock(_block);
                _block.SetColor(ColorId, color);
                _block.SetFloat(PowerId, power);
                target.SetPropertyBlock(_block);
            }
        }

        public void SetCharacterVisible(bool visible)
        {
            foreach (var target in _renderers)
                target.enabled = visible && DrawsLight;
        }

        public void AlterLateUpdate(Vector3 groundPosition)
        {
            // The caller supplies the evaluated formation position, not an animated hip/foot.
            // Keep the authored child mesh offset, rotation and scale intact.
            transform.position = groundPosition;
        }
    }
}
