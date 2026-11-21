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

        // Native CameraData fields read by GallopRenderer.SetupCullingParameters (+0x104 / +0x101 / +0x102 / +0x148).
        // The Live code never writes them (MaxShadowDistance is 0 in every camera of live 1093), so they keep the
        // native defaults.
        public float MaxShadowDistance;
        public bool IsDisableObjectCulling;
        public bool IsDisableLightCulling;
        public bool IsUIRendering;
        // Native <IsOpaqueRenderingForUICamera>k__BackingField (+0x149, dump_jp.cs CameraData TypeDefIndex
        // 33586488): UIRenderingPass.Execute (JP 0x7ffd59616110) draws an extra opaque-queue pass before the
        // transparent one when set.
        public bool IsOpaqueRenderingForUICamera;

        // Native CameraData.MirrorQuality { Normal, Simple }.
        public enum MirrorQuality
        {
            Normal = 0,
            Simple = 1,
        }

        // Native fields +0xd0 / +0xd9 / +0xda / +0xdc. CameraData..ctor (JP 0x7ffd595ba770) sets RenderingLayerMask = 1
        // and MirrorCasterQuality = 1 (Simple); MirrorReflection.CreateMirrorCamera (JP 0x7ffd58209df0) sets
        // IsMirrorRendering = true and MirrorCasterQuality = Simple on the mirror camera, and
        // MirrorReflection.SetLightMirrorShader (JP 0x7ffd5820baf0) rewrites MirrorCasterQuality.
        // GallopRenderer.Setup draws with the SimpleMirrorCaster RenderObjectPass pair instead of the DrawObjectsPass pair
        // when IsMirrorRendering && MirrorCasterQuality == Simple.
        public uint RenderingLayerMask = 1;
        public bool IsMirrorRendering;
        // Native IsInvertCulling (+0xda, dump_jp.cs CameraData TypeDefIndex 33586488): when set (or when
        // IsMirrorRendering), GallopRenderer.Setup enqueues the SetInvertCullingPass pair (events 0 / 1000)
        // so the camera renders with inverted triangle culling. The Live code never writes it; the mirror
        // camera gets its inversion from IsMirrorRendering.
        public bool IsInvertCulling;
        public MirrorQuality MirrorCasterQuality = MirrorQuality.Simple;

        // Native <IsSkipDrawObjectsPass>k__BackingField (+0x100): DrawObjectsPass.Setup (JP 0x7ffd595e76a0) returns
        // false when set, so GallopRenderer.Setup does not enqueue the DrawObjectsPass. The Live code never sets it.
        public bool IsSkipDrawObjectsPass;

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

        private Camera _camera;

        private static readonly int GlobalCameraFovId = Shader.PropertyToID("_GlobalCameraFov");
        private static readonly int GlobalOutlineWidthId = Shader.PropertyToID("_GlobalOutlineWidth");
        private static readonly int GlobalOutlineOffsetId = Shader.PropertyToID("_GlobalOutlineOffset");

        // Native CameraData.OnBeforeCameraRendering (JP 0x7ffd595b9df0), not-adjust-fov branch:
        // GraphicSettings.SetGlobalCameraFov(camera.fieldOfView) (0x7ffd58e9c540): _GlobalCameraFov = min(1, fov / 30).
        // The Outline and SHADOWCASTER passes of the character / prop toon shaders (TSER...) read that global to push
        // vertices (xy) and depth (z); the SHADOWCASTER one is what DepthOnlyPass draws, so a wrong value moves the
        // props in the depth the DOF / fog read. GraphicSettings.UpdateCharacterEnv (0x7ffd58e9ccd0) also publishes
        // _GlobalOutlineWidth / _GlobalOutlineOffset, whose defaults are 1.0 (GraphicSettings fields 0x208 / 0x20C). Inside a
        // Live the viewer's own model-viewer hacks (scene values 0.1, UmaViewerGlobalShader.FixedUpdate) are replaced
        // by the game's values.
        public void OnBeforeCameraRendering()
        {
            if (Gallop.Live.Director.instance == null || _camera == null)
                return;

            Shader.SetGlobalFloat(GlobalCameraFovId, Mathf.Min(1f, _camera.fieldOfView / 30f));
            Shader.SetGlobalFloat(GlobalOutlineWidthId, 1f);
            Shader.SetGlobalFloat(GlobalOutlineOffsetId, 1f);
            // Lets the model viewer restore its own values once the Live is gone (UmaViewerGlobalShader.FixedUpdate).
            UmaViewerGlobalShader.LiveGlobalsWritten = true;
        }

        private void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
                OnBeforeCameraRendering();
        }

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void Awake()
        {
            Initialize(false);
        }
    }
}
