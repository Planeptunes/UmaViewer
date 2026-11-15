using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Port of Gallop.RenderPipeline.DepthOnlyPass (JP): ctor 0x7ffd595def60, Setup 0x7ffd595dee70,
    /// OnCameraSetup 0x7ffd595dec50, Execute 0x7ffd595de9a0, ResetState 0x7ffd58933ee0.
    ///
    /// The client does NOT copy URP's depth: GallopRenderer.Setup schedules this pass (event 0x96 = 150) when any
    /// effect requests the depth texture, and it fills `_CameraDepthTexture` by drawing the opaque renderers
    /// (RenderQueueRange.opaque, every layer) with the SHADOWCASTER pass (RenderUtils.SHADOWCASTER_SHADERTAG)
    /// into a 32-bit depth target (msaa 1, RenderTextureFormat.Depth), cleared with ClearFlag 7, and then
    /// publishes it with SetGlobalTexture(handle id, handle). Nothing else writes the depth the GlobalFog,
    /// DOF, Vortex... shaders read.
    /// </summary>
    public sealed class DepthOnlyPass : ScriptableRenderPass
    {
        // RenderUtils.SHADOWCASTER_SHADERTAG (the ctor stores it at +0x15c)
        private readonly ShaderTagId _shaderTagId = new ShaderTagId("SHADOWCASTER");
        private readonly ProfilingSampler _profilingSampler = new ProfilingSampler("DepthOnlyPass");
        private FilteringSettings _filteringSettings;       // +0x144
        private int _depthAttachmentId = -1;                // depthAttachmentHandle (+0xe0), RenderTargetHandle.id
        private RenderTextureDescriptor _descriptor;        // +0x110
        private bool _isAllocated;                          // +0x160
        private bool _needRealloc;                          // +0x161

        public int DepthAttachmentId => _depthAttachmentId;
        // ctor(RenderPassEvent event, RenderQueueRange range, LayerMask layerMask): the native call is
        // DepthOnlyPass(0x96, RenderQueueRange.opaque, 0xffffffff). The renderingLayerMask argument is 0xffffffff.
        public DepthOnlyPass(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            _filteringSettings = new FilteringSettings(renderQueueRange, layerMask, 0xffffffff, 0);
            renderPassEvent = evt;
        }

        // Setup(CameraData gallopCameraData, ref RenderTextureDescriptor baseDescriptor, RenderTargetHandle depthHandle):
        // depth format / 32 bits / msaa 1, the filtering rendering layer mask is CameraData.ShadowCasterRenderingLayerMask
        // (0xffffffff by default in the CameraData ctor), and a new allocation is needed when the target is not
        // allocated yet or the size changed.
        public void Setup(uint shadowCasterRenderingLayerMask, int depthAttachmentId, RenderTextureDescriptor baseDescriptor)
        {
            _depthAttachmentId = depthAttachmentId;
            baseDescriptor.colorFormat = RenderTextureFormat.Depth;
            baseDescriptor.depthBufferBits = 32;
            _filteringSettings.renderingLayerMask = shadowCasterRenderingLayerMask;
            baseDescriptor.msaaSamples = 1;
            _needRealloc = false;
            if (!_isAllocated || _descriptor.width != baseDescriptor.width || _descriptor.height != baseDescriptor.height)
                _needRealloc = true;
            _descriptor = baseDescriptor;
        }

        // ResetState: the target is considered not allocated (the renderer calls it from its Setup).
        public void ResetState() { _isAllocated = false; }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (_needRealloc)
            {
                if (_isAllocated)
                    cmd.ReleaseTemporaryRT(_depthAttachmentId);
                cmd.GetTemporaryRT(_depthAttachmentId, _descriptor, FilterMode.Point);
            }
            _isAllocated = true;
            ConfigureTarget(new RenderTargetIdentifier(_depthAttachmentId, 0, CubemapFace.Unknown, -1));
            // ClearFlag 7 (all). The clear colour constant (0x7ffd5a651e00) is not readable in the decompile;
            // only the depth clear matters for a depth target.
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                SortingCriteria sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, ref renderingData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref _filteringSettings);
            }
            cmd.SetGlobalTexture(_depthAttachmentId, new RenderTargetIdentifier(_depthAttachmentId, 0, CubemapFace.Unknown, -1));
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    /// <summary>
    /// Port of Gallop.RenderPipeline.DepthReleasePass (JP): ctor 0x7ffd595df390, Setup 0x7ffd595df360,
    /// OnCameraCleanup 0x7ffd595df1c0, Execute empty. Releases the depth target of DepthOnlyPass when the camera ends.
    /// </summary>
    public sealed class DepthReleasePass : ScriptableRenderPass
    {
        private int _depthAttachmentId = -1;  // depthAttachmentHandle (+0xe0); -1 = RenderTargetHandle.CameraTarget

        public DepthReleasePass(RenderPassEvent evt) { renderPassEvent = evt; }

        public void Setup(int depthAttachmentId) { _depthAttachmentId = depthAttachmentId; }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null) return;
            if (_depthAttachmentId != -1)
            {
                cmd.ReleaseTemporaryRT(_depthAttachmentId);
                _depthAttachmentId = -1;
            }
        }
    }
}
