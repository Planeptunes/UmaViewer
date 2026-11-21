using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Port of Gallop.RenderPipeline.SetInvertCullingPass (JP code_JP Gallop.RenderPipeline.SetInvertCullingPass.c).
    // GallopRenderer..ctor (JP 0x7ffd59608930) builds exactly two:
    //   +0x218 = SetInvertCullingPass(renderPassEvent 0,    invertCulling true)
    //   +0x220 = SetInvertCullingPass(renderPassEvent 1000, invertCulling false)
    // and GallopRenderer.Setup (JP 0x7ffd59607450) enqueues BOTH when
    // CameraData.IsInvertCulling (+0xda) || CameraData.IsMirrorRendering (+0xd9): the mirror camera render
    // (planar reflection matrix = flipped handedness) draws with inverted triangle culling for the whole
    // camera, restored by the event-1000 pass. This replaces the viewer's old GL.invertCulling workaround.
    public class SetInvertCullingPass : ScriptableRenderPass
    {
        // VIEWER ADAPTATION: the native ProfilingSampler / CommandBufferPool name (DAT_7ffd5bb20b40) is a
        // runtime string pointer (not readable from the dumped DLL); the class name labels it here.
        private const string PROFILER_TAG = "SetInvertCullingPass";

        // +0xe0: the value written by CommandBuffer.SetInvertCulling and to the global shader float.
        private readonly bool _invertCulling;

        // SetInvertCullingPass..ctor (JP 0x7ffd59613c10): renderPassEvent = param_2 (+0x10), flag = param_3 (+0xe0).
        public SetInvertCullingPass(RenderPassEvent evt, bool invertCulling)
        {
            renderPassEvent = evt;
            _invertCulling = invertCulling;
        }

        // SetInvertCullingPass.Execute (JP 0x7ffd59613ae0):
        //   cmd.SetInvertCulling(invertCulling)
        //   cmd.SetGlobalFloat(ShaderManager.GetPropertyId(0xf) /* _GlobalInvertCulling */,
        //                      invertCulling ? 1.0f /* DAT_7ffd5a640d6c */ : 0.0f)
        //   context.ExecuteCommandBuffer(cmd); pool release.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(PROFILER_TAG);
            cmd.SetInvertCulling(_invertCulling);
            cmd.SetGlobalFloat(
                ShaderManager.GetPropertyId(ShaderManager.PropertyId._GlobalInvertCulling),
                _invertCulling ? 1.0f : 0.0f);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
