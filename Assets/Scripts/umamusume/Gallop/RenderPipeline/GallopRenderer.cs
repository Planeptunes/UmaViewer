using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Port of Gallop.RenderPipeline.GallopRenderer.SetupCullingParameters (JP 0x7ffd596070d0) on top of URP's
    // UniversalRenderer (the project embeds URP in Packages/ with the `sealed` modifier removed from
    // UniversalRenderer). The native version derives the shadow distance and the culling options from the camera's
    // Gallop CameraData instead of URP's per-camera value:
    //   distance = static shadow distance (MultiLightShadowController, CameraData.GlobalShadowDistance here)
    //   renderShadows == false           -> 0
    //   CameraData.MaxShadowDistance > 0 -> that value
    //   cullingOptions: OcclusionCull and NeedsReflectionProbes always off; _isUIRendering -> DisablePerObjectCulling
    //   and only NeedsLighting kept; IsDisableObjectCulling -> DisablePerObjectCulling; IsDisableLightCulling ->
    //   NeedsLighting off; no shadow casters when the distance is 0 or the asset supports no shadows.
    public class GallopRenderer : UniversalRenderer
    {
        public GallopRenderer(UniversalRendererData data) : base(data)
        {
        }

        public override void SetupCullingParameters(ref ScriptableCullingParameters cullingParameters,
            ref UnityEngine.Rendering.Universal.CameraData cameraData)
        {
            // URP defaults first (visible light count, enclosing sphere); the native values are applied on top.
            base.SetupCullingParameters(ref cullingParameters, ref cameraData);

            float distance = CameraData.GlobalShadowDistance;
            Camera camera = cameraData.camera;
            // The native camera options come from the Gallop CameraData of the camera. The viewer only attaches it to
            // the cameras that carry the image effect; any other camera keeps URP's own renderShadows decision.
            if (camera != null && camera.TryGetComponent(out CameraData gallopData))
            {
                // Native cullingOptions start with ShadowCasters on; URP may have removed it for its own distance.
                cullingParameters.cullingOptions |= CullingOptions.ShadowCasters;

                var additional = camera.GetUniversalAdditionalCameraData();
                if (additional != null && !additional.renderShadows)
                    distance = 0.0f;
                else if (gallopData.MaxShadowDistance > 0.0f)
                    distance = gallopData.MaxShadowDistance;

                cullingParameters.cullingOptions &= ~CullingOptions.OcclusionCull;
                cullingParameters.cullingOptions &= ~CullingOptions.NeedsReflectionProbes;
                if (gallopData.IsUIRendering)
                {
                    cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling;
                    cullingParameters.cullingOptions &= CullingOptions.NeedsLighting;
                }
                if (gallopData.IsDisableObjectCulling)
                    cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling;
                if (gallopData.IsDisableLightCulling)
                    cullingParameters.cullingOptions &= ~CullingOptions.NeedsLighting;
            }
            else if (cameraData.maxShadowDistance <= 0.0f)
            {
                distance = 0.0f;
            }

            // The user's "shadow distance" setting stays an upper bound of the viewer.
            distance = Mathf.Min(distance, RuntimeGraphicsSettings.UserShadowDistance);

            var asset = UniversalRenderPipeline.asset;
            bool shadowsUnsupported = asset != null && !asset.supportsMainLightShadows && !asset.supportsAdditionalLightShadows;
            if (Mathf.Approximately(distance, 0.0f) || shadowsUnsupported)
                cullingParameters.cullingOptions &= ~CullingOptions.ShadowCasters;

            cameraData.maxShadowDistance = distance;
            cullingParameters.shadowDistance = distance;
        }
    }
}
