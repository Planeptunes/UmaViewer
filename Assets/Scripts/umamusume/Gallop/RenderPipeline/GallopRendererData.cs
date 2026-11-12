using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Renderer data of the live pipeline: identical to UniversalRendererData (same serialized fields), but it creates
    // the GallopRenderer so SetupCullingParameters follows the game.
    public class GallopRendererData : UniversalRendererData
    {
        protected override ScriptableRenderer Create()
        {
            return new GallopRenderer(this);
        }
    }
}
