using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Native <c>Gallop.RenderPipeline.LensFlareRenderPass</c>: the lens flares are drawn AFTER the camera's
    /// DOF / Diffusion / Bloom / ChromaticAberration chain and BEFORE the colour correction, so they are
    /// never blurred by the DOF nor bloomed (JP probe, live 1053 and 1034: per-camera pass chain
    /// ... DofDiffusionBloomOverlay, ChromaticAberration, ImageEffectBlit, InvokeOnRenderObjectCallback,
    /// LensFlareRender, ColorCorrection ...). The viewer used to draw the flare quads as scene meshes,
    /// i.e. before the whole post chain, which made the DOF blur the rays of the flare.
    ///
    /// The flare quads (CustomLensFlare) live on <see cref="CustomLensFlare.PassLayer"/>, which the
    /// presentation cameras do not render; this pass culls that layer itself with the camera's own
    /// culling parameters and draws it additively onto the chain's current colour (in place).
    /// </summary>
    public sealed class LensFlareRenderPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId UnlitTag = new ShaderTagId("SRPDefaultUnlit");

        private PostImageEffectFeature _feature;
        // True when no image-effect chain runs this frame: draw straight onto the camera colour.
        private bool _standalone;

        public LensFlareRenderPass(RenderPassEvent evt) { renderPassEvent = evt; }

        public bool Setup(PostImageEffectFeature feature, bool standalone)
        {
            _feature = feature;
            _standalone = standalone;
            return CustomLensFlare.PassMode && feature != null &&
                   (Config.Instance == null || Config.Instance.PostEffects) &&
                   PostEffectSelection.IsEnabled(PostEffectId.LensFlare);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature == null) return;
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || !camera.TryGetCullingParameters(out var parameters)) return;

            uint layerBit = 1u << CustomLensFlare.PassLayer;
            parameters.cullingMask = layerBit;
            CullingResults flares = context.Cull(ref parameters);

            var cmd = CommandBufferPool.Get("Gallop.LensFlareRender");
            try
            {
                var renderer = renderingData.cameraData.renderer;
                RTHandle cameraColor = renderer != null ? renderer.cameraColorTargetHandle : null;
                if (cameraColor == null) return;
                RenderTargetIdentifier cameraColorId = cameraColor.nameID;

                if (!_standalone && (int)renderPassEvent > (int)RenderPassEvent.BeforeRenderingPostProcessing)
                    _feature.ResetSourceRT(cmd, ref renderingData);

                RenderTargetIdentifier target = cameraColorId;
                bool intoCameraColor = true;
                if (!_standalone)
                {
                    var source = _feature.SourceRT;
                    if (source.NameId != -2)
                    {
                        target = source.RtId;
                        intoCameraColor = source.RtId.Equals(cameraColorId);
                    }
                }

                // Draw onto the chain's current colour, no depth (the native shader is ZTest Always).
                cmd.SetRenderTarget(target);
                // Orientation: into the camera colour the camera's own flip applies. Into the chain's temporaries
                // BOTH `true` (first version) and the camera's flip (which is true here) drew the flares upside
                // down, so the temporaries take the unflipped GPU projection.
                bool flipped = intoCameraColor && renderingData.cameraData.IsCameraProjectionMatrixFlipped();
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(renderingData.cameraData.GetProjectionMatrix(), flipped);
                cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), projection);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings drawing = CreateDrawingSettings(UnlitTag, ref renderingData, SortingCriteria.CommonTransparent);
                drawing.perObjectData = PerObjectData.None;
                FilteringSettings filter = new FilteringSettings(RenderQueueRange.all, (int)layerBit);
                context.DrawRenderers(flares, ref drawing, ref filter);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }
}
