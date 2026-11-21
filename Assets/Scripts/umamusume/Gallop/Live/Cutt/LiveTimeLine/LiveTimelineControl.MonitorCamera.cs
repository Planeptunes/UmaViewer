using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Monitor camera part of Gallop.Live.Cutt.LiveTimelineControl (code_JP Gallop.Live.Cutt.LiveTimelineControl.c).
    public partial class LiveTimelineControl
    {
        private CacheCamera[] _monitorCameraCacheArray;              // +0x118
        private MonitorCamera[] _monitorCameraArray;                 // +0x120
        private float[] _monitorRollArray;                           // +0x128
        private bool[] _isUsedMonitorCameraArray;                    // +0x130
        private bool _isInitializedMonitorCamera;                    // +0x138
        private Vector3[] _monitorCameraLayerOffsetMinArray;         // +0x330
        private Vector3[] _monitorCameraLayerOffsetDiffArray;        // +0x338
        private Vector3[] _monitorCameraPosLayerOffsetArray;         // +0x340
        private bool[] _isUseRenderTextureForMonitorArray;           // +0x3B0

        // SetMonitorCamera @0x7ffd592c4860.
        public void SetMonitorCamera(MonitorCamera[] monitorCameras)
        {
            if (monitorCameras == null || monitorCameras.Length == 0)
            {
                // Drop state of a previous live: its cameras were destroyed.
                _isInitializedMonitorCamera = false;
                _monitorCameraCacheArray = null;
                _monitorCameraArray = null;
                _monitorRollArray = null;
                _isUsedMonitorCameraArray = null;
                _isUseRenderTextureForMonitorArray = null;
                _monitorCameraLayerOffsetMinArray = null;
                _monitorCameraLayerOffsetDiffArray = null;
                _monitorCameraPosLayerOffsetArray = null;
                return;
            }
            int count = monitorCameras.Length;
            _monitorCameraCacheArray = new CacheCamera[count];
            for (int i = 0; i < count; i++)
            {
                Camera target = monitorCameras[i] != null ? monitorCameras[i].TargetCamera : null;
                if (target == null)
                    Debug.LogWarning($"[MonitorCamera] SetMonitorCamera: TargetCamera {i}/{count} is null");
                _monitorCameraCacheArray[i] = new CacheCamera(target);
            }
            Debug.Log($"[MonitorCamera] SetMonitorCamera: {count} cameras registered");
            _monitorCameraArray = monitorCameras;
            _monitorRollArray = new float[count];
            _isUseRenderTextureForMonitorArray = new bool[count];
            _isUsedMonitorCameraArray = new bool[count];
            _monitorCameraLayerOffsetMinArray = new Vector3[count];
            _monitorCameraLayerOffsetDiffArray = new Vector3[count];
            _monitorCameraPosLayerOffsetArray = new Vector3[count];
            _isInitializedMonitorCamera = true;
        }

        // ResetUsedMonitorCameraArray @0x7ffd592c4360.
        public void ResetUsedMonitorCameraArray()
        {
            if (!_isInitializedMonitorCamera) return;
            for (int i = 0; i < _isUsedMonitorCameraArray.Length; i++) _isUsedMonitorCameraArray[i] = false;
        }

        // IsActiveMonitorCamera @0x7ffd592c2950: a negative index asks whether any monitor camera object is active.
        public bool IsActiveMonitorCamera(int index)
        {
            if (!_isInitializedMonitorCamera) return false;
            if (index < 0)
            {
                for (int i = 0; i < _monitorCameraArray.Length; i++)
                    if (_monitorCameraArray[i].gameObject.activeSelf) return true;
                return false;
            }
            return index < _monitorCameraArray.Length && _monitorCameraArray[index].gameObject.activeSelf;
        }

        // MonitorCameraLayerOffsetMin @0x7ffd592c3890 / MonitorCameraLayerOffsetDiff @0x7ffd592c37e0.
        public Vector3 MonitorCameraLayerOffsetMin(int index)
        {
            return _monitorCameraLayerOffsetMinArray != null && index < _monitorCameraLayerOffsetMinArray.Length ? _monitorCameraLayerOffsetMinArray[index] : Vector3.zero;
        }

        public Vector3 MonitorCameraLayerOffsetDiff(int index)
        {
            return _monitorCameraLayerOffsetDiffArray != null && index < _monitorCameraLayerOffsetDiffArray.Length ? _monitorCameraLayerOffsetDiffArray[index] : Vector3.zero;
        }

        // GetHeightValueWithCharacters @0x7ffd592c0850: 1.0 without a character flag, otherwise the mean
        // liveCharaHeightValue of the flagged characters that exist (20 slots).
        private float GetHeightValueWithCharacters(LiveCharaPositionFlag flags)
        {
            float height = 1f;
            if ((int)flags > 0)
            {
                int count = 0;
                height = 0f;
                for (int i = 0; i < 20; i++)
                {
                    if (!flags.hasFlag((LiveCharaPosition)i)) continue;
                    if (liveCharactorLocators == null || i >= liveCharactorLocators.Length ||
                        liveCharactorLocators[i] == null) continue;
                    height += liveCharactorLocators[i].liveCharaHeightValue;
                    count++;
                }
                if (1 < count) height /= count;
            }
            return height;
        }

        // GetCameraLayerOffset @0x7ffd592bf540 (DAT_7ffd5a8bae48 = 130, DAT_7ffd5a8b6b40 = 60).
        private bool GetCameraLayerOffset(LiveCharaPositionFlag flags, Vector3 min, Vector3 diff, out Vector3 offset)
        {
            offset = Vector3.zero;
            float height = GetHeightValueWithCharacters(flags);
            if (0f < height)
            {
                float rate = (height - 130f) / 60f;
                offset = diff * rate + min;
            }
            return 0f < height;
        }

        // CalculateMonitorCameraPos @0x7ffd592bd140.
        public bool CalculateMonitorCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, float currentFrame,
            LiveTimelineKey curKey, LiveTimelineKey nextKey, int index)
        {
            pos = Vector3.zero;
            if (sheet == null || sheet.monitorCameraPosKeys == null || sheet.monitorCameraPosKeys.Count < 1) return false;
            var config = new FindTimelineConfig
            {
                keyType = FindTimelineConfig.KeyType.KeyDirect,
                curKey = curKey,
                nextKey = nextKey,
                posKeys = sheet.monitorCameraPosKeys[index].keys,
                lookAtKeys = null,
                extraCameraIndex = 0
            };
            return CalculateCameraPos(out pos, sheet, currentFrame, _monitorCameraCacheArray[index],
                ref config, ref fnGetCameraPosValue);
        }

        // CalculateMonitorCameraLookAt @0x7ffd592bcf00. KNOWN DIFFERENCE: the second output (key height rate,
        // GetHeightRate, only used to scale the camera transform) is not ported; a camera ignores its transform scale.
        public bool CalculateMonitorCameraLookAt(out Vector3 lookAt, LiveTimelineWorkSheet sheet, float currentFrame, int index)
        {
            lookAt = Vector3.zero;
            if (sheet == null || sheet.monitorCameraPosKeys == null || sheet.monitorCameraLookAtKeys == null ||
                sheet.monitorCameraPosKeys.Count <= index || sheet.monitorCameraLookAtKeys.Count <= index)
                return false;
            var config = new FindTimelineConfig
            {
                keyType = FindTimelineConfig.KeyType.CurrentFrame,
                curKey = null,
                nextKey = null,
                posKeys = sheet.monitorCameraPosKeys[index].keys,
                lookAtKeys = sheet.monitorCameraLookAtKeys[index].keys,
                extraCameraIndex = 0
            };
            return CalculateCameraLookAt(out lookAt, sheet, currentFrame, _monitorCameraCacheArray[index],
                ref config, ref fnGetCameraLookAtValue, ref fnGetCameraPosValue);
        }

        // AlterUpdate_MonitorCamera @0x7ffd592aaf30 (the same sequence is inlined in AlterLateUpdate @0x7ffd59294960
        // after AlterUpdate_HandShakeCamera): Layer -> Position -> LookAt -> roll of every camera.
        private void AlterUpdate_MonitorCamera(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (!_isInitializedMonitorCamera) return;
            AlterUpdate_MonitorCameraLayer(sheet, currentFrame);
            AlterUpdate_MonitorCameraPosition(sheet, currentFrame);
            AlterUpdate_MonitorCameraLookAt(sheet, currentFrame);
            for (int i = 0; i < _monitorRollArray.Length; i++)
            {
                var cache = _monitorCameraCacheArray[i];
                if (cache == null || cache.cacheTransform == null) continue;
                cache.cacheTransform.Rotate(0f, 0f, _monitorRollArray[i]);
            }
        }

        // AlterUpdate_MonitorCameraLayer @0x7ffd592a9b70: min = key offsetMinPosition (+0x3C), diff = offsetMaxPosition
        // (+0x30) - offsetMinPosition, interpolated with the next key; the position layer offset is cleared.
        private void AlterUpdate_MonitorCameraLayer(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            for (int i = 0; i < _monitorCameraLayerOffsetMinArray.Length; i++)
            {
                _monitorCameraLayerOffsetMinArray[i] = Vector3.zero;
                _monitorCameraLayerOffsetDiffArray[i] = Vector3.zero;
                _monitorCameraPosLayerOffsetArray[i] = Vector3.zero;
            }
            if (sheet == null || sheet.monitorCameraLayerKeys == null) return;
            for (int i = 0; i < sheet.monitorCameraLayerKeys.Count && i < _monitorCameraLayerOffsetMinArray.Length; i++)
            {
                var keys = sheet.monitorCameraLayerKeys[i]?.keys;
                if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                    !keys.EnablePlayModeTimeline(_playMode)) continue;
                FindTimelineKey(out var curBase, out var nextBase, keys, currentFrame);
                var cur = curBase as LiveTimelineKeyMonitorCameraLayerData;
                var next = nextBase as LiveTimelineKeyMonitorCameraLayerData;
                if (cur == null) continue;
                Vector3 max = cur.offsetMaxPosition;
                Vector3 min = cur.offsetMinPosition;
                if (next != null && next.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(cur, next, currentFrame);
                    min = (next.offsetMinPosition - cur.offsetMinPosition) * t + cur.offsetMinPosition;
                    max = (next.offsetMaxPosition - cur.offsetMaxPosition) * t + cur.offsetMaxPosition;
                }
                _monitorCameraLayerOffsetMinArray[i] = min;
                _monitorCameraLayerOffsetDiffArray[i] = max - min;
                _monitorCameraPosLayerOffsetArray[i] = Vector3.zero;
            }
        }

        // True when a MonitorControl key active at currentFrame uses the monitor camera of `cameraIndex`
        // (camera flag or forced-use flag, dispID == cameraIndex).
        private bool IsMonitorCameraRequestedByControl(LiveTimelineWorkSheet sheet, float currentFrame, int cameraIndex)
        {
            if (sheet == null || sheet.monitorControlList == null) return false;
            for (int m = 0; m < sheet.monitorControlList.Count; m++)
            {
                var monitor = sheet.monitorControlList[m];
                var keys = monitor?.keys;
                if (keys == null || keys.Count <= 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                    !keys.EnablePlayModeTimeline(_playMode) || currentFrame < keys.At(0).frame) continue;
                FindTimelineKey(out var curBase, out _, keys, currentFrame);
                var key = curBase as LiveTimelineKeyMonitorControlData;
                if (key == null) continue;
                if ((key.IsMonitorCameraFlag() || key.IsForcedUseMonitorCamera) && key.dispID == cameraIndex) return true;
            }
            return false;
        }

        // AlterUpdate_MonitorCameraPosition @0x7ffd592aa790.
        private void AlterUpdate_MonitorCameraPosition(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.monitorCameraPosKeys == null) return;
            for (int i = 0; i < sheet.monitorCameraPosKeys.Count && i < _monitorCameraCacheArray.Length; i++)
            {
                var keys = sheet.monitorCameraPosKeys[i]?.keys;
                if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                    !keys.EnablePlayModeTimeline(_playMode)) continue;
                FindTimelineKey(out var curBase, out var nextBase, keys, currentFrame);
                var cur = curBase as LiveTimelineKeyMonitorCameraPositionData;
                var next = nextBase as LiveTimelineKeyMonitorCameraPositionData;
                if (cur == null) continue;
                CacheCamera camera = _monitorCameraCacheArray[i];
                if (camera == null || camera.camera == null || camera.cacheTransform == null) continue;

                if (CalculateMonitorCameraPos(out Vector3 pos, sheet, currentFrame, curBase, nextBase, i))
                {
                    if (GetCameraLayerOffset(cur.charaRelativeBase, MonitorCameraLayerOffsetMin(i),
                        MonitorCameraLayerOffsetDiff(i), out Vector3 layerOffset))
                        _monitorCameraPosLayerOffsetArray[i] = layerOffset;
                    camera.cacheTransform.position = pos;
                }

                float fov;
                float roll;
                if (next != null && next.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(cur, next, currentFrame);
                    fov = (next.fov - cur.fov) * t + cur.fov;
                    roll = (next.roll - cur.roll) * t + cur.roll;
                }
                else
                {
                    fov = cur.fov;
                    roll = cur.roll;
                }
                _monitorRollArray[i] = roll;
                camera.camera.fieldOfView = fov;
                camera.camera.nearClipPlane = cur.nearClip;
                camera.camera.farClipPlane = cur.farClip;

                bool enable = cur.enable;
                if (!LiveQualitySettings.IsMonitor) enable = _isUsedMonitorCameraArray[i];
                _monitorCameraArray[i].gameObject.SetActive(enable);
                if (enable)
                {
                    // Key attributes 0x20000 (culling layer) and 0x40000 (background colour).
                    if (((int)cur.attribute & 0x20000) != 0)
                        camera.camera.cullingMask = cur.GetCullingMask();
                    if (((int)cur.attribute & 0x40000) != 0)
                        camera.camera.backgroundColor = cur.GetBgColor();
                }
            }
        }

        // AlterUpdate_MonitorCameraLookAt @0x7ffd592aa180.
        private void AlterUpdate_MonitorCameraLookAt(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.monitorCameraLookAtKeys == null) return;
            for (int i = 0; i < sheet.monitorCameraLookAtKeys.Count && i < _monitorCameraCacheArray.Length; i++)
            {
                var keys = sheet.monitorCameraLookAtKeys[i]?.keys;
                if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                    !keys.EnablePlayModeTimeline(_playMode)) continue;
                var cache = _monitorCameraCacheArray[i];
                if (cache == null || cache.camera == null || cache.cacheTransform == null) continue;
                // The game returns from the whole function when this fails.
                if (!CalculateMonitorCameraLookAt(out Vector3 lookAt, sheet, currentFrame, i)) return;
                Transform transform = _monitorCameraCacheArray[i].cacheTransform;
                FindTimelineKey(out var curBase, out _, keys, currentFrame);
                var cur = curBase as LiveTimelineKeyMonitorCameraLookAtData;
                transform.LookAt(lookAt);
                if (cur != null &&
                    GetCameraLayerOffset(cur.lookAtCharaPos, MonitorCameraLayerOffsetMin(i),
                        MonitorCameraLayerOffsetDiff(i), out Vector3 lookAtOffset))
                {
                    Vector3 position = transform.position;
                    lookAt += transform.rotation * lookAtOffset;
                    transform.position = position + transform.rotation * _monitorCameraPosLayerOffsetArray[i];
                    transform.LookAt(lookAt);
                }
            }
        }
    }
}
