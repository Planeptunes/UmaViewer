using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

//(Motion Probe, viewer tool) Stepped slow motion of a PAUSED live, one capture per timeline frame.
//
//Why: a single paused frame (LiveProbeCapture, "Momentum Probe") shows the physics only after it has settled. This tool
//shows the frames in between. The show stays paused (SliderControl.is_Touched, so audio stays stopped and the
//Director keeps its paused branch). Every STEP is released exactly once, and it is a normal simulation step of
//1/60 s, not a reduced dt, so the result is the same as a real-time run of those frames:
//   - Director.Update consumes the released step (TryConsumeStep) and advances the timeline by 1/60 s exactly like the
//     paused branch does (OnTimelineUpdate + ApplyTimelineLateUpdate);
//   - while the step is active, UmaContainerCharacter.AlterLateUpdateRuntime runs CySpring with dt = StepDelta, and the
//     other per-frame deltas route through Delta();
//   - frames without a released step stay frozen exactly as the paused live does today.
//Continuous mode (RunUpFrames > 0, default): see RunUpFrames. The show is rewound RunUpFrames frames and stepped one
//frame after another with no paused frame in between (the CySpring is never paused or resumed), so the captured frames
//follow a settled, continuous simulation instead of a cold start after a seek.
//
//Output (next to the Momentum folders): <project or exe folder>/LiveTrace/motion_<n>_live<id>_f<start>-<end>/
//   motion_summary.csv   one row per captured frame: frame, folder, show time, wall ms
//   manifest.txt         settings and outcome
//   f<frame>/            per captured frame: state_trace.csv, camera_trace.csv, render_cyspring_calls.csv,
//                        render_cyspring_bones.csv (same names and columns as the Momentum folder), capture.png, manifest.txt
//Read-only: it only steps the show that is already paused and writes files.
public static class LiveProbeMotion
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    //One simulation step: the game's 60 fps timeline frame (1/60 s).
    public const float StepDelta = 1f / 60f;

    //Frames captured (frame 0 = the paused frame, frame k = paused frame + k steps, so FrameCount - 1 steps are released)
    //and real Unity frames the show waits paused between two released steps.
    public static int FrameCount = 60;
    public static int StepWaitFrames = 12;

    //Continuous mode (RunUpFrames > 0): the physics of a paused show are not a continuous run (CySpring is paused between
    //steps and a seek leaves its state stale), so the capture first rewinds RunUpFrames timeline frames and then steps
    //every frame back to back (one released step per real frame, no paused waits, the CySpring never pauses). The
    //run-up steps are not captured; the captured frames (the paused frame and the next FrameCount - 1) come right after
    //them with a settled simulation, like a real-time run. RunUpFrames = 0 keeps the old paused stepping.
    public static int RunUpFrames = 180;

    private const string CancelReason = "cancelled";

    private static bool _running;
    private static bool _cancelRequested;
    private static bool _stepPending;
    private static bool _stepActive;
    private static double _pendingTime;

    //True from the moment a Motion capture runs until it ends (or is cancelled).
    public static bool Running => _running;

    //True only inside the frame of a released step (from Director.Update until the capture at the end of that frame).
    public static bool StepActive => _stepActive;

    //Delta for the consumers that use Time.deltaTime: the fixed step while a step is active, the given value otherwise.
    public static float Delta(float unityDeltaTime) => _stepActive ? StepDelta : unityDeltaTime;

    public static void RequestCancel()
    {
        if (_running) _cancelRequested = true;
    }

    //Called by Director.Update in its paused branch. True once per released step.
    public static bool TryConsumeStep(out float time)
    {
        time = 0f;
        if (!_stepPending) return false;
        _stepPending = false;
        _stepActive = true;
        time = (float)_pendingTime;
        return true;
    }

    //Resets everything (used when the owning MonoBehaviour is destroyed, which stops the coroutine without running its finally).
    public static void Abort()
    {
        _running = false;
        _cancelRequested = false;
        _stepPending = false;
        _stepActive = false;
        LiveProbeTraces.CySpringDisarmMotion();
    }

    //Runs the capture. The caller has checked the preconditions (live running, show paused, no other capture).
    public static IEnumerator Run(Action<string> status)
    {
        var director = Gallop.Live.Director.instance;
        _running = true;
        _cancelRequested = false;
        _stepPending = false;
        _stepActive = false;

        int frames = Mathf.Max(2, FrameCount);
        int wait = Mathf.Max(1, StepWaitFrames);
        int live = LiveTraceExporter.CaptureLiveId;
        double startWall = Time.realtimeSinceStartupAsDouble;
        double baseTime = director._liveCurrentTime;
        int startFrame = Mathf.FloorToInt((float)baseTime * 60f + 0.001f);
        int endFrame = startFrame + frames - 1;
        // Continuous mode: steps before the first captured frame (never before frame 0 of the show).
        int runUp = RunUpFrames > 0 ? Mathf.Min(RunUpFrames, startFrame) : 0;
        bool continuous = RunUpFrames > 0;

        string runDir = null;
        string outcome = null;
        int captured = 0;
        var summary = new StringBuilder("frame,folder,show_time,wall_ms\n");
        try
        {
            runDir = CreateRunFolder(live, startFrame, endFrame);
            LiveProbeTraces.CySpringArmMotion();

            if (continuous)
            {
                // Run-up + captured frames: every step is released right after the previous frame, so the show never
                // sits paused between steps. step j = baseTime - (runUp - j)/60; steps j < runUp are not captured.
                yield return new WaitForEndOfFrame();
                for (int j = 0; j < runUp + frames; j++)
                {
                    outcome = CheckRunning();
                    if (outcome != null) break;

                    LiveProbeTraces.CySpringResetRows();
                    _stepActive = false;   // the previous step is over (end of its frame); the next one sets it again
                    _pendingTime = baseTime - (runUp - j) / 60.0;
                    _stepPending = true;
                    yield return new WaitForEndOfFrame();

                    if (!_stepActive)
                    {
                        _stepPending = false;
                        outcome = CheckRunning() ?? "the show did not advance";
                        break;
                    }

                    if (j < runUp)
                    {
                        if (j % 30 == 0) status("Motion run-up: " + (j + 1) + " of " + runUp);
                        continue;   // _stepActive stays true: the next step is released in the next frame
                    }

                    int k = j - runUp;
                    int frame = startFrame + k;
                    CaptureFrame(runDir, live, frame, startWall, out double wallMs, out float showTime);
                    summary.Append(frame).Append(",f").Append(frame).Append(',').Append(LiveProbeTraces.F5(showTime))
                           .Append(',').Append(wallMs.ToString("F1", Inv)).Append('\n');
                    captured++;
                    status("Motion: frame " + (k + 1) + " of " + frames + " (f" + frame + ")");
                }
                _stepActive = false;
            }
            else
            {
            // Frame 0: the paused frame itself, nothing released.
            yield return new WaitForEndOfFrame();

            for (int k = 0; k < frames; k++)
            {
                if (k > 0)
                {
                    // StepWaitFrames real frames with the show paused.
                    for (int w = 0; w < wait && outcome == null; w++)
                    {
                        yield return new WaitForEndOfFrame();
                        outcome = CheckRunning();
                    }
                    if (outcome != null) break;

                    // Release exactly one step; Director.Update consumes it in the next frame.
                    LiveProbeTraces.CySpringResetRows();
                    _pendingTime = baseTime + k / 60.0;
                    _stepPending = true;
                    yield return new WaitForEndOfFrame();

                    if (!_stepActive)
                    {
                        _stepPending = false;
                        outcome = CheckRunning() ?? "the show did not advance";
                        break;
                    }
                }

                // End of the frame: the show, the characters and the cameras are fully updated for this frame.
                int frame = startFrame + k;
                CaptureFrame(runDir, live, frame, startWall, out double wallMs, out float showTime);
                _stepActive = false;
                LiveProbeTraces.CySpringResetRows();
                summary.Append(frame).Append(",f").Append(frame).Append(',').Append(LiveProbeTraces.F5(showTime))
                       .Append(',').Append(wallMs.ToString("F1", Inv)).Append('\n');
                captured++;
                status("Motion: frame " + (k + 1) + " of " + frames + " (f" + frame + ")");

                outcome = CheckRunning();
                if (outcome != null) break;
            }
            }
        }
        finally
        {
            // Always: no step left half-applied, CySpring recording off, the show left paused where it stopped.
            _stepPending = false;
            _stepActive = false;
            LiveProbeTraces.CySpringDisarmMotion();
            _running = false;
            _cancelRequested = false;

            string message;
            if (runDir == null)
            {
                message = "Motion not started: " + (outcome ?? "error (see the console)");
            }
            else
            {
                try { WriteRunFiles(runDir, live, startFrame, endFrame, frames, wait, baseTime, captured, outcome, startWall, summary); }
                catch (Exception ex) { Debug.LogException(ex); }

                if (outcome == null) message = "Motion saved: " + runDir;
                else if (outcome == CancelReason) message = "Motion cancelled (" + captured + " frames kept): " + runDir;
                else message = "Motion stopped (" + captured + " frames kept): " + outcome + ". " + runDir;
            }
            status(message);
        }
    }

    //null while the capture may go on; otherwise the reason it must stop.
    private static string CheckRunning()
    {
        if (_cancelRequested) return CancelReason;
        if (!LiveTraceExporter.CaptureAvailable || Gallop.Live.Director.instance == null) return "the live ended";
        if (!Gallop.Live.Director.IsPlaybackPaused) return "the show was resumed";
        return null;
    }

    private static string CreateRunFolder(int live, int startFrame, int endFrame)
    {
        string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LiveTrace"));
        Directory.CreateDirectory(baseDir);
        int n = Directory.GetDirectories(baseDir, "motion_*").Length + 1;
        string dir = Path.Combine(baseDir, string.Format(Inv, "motion_{0:000}_live{1}_f{2}-{3}", n, live, startFrame, endFrame));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Writes the files of one frame. Each writer is isolated: a failing one is logged and the others still run.
    private static void CaptureFrame(string runDir, int live, int frame, double startWall, out double wallMs, out float showTime)
    {
        var director = Gallop.Live.Director.instance;
        showTime = director != null ? director._liveCurrentTime : 0f;
        wallMs = (Time.realtimeSinceStartupAsDouble - startWall) * 1000.0;

        string dir = Path.Combine(runDir, "f" + frame.ToString(Inv));
        Directory.CreateDirectory(dir);
        var notes = new List<string>();

        Step(notes, "capture.png", () =>
        {
            // Unity writes the PNG at the end of this frame, like the Momentum capture.
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "capture.png"));
            return "[image] capture.png requested";
        });
        Step(notes, "state_trace.csv", () => LiveProbeTraces.WriteStateTrace(dir, live, frame, LiveTraceExporter.CaptureRootTransform));
        Step(notes, "camera_trace.csv", () => LiveProbeCapture.WriteCameras(dir, live, frame));
        Step(notes, "render_cyspring", () => LiveProbeTraces.WriteCySpring(dir, frame));

        var m = new StringBuilder();
        m.AppendLine("viewer motion probe frame");
        m.AppendLine("live = " + live);
        m.AppendLine("frame = " + frame);
        m.AppendLine("time = " + showTime.ToString("F5", Inv));
        m.AppendLine("wall_ms = " + wallMs.ToString("F1", Inv));
        m.AppendLine();
        foreach (string s in notes) m.AppendLine(s);
        File.WriteAllText(Path.Combine(dir, "manifest.txt"), m.ToString(), new UTF8Encoding(false));
    }

    private static void Step(List<string> notes, string label, Func<string> write)
    {
        try { notes.Add(write()); }
        catch (Exception ex)
        {
            notes.Add("[error] " + label + ": " + ex.Message);
            Debug.LogException(ex);
        }
    }

    private static void WriteRunFiles(string runDir, int live, int startFrame, int endFrame, int frames, int wait, double baseTime,
                                      int captured, string outcome, double startWall, StringBuilder summary)
    {
        File.WriteAllText(Path.Combine(runDir, "motion_summary.csv"), summary.ToString(), new UTF8Encoding(false));

        var m = new StringBuilder();
        m.AppendLine("viewer motion probe");
        m.AppendLine("live = " + live);
        m.AppendLine("start_frame = " + startFrame);
        m.AppendLine("end_frame_planned = " + endFrame);
        m.AppendLine("frame_count = " + frames);
        m.AppendLine("step_wait_frames = " + wait);
        m.AppendLine("run_up_frames = " + (RunUpFrames > 0 ? Mathf.Min(RunUpFrames, startFrame) : 0) + (RunUpFrames > 0 ? " (continuous: no paused waits)" : " (off: paused stepping)"));
        m.AppendLine("step_delta_seconds = " + StepDelta.ToString("F7", Inv));
        m.AppendLine("base_time = " + LiveProbeTraces.F5((float)baseTime));
        m.AppendLine("frames_captured = " + captured);
        m.AppendLine("outcome = " + (outcome ?? "complete"));
        m.AppendLine("wall_total_ms = " + ((Time.realtimeSinceStartupAsDouble - startWall) * 1000.0).ToString("F1", Inv));
        File.WriteAllText(Path.Combine(runDir, "manifest.txt"), m.ToString(), new UTF8Encoding(false));
    }
}
