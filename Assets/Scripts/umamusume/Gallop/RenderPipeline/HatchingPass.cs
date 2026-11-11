using System;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Port of the JP Gallop.RenderPipeline.HatchingPass (Execute 0x7ffd595f00e0, Setup 0x7ffd595f0930).
    /// Enabled when BlendMode != None (HatchingBlendMode: None 0, Alpha 1, Multiply 2, Screen 3, Additive 4,
    /// Overlay 5; values above 5 fall back to 1). The material of Gallop/ImageEffect/Hatching gets:
    ///   _BlendMode (int), _BlendAlpha, _SecondTex = NoiseTexture,
    ///   _NoiseMatrix = (cos a, -sin a, sin a, cos a) with a = StrokeAngle in radians,
    ///   _StrokeTiling = (StrokeTiling, StrokeHeight, 0, 0),
    ///   _StrokeOffset = StrokeLength * 0.1 * (sin a, cos a),
    ///   _CircleMaskOffset = CircleMaskOffset + (0.5, 0.5)  (Gallop.Math.VECTOR2_HALF),
    ///   _CircleMaskSize = (CircleMaskSize, Screen.width / Screen.height),
    ///   _CircleMaskStretchRatio, _CircleMaskMatrix (same rotation with CircleMaskAngle),
    ///   _CircleMaskInnerRatio, _CircleMaskInnerEffect
    /// and blits source -> temporary with pass 0. JP chain position: TiltShift -> Hatching -> RadialBlur.
    /// </summary>
    public sealed class HatchingPass : ScriptableRenderPass, IDisposable
    {
        private const float kStrokeOffsetScale = 0.1f; // DAT_7ffd5a640d54 in the JP binary

        [Serializable]
        public struct Parameter
        {
            public int BlendMode;
            public float BlendAlpha;
            public Texture2D NoiseTexture;
            public float StrokeTiling, StrokeHeight, StrokeAngle, StrokeLength;
            public Vector2 CircleMaskOffset;
            public float CircleMaskSize;
            public Vector2 CircleMaskStretchRatio;
            public float CircleMaskAngle, CircleMaskInnerRatio, CircleMaskInnerEffect;

            public bool IsValid => BlendMode != 0;
        }

        private static readonly int BlendModeId = Shader.PropertyToID("_BlendMode");
        private static readonly int BlendAlphaId = Shader.PropertyToID("_BlendAlpha");
        private static readonly int SecondTexId = Shader.PropertyToID("_SecondTex");
        private static readonly int NoiseMatrixId = Shader.PropertyToID("_NoiseMatrix");
        private static readonly int StrokeTilingId = Shader.PropertyToID("_StrokeTiling");
        private static readonly int StrokeOffsetId = Shader.PropertyToID("_StrokeOffset");
        private static readonly int CircleMaskOffsetId = Shader.PropertyToID("_CircleMaskOffset");
        private static readonly int CircleMaskSizeId = Shader.PropertyToID("_CircleMaskSize");
        private static readonly int CircleMaskStretchId = Shader.PropertyToID("_CircleMaskStretchRatio");
        private static readonly int CircleMaskMatrixId = Shader.PropertyToID("_CircleMaskMatrix");
        private static readonly int CircleMaskInnerRatioId = Shader.PropertyToID("_CircleMaskInnerRatio");
        private static readonly int CircleMaskInnerEffectId = Shader.PropertyToID("_CircleMaskInnerEffect");
        private static readonly int Temp = Shader.PropertyToID("_RecoveredHatching");

        private Material _material;
        private Parameter _parameter;
        private PostImageEffectFeature _feature;

        public HatchingPass(RenderPassEvent evt) { renderPassEvent = evt; }

        public bool Setup(Parameter parameter, PostImageEffectFeature feature)
        {
            _parameter = parameter;
            _feature = feature;
            if (!parameter.IsValid) return false;
            Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.Hatching);
            if (shader == null || !shader.isSupported) return false;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            else if (_material.shader != shader) _material.shader = shader;
            return _material.passCount > 0;
        }

        private static Vector4 Rotation(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float s = Mathf.Sin(rad), c = Mathf.Cos(rad);
            return new Vector4(c, -s, s, c);
        }

        public static void ApplyMaterial(Material m, Parameter p, float aspect)
        {
            int blend = p.BlendMode > 5 ? 1 : p.BlendMode;
            m.SetInt(BlendModeId, blend);
            m.SetFloat(BlendAlphaId, p.BlendAlpha);
            m.SetTexture(SecondTexId, p.NoiseTexture);
            m.SetVector(NoiseMatrixId, Rotation(p.StrokeAngle));
            m.SetVector(StrokeTilingId, new Vector4(p.StrokeTiling, p.StrokeHeight, 0f, 0f));
            float rad = p.StrokeAngle * Mathf.Deg2Rad;
            float k = p.StrokeLength * kStrokeOffsetScale;
            m.SetVector(StrokeOffsetId, new Vector4(k * Mathf.Sin(rad), k * Mathf.Cos(rad), 0f, 0f));
            m.SetVector(CircleMaskOffsetId, new Vector4(p.CircleMaskOffset.x + 0.5f, p.CircleMaskOffset.y + 0.5f, 0f, 0f));
            m.SetVector(CircleMaskSizeId, new Vector4(p.CircleMaskSize, aspect, 0f, 0f));
            m.SetVector(CircleMaskStretchId, new Vector4(p.CircleMaskStretchRatio.x, p.CircleMaskStretchRatio.y, 0f, 0f));
            m.SetVector(CircleMaskMatrixId, Rotation(p.CircleMaskAngle));
            m.SetFloat(CircleMaskInnerRatioId, p.CircleMaskInnerRatio);
            m.SetFloat(CircleMaskInnerEffectId, p.CircleMaskInnerEffect);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature == null || _material == null || !_parameter.IsValid) return;
            var cmd = CommandBufferPool.Get("Recovered.Hatching");
            try
            {
                if ((int)renderPassEvent > (int)RenderPassEvent.BeforeRenderingPostProcessing)
                    _feature.ResetSourceRT(cmd, ref renderingData);

                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                cmd.GetTemporaryRT(Temp, descriptor.width, descriptor.height, 0,
                    FilterMode.Bilinear, descriptor.colorFormat);

                // The native pass uses Screen.width / Screen.height; the camera target is what the viewer renders.
                float aspect = descriptor.height > 0 ? (float)descriptor.width / descriptor.height : 1f;
                ApplyMaterial(_material, _parameter, aspect);

                var inputHandle = _feature.SourceRT;
                cmd.Blit(inputHandle.RtId, Temp, _material, 0);
                _feature.SetSourceRT(RenderTextureHandle.Make(Temp, descriptor.width, descriptor.height),
                    cmd, ref renderingData);

                LiveRuntimeDiagnostics.RecordRenderExecution(renderingData.cameraData.camera,
                    "Hatching", renderPassEvent, inputHandle, _feature.SourceRT);

                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose() { CoreUtils.Destroy(_material); _material = null; }
    }
}
