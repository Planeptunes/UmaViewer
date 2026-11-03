using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Qué post efectos usa de verdad la canción cargada. Los que la canción nunca activa (p. ej.
    /// Vortex, que solo usa una canción) se ocultan del listado de post efectos. Los limitadores
    /// (Saturation Limit, DOF Limit) se muestran solo si la canción usa algún efecto que limitan.
    ///
    /// Una pista "se usa" si alguna key cambia algo: el criterio es el mismo con el que se hizo la
    /// auditoría de docs/POST_EFFECTS_POR_CANCION.md.
    /// </summary>
    public static class LivePostEffectUsage
    {
        // null = canción sin analizar: se muestran todos.
        private static HashSet<PostEffectId> _used;
        private static LiveTimelineData _source;

        /// <summary>Se dispara cuando cambia la canción analizada.</summary>
        public static event System.Action Changed;

        /// <summary>false si la canción cargada no usa el efecto.</summary>
        public static bool IsRelevant(PostEffectId id)
        {
            if (_used == null) return true;
            switch (id)
            {
                // Limitan la luz añadida por Bloom/PostFilm/SunShafts/Diffusion sobre los cantantes.
                case PostEffectId.CharaLimit:
                case PostEffectId.DiffusionLimit:
                    return _used.Contains(PostEffectId.Bloom) || _used.Contains(PostEffectId.PostFilm) ||
                           _used.Contains(PostEffectId.SunShafts) || _used.Contains(PostEffectId.Diffusion);
                case PostEffectId.DofLimit:
                    return _used.Contains(PostEffectId.Dof);
                default:
                    return _used.Contains(id);
            }
        }

        /// <summary>Analiza los datos de la canción (una vez por LiveTimelineData).</summary>
        public static void Refresh(LiveTimelineData data)
        {
            if (data == _source) return;
            _source = data;
            _used = data != null ? Analyze(data) : null;
            Changed?.Invoke();
        }

        /// <summary>
        /// Igual que Refresh, pero leyendo los datos del cutt de la canción SIN cargar el live (para el
        /// menú de grabación, que se abre antes). Si no se puede leer, se muestran todos.
        /// </summary>
        public static void RefreshFromSong(string musicId)
        {
            LiveTimelineData data = null;
            try
            {
                string path = "cutt/cutt_son" + musicId + "/cutt_son" + musicId;
                var main = UmaViewerMain.Instance;
                if (main != null && main.AbList != null && main.AbList.TryGetValue(path, out var entry) && entry != null)
                {
                    var prefab = entry.Get<GameObject>();
                    var control = prefab != null ? prefab.GetComponentInChildren<LiveTimelineControl>(true) : null;
                    data = control != null ? control.data : null;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[LivePostEffectUsage] no se pudo leer el cutt de " + musicId + ": " + ex.Message);
            }
            Refresh(data);
        }

        private static HashSet<PostEffectId> Analyze(LiveTimelineData data)
        {
            var used = new HashSet<PostEffectId>();
            var sheets = data.worksheetList;
            if (sheets == null) return used;

            for (int s = 0; s < sheets.Count; s++)
            {
                var sheet = sheets[s];
                if (sheet == null) continue;

                if (sheet.radialBlurKeys != null)
                    foreach (var k in sheet.radialBlurKeys.thisList)
                        if (k != null && k.moveBlurType >= 1 && k.moveBlurType <= 5 && k.radialBlurPower > 0f)
                            used.Add(PostEffectId.RadialBlur);

                if (sheet.tiltShiftKeys != null)
                    foreach (var k in sheet.tiltShiftKeys.thisList)
                        if (k != null && k.mode != 0) used.Add(PostEffectId.TiltShift);

                if (sheet.VortexKeys != null)
                    foreach (var k in sheet.VortexKeys.thisList)
                        if (k != null && k.IsEnable && k.RotVolume != 0f) used.Add(PostEffectId.Vortex);

                if (sheet.FluctuationKeys != null)
                    foreach (var k in sheet.FluctuationKeys.thisList)
                        if (k != null && k.IsEnable && k.Power > 0f) used.Add(PostEffectId.Fluctuation);

                if (sheet.ExposureKeys != null)
                    foreach (var k in sheet.ExposureKeys.thisList)
                        if (k != null && k.IsEnable && (k.Gain != 0f || k.Lift != 0f)) used.Add(PostEffectId.Exposure);

                if (sheet.ToneCurveKeys != null)
                    foreach (var k in sheet.ToneCurveKeys.thisList)
                        if (k != null && k.IsEnable) used.Add(PostEffectId.ToneCurve);

                if (sheet.colorCorrectionDataLists != null)
                    foreach (var group in sheet.colorCorrectionDataLists)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && k.enable != 0 && k.mode == 0 && k.selective == 0 &&
                                    (Mathf.Abs(k.saturation - 1f) > 0.01f || !IsIdentity(k.redCurve) ||
                                     !IsIdentity(k.greenCurve) || !IsIdentity(k.blueCurve)))
                                    used.Add(PostEffectId.ColorCorrection);

                if (sheet.chromaticAberrationList != null)
                    foreach (var group in sheet.chromaticAberrationList)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && k.isEnable && k.power > 0f &&
                                    (k.redOffset != Vector2.zero || k.greenOffset != Vector2.zero ||
                                     k.blueOffset != Vector2.zero))
                                    used.Add(PostEffectId.ChromaticAberration);

                if (sheet.lightShaftsKeysLine != null)
                    foreach (var group in sheet.lightShaftsKeysLine)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && k.enabled) used.Add(PostEffectId.LightShafts);

                // DOF: bit 18 del atributo (SetupDOFUpdateInfo).
                if (sheet.postEffectDOFKeys != null)
                    foreach (var k in sheet.postEffectDOFKeys.thisList)
                        if (k != null && ((int)k.attribute & (1 << 18)) != 0) used.Add(PostEffectId.Dof);

                if (sheet.postEffectBloomDiffusionKeys != null)
                    foreach (var k in sheet.postEffectBloomDiffusionKeys.thisList)
                        AddBloomDiffusion(used, k);
                if (sheet.postEffectBloomDiffusionMultiCameraKeys != null)
                    foreach (var group in sheet.postEffectBloomDiffusionMultiCameraKeys)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                AddBloomDiffusion(used, k);

                AddPostFilm(used, sheet.postFilmKeys);
                AddPostFilm(used, sheet.postFilm2Keys);
                AddPostFilm(used, sheet.postFilm3Keys);
                AddPostFilm(used, sheet.postFilm1MultiCameraKeys);
                AddPostFilm(used, sheet.postFilm2MultiCameraKeys);
                AddPostFilm(used, sheet.postFilm3MultiCameraKeys);

                // Global fog: con distancia y altura apagadas la key solo deja la niebla desactivada.
                if (sheet.globalFogDataLists != null)
                    foreach (var group in sheet.globalFogDataLists)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && (k.isDistance || k.isHeight)) used.Add(PostEffectId.GlobalFog);

                // Sun shafts: los activa la pista VolumeLight (LiveVolumeLightController.UpdateVolume).
                if (sheet.volumeLightKeys != null)
                    foreach (var group in sheet.volumeLightKeys)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && k.enable && k.power > 0f) used.Add(PostEffectId.SunShafts);

                if (sheet.lensFlareList != null)
                    foreach (var group in sheet.lensFlareList)
                        if (group != null && group.keys != null)
                            foreach (var k in group.keys.thisList)
                                if (k != null && k.enableFlare) used.Add(PostEffectId.LensFlare);

                if (sheet.TransmittedLightKeys != null)
                    foreach (var k in sheet.TransmittedLightKeys.thisList)
                        if (k != null && k.IsEnable) used.Add(PostEffectId.TransmittedLight);
            }
            return used;
        }

        private static void AddBloomDiffusion(HashSet<PostEffectId> used, LiveTimelineKeyPostEffectBloomDiffusionData k)
        {
            if (k == null) return;
            if (k.IsEnabledBloom) used.Add(PostEffectId.Bloom);
            if (k.IsEnabledDiffusion) used.Add(PostEffectId.Diffusion);
        }

        private static void AddPostFilm(HashSet<PostEffectId> used, LiveTimelineKeyPostFilmDataList keys)
        {
            if (keys == null) return;
            foreach (var k in keys.thisList)
                if (k != null && k.filmMode != PostFilmMode.None && k.filmPower > 0f) used.Add(PostEffectId.PostFilm);
        }

        private static void AddPostFilm(HashSet<PostEffectId> used, List<LiveTimelineMultiCameraPostFilmData> groups)
        {
            if (groups == null) return;
            foreach (var group in groups)
                if (group != null && group.keys != null)
                    foreach (var k in group.keys.thisList)
                        if (k != null && k.filmMode != PostFilmMode.None && k.filmPower > 0f) used.Add(PostEffectId.PostFilm);
        }

        // Curva de color "no hace nada": f(t) = t.
        private static bool IsIdentity(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return true;
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                if (Mathf.Abs(curve.Evaluate(t) - t) > 0.02f) return false;
            }
            return true;
        }
    }
}
