using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// Colas de render de las piezas semitransparentes de los personajes del live (shaders
    /// Gallop/3D/Chara/*Alpha*, p.ej. M_Body_Alpha0), como las asigna el juego:
    ///
    ///   ModelController.get_RenderQueueAlpha          = 2300 (0x8FC)
    ///   ModelController.GetAlphaMeshRenderQueue(off)  = RenderQueueAlpha() + off * 4
    ///   ModelController.UpdateRenderQueue(orden)      : off = (19 - orden) * 4 -> Material.renderQueue
    ///   Director.UpdateModelRenderQueue               : ordena los personajes por GetDepthOrder y llama
    ///                                                   UpdateRenderQueue(i) a cada uno.
    ///
    /// => 2300 .. 2604, siempre por debajo de 3000 (luces y efectos del escenario). Sin esto se quedaban
    /// en la cola por defecto de su shader (3000), la misma que los haces de luz: Unity ordena esa cola por
    /// el centro de cada objeto y la pieza del traje se pintaba encima de un haz que estaba delante.
    ///
    /// Orden 0 = el personaje mas cercano a la camara del live (cola mas alta: se dibuja el ultimo).
    /// </summary>
    public static class LiveModelRenderQueue
    {
        private const int CHARA_LAYER = 13;          // "CHAR"
        private const int RENDER_QUEUE_ALPHA = 2300; // ModelController.get_RenderQueueAlpha
        private const int MAX_ORDER = 19;            // UpdateRenderQueue: (0x13 - orden) * 4

        private static readonly Dictionary<UmaContainerCharacter, List<Material>> _byChara =
            new Dictionary<UmaContainerCharacter, List<Material>>();
        private static readonly HashSet<Material> _known = new HashSet<Material>();
        private static readonly List<KeyValuePair<UmaContainerCharacter, float>> _sorted =
            new List<KeyValuePair<UmaContainerCharacter, float>>();
        private static float _nextScan;
        private static Director _director;   // al cambiar de live se vacian las caches

        private static bool IsCharaAlphaShader(Shader s)
        {
            if (s == null) return false;
            string n = s.name;
            return n.StartsWith("Gallop/3D/Chara/") && n.Contains("Alpha");
        }

        private static void Scan(Director director)
        {
            _byChara.Clear();
            foreach (var chara in director.CharaContainerScript)
            {
                if (chara == null) continue;
                var list = new List<Material>();
                foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.gameObject.layer != CHARA_LAYER) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !IsCharaAlphaShader(m.shader)) continue;
                        // Solo los que llegan en la cola de transparentes (la por defecto del shader);
                        // una vez reasignados ya se reconocen aunque esten por debajo de 2500.
                        if (!_known.Contains(m))
                        {
                            if (m.renderQueue <= 2500) continue;
                            _known.Add(m);
                        }
                        if (!list.Contains(m)) list.Add(m);
                    }
                }
                if (list.Count > 0) _byChara[chara] = list;
            }
        }

        /// <summary>Lo llama Director.LateUpdate en cada frame del live.</summary>
        public static void Update(Director director)
        {
            if (director == null || director.CharaContainerScript == null) return;

            if (director != _director)
            {
                _director = director;
                _known.Clear();
                _byChara.Clear();
                _nextScan = 0f;
            }

            if (Time.realtimeSinceStartup >= _nextScan)
            {
                _nextScan = Time.realtimeSinceStartup + 0.5f;
                Scan(director);
            }
            if (_byChara.Count == 0) return;

            Camera cam = director.MainRenderCamera != null ? director.MainRenderCamera : Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;

            _sorted.Clear();
            foreach (var kv in _byChara)
            {
                if (kv.Key == null) continue;
                float depth = Vector3.Dot(kv.Key.transform.position - camPos, camFwd);
                _sorted.Add(new KeyValuePair<UmaContainerCharacter, float>(kv.Key, depth));
            }
            _sorted.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int order = 0; order < _sorted.Count; order++)
            {
                int off = (MAX_ORDER - Mathf.Min(order, MAX_ORDER)) * 4;   // UpdateRenderQueue
                int queue = RENDER_QUEUE_ALPHA + off * 4;                  // GetAlphaMeshRenderQueue
                foreach (var m in _byChara[_sorted[order].Key])
                    if (m != null && m.renderQueue != queue) m.renderQueue = queue;
            }
        }
    }
}
