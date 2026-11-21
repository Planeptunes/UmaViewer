using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

//Image grid of the viewer: while the toggle Canvas/UI/Panels/CharaSelect/VMDToggle is on, one screenshot every
//EveryFrames timeline frames of the live, in <project>/Screenshots/<live id>/<frame>.png. The first tick at or after
//each multiple of EveryFrames is captured and the file carries that REAL timeline frame (floor(time * 60)), so the
//gallery pairs one to one with a reference gallery that uses the same rule and names.
//Only during normal playback (not the offline recorder: its camera renders to a RenderTexture, the screen is empty).
//
//The capture must NOT stall the frame. ScreenCapture.CaptureScreenshot reads the pixels, encodes the PNG and writes the
//file on the main thread; the next frame then gets a huge Time.deltaTime, which advances the live clock and makes the
//CySpring solver (one step per frame with the frame delta) lift the skirt. Instead: the screen is copied to a
//RenderTexture at the end of the frame (same moment and same pixels as before), read back with AsyncGPUReadback and the
//PNG is encoded and written on a worker thread. The frame number of the file is fixed when the capture is requested.
public static class LiveGridCapture
{
    public const int EveryFrames = 30;

    private static int _liveId = -1;
    private static int _lastSlot = -1;
    private static int _lastFrame = int.MinValue;
    private static GridCaptureRunner _runner;

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

        if (_runner == null)
        {
            var go = new GameObject("LiveGridCaptureRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<GridCaptureRunner>();
        }
        //Captured at the end of this frame; the file keeps the frame number computed now.
        _runner.Request(Path.Combine(dir, frame.ToString(CultureInfo.InvariantCulture) + ".png"));
    }

    private sealed class GridCaptureRunner : MonoBehaviour
    {
        public void Request(string path)
        {
            StartCoroutine(Capture(path));
        }

        private IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();

            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) yield break;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            }
            catch (Exception e)
            {
                RenderTexture.ReleaseTemporary(rt);
                Debug.LogWarning("[LiveGridCapture] Capture failed: " + e.Message);
                yield break;
            }

            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
            {
                RenderTexture.ReleaseTemporary(rt);
                if (request.hasError)
                {
                    Debug.LogWarning("[LiveGridCapture] Readback failed: " + path);
                    return;
                }

                //The native data is only valid during this callback: copy it, then encode off the main thread.
                NativeArray<byte> data = request.GetData<byte>();
                var pixels = new byte[data.Length];
                data.CopyTo(pixels);
                ThreadPool.QueueUserWorkItem(_ => WritePng(path, pixels, w, h));
            });
        }
    }

    //Runs on a worker thread. Rows are written in the order they come from the readback (same as LiveClipRecorder,
    //which needs no vertical flip for the screen capture). Alpha is dropped (RGB, like a screenshot).
    private static void WritePng(string path, byte[] rgba, int w, int h)
    {
        try
        {
            string temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                var ihdr = new byte[13];
                WriteBE(ihdr, 0, (uint)w);
                WriteBE(ihdr, 4, (uint)h);
                ihdr[8] = 8;    //bit depth
                ihdr[9] = 2;    //RGB
                WriteChunk(fs, "IHDR", ihdr, ihdr.Length);

                //zlib stream: header + deflate + adler32 of the raw (filtered) data.
                using (var ms = new MemoryStream(w * h * 3 / 2 + 1024))
                {
                    ms.WriteByte(0x78);
                    ms.WriteByte(0x01);
                    uint a = 1, b = 0;
                    var row = new byte[1 + w * 3];   //filter byte 0 (None) + RGB
                    using (var deflate = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                    {
                        for (int y = 0; y < h; y++)
                        {
                            int src = y * w * 4;
                            int dst = 1;
                            row[0] = 0;
                            for (int x = 0; x < w; x++, src += 4, dst += 3)
                            {
                                row[dst] = rgba[src];
                                row[dst + 1] = rgba[src + 1];
                                row[dst + 2] = rgba[src + 2];
                            }
                            deflate.Write(row, 0, row.Length);
                            for (int i = 0; i < row.Length; i++)
                            {
                                a = (a + row[i]) % 65521;
                                b = (b + a) % 65521;
                            }
                        }
                    }
                    uint adler = (b << 16) | a;
                    ms.WriteByte((byte)(adler >> 24));
                    ms.WriteByte((byte)(adler >> 16));
                    ms.WriteByte((byte)(adler >> 8));
                    ms.WriteByte((byte)adler);
                    WriteChunk(fs, "IDAT", ms.GetBuffer(), (int)ms.Length);
                }

                WriteChunk(fs, "IEND", new byte[0], 0);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LiveGridCapture] Could not write " + path + ": " + e.Message);
        }
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data, int length)
    {
        var head = new byte[8];
        WriteBE(head, 0, (uint)length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head, 0, 8);
        if (length > 0) s.Write(data, 0, length);

        uint crc = Crc32(0xFFFFFFFFu, head, 4, 4);
        crc = Crc32(crc, data, 0, length) ^ 0xFFFFFFFFu;
        var tail = new byte[4];
        WriteBE(tail, 0, crc);
        s.Write(tail, 0, 4);
    }

    private static uint[] _crcTable;
    private static readonly object _crcLock = new object();

    private static uint Crc32(uint crc, byte[] buf, int offset, int length)
    {
        var table = _crcTable;
        if (table == null)
        {
            lock (_crcLock)
            {
                if (_crcTable == null)
                {
                    var t = new uint[256];
                    for (uint n = 0; n < 256; n++)
                    {
                        uint c = n;
                        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                        t[n] = c;
                    }
                    _crcTable = t;
                }
                table = _crcTable;
            }
        }
        for (int i = 0; i < length; i++)
            crc = table[(crc ^ buf[offset + i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
