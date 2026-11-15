using System;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // TransmittedLight (glow of the stage lights). Port of the JP client's
    // Gallop.RenderPipeline.TransmittedLightPass (Execute 0x7ffd595cecd0, DrawTransmittedLightMask
    // 0x7ffd595ce920, CreateBloomRenderTexture 0x7ffd595ce4a0, Setup 0x7ffd595cf890):
    //  1) mask RT (RenderTextureFormat 5): cleared to black (GameDefine.COLOR_BLACK), stage light
    //     materials drawn with the "TransmittedLightMask" LightMode against the scene depth,
    //     published as _TransmittedMaskTex;
    //  2) Iterations (clamped to [1, 5]) mip levels of size source / 2^(i + 2): every level is made from
    //     the previous one (level 0 from the frame) with FastBloom pass 4 DownSampleMask and
    //     _Parameter written with Material.SetVector (immediate) as (DefaultShaderParameter.xy, 0, 1), but the
    //     blits are only queued: the first buffer execution comes after step 3's SetVector, so the
    //     downsample passes run with the step 3 _Parameter (threshold / intensity applied);
    //  3) every level is blurred once: pass 2 (vertical) then pass 3 (horizontal) with
    //     _Parameter = (spread / aspect / 512, spread / 512, Threshold, Intensity) (Material.SetVector),
    //     same spread for every level;
    //  4) Iterations > 1: merged from the smallest level up (_Bloom = level k, pass 0 over level k - 1,
    //     copied back); Material.SetFloat(_BloomIsScreenBlend, 1) is overwritten by step 5's value
    //     before these queued blits run;
    //  5) _BloomIsScreenBlend = BlendMode as a raw int (Screen 0, Add 1, Multiply 2), _Bloom = level 0,
    //     pass 0 over the frame.
    // Chain position (PostImageEffectFeature.RegisterPass): after SunShafts, before IndirectLightShafts.
    // The game enables it with GraphicSettings' quality flag (true for the live) AND the track flag;
    // there is no intensity gate.
    public sealed class TransmittedLightPass : ScriptableRenderPass, IDisposable
    {
        private const int MaxIterations = 5;
        private const float OneOverBaseSize = 1f / 512f;
        private const int PassBloom = 0, PassBlurVertical = 2, PassBlurHorizontal = 3, PassDownSampleMask = 4;

        private static readonly ShaderTagId MaskTag = new ShaderTagId("TransmittedLightMask");
        private static readonly int MaskId = Shader.PropertyToID("_RecoveredTransmittedLightMask");
        private static readonly int TempId = Shader.PropertyToID("_RecoveredTransmittedLightTemp");
        private static readonly int OutputId = Shader.PropertyToID("_RecoveredTransmittedLightOutput");
        private static readonly int[] LevelIds = MakeIds("_RecoveredTransmittedLightLevel", MaxIterations);
        private static readonly int[] VerticalIds = MakeIds("_RecoveredTransmittedLightVertical", MaxIterations);
        private static readonly int MaskTexId = Shader.PropertyToID("_TransmittedMaskTex");
        private static readonly int DepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int ParameterId = Shader.PropertyToID("_Parameter");
        private static readonly int BloomId = Shader.PropertyToID("_Bloom");
        private static readonly int ScreenBlendId = Shader.PropertyToID("_BloomIsScreenBlend");

        private Material _material;
        private PostImageEffectFeature _feature;
        private LiveTransmittedLightTimeline.State _state;

        public TransmittedLightPass(RenderPassEvent evt) { renderPassEvent = evt; }

        private static int[] MakeIds(string prefix, int count)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++) ids[i] = Shader.PropertyToID(prefix + i);
            return ids;
        }

        public bool Setup(LiveTransmittedLightTimeline.State state, PostImageEffectFeature feature)
        {
            _state = state;
            _feature = feature;
            if (!state.IsEnable) return false;
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
                maskDesc.colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB4444)
                    ? RenderTextureFormat.ARGB4444 : RenderTextureFormat.ARGB32; // game: format 5
                maskDesc.bindMS = false;
                // JP DrawTransmittedLightMask (0x7ffd595ce920): the depth attachment is the property id 0xdb
                // (_CameraDepthTexture), i.e. the DepthOnlyPass depth (opaque + DepthOnlyCubes, 1 sample), NOT the camera's
                // own depth buffer. Walls made by the DepthOnlyCubes therefore hide the lights behind them.
                bool useDepthTexture = _feature.DepthTextureReady;
                if (useDepthTexture)
                {
                    maskDesc.msaaSamples = 1;
                }
                else
                {
                    // Fallback (DepthOnlyPass not scheduled): the scene depth is multisampled when the camera uses MSAA, the
                    // mask must have the same sample count to be bound together with it (a 1-sample colour with an MSAA depth
                    // makes every depth test fail).
                    maskDesc.msaaSamples = Mathf.Max(1, camDesc.msaaSamples);
                    if (maskDesc.msaaSamples > 1) maskDesc.colorFormat = RenderTextureFormat.ARGB32;
                }
                maskDesc.useMipMap = maskDesc.autoGenerateMips = false;
                cmd.GetTemporaryRT(MaskId, maskDesc, FilterMode.Bilinear);
                RenderTargetIdentifier maskDepth = useDepthTexture
                    ? new RenderTargetIdentifier(DepthTextureId, 0, CubemapFace.Unknown, -1)
                    : (RenderTargetIdentifier)depth.nameID;
                cmd.SetRenderTarget(new RenderTargetIdentifier(MaskId), maskDepth);
                cmd.ClearRenderTarget(false, true, Color.black); // GameDefine.COLOR_BLACK
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                var drawing = CreateDrawingSettings(MaskTag, ref renderingData, SortingCriteria.CommonTransparent);
                drawing.perObjectData = PerObjectData.None;
                var filter = new FilteringSettings(RenderQueueRange.all, renderingData.cameraData.camera.cullingMask);
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
                cmd.SetGlobalTexture(MaskTexId, MaskId);

                var full = camDesc;
                full.depthBufferBits = 0; full.msaaSamples = 1; full.bindMS = false;
                full.useMipMap = full.autoGenerateMips = false;
                float aspect = full.height > 0 ? (float)full.width / full.height : 1f;

                int iterations = Mathf.Clamp(_state.Iterations, 1, MaxIterations);
                var levelDesc = new RenderTextureDescriptor[iterations];

                // 2) Mip chain with DownSampleMask: level i = source / 2^(i + 2).
                // JP Execute (0x7ffd595cecd0) writes _Parameter = (default.xy, 0, 1) with Material.SetVector, which is immediate,
                // while the downsample blits are only queued in the command buffer. The first buffer execution happens at the end
                // of CreateBloomRenderTexture, after its own Material.SetVector, so the downsample passes really run with the
                // blur _Parameter (threshold / intensity included) and this first value is overwritten before it is ever read.
                var defaultXY = DofDiffusionBloomOverlayPass.DefaultShaderParameter;
                _material.SetVector(ParameterId, new Vector4(defaultXY.x, defaultXY.y, 0f, 1f));
                RenderTargetIdentifier previous = input.RtId;
                for (int i = 0; i < iterations; i++)
                {
                    int divider = 1 << (i + 2);
                    var d = full;
                    d.width = Mathf.Max(1, full.width / divider);
                    d.height = Mathf.Max(1, full.height / divider);
                    levelDesc[i] = d;
                    cmd.GetTemporaryRT(LevelIds[i], d, FilterMode.Bilinear);
                    cmd.Blit(previous, LevelIds[i], _material, PassDownSampleMask);
                    previous = LevelIds[i];
                }

                // 3) One vertical + horizontal blur per level (the game flushes the buffer per level).
                float spread = _state.BlurSpread;
                for (int i = 0; i < iterations; i++)
                {
                    // CreateBloomRenderTexture (JP 0x7ffd595ce4a0): Material.SetVector, immediate.
                    _material.SetVector(ParameterId, new Vector4(spread / aspect * OneOverBaseSize,
                        spread * OneOverBaseSize, _state.Threshold, _state.Intensity));
                    cmd.GetTemporaryRT(VerticalIds[i], levelDesc[i], FilterMode.Bilinear);
                    cmd.Blit(LevelIds[i], VerticalIds[i], _material, PassBlurVertical);
                    cmd.Blit(VerticalIds[i], LevelIds[i], _material, PassBlurHorizontal);
                    cmd.ReleaseTemporaryRT(VerticalIds[i]);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                }

                // 4) Merge from the smallest level up with the Bloom pass. JP: Material.SetFloat(_BloomIsScreenBlend, 1) is immediate
                // and is replaced by Material.SetInt(BlendMode) before the queued merge / compose blits run, so all of them use BlendMode.
                if (iterations > 1)
                {
                    _material.SetFloat(ScreenBlendId, 1f);
                    for (int k = iterations - 1; k >= 1; k--)
                    {
                        cmd.SetGlobalTexture(BloomId, LevelIds[k]);
                        cmd.GetTemporaryRT(TempId, levelDesc[k - 1], FilterMode.Bilinear);
                        cmd.Blit(LevelIds[k - 1], TempId, _material, PassBloom);
                        cmd.CopyTexture(TempId, LevelIds[k - 1]);
                        cmd.ReleaseTemporaryRT(TempId);
                    }
                }

                // 5) Compose over the frame: BlendMode raw int (Screen 0, Add 1, Multiply 2).
                cmd.GetTemporaryRT(OutputId, full, FilterMode.Bilinear);
                _material.SetInt(ScreenBlendId, _state.BlendMode);
                cmd.SetGlobalTexture(BloomId, LevelIds[0]);
                cmd.Blit(input.RtId, OutputId, _material, PassBloom);
                cmd.SetGlobalTexture(MaskTexId, Texture2D.blackTexture);

                for (int i = 0; i < iterations; i++) cmd.ReleaseTemporaryRT(LevelIds[i]);
                cmd.ReleaseTemporaryRT(MaskId);
                _feature.SetSourceRT(RenderTextureHandle.Make(OutputId, full.width, full.height), cmd, ref renderingData);
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
