using System.Globalization;
using System.IO;
using UnityEngine;

//Image grid of the viewer: while the toggle Canvas/UI/Panels/CharaSelect/VMDToggle is on, one screenshot every
//EveryFrames timeline frames of the live, in <project>/Screenshots/<live id>/<frame>.png. The first tick at or after
//each multiple of EveryFrames is captured and the file carries that REAL timeline frame (floor(time * 60)), so the
//gallery pairs one to one with a reference gallery that uses the same rule and names.
//Only during normal playback (not the offline recorder: its camera renders to a RenderTexture, the screen is empty).
public static class LiveGridCapture
{
    public const int EveryFrames = 30;

    private static int _liveId = -1;
    private static int _lastSlot = -1;
    private static int _lastFrame = int.MinValue;

    /// <summary>Called by Director.Update once the timeline of this frame has been applied.</summary>
    public static void Tick(int liveId, float liveTime)
    {
        var ui = UmaViewerUI.Instance;
        if (ui == null || !ui.liveGridCapture || liveId <= 0 || liveTime < 0f) return;

        int frame = Mathf.FloorToInt(liveTime * 60f + 0.001f);
        //New live, or the time went back (seek / replay): start the slots again.
        if (liveId != _liveId || frame + EveryFrames < _lastFrame)
        {
            _liveId = liveId;
            _lastSlot = -1;
        }
        _lastFrame = frame;

        int slot = frame / EveryFrames;
        if (slot <= _lastSlot) return;
        _lastSlot = slot;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", liveId.ToString(CultureInfo.InvariantCulture)));
        Directory.CreateDirectory(dir);
        //Written by Unity at the end of this frame.
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, frame.ToString(CultureInfo.InvariantCulture) + ".png"));
    }
}
