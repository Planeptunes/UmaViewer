using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Receiver data of the game's additional-light shadows that only the client's own
    /// Gallop.RenderPipeline.AdditionalLightsShadowCasterPass uploads. The stage shadow shaders
    /// (Gallop/3D/Live/Stage/DefaultShadow*, StageDefaultShadowPanel...) read, besides the stock URP arrays
    /// (_AdditionalShadowParams, _AdditionalLightsWorldToShadow, _AdditionalLightsPosition, the shadow map):
    ///
    ///   _AdditionalShadowSliceUvRanges  Vector4[]  per shadow slice: (uMin, uMax, vMin, vMax) of the slice in the
    ///                                              atlas. A pixel whose shadow UV is outside is treated as lit, so
    ///                                              with the array missing (all zeros) every pixel is lit and the
    ///                                              character shadows never show.
    ///   _AdditionalLightsWorldToLight   Matrix4x4[] per pixel-light slot: world -> light space for the spot mask.
    ///   _LightTexture0                  Texture2D  spot mask: the shader takes max(shadow, 1 - mask.a), so an
    ///                                              unbound texture (alpha 0) removes the shadow as well.
    ///   _CustomLightIndices             float[8]   per pixel-light slot -> additional light index.
    ///
    /// The viewer renders with stock URP, so it feeds them here. Values recovered from the JP client
    /// (DXBC of DefaultShadow's UniversalForward pass + AdditionalLightsShadowCasterPass decompile):
    /// the mask is a plain white texture (the client loads a Resources texture and never draws a cone mask),
    /// so identity matrices are enough; the slice ranges are the atlas rectangle of each spot slice, taken from
    /// the WorldToShadow matrix URP just built.
    /// </summary>
    public static class AdditionalLightShadowReceiverData
    {
        private const int MaxSlices = 256;
        private const int MaxPixelLights = 8;

        private static readonly int SliceUvRangesId = Shader.PropertyToID("_AdditionalShadowSliceUvRanges");
        private static readonly int WorldToLightId = Shader.PropertyToID("_AdditionalLightsWorldToLight");
        private static readonly int LightTexture0Id = Shader.PropertyToID("_LightTexture0");
        private static readonly int CustomLightIndicesId = Shader.PropertyToID("_CustomLightIndices");
        private static readonly int ShadowParamsId = Shader.PropertyToID("_AdditionalShadowParams");
        private static readonly int WorldToShadowId = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
        private static readonly int LightPositionId = Shader.PropertyToID("_AdditionalLightsPosition");

        private static readonly Vector4[] sRanges = new Vector4[MaxSlices];
        private static readonly List<Vector4> sParams = new List<Vector4>(MaxSlices);
        private static readonly List<Vector4> sPositions = new List<Vector4>(MaxSlices);
        private static readonly List<Matrix4x4> sWorldToShadow = new List<Matrix4x4>(MaxSlices);
        private static Light[] sLights = new Light[0];
        private static int sLightsFrame = -1000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Hook()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

            // Constant parts: white spot mask, identity light matrices, identity light index map, full-atlas
            // slice ranges until the first camera has produced the real ones.
            var identities = new Matrix4x4[MaxSlices];
            for (int i = 0; i < identities.Length; i++)
                identities[i] = Matrix4x4.identity;
            Shader.SetGlobalMatrixArray(WorldToLightId, identities);

            var indices = new float[MaxPixelLights];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;
            Shader.SetGlobalFloatArray(CustomLightIndicesId, indices);

            for (int i = 0; i < sRanges.Length; i++)
                sRanges[i] = new Vector4(0f, 1f, 0f, 1f);
            Shader.SetGlobalVectorArray(SliceUvRangesId, sRanges);
        }

        // The texture is set per camera: URP can reset global textures between cameras.
        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(LightTexture0Id, Texture2D.whiteTexture);
        }

        // Slice rectangles come from the matrices URP uploaded while rendering this camera; they are used from the
        // next frame on (the atlas layout only changes when the number of shadow lights does).
        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            var director = Gallop.Live.Director.instance;
            if (director == null || camera == null || camera != director.MainRenderCamera)
                return;

            Shader.GetGlobalVectorArray(ShadowParamsId, sParams);
            Shader.GetGlobalVectorArray(LightPositionId, sPositions);
            Shader.GetGlobalMatrixArray(WorldToShadowId, sWorldToShadow);
            if (sParams.Count == 0 || sPositions.Count == 0 || sWorldToShadow.Count == 0)
                return;

            if (Time.frameCount - sLightsFrame > 30)
            {
                sLights = Object.FindObjectsOfType<Light>();
                sLightsFrame = Time.frameCount;
            }

            for (int i = 0; i < sRanges.Length; i++)
                sRanges[i] = new Vector4(0f, 1f, 0f, 1f);

            for (int l = 0; l < sLights.Length; l++)
            {
                Light light = sLights[l];
                if (light == null || light.type != LightType.Spot || light.shadows == LightShadows.None
                    || !light.isActiveAndEnabled)
                    continue;

                int lightIndex = FindLightIndex(light.transform.position);
                if (lightIndex < 0 || lightIndex >= sParams.Count)
                    continue;
                int slice = Mathf.RoundToInt(sParams[lightIndex].w);
                if (slice < 0 || slice >= sWorldToShadow.Count || slice >= MaxSlices)
                    continue;

                sRanges[slice] = ComputeSliceRange(light, sWorldToShadow[slice]);
            }
            Shader.SetGlobalVectorArray(SliceUvRangesId, sRanges);
        }

        private static int FindLightIndex(Vector3 position)
        {
            for (int i = 0; i < sPositions.Count; i++)
            {
                Vector4 p = sPositions[i];
                if ((new Vector3(p.x, p.y, p.z) - position).sqrMagnitude < 1e-4f)
                    return i;
            }
            return -1;
        }

        // The four corners of the spot's far plane are the corners of its shadow slice: projecting them with
        // the WorldToShadow matrix gives the slice rectangle in atlas UVs.
        private static Vector4 ComputeSliceRange(Light light, Matrix4x4 worldToShadow)
        {
            float half = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad) * light.range;
            Transform t = light.transform;
            Vector3 center = t.position + t.forward * light.range;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector3 p = center + t.right * (sx * half) + t.up * (sy * half);
                    Vector4 c = worldToShadow * new Vector4(p.x, p.y, p.z, 1f);
                    if (Mathf.Abs(c.w) < 1e-6f)
                        continue;
                    float u = c.x / c.w, v = c.y / c.w;
                    uMin = Mathf.Min(uMin, u); uMax = Mathf.Max(uMax, u);
                    vMin = Mathf.Min(vMin, v); vMax = Mathf.Max(vMax, v);
                }
            }
            if (uMin > uMax || vMin > vMax)
                return new Vector4(0f, 1f, 0f, 1f);
            return new Vector4(uMin, uMax, vMin, vMax);
        }
    }
}
