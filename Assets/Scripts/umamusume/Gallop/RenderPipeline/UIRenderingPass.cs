using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Port of Gallop.RenderPipeline.UIRenderingPass (JP code_JP Gallop.RenderPipeline.UIRenderingPass.c):
    //   ctor 0x7ffd596169b0 / Dispose 0x7ffd596160c0 / Setup 0x7ffd59616900 /
    //   InitializeSortingLayer 0x7ffd59616550 / Execute 0x7ffd59616110 / OnRendering 0x7ffd59616690.
    // GallopRenderer..ctor builds one at +0x1f0 as UIRenderingPass(0x1c2 /*450*/), and GallopRenderer.Setup
    // draws ONLY this pass (instead of the DrawObjectsPass pair; OpaqueBlitPass and the +0x1e0 pass are also
    // skipped there) when CameraData._isUIRendering (+0x148) is set on the camera.
    public class UIRenderingPass : ScriptableRenderPass
    {
        // VIEWER ADAPTATION: the native POOL_NAME (DAT_7ffd5bbd58c8, ProfilingSampler + CommandBufferPool
        // name) is a runtime string pointer (not readable from the dumped DLL); the class name labels it here.
        private const string POOL_NAME = "UIRenderingPass";

        // DATA GAP (rule 8.1) - UIRenderingPass.SHADER_PASS_NAME (static String, JP static field read by
        // Setup at 0x7ffd59616900): the il2cpp string literal backing it is runtime metadata and is not
        // present in the dumped GameAssembly/dump_jp.cs, so its value cannot be recovered offline yet.
        // Setup passes it to RenderUtils.CheckAndCreateMaterialFromBinary (Shader.Find) and returns false
        // when the shader cannot be found - exactly the game's behaviour - so the pass does not draw until
        // the literal is recovered (see the report for how to obtain it) and set here.
        private static readonly string SHADER_PASS_NAME = string.Empty;

        // +0xe0: camera data of the camera being rendered (set by Setup).
        private CameraData _cameraData;
        // +0xe8: material created from SHADER_PASS_NAME; only consumed by the BlurPanelManager branch of
        // Execute (BlurPanelManager.OnBlurRendering), which is not reachable in the viewer (see Execute).
        private Material _material;
        // +0xf0: Dictionary<int, short> sortingLayer.id -> sortingLayer.value, built from SortingLayer.layers
        // (InitializeSortingLayer); iterated by the BlurPanelManager branch of Execute.
        private Dictionary<int, short> _sortingLayerDictionary;
        // +0xf8: List<ShaderTagId> copy of RenderUtils.UI_SHADERTAG_ARRAY (RenderUtils statics +0x38).
        private readonly List<ShaderTagId> _uiShaderTagIdList;

        // UIRenderingPass..ctor (JP 0x7ffd596169b0): base ctor, profiling sampler (POOL_NAME),
        // renderPassEvent = param_2 (+0x10), sorting-layer dictionary from SortingLayer.layers and
        // _uiShaderTagIdList = List<ShaderTagId>(RenderUtils UI tag array).
        public UIRenderingPass(RenderPassEvent evt)
        {
            renderPassEvent = evt;
            InitializeSortingLayer();
            _uiShaderTagIdList = new List<ShaderTagId>(RenderUtils.UI_SHADERTAG_ARRAY);
        }

        // UIRenderingPass.Dispose (JP 0x7ffd596160c0).
        public void Dispose()
        {
            RenderUtils.Destroy(ref _material);
        }

        // UIRenderingPass.Setup (JP 0x7ffd59616900): clears the camera data, creates the material from
        // SHADER_PASS_NAME (once) and stores the camera data; false when the material cannot be created.
        public bool Setup(CameraData cameraData)
        {
            _cameraData = null;
            if (!RenderUtils.CheckAndCreateMaterialFromBinary(ref _material, SHADER_PASS_NAME))
                return false;
            _cameraData = cameraData;
            return true;
        }

        // UIRenderingPass.InitializeSortingLayer (JP 0x7ffd59616550): _sortingLayerDictionary
        // (Dictionary<int, short>) filled from SortingLayer.layers: [layer.id] = layer.value.
        public void InitializeSortingLayer()
        {
            SortingLayer[] layers = SortingLayer.layers;
            if (_sortingLayerDictionary == null)
                _sortingLayerDictionary = new Dictionary<int, short>(layers.Length);
            else
                _sortingLayerDictionary.Clear();
            for (int i = 0; i < layers.Length; i++)
                _sortingLayerDictionary[layers[i].id] = (short)layers[i].value; // game dictionary is Dictionary<int,short>
        }

        // UIRenderingPass.Execute (JP 0x7ffd59616110). The game has two paths here:
        //   - BlurPanelManager.Instance.PanelNum >= 1: iterate _sortingLayerDictionary and draw each
        //     sorting layer through BlurPanelManager.OnBlurRendering (manager +0x48 chain), falling back to
        //     OnRendering per layer when the panel manager does not handle it.
        //   - PanelNum < 1 (no blur panels): the simple path ported below.
        // The viewer has no Gallop.RenderPipeline.BlurPanelManager port (its UI blur-panel registration and
        // consumers are out of scope), so the game's own PanelNum < 1 path always runs; the material above
        // is only used by the unported branch.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
                return;

            int cullingMask = camera.cullingMask;
            CommandBuffer cmd = CommandBufferPool.Get(POOL_NAME);
            if (_cameraData != null)
            {
                // SortingLayerRange.all: the game reads get_lowerBound / get_upperBound of SortingLayerRange.all
                // for the simple path.
                short lowerBound = short.MinValue;
                short upperBound = short.MaxValue;
                // CameraData.<IsOpaqueRenderingForUICamera>k__BackingField (+0x149): when set, an extra
                // opaque-queue pass runs before the transparent one.
                if (_cameraData.IsOpaqueRenderingForUICamera)
                    OnRendering(context, ref renderingData, cmd, lowerBound, upperBound, cullingMask, true);
                OnRendering(context, ref renderingData, cmd, lowerBound, upperBound, cullingMask, false);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // UIRenderingPass.OnRendering (JP 0x7ffd59616690): one DrawRenderers over _uiShaderTagIdList.
        // drawOpaque -> RenderQueueRange.opaque + cameraData.defaultOpaqueSortFlags, else
        // RenderQueueRange.transparent + 0x17 (SortingCriteria.CommonTransparent);
        // FilteringSettings(range, layerMask: cullingMask, renderingLayerMask: 0xffffffff,
        // excludeMotionVectorObjects: false) with sortingLayerRange = [lowerBound, upperBound].
        private void OnRendering(ScriptableRenderContext context, ref RenderingData renderingData,
            CommandBuffer cmd, short lowerBound, short upperBound, int cullingMask, bool drawOpaque)
        {
            SortingCriteria sortFlags = drawOpaque
                ? renderingData.cameraData.defaultOpaqueSortFlags
                : SortingCriteria.CommonTransparent;
            DrawingSettings drawingSettings =
                CreateDrawingSettings(_uiShaderTagIdList, ref renderingData, sortFlags);

            RenderQueueRange range = drawOpaque ? RenderQueueRange.opaque : RenderQueueRange.transparent;
            FilteringSettings filteringSettings = new FilteringSettings(range, cullingMask, 0xffffffffu, 0);
            filteringSettings.sortingLayerRange = new SortingLayerRange(lowerBound, upperBound);

            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
    }
}
