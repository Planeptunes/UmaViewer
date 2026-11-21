using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.RenderPipeline
{
    public static class RenderUtils
    {
        // RenderUtils..cctor (JP 0x7ffd595ccc60), the statics DrawObjectsPass uses. The ShaderTagId string literals are
        // runtime string pointers (not readable from the dumped DLL); the names are the LightMode tags of the game
        // shaders (tools 09-shaders INDEX: SRPDefaultUnlit, PreUniversalForward, UniversalForward,
        // AfterUniversalForward, UniversalForwardOnly, Outline) matching each static field name.
        public static readonly ShaderTagId SRPDEFAULT_SHADERTAG = new ShaderTagId("SRPDefaultUnlit");            // +0x08
        public static readonly ShaderTagId PREUNIVERSALFORWARD_SHADERTAG = new ShaderTagId("PreUniversalForward"); // +0x0C
        public static readonly ShaderTagId AFTERUNIVERSALFORWARD_SHADERTAG = new ShaderTagId("AfterUniversalForward"); // +0x10
        public static readonly ShaderTagId UNIVERSALFORWARD_SHADERTAG = new ShaderTagId("UniversalForward");       // +0x14
        public static readonly ShaderTagId UNIVERSALFORWARDONLY_SHADERTAG = new ShaderTagId("UniversalForwardOnly"); // +0x18
        public static readonly ShaderTagId OUTLINE_SHADERTAG = new ShaderTagId("Outline");                         // +0x1C

        // +0x40 DEFAULT_SHADERTAG_ARRAY = { SRPDEFAULT }
        public static readonly ShaderTagId[] DEFAULT_SHADERTAG_ARRAY = { SRPDEFAULT_SHADERTAG };

        // +0x48 FORWARD_SHADERTAG_ARRAY = { PREUNIVERSALFORWARD (+0xC), UNIVERSALFORWARD (+0x14),
        // AFTERUNIVERSALFORWARD (+0x10), UNIVERSALFORWARDONLY (+0x18), OUTLINE (+0x1C) } (cctor order).
        public static readonly ShaderTagId[] FORWARD_SHADERTAG_ARRAY =
        {
            PREUNIVERSALFORWARD_SHADERTAG,
            UNIVERSALFORWARD_SHADERTAG,
            AFTERUNIVERSALFORWARD_SHADERTAG,
            UNIVERSALFORWARDONLY_SHADERTAG,
            OUTLINE_SHADERTAG,
        };

        // +0x38 UI_SHADERTAG_ARRAY = { SRPDEFAULT (+0x08), PREUNIVERSALFORWARD (+0x0C), UNIVERSALFORWARD (+0x14),
        // AFTERUNIVERSALFORWARD (+0x10), UNIVERSALFORWARDONLY (+0x18) } (RenderUtils..cctor 0x7ffd595ccc60 fill
        // order): the FORWARD list without Outline, with SRPDefaultUnlit first. Consumed by
        // UIRenderingPass._uiShaderTagIdList (+0xf8) (UIRenderingPass..ctor 0x7ffd596169b0).
        public static readonly ShaderTagId[] UI_SHADERTAG_ARRAY =
        {
            SRPDEFAULT_SHADERTAG,
            PREUNIVERSALFORWARD_SHADERTAG,
            UNIVERSALFORWARD_SHADERTAG,
            AFTERUNIVERSALFORWARD_SHADERTAG,
            UNIVERSALFORWARDONLY_SHADERTAG,
        };

        public static Material GetMaterial(Renderer renderer)
        {
            if (renderer == null)
                return null;

            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial == null)
                return null;

            string originalName = sharedMaterial.name;

            Material material = renderer.material;
            if (material == null)
                return null;

            material.name = originalName;
            return material;
        }

        public static void CopyMaterial(
            Material source,
            Material destination,
            bool nameReplace = true)
        {
            if (source == null || destination == null)
                return;

            if (nameReplace)
                destination.name = source.name;

            if (destination.shader != source.shader)
                destination.shader = source.shader;

            destination.CopyPropertiesFromMaterial(source);
        }

        public static void Destroy(ref Material obj)
        {
            CoreUtils.Destroy(obj);
            obj = null;
        }

        // RenderUtils.CheckAndCreateMaterialFromBinary (JP 0x7ffd595c8ab0): creates the material with
        // Shader.Find(shaderName) only when it does not exist yet; returns false when the shader is not found.
        public static bool CheckAndCreateMaterialFromBinary(ref Material material, string shaderName)
        {
            if (material != null)
                return true;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                return false;
            material = new Material(shader);
            return true;
        }
    }
}