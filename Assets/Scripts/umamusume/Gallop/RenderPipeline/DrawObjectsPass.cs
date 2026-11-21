using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Port of Gallop.RenderPipeline.DrawObjectsPass (JP code_JP Gallop.RenderPipeline.DrawObjectsPass.c):
    ///   .ctor(string, ShaderTagId[], ShaderTagId[], bool, RenderPassEvent, RenderQueueRange, LayerMask, StencilState, int)
    ///         RVA 0x5267850 (0x7ffd595e7850)
    ///   .ctor(string, bool, RenderPassEvent, RenderQueueRange, LayerMask, StencilState, int)
    ///         RVA 0x5267760 (0x7ffd595e7760) -> RenderUtils.DEFAULT_SHADERTAG_ARRAY / FORWARD_SHADERTAG_ARRAY
    ///   Setup(CameraData)  RVA 0x52676A0 (0x7ffd595e76a0)
    ///   Execute            RVA 0x5267200 (0x7ffd595e7200)
    ///   .cctor             RVA 0x52676E0 (0x7ffd595e76e0)
    /// GallopRenderer..ctor builds DrawObjectsPass(tag, true, 0xfa, opaque, -1, stencil, 0) at +0x1d0 and
    /// DrawObjectsPass(tag, false, 0x1c2, transparent, -1, stencil, 0) at +0x1e8. Unlike URP's DrawObjectsPass it draws
    /// twice: the "before" list (SRPDefaultUnlit) and then the "after" list (PreUniversalForward, UniversalForward,
    /// AfterUniversalForward, UniversalForwardOnly, Outline). The second list is what draws the inverted-hull Outline
    /// pass (LightMode "Outline") of the character toon shaders.
    /// </summary>
    public class DrawObjectsPass : ScriptableRenderPass
    {
        // .cctor: s_DrawObjectPassDataPropID = Shader.PropertyToID(literal). The literal is a runtime string pointer
        // (DAT_7ffd5bb4e078, not in the dumped DLL); the class is URP's DrawObjectsPass with the same field names
        // (m_FilteringSettings, m_RenderStateBlock, m_IsOpaque, s_DrawObjectPassDataPropID, m_ScaleBias), whose literal
        // is "_DrawObjectPassData".
        private static readonly int s_DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");
        // .cctor: DrawObjectPassData_Opaque (static +0x4) = constant 0x7ffd5a651e00 = (0, 0, 0, 1) (Ghidra read);
        // DrawObjectPassData (static +0x14) = (0, 0, 0, 0).
        private static readonly Vector4 DrawObjectPassData_Opaque = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        private static readonly Vector4 DrawObjectPassData = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        // Execute: the scale-bias id is ShaderPropertyIdInternal.scaleBiasRt (static +0x0), copied in its .cctor from
        // URP's ShaderPropertyId.scaleBiasRt ("_ScaleBiasRt").
        private static readonly int s_ScaleBiasRtPropID = Shader.PropertyToID("_ScaleBiasRt");

        private FilteringSettings m_FilteringSettings;                 // +0xE0
        private RenderStateBlock m_RenderStateBlock;                   // +0xF8
        private readonly List<ShaderTagId> m_ShaderTagIdBeforeList;    // +0x168
        private readonly List<ShaderTagId> m_ShaderTagIdAfterList;     // +0x170
        private readonly bool m_IsOpaque;                              // +0x178
        // +0x17C: initialised in the ctor from Gallop.Math.VECTOR4_ONE (static +0x58); Execute rewrites x, y, z.
        private Vector4 m_ScaleBias;

        // .ctor RVA 0x5267760: the two tag lists are RenderUtils.DEFAULT_SHADERTAG_ARRAY (+0x40) and
        // RenderUtils.FORWARD_SHADERTAG_ARRAY (+0x48).
        public DrawObjectsPass(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange,
            LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(profilerTag, RenderUtils.DEFAULT_SHADERTAG_ARRAY, RenderUtils.FORWARD_SHADERTAG_ARRAY, opaque, evt,
                renderQueueRange, layerMask, stencilState, stencilReference)
        {
        }

        // .ctor RVA 0x5267850. The profiler tag argument is not stored by the native ctor.
        public DrawObjectsPass(string profilerTag, ShaderTagId[] shaderTagIdsBefore, ShaderTagId[] shaderTagIdsAfter,
            bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask,
            StencilState stencilState, int stencilReference)
        {
            m_ShaderTagIdBeforeList = new List<ShaderTagId>(shaderTagIdsBefore);
            m_ShaderTagIdAfterList = new List<ShaderTagId>(shaderTagIdsAfter);
            m_ScaleBias = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            renderPassEvent = evt;
            // FilteringSettings(renderQueueRange, layerMask, renderingLayerMask 0xffffffff, excludeMotionVectorObjects 0).
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask, 0xffffffffu, 0);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_IsOpaque = opaque;
            if (stencilState.enabled)
            {
                m_RenderStateBlock.stencilReference = stencilReference;
                m_RenderStateBlock.mask = RenderStateMask.Stencil;   // 8
                m_RenderStateBlock.stencilState = stencilState;
            }
        }

        // Setup RVA 0x52676A0: skipped when CameraData.IsSkipDrawObjectsPass (+0x100); otherwise the filter's rendering
        // layer mask is CameraData.RenderingLayerMask (+0xd0).
        public bool Setup(CameraData cameraData)
        {
            if (cameraData.IsSkipDrawObjectsPass)
                return false;
            m_FilteringSettings.renderingLayerMask = cameraData.RenderingLayerMask;
            return true;
        }

        // Execute RVA 0x5267200.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
            {
                CommandBufferPool.Release(cmd);
                return;
            }

            // Native: cmd.SetViewport(camera.pixelRect). VIEWER ADAPTATION (not a port, reported): the viewer renders
            // every camera into URP's intermediate colour attachment (IntermediateTextureMode Always), sized
            // cameraTargetDescriptor (pixelRect * renderScale, origin 0). camera.pixelRect would cover only part of that
            // attachment at a render scale != 1, so the same region is expressed in attachment pixels.
            RenderTextureDescriptor targetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            cmd.SetViewport(new Rect(0.0f, 0.0f, targetDescriptor.width, targetDescriptor.height));

            cmd.SetGlobalVector(s_DrawObjectPassDataPropID, m_IsOpaque ? DrawObjectPassData_Opaque : DrawObjectPassData);

            // flipped -> (-1, 1, -1, w), otherwise (1, 0, 1, w) (constants 0x7ffd5a640dac = -1, 0x7ffd5a640d6c = 1).
            if (renderingData.cameraData.IsCameraProjectionMatrixFlipped())
            {
                m_ScaleBias.x = -1.0f;
                m_ScaleBias.y = 1.0f;
                m_ScaleBias.z = -1.0f;
            }
            else
            {
                m_ScaleBias.x = 1.0f;
                m_ScaleBias.y = 0.0f;
                m_ScaleBias.z = 1.0f;
            }
            cmd.SetGlobalVector(s_ScaleBiasRtPropID, m_ScaleBias);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Opaque: cameraData.defaultOpaqueSortFlags (RenderingData +0x18c); transparent: 0x17 (CommonTransparent).
            SortingCriteria sortFlags = m_IsOpaque
                ? renderingData.cameraData.defaultOpaqueSortFlags
                : SortingCriteria.CommonTransparent;

            DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagIdBeforeList, ref renderingData, sortFlags);
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings, ref m_RenderStateBlock);

            drawingSettings = CreateDrawingSettings(m_ShaderTagIdAfterList, ref renderingData, sortFlags);
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings, ref m_RenderStateBlock);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
