using System;
using System.Collections;
using System.IO;
using uGIF;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Image = uGIF.Image;

public class Screenshot : MonoBehaviour
{
    public static Screenshot Instance;
    bool recording = false;

    static UISettingsAnimation AnimationSettings => UmaViewerUI.Instance.AnimationSettings;
    static UISettingsScreenshot ScreenshotSettings => UmaViewerUI.Instance.ScreenshotSettings;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Shortens an absolute path under the project folder to "root\relative\path" for UI messages.
    /// Paths outside the project folder are returned unchanged.
    /// </summary>
    public static string ToRootRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return fullPath;

        var root = Path.GetFullPath(Application.dataPath + "/..").TrimEnd('\\', '/');
        var full = Path.GetFullPath(fullPath);
        if (full.Length > root.Length
            && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (full[root.Length] == '\\' || full[root.Length] == '/'))
        {
            return "root\\" + full.Substring(root.Length + 1).Replace('/', '\\');
        }
        return fullPath;
    }

    /// <summary>
    /// Builds the base name of a capture file: "[character name]_[animation]" where the animation is the
    /// loaded clip without its "...chr"/"...crd" prefix and without the "_loop"/"_sl"/"_s"/"_e" suffix, e.g.
    /// "Mejiro McQueen_1012_00_pdk01". Returns false (and warns the user) if there is no character or
    /// no animation loaded.
    /// </summary>
    public static bool TryGetCaptureBaseName(out string baseName)
    {
        baseName = null;
        var uma = UmaViewerBuilder.Instance != null ? UmaViewerBuilder.Instance.CurrentUMAContainer : null;
        var clip = uma != null && uma.OverrideController != null ? uma.OverrideController["clip_2"] : null;
        if (uma == null || uma.CharaEntry == null || clip == null || clip.name == "clip_2")
        {
            UmaViewerUI.Instance.ShowMessage("Load a character and an animation before capturing.", UIMessageType.Warning);
            return false;
        }

        var animation = clip.name.Replace('\\', '/');
        animation = animation.Substring(animation.LastIndexOf('/') + 1);
        var match = System.Text.RegularExpressions.Regex.Match(animation, "(?:^|_)(?:chr|crd)(.+)$");
        if (match.Success) animation = match.Groups[1].Value;
        animation = System.Text.RegularExpressions.Regex.Replace(animation, "_(loop|sl|s|e)$", "");

        baseName = SanitizeFileName(uma.CharaEntry.GetName() + "_" + animation);
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "");
        return name.Trim();
    }

    /// <summary>First "[baseName]-NN" (NN = 01, 02...) that does not exist yet as a file or folder.</summary>
    public static string NextFreeName(string directory, string baseName, string extension)
    {
        for (int n = 1; ; n++)
        {
            var name = baseName + "-" + n.ToString("00");
            if (!File.Exists(Path.Combine(directory, name + extension)) && !Directory.Exists(Path.Combine(directory, name + extension)))
                return name;
        }
    }

    public static void TakeScreenshot()
    {
        if (!TryGetCaptureBaseName(out var baseName)) return;

        var camera = GetActiveCamera();
        int width = ScreenshotSettings.Width;
        int height = ScreenshotSettings.Height;
        width = width == -1 ? Screen.width : width;
        height = height == -1 ? Screen.height : height;
        var image = GrabFrame(camera, width, height, ScreenshotSettings.Transparent);

#if UNITY_ANDROID && !UNITY_EDITOR
        string fileDirectory = Application.persistentDataPath + "/../Screenshots/";
#else
        string fileDirectory = Application.dataPath + "/../Screenshots/";
#endif

        string fileName = fileDirectory + NextFreeName(fileDirectory, baseName, ".png");
        byte[] pngShot = ImageConversion.EncodeToPNG(image);
        Directory.CreateDirectory(fileDirectory);
        //fixes "/../" in path
        var fullpath = Path.GetFullPath($"{fileName}.png");
        File.WriteAllBytes(fullpath, pngShot);
        UmaViewerUI.Instance.ShowMessage($"Screenshot saved: {ToRootRelativePath(fullpath)}", UIMessageType.Success);
        Destroy(image);
    }

    public void BeginRecordGif()
    {
        if (recording || UmaViewerBuilder.Instance.CurrentUMAContainer == null) return;
        if (!TryGetCaptureBaseName(out var baseName)) return;
        CaptureToGIFCustom.Instance.BaseName = baseName;
        recording = true;
        ScreenshotSettings.GifButton.interactable = false;
        StartCoroutine(RecordGif(ScreenshotSettings.GifWidth, ScreenshotSettings.GifHeight, ScreenshotSettings.GifTransparent, 10)); //(int)UmaViewerUI.Instance.GifQuality.value));
    }

    private IEnumerator RecordGif(int width, int height, bool transparent, int quality)
    {
        var camera = GetActiveCamera();
        var ppLayer = camera.GetComponent<PostProcessLayer>();
        bool oldPpState = ppLayer != null && ppLayer.enabled;

        if (ppLayer != null)
            ppLayer.enabled = false;

        var uma = UmaViewerBuilder.Instance.CurrentUMAContainer;
        var animator = uma != null ? uma.UmaAnimator : null;
        if (animator == null)
        {
            recording = false;
            if (ppLayer != null) ppLayer.enabled = oldPpState;
            ScreenshotSettings.GifButton.interactable = true;
            yield break;
        }

        var animeClip = uma.OverrideController["clip_2"];
        float clipLength = animeClip != null ? animeClip.length : 0f;
        if (animeClip == null || !(clipLength > 0f) || float.IsNaN(clipLength))
        {
            recording = false;
            if (ppLayer != null) ppLayer.enabled = oldPpState;
            ScreenshotSettings.GifButton.interactable = true;
            yield break;
        }

        // The GIF fps is the clip's own (clamped). It is used as the CAPTURE CADENCE, not as a frame
        // count, because the animation is really played and recorded live.
        int captureFps = Mathf.Clamp(Mathf.RoundToInt(animeClip.frameRate), 1, 60);
        if (captureFps <= 1) captureFps = 30;
        float captureInterval = 1f / captureFps;

        // (A) Turn the automatic loop off while recording: otherwise the app would reload the
        //     animation midway. It is restored at the end.
        var loopToggle = AnimationSettings.EnableLoop;
        bool wasLoop = loopToggle != null && loopToggle.isOn;
        if (loopToggle != null) loopToggle.SetIsOnWithoutNotify(false);

        // (B) Set progress to 0: cycle counter to 0 and pose at the start of the animation.
        //     `ChangeProgress` seeks with a FIXED state hash (0) that does not move the pose, so the
        //     REAL animator state is also repositioned to guarantee the start at 0.
        AnimationSettings.ResetCycleTracking();
        AnimationSettings.ChangeProgress(0f);
        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        animator.speed = 0f;
        animator.Play(stateInfo.shortNameHash, 0, 0f);
        animator.Update(0f);

        // (C) Start recording.
        CaptureToGIFCustom.Instance.transparentBackground = transparent;
        CaptureToGIFCustom.Instance.stop = false;
        CaptureToGIFCustom.Instance.Frames.Clear();
        StartCoroutine(CaptureToGIFCustom.Instance.Encode(captureFps, quality));

        // (D) Play from 0 and capture at the clip cadence until progress reaches 1.
        yield return null;
        AnimationSettings.ChangeSpeed(1f);

        float startTime = Time.time;
        float endTime = startTime + clipLength;
        float nextCaptureTime = startTime;

        // Initial frame (progress 0).
        {
            var tex = GrabFrame(camera, width, height, transparent);
            CaptureToGIFCustom.Instance.Frames.Add(new Image(tex));
            Destroy(tex);
        }

        while (Time.time < endTime)
        {
            yield return new WaitForEndOfFrame();

            if (Time.time >= nextCaptureTime)
            {
                nextCaptureTime += captureInterval;

                var tex = GrabFrame(camera, width, height, transparent);
                CaptureToGIFCustom.Instance.Frames.Add(new Image(tex));
                Destroy(tex);
            }

            // Safety: do not hang if time does not advance.
            if (Time.time - startTime > clipLength * 2f + 2f) break;
        }

        // Final frame (progress 1).
        {
            var tex = GrabFrame(camera, width, height, transparent);
            CaptureToGIFCustom.Instance.Frames.Add(new Image(tex));
            Destroy(tex);
        }

        // Restore the editor state.
        recording = false;
        if (loopToggle != null) loopToggle.SetIsOnWithoutNotify(wasLoop);
        AnimationSettings.ChangeProgress(0f);
        AnimationSettings.ResetCycleTracking();
        AnimationSettings.ChangeSpeed(1);

        if (ppLayer != null)
            ppLayer.enabled = oldPpState;

        CaptureToGIFCustom.Instance.stop = true;
        ScreenshotSettings.GifButton.interactable = true;
    }

    public void BeginRecordImageSequence()
    {
        if (recording || UmaViewerBuilder.Instance.CurrentUMAContainer == null) return;
        if (!TryGetCaptureBaseName(out var baseName)) return;
        recording = true;
        ScreenshotSettings.SequenceButton.interactable = false;
        StartCoroutine(RecordSequence(ScreenshotSettings.GifWidth, ScreenshotSettings.GifHeight, ScreenshotSettings.GifTransparent, 10, ScreenshotSettings.GifFPS, baseName)); //(int)UmaViewerUI.Instance.GifQuality.value));
    }

    private IEnumerator RecordSequence(int width, int height, bool transparent, int quality, int frameRateOverride = -1, string baseName = "UmaViewer")
    {
        ScreenshotSettings.SequenceButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Recording...";
#if UNITY_ANDROID && !UNITY_EDITOR
        string screenshotsDirectory = Application.persistentDataPath + "/../Screenshots/";
#else
        string screenshotsDirectory = Application.dataPath + "/../Screenshots/";
#endif
        Directory.CreateDirectory(screenshotsDirectory);
        string folderName = NextFreeName(screenshotsDirectory, baseName, "");
        string fileDirectory = screenshotsDirectory + folderName;
        Directory.CreateDirectory(fileDirectory);

        var camera = GetActiveCamera();
        var ppLayer = camera.GetComponent<PostProcessLayer>();
        bool oldPpState = ppLayer != null && ppLayer.enabled;
        if (ppLayer != null) ppLayer.enabled = false;

        var uma = UmaViewerBuilder.Instance.CurrentUMAContainer;
        var animator = uma != null ? uma.UmaAnimator : null;
        if (animator == null)
        {
            recording = false;
            if (ppLayer != null) ppLayer.enabled = oldPpState;
            ScreenshotSettings.SequenceButton.interactable = true;
            ScreenshotSettings.SequenceButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Record PNG Sequence";
            yield break;
        }

        var animeClip = uma.OverrideController["clip_2"];
        float clipLength = animeClip != null ? animeClip.length : 0f;
        if (animeClip == null || !(clipLength > 0f) || float.IsNaN(clipLength))
        {
            recording = false;
            if (ppLayer != null) ppLayer.enabled = oldPpState;
            ScreenshotSettings.SequenceButton.interactable = true;
            ScreenshotSettings.SequenceButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Record PNG Sequence";
            yield break;
        }

        // Capture cadence in fps: the dropdown override (30/60/120) or the clip's own.
        // As with the GIF, the animation is played and captured live, not seeked.
        int captureFps = frameRateOverride > 0 ? frameRateOverride : Mathf.RoundToInt(animeClip.frameRate);
        if (captureFps <= 1) captureFps = 30;
        float captureInterval = 1f / captureFps;

        var maxNameLength = Mathf.Max(1, Mathf.RoundToInt(clipLength * captureFps)).ToString().Length + 1;

        // (A) Turn the automatic loop off while recording (restored at the end).
        var loopToggle = AnimationSettings.EnableLoop;
        bool wasLoop = loopToggle != null && loopToggle.isOn;
        if (loopToggle != null) loopToggle.SetIsOnWithoutNotify(false);

        // (B) Set progress to 0: cycle counter to 0 and pose at the start of the animation.
        //     See RecordGif: the REAL animator state is repositioned as well.
        AnimationSettings.ResetCycleTracking();
        AnimationSettings.ChangeProgress(0f);
        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        animator.speed = 0f;
        animator.Play(stateInfo.shortNameHash, 0, 0f);
        animator.Update(0f);

        // (C) Play from 0 and capture at the cadence until progress reaches 1.
        yield return null;
        AnimationSettings.ChangeSpeed(1f);

        float startTime = Time.time;
        float endTime = startTime + clipLength;
        float nextCaptureTime = startTime;
        int frame = 0;

        // Initial frame (progress 0).
        WriteSequenceFrame(camera, fileDirectory, frame, maxNameLength, width, height, transparent);
        frame++;

        while (Time.time < endTime)
        {
            yield return new WaitForEndOfFrame();

            if (Time.time >= nextCaptureTime)
            {
                nextCaptureTime += captureInterval;

                WriteSequenceFrame(camera, fileDirectory, frame, maxNameLength, width, height, transparent);
                frame++;
            }

            // Safety: do not hang if time does not advance.
            if (Time.time - startTime > clipLength * 2f + 2f) break;
        }

        // Final frame (progress 1).
        WriteSequenceFrame(camera, fileDirectory, frame, maxNameLength, width, height, transparent);

        // Restore the editor state.
        recording = false;
        if (loopToggle != null) loopToggle.SetIsOnWithoutNotify(wasLoop);
        AnimationSettings.ChangeProgress(0f);
        AnimationSettings.ResetCycleTracking();
        AnimationSettings.ChangeSpeed(1);
        if (ppLayer != null) ppLayer.enabled = oldPpState;
        ScreenshotSettings.SequenceButton.interactable = true;
        ScreenshotSettings.SequenceButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Record PNG Sequence";

        var fullpath = Path.GetFullPath(fileDirectory);
        UmaViewerUI.Instance.ShowMessage($"PNG Sequence saved: {ToRootRelativePath(fullpath)}", UIMessageType.Success);
    }

    private static void WriteSequenceFrame(Camera camera, string fileDirectory, int frame, int maxNameLength, int width, int height, bool transparent)
    {
        var tex = GrabFrame(camera, width, height, transparent);
        var bytes = tex.EncodeToPNG();

        var fileName = $"{fileDirectory}/{frame.ToString().PadLeft(maxNameLength, '0')}.png";
        File.WriteAllBytes(fileName, bytes);

        Destroy(tex);
    }

    public static Texture2D GrabFrame(Camera cam, int width, int height, bool transparent = true)
    {
        var dimensions = GetResolution(width, height);
        width = dimensions.x;
        height = dimensions.y;

        int oldMask = cam.cullingMask;
        var oldClearFlags = cam.clearFlags;
        Color oldBG = cam.backgroundColor;

        cam.cullingMask = ~LayerMask.GetMask("UI");
        if (transparent)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0, 0, 0, 0);
        }

        var tex_color = new Texture2D(width, height, TextureFormat.ARGB32, false);
        int aaLevel = QualitySettings.antiAliasing == 0? 1 : QualitySettings.antiAliasing; //RT uses '1' for no antialiasing
        var render_texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default, aaLevel);
        var grab_area = new Rect(0, 0, width, height);

        RenderTexture.active = render_texture;
        cam.targetTexture = render_texture;

        cam.Render();
        tex_color.ReadPixels(grab_area, 0, 0);
        tex_color.Apply();

        cam.clearFlags = oldClearFlags;
        cam.cullingMask = oldMask;
        cam.backgroundColor = oldBG;
        cam.targetTexture = null;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(render_texture);

        return tex_color;
    }

    private static Camera GetActiveCamera()
    {
        //sometimes animation camera gameobject is enabled even when camera component is disabled
        return UmaViewerBuilder.Instance.AnimationCamera.isActiveAndEnabled ? UmaViewerBuilder.Instance.AnimationCamera : Camera.main;
    }

    public static Vector2Int GetResolution(int width, int height)
    {
        if (width == 0 && height == 0)
        {
            width = Screen.width;
            height = Screen.height;
        }
        else if (width == 0)
        {
            float ratio = (float)Screen.width / (float)Screen.height;
            width = Mathf.RoundToInt((float)height * ratio);
        }
        else if (height == 0)
        {
            float ratio = (float)Screen.height / (float)Screen.width;
            height = Mathf.RoundToInt((float)width * ratio);
        }
        return new Vector2Int(width, height);
    }
}
