using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    // Monitor texture / monitor camera part of Gallop.Live.Director (code_JP Gallop.Live.Director.c).
    public partial class Director
    {
        // Prefab values in the game (live 1001, MonitorCamera(Clone)): depth 0, clearFlags 2
        // (solid colour), cullingMask 503329027 (0x1E003103), fieldOfView 20. The prefab itself
        // (Resources "Prefabs/Live/MonitorCamera") is not available to the viewer.
        private const int MonitorCameraCullingMask = 0x1E003103;
        private const float MonitorCameraFieldOfView = 20f;

        public RenderTexture[] MonitorTextureArray;          // +0x90
        private GameObject[] _monitorCameraObjectArray;      // +0x108
        private MonitorCamera[] _monitorCameraArray;         // +0x110

        // GetMonitorTextureSize @0x7ffd591eed90.
        public void GetMonitorTextureSize(out int width, out int height)
        {
            MonitorCamera.GetTextureSideLength(out width, out height, true);
            if (!LiveQualitySettings.IsMonitorCamera)
            {
                width = Mathf.RoundToInt(width * 0.6f);   // DAT_7ffd5a640f1c = 0.6
                height = Mathf.RoundToInt(height * 0.6f);
            }
        }

        // CreateMonitorTexture @0x7ffd591ebb90: releases the previous array and makes `count` textures
        // (w, h, depth 0, default format, Bilinear).
        public void CreateMonitorTexture(int count)
        {
            if (MonitorTextureArray != null)
            {
                for (int i = 0; i < MonitorTextureArray.Length; i++)
                {
                    if (MonitorTextureArray[i] == null) continue;
                    MonitorTextureArray[i].Release();
                    Destroy(MonitorTextureArray[i]);
                }
                MonitorTextureArray = null;
            }
            MonitorTextureArray = new RenderTexture[count];
            GetMonitorTextureSize(out int width, out int height);
            for (int i = 0; i < count; i++)
            {
                MonitorTextureArray[i] = new RenderTexture(width, height, 0, RenderTextureFormat.Default);
                MonitorTextureArray[i].Create();
                MonitorTextureArray[i].filterMode = FilterMode.Bilinear;
            }
        }

        // RemakeMonitorTexture @0x7ffd591ff4c0: every camera re-creates its own texture, the shared array is rebuilt
        // with one texture per camera and each camera blits into its texture. (The MultiCameraFinalComposite
        // +0x48 and LiveImageEffect +0x58 assignments of the original are not ported: see report.)
        public void RemakeMonitorTexture()
        {
            if (_monitorCameraArray == null) return;
            GetMonitorTextureSize(out int width, out int height);
            for (int i = 0; i < _monitorCameraArray.Length; i++)
                _monitorCameraArray[i].Initialize(width, height);
            CreateMonitorTexture(_monitorCameraArray.Length);
            for (int i = 0; i < _monitorCameraArray.Length; i++)
                _monitorCameraArray[i].SetupCaptureTexture(MonitorTextureArray[i]);
        }

        // IsActiveMonitorCamera @0x7ffd591f2a00.
        public bool IsActiveMonitorCamera(int index)
        {
            return _liveTimelineControl != null && _liveTimelineControl.IsActiveMonitorCamera(index);
        }

        // ApplyGraphicSetting @0x7ffd591e9ff0 makes the one-texture array before any camera exists.
        private void InitializeMonitorTexture()
        {
            CreateMonitorTexture(1);
        }

        // Director.InitializeMonitorCamera @0x7ffd591f15b0 is a coroutine whose body (<InitializeMonitorCamera>d__468
        // .MoveNext @RVA 0x4E9D1A0) is not in code_JP: it loads the prefab through a ResourceRequest and instantiates
        // monitorCameraNum objects. KNOWN DIFFERENCES: the viewer builds the objects synchronously with the measured
        // prefab values, parents them to the Director, starts them inactive (the key's enable toggles them), and takes
        // monitorCameraNum as the number of authored MonitorCameraPos tracks of the camera sheet.
        private void InitializeMonitorCamera(int monitorCameraNum)
        {
            ReleaseMonitorCamera();
            if (monitorCameraNum <= 0)
            {
                // Clear stale per-camera state of the previous live (its cameras were just destroyed).
                _liveTimelineControl?.SetMonitorCamera(null);
                return;
            }
            Debug.Log($"[MonitorCamera] InitializeMonitorCamera: creating {monitorCameraNum} cameras");
            _monitorCameraObjectArray = new GameObject[monitorCameraNum];
            _monitorCameraArray = new MonitorCamera[monitorCameraNum];
            for (int i = 0; i < monitorCameraNum; i++)
            {
                var go = new GameObject("MonitorCamera(Clone)");
                go.transform.SetParent(transform, false);
                var camera = go.AddComponent<Camera>();
                camera.depth = 0f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = MonitorCameraCullingMask;
                camera.fieldOfView = MonitorCameraFieldOfView;
                camera.allowMSAA = false;
                var cameraData = go.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderType = CameraRenderType.Base;
                _monitorCameraArray[i] = go.AddComponent<MonitorCamera>();
                // The game prefab serialises _targetCamera; the viewer builds the object in code, so the reference is
                // resolved here (SetMonitorCamera reads TargetCamera before Initialize runs).
                _monitorCameraArray[i].FindTargetCamera();
                go.SetActive(false);
                _monitorCameraObjectArray[i] = go;
            }
            var settings = _liveTimelineControl.data != null ? _liveTimelineControl.data.MonitorCameraSettings : null;
            // Inferred: the cutt's MonitorCameraSettings rate is applied when its enable flag is set (the original
            // call site is inside the coroutine); live 1001 authors IsEnabledTextureWidthRate = 0.
            if (settings != null && settings.IsEnabledTextureWidthRate)
                MonitorCamera.TextureWidthRate = settings.TextureWidthRate;
            _liveTimelineControl.SetMonitorCamera(_monitorCameraArray);
            RemakeMonitorTexture();
        }

        // Viewer: monitorCameraNum = authored MonitorCameraPos tracks of the camera sheet (worksheet 0, the sheet
        // the timeline updates read), see the known difference above.
        private static int CountMonitorCameraTracks(LiveTimelineControl control)
        {
            if (control == null || control.data == null || control.data.worksheetList == null ||
                control.data.worksheetList.Count == 0) return 0;
            var sheet = control.data.worksheetList[0];
            return sheet != null && sheet.monitorCameraPosKeys != null ? sheet.monitorCameraPosKeys.Count : 0;
        }

        private void ReleaseMonitorCamera()
        {
            if (_monitorCameraObjectArray != null)
            {
                for (int i = 0; i < _monitorCameraObjectArray.Length; i++)
                    if (_monitorCameraObjectArray[i] != null) Destroy(_monitorCameraObjectArray[i]);
            }
            _monitorCameraObjectArray = null;
            _monitorCameraArray = null;
        }

        private void ReleaseMonitorTexture()
        {
            ReleaseMonitorCamera();
            if (MonitorTextureArray == null) return;
            for (int i = 0; i < MonitorTextureArray.Length; i++)
            {
                if (MonitorTextureArray[i] == null) continue;
                MonitorTextureArray[i].Release();
                Destroy(MonitorTextureArray[i]);
            }
            MonitorTextureArray = null;
        }
    }
}
