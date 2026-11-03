using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Qué post efectos con pista propia usa de verdad la canción cargada. Los efectos de esta lista
    /// que la canción nunca activa (p. ej. Vortex, que solo usa una canción) se ocultan del listado de
    /// post efectos. El resto de efectos (DOF, bloom, PostFilm, niebla, shafts, limitadores...) no
    /// dependen de una pista de la cámara y se muestran siempre.
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

        /// <summary>false si el efecto tiene pista propia y la canción cargada no la usa.</summary>
        public static bool IsRelevant(PostEffectId id)
        {
            if (_used == null) return true;
            switch (id)
            {
                case PostEffectId.RadialBlur:
                case PostEffectId.TiltShift:
                case PostEffectId.ChromaticAberration:
                case PostEffectId.Vortex:
                case PostEffectId.Fluctuation:
                case PostEffectId.ColorCorrection:
                case PostEffectId.Exposure:
                case PostEffectId.ToneCurve:
                case PostEffectId.LightShafts:
                    return _used.Contains(id);
                default:
                    return true;
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
            }
            return used;
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
