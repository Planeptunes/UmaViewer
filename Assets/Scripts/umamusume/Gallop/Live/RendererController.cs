using UnityEngine;

namespace Gallop.Live
{
    // Componente de los haces del escenario (el prefab lo referencia por nombre). Al arrancar fija
    // en su Renderer el lightmap y el orden de dibujado guardados en el prefab, y luego se desactiva,
    // igual que en el juego (Gallop.Live.RendererController.Start / UpdateRenderer).
    [DisallowMultipleComponent]
    public class RendererController : MonoBehaviour
    {
        [SerializeField] private int _lightmapIndex = -1;
        [SerializeField] private Vector4 _lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
        [SerializeField] private Vector4 _realtimeLightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
        [SerializeField] private int _sortingOrder = 1;

        private void Start()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                UpdateRenderer(renderer);
            enabled = false;
        }

        public void UpdateRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;

            renderer.lightmapIndex = _lightmapIndex;
            renderer.lightmapScaleOffset = _lightmapScaleOffset;
            renderer.realtimeLightmapScaleOffset = _realtimeLightmapScaleOffset;
            renderer.sortingOrder = _sortingOrder;
        }
    }
}
