using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // The ear random motion members of Gallop.LiveModelController (code_JP Gallop.LiveModelController.c). The viewer
    // has no LiveModelController (its per-character counterpart is UmaContainerCharacter, which is not touched), so
    // the fields and methods used by the timeline ear random motion live in this per-character object, owned by
    // FaceDrivenKeyTarget.
    //   _randomEarMotionDictionary          +0x840  Dictionary<EarType, List<DrivenKeyAnimation>>
    //   _currentFrameEarRandomMotion        +0x878  (ResetFrameEar / StopEarRandomMotion write -1)
    //   PreloadEarRandomMotionsForTimeline  0x57B9E20 (@0x7ffd59b39e20)
    //   LoadEarMotionsForTypes              0x57B82E0 (@0x7ffd59b382e0)
    //   PlayEarRandomMotionFromTimeline     0x57B8D80 (@0x7ffd59b38d80)
    //   StopEarRandomMotion                 0x57BB980 (@0x7ffd59b3b980)
    //   ResetFrameEar                       0x57BA2F0 (@0x7ffd59b3a2f0)
    // NOT ported: the SystemText ear motion (_systemTextEarMotionDictionary +0x748, IsPlaySystemTextEarMotion,
    // HasSystemTextEarMotion, GetCurrentSystemTextEarType, PreloadAllEarRandomMotion): the viewer has no
    // LiveSystemTextBodyMotionPlayer, so the three queries below always answer "no system text ear motion".
    public sealed class LiveModelControllerEarMotion
    {
        // Gallop.LiveModelController.LoadEarMotionsForTypes builds the asset path with TextUtil.Format of a literal
        // that the decompile does not show; the game path is
        // "3d/Motion/Event/Facial/Type00/anm_eve_type00_{MotionName}_driven". The viewer's asset index is lowercase.
        private const string kEarMotionPathFormat = "3d/motion/event/facial/type00/anm_eve_type00_{0}_driven";

        private readonly FaceDrivenKeyTarget _face;
        private readonly Dictionary<EarType, List<DrivenKeyAnimation>> _randomEarMotionDictionary
            = new Dictionary<EarType, List<DrivenKeyAnimation>>(); // +0x840

        // +0x878 (get_CurrentFrameEarRandomMotion @0x57BD420). It is -1 before the first key.
        public int CurrentFrameEarRandomMotion { get; private set; } = -1;

        public IReadOnlyDictionary<EarType, List<DrivenKeyAnimation>> RandomEarMotionDictionary => _randomEarMotionDictionary;

        public LiveModelControllerEarMotion(FaceDrivenKeyTarget face)
        {
            _face = face;
        }

        // PreloadEarRandomMotionsForTimeline @0x7ffd59b39e20: Dictionary.Clear(), then LoadEarMotionsForTypes.
        public void PreloadEarRandomMotionsForTimeline(IEnumerable<EarType> earTypes)
        {
            _randomEarMotionDictionary.Clear();
            LoadEarMotionsForTypes(earTypes);
        }

        // LoadEarMotionsForTypes @0x7ffd59b382e0: for each EarType, the ear rows of MasterRandomEarTailMotion.GetEarList
        // with that ear_type are loaded (DrivenKeyAnimation, path format above) into a list (null assets skipped) and the
        // list is added to the dictionary when it has entries.
        public void LoadEarMotionsForTypes(IEnumerable<EarType> earTypes)
        {
            if (earTypes == null) return;
            var controller = UmaDatabaseController.Instance;
            var master = controller != null ? controller.RandomEarTailMotionData : null;
            if (master == null) return;
            var earList = master.GetEarList();
            foreach (EarType earType in earTypes)
            {
                var animations = new List<DrivenKeyAnimation>();
                foreach (var row in earList)
                {
                    if (row.EarType != (int)earType) continue;
                    string path = string.Format(kEarMotionPathFormat, row.MotionName.ToLowerInvariant());
                    if (!UmaViewerMain.Instance.AbList.TryGetValue(path, out var entry))
                    {
                        Debug.LogWarning("[RandomEar] ear motion asset not in the asset index: " + path);
                        continue;
                    }
                    var animation = entry.Get<DrivenKeyAnimation>();
                    if (animation == null)
                    {
                        Debug.LogWarning("[RandomEar] DrivenKeyAnimation not found in " + path);
                        continue;
                    }
                    animations.Add(animation);
                }
                if (animations.Count > 0) _randomEarMotionDictionary[earType] = animations;
            }
        }

        // PlayEarRandomMotionFromTimeline @0x7ffd59b38d80: when the dictionary has the type with entries,
        // ModelController.PlayEarDrivenKey(list[index]) and _currentFrameEarRandomMotion = frame (only then).
        // The game's List indexer throws on an out-of-range index; here the key is ignored.
        public void PlayEarRandomMotionFromTimeline(EarType earType, int index, int frame)
        {
            if (!_randomEarMotionDictionary.TryGetValue(earType, out var list) || list.Count <= 0) return;
            if (index < 0 || index >= list.Count) return;
            _face.PlayEarDrivenKey(list[index]);
            CurrentFrameEarRandomMotion = frame;
        }

        // StopEarRandomMotion @0x7ffd59b3b980: DrivenKeyComponent.StopLayer(1), _currentFrameEarRandomMotion = -1.
        public void StopEarRandomMotion()
        {
            _face.StopEarDrivenKeyLayer();
            CurrentFrameEarRandomMotion = -1;
        }

        // ResetFrameEar @0x7ffd59b3a2f0.
        public void ResetFrameEar()
        {
            CurrentFrameEarRandomMotion = -1;
        }

        // SystemText ear motion queries (not ported, see the class comment).
        public bool IsPlaySystemTextEarMotion() => false;   // @0x7ffd59b38270
        public bool HasSystemTextEarMotion() => false;      // @0x7ffd59b36af0
        public EarType? GetCurrentSystemTextEarType() => null; // @0x7ffd59b365f0
    }
}
