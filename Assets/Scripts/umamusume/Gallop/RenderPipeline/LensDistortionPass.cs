using System;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Port of the JP Gallop.RenderPipeline.LensDistortionPass (Execute 0x7ffd595f22b0, Setup 0x7ffd595f2800).
    /// Same maths as the post-processing stack lens distortion: two global vectors and a blit with pass 0 of
    /// Gallop/ImageEffect/LensDistortion:
    ///   _LensDistortion_CenterScale = (CenterX, CenterY, max(IntensityX, 1e-4), max(IntensityY, 1e-4))
    ///   _LensDistortion_Amount      = (Intensity >= 0 ? theta : 1/theta, sigma, 1/Scale, Intensity)
    ///   amount = 1.6 * max(|Intensity|, 1); theta = deg2rad * min(160, amount); sigma = 2 * tan(theta * 0.5).
    /// JP event: 0x25a (602) written by LiveImageEffect.Initialize (here it runs in the chain order: after
    /// Fluctuation, before ChromaticAberration, like RegisterPass).
    /// </summary>
    public sealed class LensDistortionPass : ScriptableRenderPass, IDisposable
    {
        private const float kMinIntensity = 1e-4f;

        [Serializable]
        public struct Parameter
        {
            public float Intensity, IntensityX, IntensityY, CenterX, CenterY, Scale;

            public bool IsValid => Intensity != 0f && Finite(Intensity) && Finite(IntensityX) && Finite(IntensityY) &&
                                   Finite(CenterX) && Finite(CenterY) && Finite(Scale) && Scale != 0f;

            private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private Material _material;
        private Parameter _parameter;
        private PostImageEffectFeature _feature;
        private static readonly int Temp = Shader.PropertyToID("_RecoveredLensDistortion");

        public LensDistortionPass(RenderPassEvent evt) { renderPassEvent = evt; }

        public bool Setup(Parameter parameter, PostImageEffectFeature feature)
        {
            _parameter = parameter;
            _feature = feature;
            if (!parameter.IsValid) return false;
            Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.LensDistortion);
            if (shader == null || !shader.isSupported) return false;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            else if (_material.shader != shader) _material.shader = shader;
            return _material.passCount > 0;
        }

        public static void SetGlobals(CommandBuffer cmd, Parameter p)
        {
            float amount = 1.6f * Mathf.Max(Mathf.Abs(p.Intensity), 1f);
            float theta = Mathf.Deg2Rad * Mathf.Min(160f, amount);
            float sigma = 2f * Mathf.Tan(theta * 0.5f);
            cmd.SetGlobalVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._LensDistortion_CenterScale),
                new Vector4(p.CenterX, p.CenterY, Mathf.Max(p.IntensityX, kMinIntensity), Mathf.Max(p.IntensityY, kMinIntensity)));
            cmd.SetGlobalVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._LensDistortion_Amount),
                new Vector4(p.Intensity >= 0f ? theta : 1f / theta, sigma, 1f / p.Scale, p.Intensity));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature == null || _material == null || !_parameter.IsValid) return;
            var cmd = CommandBufferPool.Get("Recovered.LensDistortion");
            try
            {
                if ((int)renderPassEvent > (int)RenderPassEvent.BeforeRenderingPostProcessing)
                    _feature.ResetSourceRT(cmd, ref renderingData);

                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                cmd.GetTemporaryRT(Temp, descriptor.width, descriptor.height, 0,
                    FilterMode.Bilinear, descriptor.colorFormat);

                var inputHandle = _feature.SourceRT;
                SetGlobals(cmd, _parameter);
                cmd.Blit(inputHandle.RtId, Temp, _material, 0);
                _feature.SetSourceRT(RenderTextureHandle.Make(Temp, descriptor.width, descriptor.height),
                    cmd, ref renderingData);

                LiveRuntimeDiagnostics.RecordRenderExecution(renderingData.cameraData.camera,
                    "LensDistortion", renderPassEvent, inputHandle, _feature.SourceRT);

                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose() { CoreUtils.Destroy(_material); _material = null; }
    }
}
