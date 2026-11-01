// =====================================================================================================
// Limitadores de post efectos sobre los CANTANTES del live (dos interruptores del listado de post efectos):
//
//   "Saturation Limit" (PostEffectId.CharaLimit): limitador POR CAPAS sobre los cantantes. Se evaluan en
//       orden Bloom -> PostFilm -> SunShafts/LightShafts; cada capa mira el cambio ACUMULADO medio sobre el
//       cantante y recibe un factor unico para todo el personaje (se conserva o se reduce, hasta
//       maxReduction; nunca se apaga).
//       La pasada DOF/Bloom/PostFilm se repite con la imagen sin shafts y con Bloom/PostFilm apagados
//       para obtener cada capa por separado. El escenario no cambia.
//   "Diffusion Limit" (PostEffectId.DiffusionLimit): los cantantes no reciben Diffusion (la pasada
//       DOF/Bloom/PostFilm se ejecuta otra vez sin Diffusion y se usa esa version en sus pixeles).
//
// Pasadas (las registra PostImageEffectFeature):
//   CapturePass : tras SourceSetup/GlobalFog y ANTES de SunShafts -> copia la imagen "limpia" y dibuja la
//                 mascara de personajes (R = profundidad lineal, G = peso, B = id de cantante).
//   ApplyPass   : tras DofDiffusionBloomOverlay -> mide la cobertura de cada cantante y el cambio de cada
//                 capa, aplica el shader (Resources/Shaders/CharaPostEffectLimiter) y entrega el resultado
//                 como nuevo SourceRT.
//
// Capas (las genera DofDiffusionBloomOverlayPass a partir de _CharaLimitOrig, imagen sin shafts):
//   Base = DOF/Diffusion | B = Base + Bloom | BPF = B + PostFilm | Normal = BPF + SunShafts/LightShafts.
//   Una capa que no existe este frame es alias de la siguiente (sin coste).
//
// Criterios:
//   - Solo cantantes: slot < Director.partInfo.SingerCount (mobs intactos, ver mobStrength).
//   - Siluetas: peso por personaje segun max(_CharaColor.rgb) (pista BgColor1); oscuro -> no actua.
//   - Planos abiertos: cada cantante se gradua con SU fraccion de pantalla visible (coverageStart/End).
//   - La capa Base ya lleva DOF/Diffusion: el desenfoque no cuenta como luz añadida.
//   - Mascara: solo renderers que se dibujan este frame (un SkinnedMeshRenderer descartado por culling no
//     actualiza su skinning y se pintaria con una pose antigua).
// =====================================================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>Valores del limitador de post efectos sobre los cantantes.</summary>
    public sealed class CharaPostEffectLimiterSettings
    {
        public static readonly CharaPostEffectLimiterSettings Instance = new CharaPostEffectLimiterSettings();

        // ---- Limitador por capas (Bloom -> PostFilm -> SunShafts/LightShafts) ----
        // Las capas se evaluan en orden; cada una mira el cambio ACUMULADO sobre el cantante (las capas
        // anteriores ya con su reduccion). El cambio es la media, sobre los pixeles visibles del cantante,
        // de lum(luz añadida) + whiteWeight * gris(luz añadida) (el gris comun a R,G,B lava el color).
        // Ninguna capa se apaga: la reduccion crece de 0 (acumulado <= notable) a maxReduction
        // (acumulado >= excessive) con una rampa suave.
        public float notable = 0.05f;      // diferencia notoria: empieza a reducir
        public float excessive = 0.15f;    // excesivo: reduccion maxima
        public float maxReduction = 0.6f;  // reduccion maxima por capa (0.6 = se conserva un 40 %)
        public float whiteWeight = 2.5f;   // peso extra de la "blancura" en la medida

        // 0 = sin efecto, 1 = limitador completo.
        public float strength = 1f;

        // Siluetas: max(_CharaColor.rgb) <= start -> peso 0 (no actua); >= end -> peso 1.
        // Personaje iluminado normal: ~0.56-1.0; silueta: ~0.07-0.22.
        public float charaColorFadeStart = 0.25f;
        public float charaColorFadeEnd = 0.55f;

        // Fuerza en los personajes de fondo (mobs). 0 = se quedan como el original.
        public float mobStrength = 0f;

        // Planos abiertos, POR CANTANTE: fraccion de pantalla visible de ese cantante por debajo de la
        // cual no actua (start) y a partir de la cual actua al 100% (end).
        public float coverageStart = 0.0125f;
        public float coverageEnd = 0.02f;

        // Diffusion Limit: atenuarlo en siluetas / en planos abiertos (con los mismos criterios).
        public bool diffusionUseSilhouetteFade = false;
        public bool diffusionUseCoverageFade = true;

        public Vector4 ShaderParams => new Vector4(0f, 0f, strength, 0f);
        public Vector4 ShaderCurveParams => new Vector4(0f, 0f, coverageStart, coverageEnd);
        // x = notable, y = excessive, z = maxReduction
        public Vector4 ShaderThresholds => new Vector4(notable, excessive, Mathf.Clamp01(maxReduction), 0f);
        // z = whiteWeight
        public Vector4 ShaderReduce => new Vector4(0f, 0f, whiteWeight, 0f);
    }

    /// <summary>
    /// Ticks "Saturation Limit" y "Diffusion Limit" dentro del listado de post efectos de LiveScene. Los
    /// ticks originales viven en la escena: aqui se clona el ultimo por cada id de PostEffectSelection.All
    /// que la escena no tiene y se devuelve el array ampliado para que LiveGraphicsSettingsUI los cablee
    /// por indice como los demas. En el panel RECORD la lista se construye entera desde
    /// PostEffectSelection.All, asi que alli aparecen solos.
    /// </summary>
    public static class CharaPostEffectLimiterUI
    {
        public static UnityEngine.UI.Toggle[] AppendListToggle(UnityEngine.UI.Toggle[] toggles)
        {
            var all = PostEffectSelection.All;
            if (toggles == null || toggles.Length == 0) return toggles;
            while (toggles.Length < all.Length)
            {
                var appended = AppendOne(toggles, all[toggles.Length]);
                if (appended == toggles) break;
                toggles = appended;
            }
            return toggles;
        }

        private static UnityEngine.UI.Toggle[] AppendOne(UnityEngine.UI.Toggle[] toggles, PostEffectId id)
        {
            var template = toggles[toggles.Length - 1];
            if (template == null || template.transform.parent == null) return toggles;

            string toggleName = "PostEffect_" + id;
            Transform parent = template.transform.parent;
            var existing = parent.Find(toggleName);
            GameObject row = existing != null
                ? existing.gameObject
                : Object.Instantiate(template.gameObject, parent, false);
            row.name = toggleName;
            row.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            var toggle = row.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle == null) toggle = row.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
            if (toggle == null)
            {
                Object.Destroy(row);
                return toggles;
            }
            // El clon copia los listeners persistentes del original; los cablea LiveGraphicsSettingsUI.
            toggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();

            string label = PostEffectSelection.Label(id);
            var text = row.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (text != null) text.text = label;
            else
            {
                var tmp = row.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null) tmp.text = label;
            }

            // Sin LayoutGroup en el contenido: se coloca a mano un paso por debajo del ultimo y se
            // alarga el contenido (si hay LayoutGroup, el layout lo coloca solo).
            if (existing == null && parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null &&
                toggles.Length >= 2 && toggles[toggles.Length - 2] != null)
            {
                var a = toggles[toggles.Length - 2].transform as RectTransform;
                var b = template.transform as RectTransform;
                var c = row.transform as RectTransform;
                if (a != null && b != null && c != null)
                {
                    Vector2 step = b.anchoredPosition - a.anchoredPosition;
                    c.anchoredPosition = b.anchoredPosition + step;
                    var content = parent as RectTransform;
                    if (content != null && content.GetComponent<UnityEngine.UI.ContentSizeFitter>() == null)
                        content.sizeDelta += new Vector2(0f, Mathf.Abs(step.y));
                }
            }

            var result = new UnityEngine.UI.Toggle[toggles.Length + 1];
            System.Array.Copy(toggles, result, toggles.Length);
            result[toggles.Length] = toggle;
            return result;
        }
    }

    /// <summary>Estado compartido entre CapturePass, DofDiffusionBloomOverlayPass y ApplyPass.</summary>
    public static class CharaPostEffectLimiter
    {
        public const int CHARA_LAYER = 13; // "CHAR"
        public const int MAX_SINGERS = 8;  // 2 RT RGBA de cobertura = 8 canales
        private const string SHADER_RESOURCE = "Shaders/CharaPostEffectLimiter";

        internal static readonly int OrigId = Shader.PropertyToID("_CharaLimitOrig");
        internal static readonly int MaskId = Shader.PropertyToID("_CharaLimitMask");
        internal static readonly int OutId = Shader.PropertyToID("_CharaLimitOut");
        internal static readonly int ParamsId = Shader.PropertyToID("_CharaLimitParams");
        internal static readonly int CurveParamsId = Shader.PropertyToID("_CharaLimitCurve");
        internal static readonly int RefParamsId = Shader.PropertyToID("_CharaLimitRef");
        internal static readonly int ThresholdsId = Shader.PropertyToID("_CharaLimitThresholds");
        internal static readonly int ReduceId = Shader.PropertyToID("_CharaLimitReduce");
        // Capas del limitador por capas (ver cabecera). Las RT las reserva DofDiffusionBloomOverlayPass.
        internal static readonly int LayerBaseId = Shader.PropertyToID("_CharaLayerBase");
        internal static readonly int LayerBloomId = Shader.PropertyToID("_CharaLayerBloom");
        internal static readonly int LayerBPFId = Shader.PropertyToID("_CharaLayerBPF");
        // Entradas de la pasada de medida (pass 3) y sus 3 x 2 RT de salida (transicion x grupo).
        internal static readonly int LayerAId = Shader.PropertyToID("_CharaLayerA");
        internal static readonly int LayerBId = Shader.PropertyToID("_CharaLayerB");
        internal static readonly int[] MeasureIds =
        {
            // Base->B (Bloom), B->BPF (PostFilm), BPF->Normal (SunShafts/LightShafts)
            Shader.PropertyToID("_CharaLimitMeasureB0"), Shader.PropertyToID("_CharaLimitMeasureB1"),
            Shader.PropertyToID("_CharaLimitMeasurePF0"), Shader.PropertyToID("_CharaLimitMeasurePF1"),
            Shader.PropertyToID("_CharaLimitMeasureS0"), Shader.PropertyToID("_CharaLimitMeasureS1"),
        };
        internal static readonly int LayerFlagsId = Shader.PropertyToID("_CharaLimitLayers");
        internal static readonly int WeightId = Shader.PropertyToID("_CharaLimitWeight");
        internal static readonly int SingerId = Shader.PropertyToID("_CharaLimitSinger");
        // Cobertura POR CANTANTE: RT0 = cantantes 1-4 (RGBA), RT1 = cantantes 5-8.
        internal static readonly int Coverage0Id = Shader.PropertyToID("_CharaLimitCoverage0");
        internal static readonly int Coverage1Id = Shader.PropertyToID("_CharaLimitCoverage1");
        internal static readonly int CoverageGroupId = Shader.PropertyToID("_CharaLimitCoverageGroup");
        internal static readonly int CoverageMipId = Shader.PropertyToID("_CharaLimitCoverageMip");
        internal const int COVERAGE_SIZE = 256;   // potencia de 2: el ultimo mip (8) es 1x1 = la media
        internal const int COVERAGE_LAST_MIP = 8;
        // Version del frame SIN Diffusion (Bloom/DOF/PostFilm intactos) para el Diffusion Limit.
        internal static readonly int NoDiffusionId = Shader.PropertyToID("_CharaNoDiffusion");
        internal static readonly int DiffusionParamsId = Shader.PropertyToID("_CharaLimitDiff");
        private static readonly int CharaColorId = Shader.PropertyToID("_CharaColor");

        private static MaterialPropertyBlock _mpb;
        private static Material _material;
        private static readonly List<Renderer> _renderers = new List<Renderer>();
        private static readonly List<int> _owners = new List<int>();                 // indice en _charas
        private static readonly List<UmaContainerCharacter> _charas = new List<UmaContainerCharacter>();
        private static readonly List<float> _charaWeights = new List<float>();
        private static readonly List<bool> _charaIsMain = new List<bool>();          // cantante
        private static readonly List<int> _charaSingerId = new List<int>();          // 1..8 cantante, 0 mob
        private static float _nextRendererScan;

        // true entre CapturePass y ApplyPass del mismo frame/camara.
        internal static bool Captured;
        // true si DofDiffusionBloomOverlayPass genero este frame la version sin Diffusion.
        internal static bool NoDiffusionReady;
        // true si este frame hay SunShafts/LightShafts entre la captura y la pasada DOF (lo fija
        // PostImageEffectFeature al registrar las pasadas).
        internal static bool ShaftsQueued;

        // Capas generadas este frame por DofDiffusionBloomOverlayPass. Cada entrada es el nameID de la RT
        // de esa capa, o LAYER_NORMAL si es la imagen final (alias: esa capa no aporta nada este frame).
        internal const int LAYER_NORMAL = -1;
        internal static bool LayersReady;
        internal static int LayerBase = LAYER_NORMAL, LayerB = LAYER_NORMAL, LayerBPF = LAYER_NORMAL;
        internal static readonly List<int> LayerAllocated = new List<int>(3);

        internal static void ResetLayers()
        {
            LayersReady = false;
            LayerBase = LayerB = LayerBPF = LAYER_NORMAL;
            LayerAllocated.Clear();
        }

        /// <summary>Lo consulta DofDiffusionBloomOverlayPass: generar las capas del Saturation Limit.</summary>
        public static bool WantsLayers => Captured && SaturationActive;

        /// <summary>Mayor id de cantante (1..8) entre los personajes escaneados (0 = ninguno).</summary>
        internal static int MaxSingerId
        {
            get
            {
                int m = 0;
                for (int i = 0; i < _charaSingerId.Count; i++) m = Mathf.Max(m, _charaSingerId[i]);
                return m;
            }
        }

        /// <summary>Se registran las pasadas si esta activo cualquiera de los dos ticks.</summary>
        public static bool IsActive => Material != null && (SaturationActive || DiffusionActive);

        /// <summary>Tick "Saturation Limit". IsEnabled respeta la seleccion propia de la grabacion.</summary>
        public static bool SaturationActive =>
            CharaPostEffectLimiterSettings.Instance.strength > 0f &&
            PostEffectSelection.IsEnabled(PostEffectId.CharaLimit);

        /// <summary>Tick "Diffusion Limit".</summary>
        public static bool DiffusionActive => PostEffectSelection.IsEnabled(PostEffectId.DiffusionLimit);

        /// <summary>
        /// Lo consulta DofDiffusionBloomOverlayPass.Execute: generar la version sin Diffusion si el tick
        /// esta activo y la captura de este frame existe (mascara de cantantes lista).
        /// </summary>
        public static bool WantsNoDiffusion => Captured && DiffusionActive;

        internal static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Resources.Load<Shader>(SHADER_RESOURCE);
                    if (shader == null || !shader.isSupported) return null;
                    _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return _material;
            }
        }

        /// <summary>
        /// Nivel max(_CharaColor.rgb) de un renderer (bloque de propiedades que pone el Director desde la
        /// pista BgColor1). -1 si el renderer no tiene _CharaColor (p.ej. MultiplyCheek).
        /// </summary>
        private static float CharaColorLevel(Renderer r)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            Color c;
            if (_mpb.HasColor(CharaColorId)) c = _mpb.GetColor(CharaColorId);
            else if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(CharaColorId)) c = r.sharedMaterial.GetColor(CharaColorId);
            else return -1f;
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        /// <summary>Silueta oscura -> 0; iluminado -> 1 (smoothstep entre fadeStart y fadeEnd).</summary>
        private static float WeightFromLevel(float level, CharaPostEffectLimiterSettings s)
        {
            float start = s.charaColorFadeStart, end = Mathf.Max(s.charaColorFadeEnd, start + 1e-3f);
            float t = Mathf.Clamp01((level - start) / (end - start));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Peso POR PERSONAJE: se toma el _CharaColor de sus renderers que lo tienen (cuerpo/cara) y se
        /// aplica a TODAS sus piezas, para que las que no lo tienen (mejillas, lagrimas, accesorios) no
        /// reciban un peso distinto al de la cara. Los mobs se escalan por mobStrength.
        /// </summary>
        internal static void UpdateCharaWeights(CharaPostEffectLimiterSettings s)
        {
            _charaWeights.Clear();
            for (int ci = 0; ci < _charas.Count; ci++) _charaWeights.Add(-1f);
            for (int i = 0; i < _renderers.Count; i++)
            {
                int owner = _owners[i];
                if (_charaWeights[owner] >= 0f) continue;
                var r = _renderers[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                float level = CharaColorLevel(r);
                if (level >= 0f) _charaWeights[owner] = WeightFromLevel(level, s);
            }
            for (int ci = 0; ci < _charaWeights.Count; ci++)
            {
                if (_charaWeights[ci] < 0f) _charaWeights[ci] = 1f;
                if (ci < _charaIsMain.Count && !_charaIsMain[ci]) _charaWeights[ci] *= s.mobStrength;
            }
        }

        /// <summary>
        /// true si el personaje ocupa un slot de CANTANTE: slot &lt; Director.partInfo.SingerCount (columnas
        /// de voz del _part que llegan a sonar; mismo numero que el overlay de voces). Respaldo:
        /// characterSettings.useHighPolygonModel[slot] y luego slot &lt; charaMotSeqList.Count. Fuera de un
        /// live (sin Director) se trata como cantante.
        /// </summary>
        private static bool IsMainMember(UmaContainerCharacter chara)
        {
            var director = Gallop.Live.Director.instance;
            if (director == null || director.CharaContainerScript == null) return true;
            int slot = director.CharaContainerScript.IndexOf(chara);
            if (slot < 0) return true;

            var part = director.partInfo;
            if (part != null && part.SingerCount > 0) return slot < part.SingerCount;

            var control = Object.FindObjectOfType<Gallop.Live.Cutt.LiveTimelineControl>();
            var data = control != null ? control.data : null;
            if (data == null) return true;

            var flags = data.characterSettings != null ? data.characterSettings.useHighPolygonModel : null;
            if (flags != null && flags.Length > 0)
                return slot < flags.Length && flags[slot];

            var sheets = data.worksheetList;
            var seq = sheets != null && sheets.Count > 0 && sheets[0] != null ? sheets[0].charaMotSeqList : null;
            return seq == null || slot < seq.Count;
        }

        /// <summary>Id de cantante (canal B de la mascara): slot + 1 (1..8), 0 = mob.</summary>
        private static int ComputeSingerId(UmaContainerCharacter chara, bool isMain, int fallbackIndex)
        {
            if (!isMain) return 0;
            var director = Gallop.Live.Director.instance;
            int slot = director != null && director.CharaContainerScript != null
                ? director.CharaContainerScript.IndexOf(chara) : -1;
            if (slot < 0) slot = fallbackIndex;
            return slot < MAX_SINGERS ? slot + 1 : 0;
        }

        internal static float WeightOfRenderer(int rendererIndex)
        {
            int owner = _owners[rendererIndex];
            return owner < _charaWeights.Count ? _charaWeights[owner] : 1f;
        }

        internal static float SingerIdOfRenderer(int rendererIndex)
        {
            int owner = _owners[rendererIndex];
            return owner < _charaSingerId.Count ? _charaSingerId[owner] : 0f;
        }

        internal static List<Renderer> CharaRenderers
        {
            get
            {
                if (Time.realtimeSinceStartup >= _nextRendererScan)
                {
                    _nextRendererScan = Time.realtimeSinceStartup + 0.5f;
                    _renderers.Clear();
                    _owners.Clear();
                    _charas.Clear();
                    _charaIsMain.Clear();
                    _charaSingerId.Clear();
                    foreach (var chara in Object.FindObjectsOfType<UmaContainerCharacter>())
                    {
                        int owner = _charas.Count;
                        _charas.Add(chara);
                        bool isMain = IsMainMember(chara);
                        _charaIsMain.Add(isMain);
                        _charaSingerId.Add(ComputeSingerId(chara, isMain, owner));
                        // Incluye piezas inactivas (las mejillas se activan/desactivan en pleno show);
                        // en el dibujo se saltan las que no se esten dibujando.
                        foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r.gameObject.layer != CHARA_LAYER) continue;
                            _renderers.Add(r);
                            _owners.Add(owner);
                        }
                    }
                }
                return _renderers;
            }
        }

        internal static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
    }

    /// <summary>Copia la imagen antes de SunShafts y dibuja la mascara de personajes.</summary>
    public sealed class CharaPostEffectLimiterCapturePass : ScriptableRenderPass
    {
        private PostImageEffectFeature _feature;

        public void Setup(PostImageEffectFeature feature, RenderPassEvent evt)
        {
            _feature = feature;
            renderPassEvent = evt;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CharaPostEffectLimiter.Captured = false;
            CharaPostEffectLimiter.NoDiffusionReady = false;
            CharaPostEffectLimiter.ResetLayers();
            var material = CharaPostEffectLimiter.Material;
            if (_feature == null || material == null) return;

            var cmd = CommandBufferPool.Get("CharaLimiter.Capture");
            try
            {
                var settings = CharaPostEffectLimiterSettings.Instance;
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0; desc.msaaSamples = 1; desc.bindMS = false;
                desc.useMipMap = desc.autoGenerateMips = false;

                // 1) Imagen limpia (antes de SunShafts / LightShafts / Bloom / PostFilm)
                cmd.GetTemporaryRT(CharaPostEffectLimiter.OrigId, desc, FilterMode.Bilinear);
                cmd.Blit(_feature.SourceRT.RtId, CharaPostEffectLimiter.OrigId);

                // 2) Mascara: R = profundidad lineal, G = peso, B = id de cantante; depth buffer propio
                cmd.GetTemporaryRT(CharaPostEffectLimiter.MaskId, desc.width, desc.height, 24,
                    FilterMode.Point, RenderTextureFormat.ARGBFloat);
                cmd.SetRenderTarget(CharaPostEffectLimiter.MaskId);
                cmd.ClearRenderTarget(true, true, Color.clear);
                var camData = renderingData.cameraData;
                RenderingUtils.SetViewAndProjectionMatrices(cmd, camData.GetViewMatrix(),
                    camData.GetGPUProjectionMatrix(), false);
                var renderers = CharaPostEffectLimiter.CharaRenderers;
                CharaPostEffectLimiter.UpdateCharaWeights(settings);
                for (int i = 0; i < renderers.Count; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    // Solo lo que de verdad se dibuja este frame: un SkinnedMeshRenderer descartado por
                    // culling no actualiza su skinning y se pintaria con una pose antigua.
                    if (!r.isVisible || r.forceRenderingOff ||
                        r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    var mesh = CharaPostEffectLimiter.GetMesh(r);
                    if (mesh == null) continue;
                    cmd.SetGlobalFloat(CharaPostEffectLimiter.WeightId, CharaPostEffectLimiter.WeightOfRenderer(i));
                    cmd.SetGlobalFloat(CharaPostEffectLimiter.SingerId, CharaPostEffectLimiter.SingerIdOfRenderer(i));
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        cmd.DrawRenderer(r, material, sub, 0);
                }

                context.ExecuteCommandBuffer(cmd);
                CharaPostEffectLimiter.Captured = true;
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }

    /// <summary>Aplica los limitadores tras Bloom/PostFilm y entrega el resultado como nuevo SourceRT.</summary>
    public sealed class CharaPostEffectLimiterApplyPass : ScriptableRenderPass
    {
        private PostImageEffectFeature _feature;

        public void Setup(PostImageEffectFeature feature, RenderPassEvent evt)
        {
            _feature = feature;
            renderPassEvent = evt;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var material = CharaPostEffectLimiter.Material;
            var settings = CharaPostEffectLimiterSettings.Instance;
            var cmd = CommandBufferPool.Get("CharaLimiter.Apply");
            try
            {
                if (_feature != null && material != null && CharaPostEffectLimiter.Captured)
                {
                    var desc = renderingData.cameraData.cameraTargetDescriptor;
                    desc.depthBufferBits = 0; desc.msaaSamples = 1; desc.bindMS = false;
                    desc.useMipMap = desc.autoGenerateMips = false;

                    var source = _feature.SourceRT;
                    cmd.GetTemporaryRT(CharaPostEffectLimiter.OutId, desc, FilterMode.Bilinear);

                    // Fuerza del Saturation Limit = 0 si su tick esta apagado (solo actua el Diffusion Limit).
                    var mainParams = settings.ShaderParams;
                    if (!CharaPostEffectLimiter.SaturationActive) mainParams.z = 0f;
                    cmd.SetGlobalVector(CharaPostEffectLimiter.ParamsId, mainParams);

                    // Diffusion Limit: x = activo (hay version sin Diffusion este frame),
                    // y = atenuar en siluetas, z = atenuar en planos abiertos.
                    bool noDiff = CharaPostEffectLimiter.NoDiffusionReady && CharaPostEffectLimiter.DiffusionActive;
                    cmd.SetGlobalVector(CharaPostEffectLimiter.DiffusionParamsId, new Vector4(
                        noDiff ? 1f : 0f,
                        settings.diffusionUseSilhouetteFade ? 1f : 0f,
                        settings.diffusionUseCoverageFade ? 1f : 0f, 0f));
                    if (noDiff)
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.NoDiffusionId, CharaPostEffectLimiter.NoDiffusionId);
                    else
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.NoDiffusionId, Texture2D.blackTexture);

                    cmd.SetGlobalVector(CharaPostEffectLimiter.CurveParamsId, settings.ShaderCurveParams);
                    // yz = tamaño de texel de la imagen de render (no se depende de que Unity rellene
                    // *_TexelSize en texturas globales temporales).
                    cmd.SetGlobalVector(CharaPostEffectLimiter.RefParamsId, new Vector4(0f,
                        1f / Mathf.Max(1, desc.width), 1f / Mathf.Max(1, desc.height), 0f));
                    cmd.SetGlobalVector(CharaPostEffectLimiter.ThresholdsId, settings.ShaderThresholds);
                    cmd.SetGlobalVector(CharaPostEffectLimiter.ReduceId, settings.ShaderReduce);
                    cmd.SetGlobalTexture(CharaPostEffectLimiter.OrigId, CharaPostEffectLimiter.OrigId);
                    cmd.SetGlobalTexture(CharaPostEffectLimiter.MaskId, CharaPostEffectLimiter.MaskId);

                    // Cobertura POR CANTANTE: 2 RT 256x256 RGBA con mipmaps automaticos (un canal por
                    // cantante: RT0 = 1-4, RT1 = 5-8); el ultimo mip (1x1) de cada canal es la fraccion
                    // de pantalla visible de ese cantante. Todo en GPU y en el mismo frame.
                    var coverageDesc = new RenderTextureDescriptor(CharaPostEffectLimiter.COVERAGE_SIZE,
                        CharaPostEffectLimiter.COVERAGE_SIZE, RenderTextureFormat.ARGBHalf, 0)
                    {
                        useMipMap = true,
                        autoGenerateMips = true,
                        msaaSamples = 1
                    };
                    bool twoGroups = CharaPostEffectLimiter.MaxSingerId > 4;
                    cmd.GetTemporaryRT(CharaPostEffectLimiter.Coverage0Id, coverageDesc, FilterMode.Bilinear);
                    cmd.SetGlobalFloat(CharaPostEffectLimiter.CoverageGroupId, 0f);
                    cmd.Blit(CharaPostEffectLimiter.MaskId, CharaPostEffectLimiter.Coverage0Id, material, 2);
                    cmd.SetGlobalTexture(CharaPostEffectLimiter.Coverage0Id, CharaPostEffectLimiter.Coverage0Id);
                    if (twoGroups)
                    {
                        cmd.GetTemporaryRT(CharaPostEffectLimiter.Coverage1Id, coverageDesc, FilterMode.Bilinear);
                        cmd.SetGlobalFloat(CharaPostEffectLimiter.CoverageGroupId, 1f);
                        cmd.Blit(CharaPostEffectLimiter.MaskId, CharaPostEffectLimiter.Coverage1Id, material, 2);
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.Coverage1Id, CharaPostEffectLimiter.Coverage1Id);
                    }
                    else
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.Coverage1Id, Texture2D.blackTexture);
                    cmd.SetGlobalFloat(CharaPostEffectLimiter.CoverageMipId, CharaPostEffectLimiter.COVERAGE_LAST_MIP);

                    // ---- Limitador por capas ----
                    // "Normal" = imagen final (en los cantantes, la version sin Diffusion si actua el
                    // Diffusion Limit). Sin pasada DOF este frame (solo shafts): Base = B = BPF = original.
                    bool layers = CharaPostEffectLimiter.SaturationActive;
                    _measured.Clear();
                    if (layers)
                    {
                        RenderTargetIdentifier normal = noDiff
                            ? new RenderTargetIdentifier(CharaPostEffectLimiter.NoDiffusionId) : source.RtId;
                        int baseL, bL, bpfL;
                        if (CharaPostEffectLimiter.LayersReady)
                        {
                            baseL = CharaPostEffectLimiter.LayerBase;
                            bL = CharaPostEffectLimiter.LayerB;
                            bpfL = CharaPostEffectLimiter.LayerBPF;
                        }
                        else
                        {
                            baseL = bL = bpfL = CharaPostEffectLimiter.ShaftsQueued
                                ? CharaPostEffectLimiter.OrigId : CharaPostEffectLimiter.LAYER_NORMAL;
                        }

                        cmd.SetGlobalTexture(CharaPostEffectLimiter.LayerBaseId, Resolve(baseL, normal));
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.LayerBloomId, Resolve(bL, normal));
                        cmd.SetGlobalTexture(CharaPostEffectLimiter.LayerBPFId, Resolve(bpfL, normal));

                        // Medida por transicion (Base->B, B->BPF, BPF->Normal): media del cambio en el
                        // canal de cada cantante (ultimo mip). Si las dos capas son la misma, 0 sin coste.
                        _from[0] = baseL; _from[1] = bL; _from[2] = bpfL;
                        _to[0] = bL; _to[1] = bpfL; _to[2] = CharaPostEffectLimiter.LAYER_NORMAL;
                        for (int t = 0; t < 3; t++)
                        {
                            for (int g = 0; g < 2; g++)
                            {
                                int mid = CharaPostEffectLimiter.MeasureIds[t * 2 + g];
                                if (_from[t] == _to[t] || (g == 1 && !twoGroups))
                                {
                                    cmd.SetGlobalTexture(mid, Texture2D.blackTexture);
                                    continue;
                                }
                                cmd.GetTemporaryRT(mid, coverageDesc, FilterMode.Bilinear);
                                _measured.Add(mid);
                                cmd.SetGlobalTexture(CharaPostEffectLimiter.LayerAId, Resolve(_from[t], normal));
                                cmd.SetGlobalTexture(CharaPostEffectLimiter.LayerBId, Resolve(_to[t], normal));
                                cmd.SetGlobalFloat(CharaPostEffectLimiter.CoverageGroupId, g);
                                cmd.Blit(CharaPostEffectLimiter.MaskId, mid, material, 3);
                                cmd.SetGlobalTexture(mid, mid);
                            }
                        }
                    }
                    cmd.SetGlobalVector(CharaPostEffectLimiter.LayerFlagsId, new Vector4(layers ? 1f : 0f, 0f, 0f, 0f));

                    cmd.Blit(source.RtId, CharaPostEffectLimiter.OutId, material, 1);
                    cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.Coverage0Id);
                    if (twoGroups) cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.Coverage1Id);
                    for (int i = 0; i < _measured.Count; i++) cmd.ReleaseTemporaryRT(_measured[i]);
                    _feature.SetSourceRT(RenderTextureHandle.Make(CharaPostEffectLimiter.OutId,
                        desc.width, desc.height), cmd, ref renderingData);
                }
                if (CharaPostEffectLimiter.Captured)
                {
                    cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.OrigId);
                    cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.MaskId);
                }
                if (CharaPostEffectLimiter.NoDiffusionReady)
                    cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.NoDiffusionId);
                for (int i = 0; i < CharaPostEffectLimiter.LayerAllocated.Count; i++)
                    cmd.ReleaseTemporaryRT(CharaPostEffectLimiter.LayerAllocated[i]);
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CharaPostEffectLimiter.Captured = false;
                CharaPostEffectLimiter.NoDiffusionReady = false;
                CharaPostEffectLimiter.ResetLayers();
                CommandBufferPool.Release(cmd);
            }
        }

        private readonly List<int> _measured = new List<int>(6);
        private readonly int[] _from = new int[3];
        private readonly int[] _to = new int[3];

        private static RenderTargetIdentifier Resolve(int layer, RenderTargetIdentifier normal) =>
            layer == CharaPostEffectLimiter.LAYER_NORMAL ? normal : new RenderTargetIdentifier(layer);
    }
}
