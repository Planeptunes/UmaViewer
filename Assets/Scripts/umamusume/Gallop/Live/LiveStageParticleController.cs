using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Stage consumers from master5 Director.OnParticleUpdate/OnParticleGroupUpdate.
    // Native Flash.UpdateParticle is a separate simulator and remains owned by LiveFlashController.
    //
    // Stage particles are driven through the ParticleController components of the stage
    // (StageController.SetParticleControllers 0x7ffffc81f550 collects them from the stage
    // hierarchy and initializes them; SetupParticle 0x7ffffc81f750 activates them).
    public sealed class LiveStageParticleController : IDisposable
    {
        private readonly List<ParticleController> controllers = new List<ParticleController>();

        public LiveStageParticleController(Transform stageRoot)
        {
            if (stageRoot == null) return;
            foreach (var controller in stageRoot.GetComponentsInChildren<ParticleController>(true))
            {
                if (controller == null) continue;
                controller.Initialize();
                controller.gameObject.SetActive(true);
                controllers.Add(controller);
            }
        }

        // Director.UpdateParticle (0x7ffffc7962d0): every controller receives the key and
        // resolves the name itself (controller root or one of its child systems).
        public void Update(LiveTimelineParticleData group, float emissionRate)
        {
            if (group == null || string.IsNullOrEmpty(group.name)) return;
            foreach (var controller in controllers)
                if (controller != null) controller.UpdateFromTimeline(group.name, emissionRate);
        }

        // ParticleController.UpdateGroupFromTimeline (0x7ffffc7fe720) only writes flicker shader
        // parameters into the controller's MaterialPropertyBlock (and only when the name matches
        // the controller root); it never touches the emission. Feeding FlickerDarkRate/
        // FlickerLightRate into rateOverTime overwrote the particleList emission every frame.
        public void UpdateGroup(LiveTimelineParticleGroupData group, Vector2 emissionRange)
        {
        }

        // StageController.UpdateParticle (0x7ffffc815730): resume while the live runs, pause
        // while the live is paused.
        public void SetPaused(bool paused)
        {
            foreach (var controller in controllers)
                if (controller != null) controller.SetPaused(paused);
        }

        public void Dispose()
        {
            // Stage owns these ParticleSystems; this controller owns only the lookup cache.
            controllers.Clear();
        }
    }
}
