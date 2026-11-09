using Gallop.ImageEffect;
using Gallop.Live;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop
{
    /// <summary>Timeline adapter. Rendering is owned by the Gallop renderer feature,
    /// not by an additional URP Bloom volume (which would apply bloom twice).</summary>
    [DisallowMultipleComponent]
    public class GallopImageEffect : MonoBehaviour
    {
        // Class-side parameter, only used by the MultiCamera composites (LiveMultiCameraTimeline
        // evaluates their tracks into it and copies it with Parameter.Setup). The active live camera
        // does NOT go through it: like the client, the Director writes straight into
        // RenderParameter.DofDiffuionBloomOverlay (native get_DofDiffusionBloomOverlayParam,
        // JP RVA 0x3E878C0, returns a ref to CameraData.Parameter + 0x1b0).
        [SerializeField] private DofDiffusionBloomOverlayParam _dofDiffusionBloomOverlayParam =
            new DofDiffusionBloomOverlayParam();

        public DofDiffusionBloomOverlayParam DofDiffusionBloomOverlayParam => _dofDiffusionBloomOverlayParam;
        public bool IsInitialized { get; private set; }
        private CameraData _cameraData;
        public PostImageEffectFeature.Parameter RenderParameter
        {
            get
            {
                if (!IsInitialized) InitializeVolume();
                return _cameraData.Parameter;
            }
        }

        // Timeline-side Exposure / ToneCurve state. Director.OnUpdateExposure/OnUpdateToneCurve
        // fill it and publish it into RenderParameter in the same call.
        public ExposureParam ExposureParam { get; } = new ExposureParam();
        public ToneCurveParam ToneCurveParam { get; } = new ToneCurveParam();

        // User A/B gate, independent of authored timeline data and other effects. Read by
        // CameraData.UpdateImageEffectParameter when the draw type is decided for each camera.
        public static bool UserDofEnabled { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetUserDofEnabled() { UserDofEnabled = true; }

        public static void SetUserDofEnabled(bool enabled)
        {
            UserDofEnabled = enabled;
        }

        protected void Awake() { InitializeVolume(); }

        // Retained for existing callers; no spatial Volume is created.
        public virtual void InitializeVolume()
        {
            if (IsInitialized) return;
            if (!TryGetComponent(out _cameraData))
                _cameraData = gameObject.AddComponent<CameraData>();
            IsInitialized = true;
        }
    }
}
