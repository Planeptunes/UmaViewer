using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Port of Gallop.RenderPipeline.RenderObjectPass (JP code_JP Gallop.RenderPipeline.RenderObjectPass.c).
    // GallopRenderer builds two of them in its ctor with a single shader tag (SimpleMirrorCaster):
    //   opaque      = RenderObjectPass(0xfa  /*250*/, tags, isOpaque: true)
    //   transparent = RenderObjectPass(0x1c2 /*450*/, tags, isOpaque: false)
    // and draws with them instead of the DrawObjectsPass pair when the camera renders a simple mirror.
    public class RenderObjectPass : ScriptableRenderPass
    {
        private const string PROFILER_TAG = "RenderObjectPass";

        // +0xe8: true -> opaque queue range and the camera's default opaque sort flags.
        private readonly bool _isOpaque;
        // +0xe0: List<ShaderTagId> built from the ctor's string array.
        private readonly List<ShaderTagId> _shaderTagIdList;
        // +0xec: FilteringSettings(range, layerMask 0xffffffff, renderingLayerMask 0xffffffff, excludeMotionVectorObjects false).
        private FilteringSettings _filteringSettings;
        private readonly ProfilingSampler _profilingSampler = new ProfilingSampler(PROFILER_TAG);

        // RenderObjectPass..ctor (JP 0x7ffd595fb430).
        public RenderObjectPass(RenderPassEvent evt, string[] shaderTags, bool isOpaque)
        {
            renderPassEvent = evt;
            _isOpaque = isOpaque;
            _shaderTagIdList = new List<ShaderTagId>(shaderTags.Length);
            for (int i = 0; i < shaderTags.Length; i++)
                _shaderTagIdList.Add(new ShaderTagId(shaderTags[i]));
            RenderQueueRange range = isOpaque ? RenderQueueRange.opaque : RenderQueueRange.transparent;
            _filteringSettings = new FilteringSettings(range, -1, 0xffffffffu, 0);
        }

        // RenderObjectPass.Setup (JP 0x7ffd595fb400): filter rendering layer mask = CameraData.RenderingLayerMask (+0xd0).
        public bool Setup(CameraData cameraData)
        {
            _filteringSettings.renderingLayerMask = cameraData.RenderingLayerMask;
            return true;
        }

        // RenderObjectPass.Execute (JP 0x7ffd595fb260): layer mask = camera.cullingMask; sort = defaultOpaqueSortFlags
        // for the opaque pass, 0x17 (SortingCriteria.CommonTransparent) for the transparent one.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
                return;

            _filteringSettings.layerMask = camera.cullingMask;
            SortingCriteria sortFlags = _isOpaque
                ? renderingData.cameraData.defaultOpaqueSortFlags
                : SortingCriteria.CommonTransparent;
            DrawingSettings drawingSettings = CreateDrawingSettings(_shaderTagIdList, ref renderingData, sortFlags);

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filteringSettings);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
