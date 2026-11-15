using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class Spotlight3dController : MonoBehaviour
    {
        private Renderer _renderer;
        private Material[] _materials;
        private BillboardController _billboard;
        private Vector3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _scale = Vector3.one;
        private float _localHeight;
        private float _colorPower;
        private bool _enableLookAt;
        private Vector3 _lookAtTarget;
        private bool _isInitialized;
        public int TimelineIndex { get; private set; } = -1;

        // Native 0x1a7f1a0: first child renderer, per-instance materials, optional billboard.
        public void Initialize(int timelineIndex, Transform cameraTransform)
        {
            TimelineIndex = timelineIndex;
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer == null)
            {
                Debug.LogError("[Spotlight3d] Authored prefab has no renderer: " + name, this);
                return;
            }
            _renderer.enabled = false;
            _billboard = GetComponentInChildren<BillboardController>();
            if (_billboard != null) _billboard.Initialize(cameraTransform);
            _materials = _renderer.materials;
            SetMaterialValues(Color.white, 0f);
            _isInitialized = true;
        }

        // Native 0x1a7f480. Visibility is committed only by AlterLateUpdate.
        // JP Spotlight3dController.SetUpdateInfo (Ghidra 0x7ffd592dde20): `basePosition` is what Director.OnUpdateSpotlight3d
        // resolved (the character's position, or the model node TargetNodeName). PositionType 1 (Character) adds it to the
        // key position; EnableLookAt keeps it as the point the light faces and turns the billboard off.
        public void SetUpdateInfo(ref Spotlight3dUpdateInfo info, Vector3 basePosition)
        {
            if (!_isInitialized) return;
            _colorPower = info.isActive && info.colorPower > 0f ? info.colorPower : 0f;
            _position = info.position;
            if (info.PositionType == 1) _position += basePosition;
            _localHeight = info.localHeight;
            _rotation = Quaternion.Euler(info.rotation);
            _scale = info.scale;
            _enableLookAt = info.EnableLookAt;
            _lookAtTarget = basePosition;
            SetMaterialValues(info.color, _colorPower);
            if (_billboard != null)
            {
                _billboard.enabled = info.IsEnabledBillboard;
                _billboard.TargetCameraTransform = info.TargetCameraTransform;
                if (_enableLookAt) _billboard.enabled = false;
            }
        }

        // Native 0x1a7ee00: world-space placement, then local billboard/authored rotation.
        public void AlterLateUpdate()
        {
            if (!_isInitialized || _renderer == null) return;
            if (_colorPower <= 0f)
            {
                _renderer.enabled = false;
                return;
            }
            _renderer.enabled = true;
            transform.position = _position + _rotation * new Vector3(0f, _localHeight, 0f);
            // JP: the billboard only runs when the light does not look at a point.
            if (!_enableLookAt && _billboard != null)
            {
                transform.localRotation = Quaternion.identity;
                _billboard.AlterLateUpdate();
            }
            transform.localRotation = transform.localRotation * _rotation;
            transform.localScale = _scale;
            if (!_enableLookAt) return;
            // JP: turn the light's back axis (-forward) towards the target; skipped when it is on top of it.
            Vector3 toTarget = _lookAtTarget - transform.position;
            if (toTarget.sqrMagnitude <= 0.0001f) return;
            Vector3 current = transform.TransformDirection(Vector3.back);
            transform.rotation = Quaternion.FromToRotation(current, toTarget.normalized) * transform.rotation;
        }

        private void SetMaterialValues(Color color, float power)
        {
            int colorId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._MulColor0);
            int powerId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._ColorPower);
            foreach (Material material in _materials)
            {
                if (material == null) continue;
                material.SetColor(colorId, color);
                material.SetFloat(powerId, power);
            }
        }

        // Native 0x1a7f430 destroys only the renderer's cloned material array.
        private void OnDestroy()
        {
            if (_materials == null) return;
            foreach (Material material in _materials) if (material != null) Destroy(material);
            _materials = null;
        }
    }
}
