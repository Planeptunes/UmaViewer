using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    /// <summary>
    /// Gallop.LiveQualitySettings statics read by the monitor code (UpdateMonitor reads +0 IsMonitor,
    /// CreateMonitorTexture / RemakeMonitorTexture read +4 IsMonitorCamera). In live 1001 every static is
    /// true, which is the user's quality setup.
    /// </summary>
    public static class LiveQualitySettings
    {
        public static bool IsMonitor = true;       // +0x0
        public static bool IsMonitorCamera = true; // +0x4
    }

    /// <summary>
    /// Port of Gallop.Live.MonitorCamera (MonoBehaviour, code_JP Gallop.Live.MonitorCamera.c).
    /// The camera renders into its own texture (_monitorTexture +0x38); at the end of the camera's rendering
    /// OnEndRendering blits it into the shared texture (_targetTexture +0x30, Director.MonitorTextureArray[i])
    /// that the stage screens read.
    /// </summary>
    public class MonitorCamera : MonoBehaviour
    {
        // TEXTURE_WIDTH_RATE_MIN / MAX / DEF: DAT_7ffd5a640f0c = 0.25, DAT_7ffd5a640d6c = 1.0 (read from
        // GameAssembly_jp_dumped.dll); live 1001: 0.25 / 1.0 / 0.25.
        public const float TEXTURE_WIDTH_RATE_MIN = 0.25f;
        public const float TEXTURE_WIDTH_RATE_MAX = 1.0f;
        public const float TEXTURE_WIDTH_RATE_DEF = 0.25f;

        // Static backing field read by get_TextureWidthRate @0x7ffd59251710 / written by set_TextureWidthRate @0x7ffd59251760.
        private static float _textureWidthRate = TEXTURE_WIDTH_RATE_DEF;

        // Viewer adaptation: lets the render pipeline recognise a monitor camera (the game switches post effects off
        // through the CameraData sub-object written in SetupTargetCamera; the viewer's passes look the camera up here).
        private static readonly HashSet<Camera> MonitorCameras = new HashSet<Camera>();

        // Viewer adaptation: the game's camera has no depth buffer on its own render texture (depth 0)
        // because its renderer allocates an intermediate depth itself; URP drawing straight into a texture needs one.
        private const int MonitorTextureDepthBits = 24;

        [SerializeField] private Camera _targetCamera;           // +0x20
        [SerializeField] private bool _isEnabledTargetTexture;   // +0x28
        private RenderTexture _targetTexture;                    // +0x30
        private RenderTexture _monitorTexture;                   // +0x38
        private bool _endRenderingHooked;

        public Camera TargetCamera => _targetCamera;                // get_TargetCamera @0x7ffd54811170
        public RenderTexture MonitorTexture => _monitorTexture;     // get_MonitorTexture @0x7ffd54811160

        public static bool IsMonitorCamera(Camera camera)
        {
            return camera != null && MonitorCameras.Contains(camera);
        }

        // get_TextureWidthRate @0x7ffd59251710 / set_TextureWidthRate @0x7ffd59251760 (clamped to [MIN, MAX]).
        public static float TextureWidthRate
        {
            get => _textureWidthRate;
            set
            {
                if (value < TEXTURE_WIDTH_RATE_MIN) value = TEXTURE_WIDTH_RATE_MIN;
                else if (TEXTURE_WIDTH_RATE_MAX < value) value = TEXTURE_WIDTH_RATE_MAX;
                _textureWidthRate = value;
            }
        }

        /// <summary>
        /// Gallop.Live.MonitorCamera.GetTextureSideLength @0x7ffd59250ff0: w = (int)(virtualResolution * rate),
        /// h = w * 0x2d0 / 0x500 (720 / 1280). With useRate false the rate is the minimum (DAT_7ffd5a640f0c).
        /// </summary>
        public static void GetTextureSideLength(out int width, out int height, bool useTextureWidthRate)
        {
            float rate = useTextureWidthRate ? _textureWidthRate : TEXTURE_WIDTH_RATE_MIN;
            int x = GetVirtualResolution3DWidth();
            width = (int)(x * rate);
            height = (width * 0x2d0) / 0x500;
        }

        // Gallop.LiveUtil.GetVirtualResolution3D @0x7ffd57c4b800 -> GraphicSettings.GetVirtualResolution3D
        // @0x7ffd58e99190 (width of the packed result). KNOWN DIFFERENCE: the viewer has no GraphicSettings object, so
        // only the branch of `_graphics3DQuality == 3` (live 1001: _graphics3DQuality 3) is ported:
        // x = RoundToInt(2160 / min(width, height) * width) (DAT_7ffd5a8bbbfc = 2160) on the live (landscape) screen,
        // no further snapping, resolution scale (+0x60) taken as 1.
        private static int GetVirtualResolution3DWidth()
        {
            float width = Screen.width;
            float height = Screen.height;
            float shortSide = Mathf.Min(width, height);
            if (shortSide <= 0f) return 0;
            return Mathf.RoundToInt(2160f / shortSide * width);
        }

        // SetupCaptureTexture @0x7ffd59251450.
        public void SetupCaptureTexture(RenderTexture texture)
        {
            if (_targetCamera == null) return;
            _targetTexture = texture;
            _isEnabledTargetTexture = texture != null;
        }

        // Initialize @0x7ffd592510a0.
        public void Initialize(int width, int height)
        {
            FindTargetCamera();
            ReleaseMonitorTexture();
            if (_targetCamera != null) CreateMonitorTexture(width, height);
            SetupTargetCamera();
        }

        // CreateMonitorTexture @0x7ffd59250e70: new RenderTexture(w, h, 0, default format), Create, filterMode Bilinear.
        public void CreateMonitorTexture(int width, int height)
        {
            if (_targetCamera == null) return;
            _monitorTexture = new RenderTexture(width, height, MonitorTextureDepthBits, RenderTextureFormat.Default);
            _monitorTexture.Create();
            _monitorTexture.filterMode = FilterMode.Bilinear;
        }

        // FindTargetCamera @0x7ffd59250f60.
        public void FindTargetCamera()
        {
            if (_targetCamera == null) _targetCamera = GetComponent<Camera>();
        }

        // SetupTargetCamera @0x7ffd592514f0: targetTexture = _monitorTexture, allowMSAA off, the CameraData flags
        // (+0x150 sub-object, bytes +0x20 and +0x970) cleared, and OnEndRendering subscribed to the camera's callbacks.
        public void SetupTargetCamera()
        {
            if (_targetCamera == null) return;
            _targetCamera.targetTexture = _monitorTexture;
            _targetCamera.allowMSAA = false;
            // Viewer adaptation of CameraData (unidentified sub-object at +0x150): no URP post processing, and the
            // viewer's own passes skip the camera through IsMonitorCamera.
            var cameraData = _targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null) cameraData = _targetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            MonitorCameras.Add(_targetCamera);
            // Viewer adaptation of the delegate combined into the camera host component (+0x50):
            // OnEndRendering runs when this camera finishes rendering.
            if (!_endRenderingHooked)
            {
                _endRenderingHooked = true;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _targetCamera) OnEndRendering(context, camera);
        }

        // OnDestroy @0x7ffd59251210.
        private void OnDestroy()
        {
            if (_targetCamera != null)
            {
                _targetCamera.targetTexture = null;
                MonitorCameras.Remove(_targetCamera);
            }
            if (_endRenderingHooked)
            {
                _endRenderingHooked = false;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            }
            ReleaseMonitorTexture();
        }

        // ReleaseMonitorTexture @0x7ffd592513a0.
        public void ReleaseMonitorTexture()
        {
            if (_monitorTexture != null)
            {
                _monitorTexture.Release();
                Destroy(_monitorTexture);
                _monitorTexture = null;
            }
        }

        // OnEndRendering @0x7ffd59251290: CommandBuffer.Blit(_monitorTexture, _targetTexture) executed on the context.
        public void OnEndRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!_isEnabledTargetTexture || _monitorTexture == null || _targetTexture == null) return;
            // Viewer adaptation: URP raises endCameraRendering after it has already submitted the context, so a
            // CommandBuffer executed here never ran (the shared texture stayed black). Copy immediately instead.
            Graphics.Blit(_monitorTexture, _targetTexture);
        }
    }

    /// <summary>
    /// Port of Gallop.Live.MonitorController (code_JP Gallop.Live.MonitorController.c). Stateful: the owner writes
    /// m_CenterPosition / m_Scale / m_Multi from the MonitorControl key and calls UpdateParam, which clamps the
    /// fields in place and derives m_Offset.
    /// </summary>
    public class MonitorController
    {
        public bool m_Multi;               // +0x10
        public Vector2 m_CenterPosition;   // +0x14
        public Vector2 m_Scale = Vector2.one; // +0x1C
        public Vector2 m_Offset;           // +0x24

        // UpdateParam @0x7ffd592517e0 (DAT_7ffd5a640dac = -1.0, DAT_7ffd5a640d6c = 1.0, DAT_7ffd5a640d60 = 0.5).
        public void UpdateParam()
        {
            if (!m_Multi)
            {
                m_Scale.x = Mathf.Clamp(m_Scale.x, -1f, 1f);
                m_Scale.y = Mathf.Clamp(m_Scale.y, -1f, 1f);
            }
            else
            {
                m_Scale.x = Mathf.Abs(m_Scale.x);
                m_Scale.y = Mathf.Abs(m_Scale.y);
                if (m_Scale.x == 0f) m_Scale.x = 1f;
                if (m_Scale.y == 0f) m_Scale.y = 1f;
            }

            float extentX = 0f;
            if (!m_Multi)
            {
                extentX = Mathf.Repeat(m_Scale.x, 1f);
                if (extentX == 0f) extentX = 1f;
            }
            m_CenterPosition.x = Mathf.Clamp(m_CenterPosition.x, extentX - 1f, 1f - extentX);

            float extentY = 0f;
            if (!m_Multi)
            {
                extentY = Mathf.Repeat(m_Scale.y, 1f);
                if (extentY == 0f) extentY = 1f;
            }
            m_CenterPosition.y = Mathf.Clamp(m_CenterPosition.y, extentY - 1f, 1f - extentY);

            m_Offset.x = (m_CenterPosition.x * 0.5f + 0.5f) - m_Scale.x * 0.5f;
            m_Offset.y = (m_CenterPosition.y * 0.5f + 0.5f) - m_Scale.y * 0.5f;
        }
    }
}
