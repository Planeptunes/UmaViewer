using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Port of Gallop.RenderPipeline.BlurOptimizePass (JP TypeDefIndex 33586558).
    ///
    /// Native reference (JP RVA / Ghidra address):
    ///   Execute 0x5258FA0 (0x7ffd595d8fa0), Setup 0x52595C0 (0x7ffd595d95c0), .ctor 0x52597F0,
    ///   Parameter.Default 0x526B920 (0x7ffd595eb920).
    ///
    /// Who uses it: the planar-reflection camera of the stage mirror (MirrorReflection, native
    /// SetEnableBlur / SetBlurDownSample and the mirror camera setup at JP 0x7ffd5820xxxx write
    /// CameraData.ImageEffectParameter.BlurOptimized). Chain on that camera in the game:
    /// SetSourceRTPass -> BlurOptimizePass -> ImageEffectBlitPass.
    ///
    /// Execute (decompiled):
    ///  1. _parameterProp.x =  BlurSize * (1 / (1 &lt;&lt; Downsample)),  _parameterProp.y = -BlurSize * (1 / (1 &lt;&lt; Downsample)),
    ///     z/w keep the values copied in the ctor; material.SetVector(_Parameter, _parameterProp).
    ///  2. RT0 / RT1 temporaries of (width &gt;&gt; Downsample, height &gt;&gt; Downsample), bilinear, no depth.
    ///  3. Blit(SourceRT, RT0, material, pass 0), execute the command buffer.
    ///  4. for i in 0..BlurIterations-1: SetGlobalVector(_Parameter, (x + i, y - i, z, w));
    ///     Blit(RT0, RT1, pass 1 | 3), Blit(RT1, RT0, pass 2 | 4)  (BlurType != 0 selects 3 / 4); execute.
    ///  5. release RT1, Blit(RT0, SourceRT), release RT0, execute.
    /// Setup returns false when BlurOptimized.IsEnable is false or the shader (ShaderKinds.MobileBlurCustom,
    /// Hidden/FastBlurCustom) is missing; the pass event is BlurOptimized.RenderPassEvent.
    /// </summary>
    public sealed class BlurOptimizePass : ScriptableRenderPass, IDisposable
    {
        // Gallop.BlurOptimized.BlurType
        public const int BlurTypeStandardGauss = 0;
        public const int BlurTypeSgxGauss = 1;

        /// <summary> BlurOptimizePass.Parameter (value type embedded at +0x24 of PostImageEffectFeature.Parameter). </summary>
        [Serializable]
        public struct Parameter
        {
            public bool IsEnable;
            public int Downsample;
            public float BlurSize;
            public int BlurIterations;
            public int BlurType;
            public RenderPassEvent RenderPassEvent;

            /// <summary> Native Parameter.Default (0x526B920): IsEnable 0, Downsample 1, BlurSize 3, BlurIterations 2, BlurType 0, event 550 (0x226). </summary>
            public static Parameter Default()
            {
                return new Parameter
                {
                    IsEnable = false,
                    Downsample = 1,
                    BlurSize = 3f,
                    BlurIterations = 2,
                    BlurType = BlurTypeStandardGauss,
                    RenderPassEvent = (RenderPassEvent)0x226
                };
            }
        }

        private const string POOL_NAME = "BlurOptimizePass";
        // TEMP_RT0_NAME / TEMP_RT1_NAME (cctor 0x7ffd595d9770): Shader.PropertyToID of two literals.
        private static readonly int TEMP_RT0_NAME = Shader.PropertyToID("BlurOptimizePass_RT0");
        private static readonly int TEMP_RT1_NAME = Shader.PropertyToID("BlurOptimizePass_RT1");

        private CameraData _cameraData;
        private PostImageEffectFeature _feature;
        private Material _blurMaterial;
        // _parameterProp (+0xF8): ctor copies the static default Vector4 (z, w = 0 in the captured frame).
        private Vector4 _parameterProp;

        public BlurOptimizePass(RenderPassEvent renderPassEvent)
        {
            _parameterProp = Vector4.zero;
            this.renderPassEvent = renderPassEvent;
        }

        public bool Setup(CameraData cameraData, PostImageEffectFeature feature)
        {
            _cameraData = null;
            _feature = null;

            Parameter p = cameraData.Parameter.BlurOptimized;
            if (!p.IsEnable)
                return false;

            if (_blurMaterial == null)
            {
                Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.MobileBlurCustom);
                if (shader == null)
                    return false;
                _blurMaterial = new Material(shader);
            }

            renderPassEvent = p.RenderPassEvent;
            _feature = feature;
            _cameraData = cameraData;
            return true;
        }

        public void Dispose()
        {
            if (_blurMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_blurMaterial);
                else UnityEngine.Object.DestroyImmediate(_blurMaterial);
                _blurMaterial = null;
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_cameraData == null || _feature == null || _blurMaterial == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(POOL_NAME);
            Parameter p = _cameraData.Parameter.BlurOptimized;

            int parameterId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._Parameter);
            float scale = 1f / (1 << (p.Downsample & 0x1f));
            _parameterProp.x = scale * p.BlurSize;
            _parameterProp.y = -p.BlurSize * scale;
            _blurMaterial.SetVector(parameterId, _parameterProp);

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            int width = descriptor.width >> (p.Downsample & 0x1f);
            int height = descriptor.height >> (p.Downsample & 0x1f);

            RenderTargetIdentifier source = _feature.SourceRT.RtId;

            cmd.GetTemporaryRT(TEMP_RT0_NAME, width, height, 0, FilterMode.Bilinear);
            cmd.Blit(source, TEMP_RT0_NAME, _blurMaterial, 0);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.GetTemporaryRT(TEMP_RT1_NAME, width, height, 0, FilterMode.Bilinear);
            int verticalPass = p.BlurType != 0 ? 3 : 1;
            int horizontalPass = p.BlurType != 0 ? 4 : 2;
            for (int i = 0; i < p.BlurIterations; i++)
            {
                cmd.SetGlobalVector(parameterId,
                    new Vector4(i + _parameterProp.x, _parameterProp.y - i, _parameterProp.z, _parameterProp.w));
                cmd.Blit(TEMP_RT0_NAME, TEMP_RT1_NAME, _blurMaterial, verticalPass);
                cmd.Blit(TEMP_RT1_NAME, TEMP_RT0_NAME, _blurMaterial, horizontalPass);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }

            cmd.ReleaseTemporaryRT(TEMP_RT1_NAME);
            cmd.Blit(TEMP_RT0_NAME, source);
            cmd.ReleaseTemporaryRT(TEMP_RT0_NAME);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
