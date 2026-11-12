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

    // Steps the prop's CySpringController once per rendered frame, after the Animator has posed the wearer
    // (LateUpdate), with the same fixed-step cadence the characters use (GetTargetCySpringFPS: 30 / 60 Hz).
    public sealed class LivePropPhysicsDriver : MonoBehaviour
    {
        private CySpringController _controller;
        private UmaContainerCharacter _wearer;
        private float _accumulator;

        public static LivePropPhysicsDriver Create(GameObject owner, CySpringController controller, float warmUpTime,
            UmaContainerCharacter wearer = null)
        {
            var driver = owner.AddComponent<LivePropPhysicsDriver>();
            driver._controller = controller;
            driver._wearer = wearer;
            controller.Reset();
            if (warmUpTime > 0.0f)
            {
                controller.ReserveWarmingUpCySpring(warmUpTime);
                driver.CopyWearerPreviousPosture();
                controller.TryUpdateWarmUp();
            }
            return driver;
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

            float dt = Time.deltaTime;
            if (dt <= 0.0f)
                dt = Time.unscaledDeltaTime;
            dt = Mathf.Clamp(dt, 0.0f, 0.25f);

            _controller.RestoreSimulatedBoneBindTranslation();
            CopyWearerPreviousPosture();
            // Props.BeginCySpringSimulate calls TryUpdateWarmUp every frame before the step (no-op unless a warm-up
            // is reserved).
            _controller.TryUpdateWarmUp();

            float targetFps = _controller.TargetCySpringFPS;
            float fixedStep = targetFps > 0.0f ? 1.0f / targetFps : 0.0f;
            if (fixedStep <= 0.0f)
            {
                _controller.BeginSimulation(dt, false);
                _controller.EndSimulation();
                return;
            }

            _accumulator += dt;
            int steps = 0;
            while (_accumulator >= fixedStep && steps < 4)
            {
                _accumulator -= fixedStep;
                _controller.BeginSimulation(fixedStep, false);
                _controller.EndSimulation();
                steps++;
            }

            // Render frame without a solver step: the Animator rewrote the animated pose, re-apply the last result.
            if (steps == 0)
                _controller.ReapplyLastSimulation();

            if (_accumulator > fixedStep * 2.0f)
                _accumulator = fixedStep;
        }
    }
}
