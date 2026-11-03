using System;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // TransmittedLight (glow of the stage lights), reconstructed from the game's shaders:
    //  1) the stage light materials have a "TransmittedLightMask" LightMode pass (alpha blend,
    //     _TransmittedLightMaskScale): drawn with the scene depth into a mask RT;
    //  2) FastBloom pass 4 "DownSampleMask" (_MainTex = frame, _TransmittedMaskTex = mask,
    //     _Parameter.zw = threshold / intensity) down-samples the frame only where the lights are;
    //  3) FastBloom vertical / horizontal blur, Iterations times, spread from BlurSpread;
    //  4) FastBloom pass 0 "Bloom" (_Bloom, _BloomIsScreenBlend) composes it over the frame.
    // Parameters come from the TransmittedLight track (LiveTransmittedLightTimeline.Current).
    // Not recovered from game code (the datamined build predates it): the exact place in the
    // chain (here with the shafts, before DOF/Bloom), the blur spread scaling (same as the
    // viewer's FastBloom path) and the BlendMode mapping (2 = Add, otherwise Screen).
    public sealed class TransmittedLightPass : ScriptableRenderPass, IDisposable
    {
        private const int Divider = 4;
        private const float OneOverBaseSize = 1f / 512f;
        private const int PassBloom = 0, PassBlurVertical = 2, PassBlurHorizontal = 3, PassDownSampleMask = 4;

        private static readonly ShaderTagId MaskTag = new ShaderTagId("TransmittedLightMask");
        private static readonly int MaskId = Shader.PropertyToID("_RecoveredTransmittedLightMask");
        private static readonly int Low0 = Shader.PropertyToID("_RecoveredTransmittedLightLow0");
        private static readonly int Low1 = Shader.PropertyToID("_RecoveredTransmittedLightLow1");
        private static readonly int Output = Shader.PropertyToID("_RecoveredTransmittedLightOutput");
        private static readonly int MaskTexId = Shader.PropertyToID("_TransmittedMaskTex");
        private static readonly int ParameterId = Shader.PropertyToID("_Parameter");
        private static readonly int BloomId = Shader.PropertyToID("_Bloom");
        private static readonly int ScreenBlendId = Shader.PropertyToID("_BloomIsScreenBlend");

        private Material _material;
        private PostImageEffectFeature _feature;
        private LiveTransmittedLightTimeline.State _state;

        public TransmittedLightPass(RenderPassEvent evt) { renderPassEvent = evt; }

        public bool Setup(LiveTransmittedLightTimeline.State state, PostImageEffectFeature feature)
        {
            _state = state;
            _feature = feature;
            if (!state.IsEnable || state.Intensity <= 0f) return false;
            Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.FastBloom);
            if (shader == null || !shader.isSupported) return false;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            else _material.shader = shader;
            return _material.passCount > PassDownSampleMask;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature == null || _material == null || !_state.IsEnable) return;
            var renderer = renderingData.cameraData.renderer;
            var depth = renderer != null ? renderer.cameraDepthTargetHandle : null;
            if (depth == null) return;

            var cmd = CommandBufferPool.Get("Recovered.TransmittedLight");
            try
            {
                if ((int)renderPassEvent > (int)RenderPassEvent.BeforeRenderingPostProcessing)
                    _feature.ResetSourceRT(cmd, ref renderingData);
                var input = _feature.SourceRT;
                var camDesc = renderingData.cameraData.cameraTargetDescriptor;

                // 1) Mask of the stage lights, depth-tested against the scene.
                var maskDesc = camDesc;
                maskDesc.depthBufferBits = 0;
                maskDesc.colorFormat = RenderTextureFormat.ARGB32;
                maskDesc.bindMS = false;
                maskDesc.useMipMap = maskDesc.autoGenerateMips = false;
                cmd.GetTemporaryRT(MaskId, maskDesc, FilterMode.Bilinear);
                cmd.SetRenderTarget(new RenderTargetIdentifier(MaskId), depth.nameID);
                cmd.ClearRenderTarget(false, true, Color.clear);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                var drawing = CreateDrawingSettings(MaskTag, ref renderingData, SortingCriteria.CommonTransparent);
                drawing.perObjectData = PerObjectData.None;
                var filter = new FilteringSettings(RenderQueueRange.all, renderingData.cameraData.camera.cullingMask);
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);

                // 2) Masked, thresholded down-sample of the frame.
                var full = camDesc;
                full.depthBufferBits = 0; full.msaaSamples = 1; full.bindMS = false;
                full.useMipMap = full.autoGenerateMips = false;
                var low = full;
                low.width = Mathf.Max(1, full.width / Divider);
                low.height = Mathf.Max(1, full.height / Divider);
                cmd.GetTemporaryRT(Low0, low, FilterMode.Bilinear);
                cmd.GetTemporaryRT(Low1, low, FilterMode.Bilinear);
                cmd.GetTemporaryRT(Output, full, FilterMode.Bilinear);
                cmd.SetGlobalTexture(MaskTexId, MaskId);
                cmd.SetGlobalVector(ParameterId, new Vector4(0f, 0f, _state.Threshold, _state.Intensity));
                cmd.Blit(input.RtId, Low0, _material, PassDownSampleMask);

                // 3) Blur. The blur passes also read _Parameter.zw (pass 2 subtracts z from every
                // tap, pass 3 multiplies by w), so they get 0 / 1 like the game's bloom texture:
                // threshold and intensity are applied once, in DownSampleMask.
                float aspect = full.height > 0 ? (float)full.width / full.height : 1f;
                for (int i = 0; i < _state.Iterations; i++)
                {
                    float spread = _state.BlurSpread * (1f + i);
                    cmd.SetGlobalVector(ParameterId, new Vector4(spread / aspect * OneOverBaseSize,
                        spread * OneOverBaseSize, 0f, 1f));
                    cmd.Blit(Low0, Low1, _material, PassBlurVertical);
                    cmd.Blit(Low1, Low0, _material, PassBlurHorizontal);
                }

                // 4) Compose; the bloom globals are restored for the main Bloom pass.
                float previousScreenBlend = Shader.GetGlobalFloat(ScreenBlendId);
                cmd.SetGlobalTexture(BloomId, Low0);
                cmd.SetGlobalFloat(ScreenBlendId, _state.BlendMode == 2 ? 0f : 1f);
                cmd.Blit(input.RtId, Output, _material, PassBloom);
                cmd.SetGlobalFloat(ScreenBlendId, previousScreenBlend);
                cmd.SetGlobalTexture(MaskTexId, Texture2D.blackTexture);

                cmd.ReleaseTemporaryRT(Low0);
                cmd.ReleaseTemporaryRT(Low1);
                cmd.ReleaseTemporaryRT(MaskId);
                _feature.SetSourceRT(RenderTextureHandle.Make(Output, full.width, full.height), cmd, ref renderingData);
                context.ExecuteCommandBuffer(cmd);
                LiveRuntimeDiagnostics.RecordRenderExecution(renderingData.cameraData.camera,
                    "TransmittedLight", renderPassEvent, input, _feature.SourceRT);
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }
    }
}
