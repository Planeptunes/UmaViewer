using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Port of Gallop.RenderPipeline.GallopRenderer.SetupCullingParameters (JP 0x7ffd596070d0) on top of URP's
    // UniversalRenderer (the project embeds URP in Packages/ with the `sealed` modifier removed from
    // UniversalRenderer). The native version derives the shadow distance and the culling options from the camera's
    // Gallop CameraData instead of URP's per-camera value:
    //   distance = static shadow distance (MultiLightShadowController, CameraData.GlobalShadowDistance here)
    //   renderShadows == false           -> 0
    //   CameraData.MaxShadowDistance > 0 -> that value
    //   cullingOptions: OcclusionCull and NeedsReflectionProbes always off; _isUIRendering -> DisablePerObjectCulling
    //   and only NeedsLighting kept; IsDisableObjectCulling -> DisablePerObjectCulling; IsDisableLightCulling ->
    //   NeedsLighting off; no shadow casters when the distance is 0 or the asset supports no shadows.
    public class GallopRenderer : UniversalRenderer
    {
        // GallopRenderer..ctor (JP code_JP Gallop.RenderPipeline.GallopRenderer.c): +0x228 / +0x230 are
        // RenderObjectPass(0xfa, tags, true) and RenderObjectPass(0x1c2, tags, false) over a one-element tag array.
        // The literal is a runtime string pointer (not readable from the dumped DLL); SimpleMirrorCaster is the only
        // mirror LightMode present in the game shaders (tools 09-shaders: Gallop/3D/Bg/* expose it).
        private static readonly string[] SIMPLE_MIRROR_SHADERTAG_ARRAY = { "SimpleMirrorCaster" };
        private readonly RenderObjectPass _simpleMirrorOpaquePass;
        private readonly RenderObjectPass _simpleMirrorTransparentPass;

        // GallopRenderer..ctor (JP 0x7ffd59608930, code_JP GallopRenderer.c ~l.1486-1535):
        //   +0x328 stencil = StencilState.defaultValue, enabled false, compare 8 (Always), pass/fail/zfail 0 (Keep)
        //   +0x1d0 DrawObjectsPass(tag, true,  0xfa  /*250*/, RenderQueueRange.opaque,      LayerMask -1, +0x328, 0)
        //   +0x1e8 DrawObjectsPass(tag, false, 0x1c2 /*450*/, RenderQueueRange.transparent, LayerMask -1, +0x328, 0)
        // The tag strings (DAT_7ffd5bb85a48 / DAT_7ffd5bb861a0) are runtime pointers and unused by the native ctor.
        private readonly DrawObjectsPass _renderOpaqueForwardPass;
        private readonly DrawObjectsPass _renderTransparentForwardPass;

        // GallopRenderer..ctor (JP 0x7ffd59608930): +0x1f0 = UIRenderingPass(0x1c2 /*450*/), drawn instead
        // of the DrawObjectsPass pair when CameraData._isUIRendering (+0x148) is set (Setup ~l.1170-1178).
        private readonly UIRenderingPass _uiRenderingPass;

        // GallopRenderer..ctor (JP 0x7ffd59608930, code_JP GallopRenderer.c ~l.1545-1549): +0x218 =
        // SetInvertCullingPass(0, true) and +0x220 = SetInvertCullingPass(1000, false), enqueued by Setup
        // when CameraData.IsInvertCulling (+0xda) || CameraData.IsMirrorRendering (+0xd9).
        private readonly SetInvertCullingPass _setInvertCullingOnPass;
        private readonly SetInvertCullingPass _setInvertCullingOffPass;

        public GallopRenderer(UniversalRendererData data) : base(data)
        {
            _simpleMirrorOpaquePass = new RenderObjectPass((RenderPassEvent)0xfa, SIMPLE_MIRROR_SHADERTAG_ARRAY, true);
            _simpleMirrorTransparentPass = new RenderObjectPass((RenderPassEvent)0x1c2, SIMPLE_MIRROR_SHADERTAG_ARRAY, false);
            _uiRenderingPass = new UIRenderingPass((RenderPassEvent)0x1c2);
            _setInvertCullingOnPass = new SetInvertCullingPass((RenderPassEvent)0, true);
            _setInvertCullingOffPass = new SetInvertCullingPass((RenderPassEvent)1000, false);

            StencilState defaultStencilState = StencilState.defaultValue;
            defaultStencilState.enabled = false;
            defaultStencilState.SetCompareFunction(CompareFunction.Always);
            defaultStencilState.SetPassOperation(StencilOp.Keep);
            defaultStencilState.SetFailOperation(StencilOp.Keep);
            defaultStencilState.SetZFailOperation(StencilOp.Keep);
            _renderOpaqueForwardPass = new DrawObjectsPass(null, true, (RenderPassEvent)0xfa,
                RenderQueueRange.opaque, -1, defaultStencilState, 0);
            _renderTransparentForwardPass = new DrawObjectsPass(null, false, (RenderPassEvent)0x1c2,
                RenderQueueRange.transparent, -1, defaultStencilState, 0);
        }

        // GallopRenderer.Setup (JP code_JP GallopRenderer.c, line ~810):
        //   isSimpleMirror = CameraData.MirrorCasterQuality (+0xdc) == 1 && CameraData.IsMirrorRendering (+0xd9)
        //   isSimpleMirror == false -> DrawObjectsPass(opaque) / OpaqueBlit / Skybox / DrawObjectsPass(transparent)
        //   isSimpleMirror == true  -> RenderObjectPass(+0x228).Setup/Enqueue, RenderObjectPass(+0x230).Setup/Enqueue
        // The renderer features (PostImageEffectFeature: SetSourceRTPass -> BlurOptimizePass on the mirror camera) run
        // in both cases. The viewer renderer is URP's UniversalRenderer, so its object draws are swapped after
        // base.Setup: in the simple-mirror case URP's DrawObjectsPass pair (and any URP RenderObjects feature pass)
        // leave the queue and the RenderObjectPass pair takes their place; otherwise SetupDrawObjectsPasses puts the
        // ported Gallop DrawObjectsPass pair in the slots of URP's pair.
        public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            base.Setup(context, ref renderingData);

            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
                return;
            camera.TryGetComponent(out CameraData gallopData);

            // GallopRenderer.Setup (JP code_JP GallopRenderer.c ~l.1075-1080), main path right after the
            // SetBeforeRenderingPass / SetAfterRenderingPass setups: when CameraData.IsInvertCulling (+0xda)
            // || CameraData.IsMirrorRendering (+0xd9), enqueue the SetInvertCullingPass pair (events 0 / 1000)
            // so the whole camera render runs with inverted triangle culling (mirror cameras always do; the
            // planar reflection matrix flips the handedness). Both the normal and the simple-mirror object
            // draws are covered; the game's RT-format==1 early path (depth-format target texture) never
            // enqueues them and no viewer camera takes that path.
            if (gallopData != null && (gallopData.IsInvertCulling || gallopData.IsMirrorRendering))
            {
                if (!activeRenderPassQueue.Contains(_setInvertCullingOnPass))
                    EnqueuePass(_setInvertCullingOnPass);
                if (!activeRenderPassQueue.Contains(_setInvertCullingOffPass))
                    EnqueuePass(_setInvertCullingOffPass);
            }

            if (gallopData == null || !gallopData.IsMirrorRendering ||
                gallopData.MirrorCasterQuality != CameraData.MirrorQuality.Simple)
            {
                SetupDrawObjectsPasses(gallopData);
                return;
            }

            activeRenderPassQueue.RemoveAll(pass =>
                pass is UnityEngine.Rendering.Universal.Internal.DrawObjectsPass ||
                pass is UnityEngine.Experimental.Rendering.Universal.RenderObjectsPass);

            if (_simpleMirrorOpaquePass.Setup(gallopData))
                EnqueuePass(_simpleMirrorOpaquePass);
            if (_simpleMirrorTransparentPass.Setup(gallopData))
                EnqueuePass(_simpleMirrorTransparentPass);
        }

        // GallopRenderer.Setup, isSimpleMirror == false (code_JP GallopRenderer.c ~l.1148-1178):
        //   CameraData._isUIRendering (+0x148) == false:
        //     DrawObjectsPass +0x1d0 .Setup(cameraData) -> EnqueuePass; OpaqueBlitPass; DrawSkyboxPass;
        //     DrawObjectsPass +0x1e8 .Setup(cameraData) -> EnqueuePass
        //   CameraData._isUIRendering (+0x148) == true: UIRenderingPass +0x1f0 instead (ported below).
        // The viewer renderer is URP's UniversalRenderer: after base.Setup each URP DrawObjectsPass of the queue is
        // replaced in place (same slot, so the order against the other passes of the same event is kept) by the ported
        // DrawObjectsPass of the same queue range. URP's opaque pass runs at BeforeRenderingOpaques (250) and the
        // transparent one at BeforeRenderingTransparents (450), the native events.
        // VIEWER ADAPTATION (reported): every native camera has a Gallop CameraData (Setup dereferences it); viewer
        // cameras without one (model viewer, scene view) still get the ported pass, with the ctor filter (rendering
        // layer mask 0xffffffff) since there is no CameraData to read.
        private void SetupDrawObjectsPasses(CameraData gallopData)
        {
            // GallopRenderer.Setup (JP code_JP GallopRenderer.c ~l.1148-1178), CameraData._isUIRendering
            // (+0x148) branch: UIRenderingPass +0x1f0 .Setup(cameraData) -> EnqueuePass takes the place of the
            // DrawObjectsPass pair (the game also skips OpaqueBlitPass and the +0x1e0 pass there); when Setup
            // returns false the game enqueues nothing for the object draws. The previous viewer behaviour
            // (leaving URP's stock passes in place for UI rendering cameras) is replaced by the ported pass.
            if (gallopData != null && gallopData.IsUIRendering)
            {
                activeRenderPassQueue.RemoveAll(pass =>
                    pass is UnityEngine.Rendering.Universal.Internal.DrawObjectsPass ||
                    pass is UnityEngine.Experimental.Rendering.Universal.RenderObjectsPass);
                if (_uiRenderingPass.Setup(gallopData))
                    EnqueuePass(_uiRenderingPass);
                return;
            }

            bool opaqueEnabled = gallopData == null || _renderOpaqueForwardPass.Setup(gallopData);
            bool transparentEnabled = gallopData == null || _renderTransparentForwardPass.Setup(gallopData);

            for (int i = activeRenderPassQueue.Count - 1; i >= 0; i--)
            {
                if (!(activeRenderPassQueue[i] is UnityEngine.Rendering.Universal.Internal.DrawObjectsPass urpPass))
                    continue;

                bool isOpaque = urpPass.renderPassEvent < RenderPassEvent.BeforeRenderingTransparents;
                DrawObjectsPass replacement = isOpaque ? _renderOpaqueForwardPass : _renderTransparentForwardPass;
                bool enabled = isOpaque ? opaqueEnabled : transparentEnabled;
                if (enabled && !activeRenderPassQueue.Contains(replacement))
                    activeRenderPassQueue[i] = replacement;
                else
                    activeRenderPassQueue.RemoveAt(i);
            }
        }

        public override void SetupCullingParameters(ref ScriptableCullingParameters cullingParameters,
            ref UnityEngine.Rendering.Universal.CameraData cameraData)
        {
            // URP defaults first (visible light count, enclosing sphere); the native values are applied on top.
            base.SetupCullingParameters(ref cullingParameters, ref cameraData);

            float distance = CameraData.GlobalShadowDistance;
            Camera camera = cameraData.camera;
            // The native camera options come from the Gallop CameraData of the camera. The viewer only attaches it to
            // the cameras that carry the image effect; any other camera keeps URP's own renderShadows decision.
            if (camera != null && camera.TryGetComponent(out CameraData gallopData))
            {
                // Native cullingOptions start with ShadowCasters on; URP may have removed it for its own distance.
                cullingParameters.cullingOptions |= CullingOptions.ShadowCasters;

                var additional = camera.GetUniversalAdditionalCameraData();
                if (additional != null && !additional.renderShadows)
                    distance = 0.0f;
                else if (gallopData.MaxShadowDistance > 0.0f)
                    distance = gallopData.MaxShadowDistance;

                cullingParameters.cullingOptions &= ~CullingOptions.OcclusionCull;
                cullingParameters.cullingOptions &= ~CullingOptions.NeedsReflectionProbes;
                if (gallopData.IsUIRendering)
                {
                    cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling;
                    cullingParameters.cullingOptions &= CullingOptions.NeedsLighting;
                }
                if (gallopData.IsDisableObjectCulling)
                    cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling;
                if (gallopData.IsDisableLightCulling)
                    cullingParameters.cullingOptions &= ~CullingOptions.NeedsLighting;
            }
            else if (cameraData.maxShadowDistance <= 0.0f)
            {
                distance = 0.0f;
            }

            // The user's "shadow distance" setting stays an upper bound of the viewer.
            distance = Mathf.Min(distance, RuntimeGraphicsSettings.UserShadowDistance);

            var asset = UniversalRenderPipeline.asset;
            bool shadowsUnsupported = asset != null && !asset.supportsMainLightShadows && !asset.supportsAdditionalLightShadows;
            if (Mathf.Approximately(distance, 0.0f) || shadowsUnsupported)
                cullingParameters.cullingOptions &= ~CullingOptions.ShadowCasters;

            cameraData.maxShadowDistance = distance;
            cullingParameters.shadowDistance = distance;
        }
    }
}
