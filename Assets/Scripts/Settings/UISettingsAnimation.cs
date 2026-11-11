using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UISettingsAnimation : MonoBehaviour
{
    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    public TextMeshProUGUI TitleText;
    public TextMeshProUGUI ProgressText;
    public Slider ProgressSlider;
    public Button PlayButton;
    public Sprite PlayIcon;
    public Sprite PauseIcon;
    public TextMeshProUGUI SpeedText;
    public Slider SpeedSlider;
    public Button VMDButton;

    // If enabled, when progress reaches 1 the animation is reloaded from the start (covers proper
    // loops, loops that did not repeat, and animations without `_loop`). If disabled, any animation
    // (including `_loop` ones) stops at the end.
    public Toggle EnableLoop;

    // State of the single end-of-cycle point. Uses our OWN time counter instead of the Animator's
    // `normalizedTime`: the controller has its own internal loop and an `IsLoop` nobody sets, so the
    // state time is unreliable (sometimes starts at 1, sometimes does not restart). Counting here
    // makes the end of cycle deterministic for all three cases.
    private float _cycleTime;
    private string _cycleClip;
    private bool _cycleFired;

    /// <summary>Resets the end-of-cycle detector (call when a new animation is loaded).</summary>
    public void ResetCycleTracking()
    {
        _cycleTime = 0f;
        _cycleClip = null;
        _cycleFired = false;
    }

    // When the panel is enabled the loop starts ON by default. Independent from UpdateAnimationInfo,
    // which may return before resolving the Toggle if there is no animation yet.
    private void Awake()
    {
        ResolveLoopToggle();
    }

    // If the Toggle is not linked in the Inspector, it is searched by name once.
    private bool _loopToggleSearched;

    // Guarantees the loop starts ON even if the serialized Toggle value in the scene is OFF.
    private bool _loopDefaultApplied;

    private Toggle ResolveLoopToggle()
    {
        if (EnableLoop == null && !_loopToggleSearched)
        {
            _loopToggleSearched = true;

            var toggles = GetComponentsInChildren<Toggle>(true);
            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i] != null && toggles[i].name == "EnableLoop")
                {
                    EnableLoop = toggles[i];
                    break;
                }
            }
        }

        // Initial value comes from Config; SetIsOnWithoutNotify sets isOn + the visual check without
        // firing the Toggle callbacks. The listener saves later user changes to the Config.
        if (EnableLoop != null && !_loopDefaultApplied)
        {
            _loopDefaultApplied = true;
            EnableLoop.SetIsOnWithoutNotify(Config.Instance == null || Config.Instance.AnimationLoop);
            EnableLoop.onValueChanged.AddListener(OnLoopToggleChanged);
        }

        return EnableLoop;
    }

    private void OnLoopToggleChanged(bool on)
    {
        if (Config.Instance != null && Config.Instance.AnimationLoop != on)
        {
            Config.Instance.AnimationLoop = on;
            Config.Instance.UpdateConfig(false);
        }

        // Turned ON while the animation sits stopped at its end: start it again right away.
        if (on && _cycleFired)
            RestartCurrent(Builder.CurrentUMAContainer);
    }

    // The character whose animation the cycle counter is following.
    private UmaContainerCharacter _cycleContainer;

    /// <summary>
    /// Starts the current animation again from the beginning: reloads the last clicked animation if
    /// it belongs to this character, otherwise restarts the current animator states in place (e.g.
    /// the default idle loaded with the character, which was never clicked in the list).
    /// </summary>
    private void RestartCurrent(UmaContainerCharacter container)
    {
        if (!container || !container.UmaAnimator) return;

        var ui = UmaViewerUI.Instance;
        if (ui != null && ui.ReloadLastAnimation(container))
            return;

        var animator = container.UmaAnimator;
        var animator_face = container.UmaFaceAnimator;
        var animator_cam = Builder.AnimationCameraAnimator;
        animator.speed = SpeedSlider.value;
        animator.Play(0, 0, 0);
        animator.Play(0, 2, 0);
        if (animator_cam && animator_cam.runtimeAnimatorController)
        {
            animator_cam.speed = SpeedSlider.value;
            animator_cam.Play(0, -1, 0);
        }
        if (animator_face)
        {
            animator_face.speed = SpeedSlider.value;
            animator_face.Play(0, 0, 0);
            animator_face.Play(0, 1, 0);
        }
        ResetCycleTracking();
    }

    internal void UpdateAnimationInfo(UmaContainerCharacter umaContainer)
    {
        var AnimeClip = umaContainer.OverrideController["clip_2"];
        if (AnimeClip.name == "clip_2")
        {
            ResetCycleTracking();
            return;
        }
        if (!AnimeClip)
            return;

        // New clip or new character: start counting from zero.
        if (_cycleClip != AnimeClip.name || _cycleContainer != umaContainer)
        {
            _cycleContainer = umaContainer;
            _cycleClip = AnimeClip.name;
            _cycleTime = 0f;
            _cycleFired = false;
        }

        var loopToggle = ResolveLoopToggle();
        bool loopOn = (loopToggle != null && loopToggle.isOn);
        bool playingNow = umaContainer.UmaAnimator.speed > 0f;

        // Advance our own counter with real time (respects pause and speed).
        float len = AnimeClip.length;
        if (playingNow && len > 0f)
        {
            _cycleTime += Time.deltaTime * umaContainer.UmaAnimator.speed;
            if (_cycleTime > len)
                _cycleTime = len;
        }

        float normalizedTime = (len > 0f) ? Mathf.Clamp01(_cycleTime / len) : 0f;
        TitleText.text = AnimeClip.name;
        ProgressText.text = string.Format("{0} / {1}", ToFrameFormat(normalizedTime * len, AnimeClip.frameRate), ToFrameFormat(len, AnimeClip.frameRate));
        ProgressSlider.SetValueWithoutNotify(normalizedTime);
        UpdatePlayButtonIcon(playingNow && _cycleTime < len);

        // SINGLE end-of-cycle point: when OUR counter reaches the clip length.
        bool cycleEnded = playingNow && len > 0f && _cycleTime >= len;
        if (cycleEnded && !_cycleFired)
        {
            _cycleFired = true;
            if (loopOn)
            {
                // Call the animation again as the first time (reload it from the start).
                RestartCurrent(umaContainer);
            }
            else
            {
                // EnableLoop OFF: stop ANY animation (including `_loop` ones) at the end.
                umaContainer.UmaAnimator.speed = 0f;
                if (umaContainer.UmaFaceAnimator) umaContainer.UmaFaceAnimator.speed = 0f;
                UpdatePlayButtonIcon(false);
            }
        }
    }

    public void Pause()
    {
        var container = Builder.CurrentUMAContainer;
        if (!container || !container.UmaAnimator) return;
        if (Builder.OverrideController.animationClips.Length == 0) return;

        var animator = container.UmaAnimator;
        var animator_face = container.UmaFaceAnimator;
        var animator_cam = Builder.AnimationCameraAnimator;
        var AnimeState = animator.GetCurrentAnimatorStateInfo(0);
        var state = animator.speed > 0f;
        if (state)
        {
            animator.speed = 0;
            if (animator_face)
                animator_face.speed = 0;
            animator_cam.speed = 0;
            UpdatePlayButtonIcon(false);
        }
        else if (AnimeState.normalizedTime < 1f && !_cycleFired)
        {
            animator.speed = SpeedSlider.value;
            animator_cam.speed = SpeedSlider.value;
            if (animator_face)
                animator_face.speed = SpeedSlider.value;
            UpdatePlayButtonIcon(true);
        }
        else
        {
            // Stopped at the end (our counter fired): replay from the start and restart the counter.
            animator.speed = SpeedSlider.value;
            animator.Play(0, 0, 0);
            animator.Play(0, 2, 0);
            animator_cam.speed = SpeedSlider.value;
            animator_cam.Play(0, -1, 0);
            if (animator_face)
            {
                animator_face.speed = SpeedSlider.value;
                animator_face.Play(0, 0, 0);
                animator_face.Play(0, 1, 0);
            }
            ResetCycleTracking();
            UpdatePlayButtonIcon(true);
        }
    }

    public void ChangeProgress(float val)
    {
        var container = Builder.CurrentUMAContainer;
        if (!container) return;
        var animator = container.UmaAnimator;
        var animator_face = container.UmaFaceAnimator;
        var animator_cam = Builder.AnimationCameraAnimator;
        if (animator != null)
        {
            var AnimeClip = container.OverrideController["clip_2"];

            // Pause and Seek;
            animator.speed = 0;
            animator.Play(0, 0, val);
            animator.Play(0, 2, val);
            if (animator_cam.runtimeAnimatorController)
            {
                animator_cam.speed = 0;
                animator_cam.Play(0, -1, val);
            }
            if (animator_face)
            {
                animator_face.speed = 0;
                animator_face.Play(0, 0, val);
                animator_face.Play(0, 1, val);
            }

            // Keep our cycle counter in sync with the seek (also re-arms the end-of-cycle detector).
            if (AnimeClip.length > 0f)
            {
                _cycleTime = Mathf.Clamp01(val) * AnimeClip.length;
                _cycleFired = false;
            }

            UpdatePlayButtonIcon(false);

            ProgressText.text = string.Format("{0} / {1}", ToFrameFormat(val * AnimeClip.length, AnimeClip.frameRate), ToFrameFormat(AnimeClip.length, AnimeClip.frameRate));
        }
    }

    public void ChangeSpeed(float val)
    {
        var container = Builder.CurrentUMAContainer;
        SpeedText.text = string.Format("Speed: {0:F2}", val);

        if (!container || !container.UmaAnimator) return;

        container.UmaAnimator.speed = val;
        Builder.AnimationCameraAnimator.speed = val;
        if (container.UmaFaceAnimator)
        {
            container.UmaFaceAnimator.speed = val;
        }

        UpdatePlayButtonIcon(val > 0f);
    }

    private void UpdatePlayButtonIcon(bool isPlaying)
    {
        if (PlayButton)
        {
            PlayButton.image.sprite = isPlaying ? PauseIcon : PlayIcon;
        }
    }

    public void EnableRootMotion(bool enable)
    {
        var container = Builder.CurrentUMAContainer;
        if (!container || !container.UmaAnimator) return;
        container.UmaAnimator.SetLayerWeight(2, enable ? 1 : 0);
    }

    public static string ToFrameFormat(float time, float frameRate)
    {
        int frames = Mathf.FloorToInt(time % 1 * frameRate);
        int seconds = (int)time;
        int minute = seconds % 3600 / 60;
        seconds = seconds % 3600 % 60;
        return string.Format("{0:D2}m:{1:D2}s:{2:D2}f", minute, seconds, frames);
    }
}
