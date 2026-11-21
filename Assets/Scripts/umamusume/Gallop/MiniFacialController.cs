using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop
{
    // Port of Gallop.MiniFacialController (code_JP Gallop.MiniFacialController.c,
    // dump_jp.cs TypeDefIndex 33555206; constants from the global dump.cs, TypeDefIndex 349).
    //
    // The mini face is made of atlas quads (M_Mayu_L/R, M_Eye, M_Mouth). The facial clip animates
    // the x position of the Ctrl nodes of pfb_mini_facial_locator; after the driven-key update
    // AfterApplyDrivenKey turns each x into an atlas cell id (round(x * UNITY_SCALE)) and selects
    // that cell with GridTexture / GridTextureOffset.
    //
    // Not ported (no data / no consumer in the viewer yet):
    // - SetFace / SetFaceType and the IsUseFaceType branch of AfterApplyDrivenKey
    //   (need MasterMiniFaceTypeData).
    // - SetLipSync and the MasterMiniMouthType lookup at the end of SetMouth (lip sync only).
    // - CheekController (SetCheekCtrl, and the CheekController.SetVisible(false) call in Play).
    // - OverrideAnimation / Play: the game swaps the clip into an AnimatorOverrideController built
    //   on the mini animator controller and calls Animator.Play(stateHash, -1, normalizedTime).
    //   The override key (string literal DAT_7ffd5bc62870) and the state name (DAT_7ffd5bb1d728)
    //   are unresolved metadata literals, so PlayClip samples the same clip at the same
    //   normalized time through a PlayableGraph on the locator Animator instead.
    public class MiniFacialController
    {
        private const int EYEBROW_TEXTURE_SIZE_X = 1024;
        private const int EYEBROW_TEXTURE_SIZE_Y = 1024;
        private const int EYEBROW_PARTS_SIZE_X = 256;
        private const int EYEBROW_PARTS_SIZE_Y = 256;
        private const int EYE_TEXTURE_SIZE_X = 1024;
        private const int EYE_TEXTURE_SIZE_Y = 1024;
        private const int EYE_PARTS_SIZE_X = 128;
        private const int EYE_PARTS_SIZE_Y = 128;
        private const int MOUTH_TEXTURE_SIZE_X = 512;
        private const int MOUTH_TEXTURE_SIZE_Y = 1024;
        private const int MOUTH_PARTS_SIZE_X = 128;
        private const int MOUTH_PARTS_SIZE_Y = 128;
        // Read from the binary: AfterApplyDrivenKey multiplies by DAT_7ffd5a8baff8 = -100.0f.
        private const int UNITY_SCALE = -100;
        public const int EYEBROW_MAX_PARTS_NUM = 16;
        public const int EYE_MAX_PARTS_NUM = 32;
        public const int MOUTH_MAX_PARTS_NUM = 32;
        public const string LOCATOR_NAME_EYE_L = "Eye_L_Ctrl";
        public const string LOCATOR_NAME_EYE_R = "Eye_R_Ctrl";
        public const string LOCATOR_NAME_EYEBROW_L = "Mayu_L_Ctrl";
        public const string LOCATOR_NAME_EYEBROW_R = "Mayu_R_Ctrl";
        public const string LOCATOR_NAME_MOUTH = "Mouth_Ctrl";

        // Grid cell sizes passed by Init, read from the binary:
        // DAT_7ffd5a640f0c = 0.25f (cell width, and eyebrow cell height),
        // DAT_7ffd5a8bb80c = 0.125f (eye and mouth cell height). Column count is 4.
        private const float GRID_SIZE = 0.25f;
        private const float GRID_SIZE_HALF = 0.125f;
        private const int GRID_COLUMN_NUM = 4;

        private GridTexture _eyebrowL;          // 0x10
        private GridTexture _eyebrowR;          // 0x18
        private GridTexture _eyeL;              // 0x20
        private GridTexture _eyeR;              // 0x28
        private GridTexture _mouth;             // 0x30
        private Transform _locatorTrs;          // 0x40
        private Transform _locatorEyeLTrs;      // 0x48
        private Transform _locatorEyeRTrs;      // 0x50
        private Transform _locatorEyebrowLTrs;  // 0x58
        private Transform _locatorEyebrowRTrs;  // 0x60
        private Transform _locatorMouthTrs;     // 0x68
        private Animator _animator;             // 0x70
        private bool _isPause;                  // 0x90

        // Viewer-side replacement of the AnimatorOverrideController playback (see header).
        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;
        private AnimationClip _playingClip;

        public bool IsLocatorControll { get; set; }        // 0x38
        public bool IsUseFaceType { get; set; }            // 0x39
        public bool IsOverrideMouthByLipSync { get; set; } // 0xA8
        public Animator Animator => _animator;

        // MiniFacialController..ctor @0x7ffd549a32b0
        public MiniFacialController()
        {
            IsLocatorControll = true;
        }

        // MiniFacialController.Init @0x7ffd599080b0
        // Called from MiniModelController.InitFacial @0x7ffd57843fd0 with the eyebrow L/R, eye and
        // mouth materials of the parts holder and the instantiated pfb_mini_facial_locator.
        public void Init(Material eyebrowL, Material eyebrowR, Material eye, Material mouth,
            Transform locator, Animator animator)
        {
            _eyebrowL = new GridTexture(eyebrowL, GRID_SIZE, GRID_SIZE, GRID_COLUMN_NUM);
            _eyebrowR = new GridTexture(eyebrowR, GRID_SIZE, GRID_SIZE, GRID_COLUMN_NUM);
            _eyeL = new GridTextureOffset(eye, GRID_SIZE, GRID_SIZE_HALF, GRID_COLUMN_NUM, true);
            _eyeR = new GridTextureOffset(eye, GRID_SIZE, GRID_SIZE_HALF, GRID_COLUMN_NUM, false);
            _mouth = new GridTexture(mouth, GRID_SIZE, GRID_SIZE_HALF, GRID_COLUMN_NUM);
            _locatorTrs = locator;
            if (_locatorTrs != null)
            {
                _locatorEyeLTrs = _locatorTrs.Find(LOCATOR_NAME_EYE_L);
                _locatorEyeRTrs = _locatorTrs.Find(LOCATOR_NAME_EYE_R);
                _locatorEyebrowLTrs = _locatorTrs.Find(LOCATOR_NAME_EYEBROW_L);
                _locatorEyebrowRTrs = _locatorTrs.Find(LOCATOR_NAME_EYEBROW_R);
                _locatorMouthTrs = _locatorTrs.Find(LOCATOR_NAME_MOUTH);
            }
            _animator = animator;
        }

        public bool HasAllLocators => _locatorEyeLTrs != null && _locatorEyeRTrs != null
            && _locatorEyebrowLTrs != null && _locatorEyebrowRTrs != null && _locatorMouthTrs != null;

        // MiniFacialController.AfterApplyDrivenKey @0x7ffd59907ee0 (locator branch).
        // The rounding constant read from the binary is MidpointRounding.AwayFromZero; the
        // project's Gallop.Math.Round(float) already rounds that way.
        public void AfterApplyDrivenKey()
        {
            if (_isPause) return;
            if (IsUseFaceType) return; // face-type branch not ported (MasterMiniFaceTypeData)
            if (!IsLocatorControll) return;
            if (!HasAllLocators) return; // the game raises a null reference here

            int eyeL = Math.Round(_locatorEyeLTrs.localPosition.x * UNITY_SCALE);
            int eyeR = Math.Round(_locatorEyeRTrs.localPosition.x * UNITY_SCALE);
            SetEye(eyeL, eyeR);
            int eyebrowL = Math.Round(_locatorEyebrowLTrs.localPosition.x * UNITY_SCALE);
            int eyebrowR = Math.Round(_locatorEyebrowRTrs.localPosition.x * UNITY_SCALE);
            SetEyebrow(eyebrowL, eyebrowR);
            SetMouth(Math.Round(_locatorMouthTrs.localPosition.x * UNITY_SCALE));
        }

        // MiniFacialController.SetEyebrow @0x7ffd599085f0
        public void SetEyebrow(int left, int right)
        {
            if (_eyebrowL == null || _eyebrowR == null) return;
            _eyebrowL.SetId(left);
            _eyebrowR.SetId(right);
        }

        // MiniFacialController.SetEye @0x7ffd59908580
        public void SetEye(int left, int right)
        {
            if (_eyeL == null || _eyeR == null) return;
            _eyeL.SetId(left);
            _eyeR.SetId(right);
        }

        // MiniFacialController.SetMouth @0x7ffd59908ae0
        // The trailing MasterMiniMouthType.Get (lip-sync base type) is not ported.
        public void SetMouth(int id)
        {
            if (_mouth == null) return;
            if (!IsOverrideMouthByLipSync)
                _mouth.SetId(id);
        }

        // MiniFacialController.Pause @0x7ffd57a42ac0
        public void Pause() => _isPause = true;

        // MiniFacialController.Resume @0x7ffd59908570
        public void Resume() => _isPause = false;

        // Replacement of OverrideAnimation @0x7ffd599083d0 + Play @0x7ffd59908470 (see header):
        // poses the locator with clip at normalizedTime * length.
        public void PlayClip(AnimationClip clip, float normalizedTime)
        {
            if (_animator == null || clip == null) return;
            if (_playingClip != clip || !_graph.IsValid())
            {
                if (_graph.IsValid()) _graph.Destroy();
                _playingClip = clip;
                _graph = PlayableGraph.Create("MiniFacialLocator");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                _clipPlayable.SetApplyFootIK(false);
                _clipPlayable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(_graph, "MiniFacial", _animator);
                output.SetSourcePlayable(_clipPlayable);
                _graph.Play();
            }
            _clipPlayable.SetTime(normalizedTime * clip.length);
            _graph.Evaluate(0f);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _playingClip = null;
        }
    }
}
