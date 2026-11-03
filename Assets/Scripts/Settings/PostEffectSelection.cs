using System;
using System.Collections.Generic;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Which individual post effects of the live presentation chain are allowed to draw.
    ///
    /// The global switch is <see cref="Config.PostEffects"/> (the "Post effects" checkbox): when it is
    /// off the whole chain is bypassed, exactly as before. These per-effect switches are a second
    /// gate on top of it — an effect draws only when the global switch AND its own tick are on.
    ///
    /// The RECORD panel keeps its own list (<see cref="LiveRecordSettings.postEffectsDisabled"/>),
    /// imposed only while a capture runs (see <see cref="PostEffectSelection.PushOverride"/>), so a
    /// recording can drop effects without changing what the live shows.
    ///
    /// Storage is the list of *disabled* ids in Config, so an effect added later defaults to on.
    /// </summary>
    public enum PostEffectId
    {
        Dof = 0,
        Diffusion,
        Bloom,
        PostFilm,
        RadialBlur,
        TiltShift,
        ChromaticAberration,
        GlobalFog,
        Vortex,
        Fluctuation,
        ColorCorrection,
        Exposure,
        ToneCurve,
        SunShafts,
        LightShafts,
        LensFlare,
        // Limitadores sobre los cantantes (ver CharaPostEffectLimiter): luz añadida por
        // Bloom/PostFilm/SunShafts ("Saturation Limit") y Diffusion ("Diffusion Limit").
        CharaLimit,
        DiffusionLimit,
        // Glow of the stage lights (TransmittedLight track).
        TransmittedLight
    }

    public static class PostEffectSelection
    {
        private static readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded;

        // Lista impuesta temporalmente por el grabador (ver PushOverride). Mientras existe manda sobre
        // la del live, así que una grabación puede usar su propia selección sin tocar Config.
        private static HashSet<string> _overrideDisabled;

        // Panel order and labels.
        public static readonly PostEffectId[] All =
        {
            PostEffectId.Dof,
            PostEffectId.Diffusion,
            PostEffectId.Bloom,
            PostEffectId.PostFilm,
            PostEffectId.RadialBlur,
            PostEffectId.TiltShift,
            PostEffectId.ChromaticAberration,
            PostEffectId.GlobalFog,
            PostEffectId.Vortex,
            PostEffectId.Fluctuation,
            PostEffectId.ColorCorrection,
            PostEffectId.Exposure,
            PostEffectId.ToneCurve,
            PostEffectId.SunShafts,
            PostEffectId.LightShafts,
            PostEffectId.LensFlare,
            PostEffectId.TransmittedLight,
            // TEMPORAL: DiffusionLimit fundido en CharaLimit (ver CharaPostEffectLimiter.DiffusionActive).
            PostEffectId.CharaLimit
        };

        public static string Label(PostEffectId id)
        {
            switch (id)
            {
                case PostEffectId.Dof: return "Depth of field";
                case PostEffectId.Diffusion: return "Diffusion";
                case PostEffectId.Bloom: return "Bloom";
                case PostEffectId.PostFilm: return "Post film";
                case PostEffectId.RadialBlur: return "Radial blur";
                case PostEffectId.TiltShift: return "Tilt shift";
                case PostEffectId.ChromaticAberration: return "Chromatic aberration";
                case PostEffectId.GlobalFog: return "Global fog";
                case PostEffectId.Vortex: return "Vortex";
                case PostEffectId.Fluctuation: return "Fluctuation";
                case PostEffectId.ColorCorrection: return "Color correction";
                case PostEffectId.Exposure: return "Exposure";
                case PostEffectId.ToneCurve: return "Tone curve";
                case PostEffectId.SunShafts: return "Sun shafts";
                case PostEffectId.LightShafts: return "Light shafts";
                case PostEffectId.LensFlare: return "Lens flare";
                case PostEffectId.CharaLimit: return "Saturation Limit";
                case PostEffectId.DiffusionLimit: return "Diffusion Limit";
                case PostEffectId.TransmittedLight: return "Transmitted light";
                default: return id.ToString();
            }
        }

        /// <summary> Reads the disabled list from Config once (safe to call repeatedly). </summary>
        public static void LoadFromConfig()
        {
            if (_loaded) return;
            _loaded = true;
            Disabled.Clear();
            var config = Config.Instance;
            if (config == null || config.PostEffectsDisabled == null) return;
            for (int i = 0; i < config.PostEffectsDisabled.Count; i++)
            {
                string id = config.PostEffectsDisabled[i];
                if (!string.IsNullOrEmpty(id)) Disabled.Add(id);
            }
        }

        public static bool IsEnabled(PostEffectId id)
        {
            if (_overrideDisabled != null) return !_overrideDisabled.Contains(id.ToString());
            LoadFromConfig();
            return !Disabled.Contains(id.ToString());
        }

        // =========================================================================================
        // Override temporal (grabación)
        //
        // El panel de grabación tiene SU PROPIA selección (LiveRecordSettings.postEffectsDisabled):
        // al empezar una captura se impone con PushOverride y al terminar se quita con PopOverride,
        // de modo que el live sigue con la lista que tenía antes de grabar. No escribe en Config.
        // =========================================================================================

        public static bool OverrideActive => _overrideDisabled != null;

        /// <summary> Imposes a disabled list until <see cref="PopOverride"/> (recording only). </summary>
        public static void PushOverride(IEnumerable<string> disabled)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (disabled != null)
            {
                foreach (string id in disabled)
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            _overrideDisabled = set;
        }

        public static void PopOverride()
        {
            _overrideDisabled = null;
        }

        /// <summary> Stores the switch in Config and refreshes the runtime chain. </summary>
        public static void SetEnabled(PostEffectId id, bool enabled)
        {
            LoadFromConfig();
            string key = id.ToString();
            bool changed = enabled ? Disabled.Remove(key) : Disabled.Add(key);
            if (!changed) return;

            var config = Config.Instance;
            if (config != null)
            {
                if (config.PostEffectsDisabled == null)
                    config.PostEffectsDisabled = new List<string>();
                config.PostEffectsDisabled.Clear();
                foreach (string entry in Disabled) config.PostEffectsDisabled.Add(entry);
                config.PostEffectsDisabled.Sort(StringComparer.Ordinal);
                config.UpdateConfig(false);
            }

            UISettingsGraphics.ApplyLivePostEffects();
        }
    }
}
