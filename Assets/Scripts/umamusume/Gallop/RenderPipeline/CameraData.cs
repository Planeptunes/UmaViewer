using UnityEngine;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Runtime image-effect state attached to a render camera.
    /// The recovered Gallop feature reads this parameter from CameraData.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraData : MonoBehaviour
    {
        [SerializeField]
        private PostImageEffectFeature.Parameter _parameter;

        public PostImageEffectFeature.Parameter Parameter
        {
            get
            {
                if (_parameter == null)
                    _parameter = new PostImageEffectFeature.Parameter();
                return _parameter;
            }
            set => _parameter = value;
        }

        // Native CameraData.GLOBAL_SHADOW_DISTANCE (static, JP: written by MultiLightShadowController, 15 by default
        // inside a Live). Infinity outside a Live (viewer): only the user's shadow distance limits it.
        public static float GlobalShadowDistance = float.PositiveInfinity;

        // Native fields read by GallopRenderer.SetupCullingParameters (+0x104 / +0x101 / +0x102 / +0x148). The Live
        // code never writes them (MaxShadowDistance is 0 in every camera of live 1093, probe), so they keep the
        // native defaults.
        public float MaxShadowDistance;
        public bool IsDisableObjectCulling;
        public bool IsDisableLightCulling;
        public bool IsUIRendering;

        public void Initialize(bool _)
        {
            _ = false;
            // Native CameraData.Initialize resets these (JP 0x7ffd595b9590).
            MaxShadowDistance = 0.0f;
            IsUIRendering = false;
            if (_parameter == null)
                _parameter = new PostImageEffectFeature.Parameter();
        }

        public void UpdateImageEffectParameter()
        {
            // Native CameraData.UpdateImageEffectParameter (JP RVA 0x523A6B0, verified in Ghidra):
            // IsDisableDofTemporary = IsEnableDofAutoDisable && DofMaxFocalSize <= DofFocalSize,
            // i.e. >= (inclusive). A key whose focal size reaches the max (e.g. song 1053:
            // forcalSize 30 = maxForcalSize 30) turns the DOF off, so the far stage stays sharp.
            ref var dof = ref Parameter.DofDiffuionBloomOverlay;
            dof.IsDisableDofTemporary = dof.IsEnableDofAutoDisable &&
                dof.DofFocalSize >= dof.DofMaxFocalSize;
            // Per-effect switch of the live settings panel (Bloom/Diffusion/PostFilm are masked
            // inside DecideDrawType, which reads this parameter's own flags).
            dof.DecideDrawType(
                dof.IsEnableDof && !dof.IsDisableDofTemporary && GallopImageEffect.UserDofEnabled &&
                Gallop.RenderPipeline.PostEffectSelection.IsEnabled(Gallop.RenderPipeline.PostEffectId.Dof));
        }

        private void Awake()
        {
            Initialize(false);
        }
    }
}
