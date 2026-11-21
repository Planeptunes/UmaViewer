using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // CySpring physics of a live prop (towel of prop1069_00: Sp_Ro_Tow0_* bones + collisions against the
    // wearer's Elbow_R / Arm_R / Wrist_R / Chest). The game builds it in Props.InitCySpring: the cloth asset
    // ("3d/chara/toonprop/toon_prop<id>/clothes/pfb_toon_prop<id>_cloth00") is imported as BODY cloth data,
    // CySpringController.AddController(propObject, no hip, owner) + Load, and every frame
    // Props.BeginCySpringSimulate / EndCySpringSimulation step it (JP 0x7ffd5925b190 / 0x7ffd59259d90).
    // This is the port of that flow on top of the viewer's CySpringController.
    public sealed class LivePropCySpringOwner : CySpringOwner
    {
        private readonly UmaContainerCharacter _character;

        public LivePropCySpringOwner(UmaContainerCharacter character) : base(null)
        {
            _character = character;
        }

        public override bool IsCharaModel() { return false; }
        public override int GetCharaID() { return _character != null && _character.CharaEntry != null ? _character.CharaEntry.Id : 0; }
        public override int GetDressID() { return 0; }
        public override int GetHeadID() { return 0; }
        public override float GetCySpringCorrectScale(bool isHead) { return Scale(); }
        public override float GetBodyScale() { return Scale(); }
        public override float GetTotalScale() { return Scale(); }

        private float Scale()
        {
            return _character == null || _character.BodyScale <= 0.0f ? 1.0f : _character.BodyScale;
        }
    }

    // Steps the prop's CySpringController once per rendered frame with the frame delta, after the Animator has posed the
    // wearer (LateUpdate): Props.BeginCySpringSimulate @0x7ffd59259d90 (copy the wearer posture, TryUpdateWarmUp, no step
    // when dt is 0, BeginSimulation(dt)) and Props.AlterLateUpdatePost -> EndSimulation. The game has no accumulator, no
    // fixed step and no dt clamp for props; the 30 / 60 Hz cadence lives inside the solver (UpdateMode: props keep
    // ModeNormal = 30 Hz, characters of a 60 fps live use Mode60FPS).
    public sealed class LivePropPhysicsDriver : MonoBehaviour
    {
        private CySpringController _controller;
        private UmaContainerCharacter _wearer;
        private bool _pausedByLive;

        public static LivePropPhysicsDriver Create(GameObject owner, CySpringController controller, float warmUpTime,
            UmaContainerCharacter wearer = null)
        {
            var driver = owner.AddComponent<LivePropPhysicsDriver>();
            driver._controller = controller;
            driver._wearer = wearer;
            controller.Reset();
            // Props only RESERVE the warm-up (Props.ReserveWarmingUpCySpring @0x7ffd5925c800); it runs in the next
            // BeginCySpringSimulate (TryUpdateWarmUp), not at creation.
            if (warmUpTime > 0.0f)
                controller.ReserveWarmingUpCySpring(warmUpTime);
            return driver;
        }

        // Props.ReserveWarmingUpCySpring @0x7ffd5925c800: reserves the prop controller's warm-up.
        public void ReserveWarmingUpCySpring(float warmUpTime)
        {
            if (_controller != null)
                _controller.ReserveWarmingUpCySpring(warmUpTime);
        }

        // Props.BeginCySpringSimulate (JP 0x7ffd59259d90) copies the wearer controller's _previousPos (+0x94) and
        // _previousRot (+0xB0) into the prop controller right before TryUpdateWarmUp / BeginSimulation.
        private void CopyWearerPreviousPosture()
        {
            var wearerController = _wearer != null ? _wearer.BodyCySpringController : null;
            if (wearerController == null || _controller == null)
                return;
            _controller.PreviousPos = wearerController.PreviousPos;
            _controller.PreviousRot = wearerController.PreviousRot;
        }

        private void LateUpdate()
        {
            if (_controller == null || !isActiveAndEnabled)
                return;

            // Pause like the game (PropsManager.Pause @0x7ffd592580f0 -> Props.PauseCySpring @0x7ffd5925c500 ->
            // CySpringController.Pause: _isPlaying = 0, nothing is stepped or re-applied), except on a step released by
            // the Motion Probe (LiveProbeMotion), which advances exactly 1/60 s.
            bool frozen = Gallop.Live.Director.IsPlaybackPaused && !LiveProbeMotion.StepActive;
            if (frozen)
            {
                if (_controller.IsPlaying)
                {
                    _controller.Pause();
                    _pausedByLive = true;
                }
            }
            else if (_pausedByLive)
            {
                _controller.Resume();
                _pausedByLive = false;
            }

            // Props.BeginCySpringSimulate: wearer posture copy, then TryUpdateWarmUp every frame (no-op unless reserved).
            CopyWearerPreviousPosture();
            _controller.TryUpdateWarmUp();

            // ModelController.get_DeltaTime = Time.deltaTime; Math.IsFloatEqual(dt, 0) -> no solver step this frame.
            float dt = LiveProbeMotion.Delta(Time.deltaTime);
            if (Mathf.Approximately(dt, 0f))
                return;

            _controller.BeginSimulation(dt, false);
            _controller.EndSimulation();
        }
    }
}
