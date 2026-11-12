using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // TransmittedLightMask track (transmittedLightMaskDataList): per-frame `_TransmittedLightMaskScale` of
    // the stage's alpha mask / light cone renderers (alpha_logo_uma_summer, alpha_roof_l, ...). Field names
    // and structure follow the cutt TypeTree (son1093). Port of LiveTimelineKeyTransmittedLightMaskData
    // (JP 0x7ffd592f41e0 GetMaskScale) and LiveTimelineControl.AlterUpdate_TransmittedLightMask.
    [Serializable]
    public class LiveTransmittedLightMaskGameObjectIdentifier
    {
        // Path of the renderer relative to the group's root object (the object named like the group).
        public string _hierarchyPath;
    }

    [Serializable]
    public class LiveTransmittedLightMaskScaleKeyValue
    {
        public LiveTransmittedLightMaskGameObjectIdentifier _gameObjectIdentifier;
        public float _maskScale;

        // Native BlinkLightController.LIGHT_ALPHA_REGEX (read from the running JP client by the probe).
        private static readonly System.Text.RegularExpressions.Regex LightAlphaRegex =
            new System.Text.RegularExpressions.Regex(@"alpha(?<index>\d\d\d)_");

        private int _lightContainerIndex = int.MinValue;

        // Native SetupLightContainerIndex (JP 0x7ffd592f4900): -1, then the last path segment is matched with
        // LIGHT_ALPHA_REGEX and group 1 is parsed as the blink light container index.
        public int LightContainerIndex
        {
            get
            {
                if (_lightContainerIndex != int.MinValue)
                    return _lightContainerIndex;
                _lightContainerIndex = -1;
                string path = _gameObjectIdentifier != null ? _gameObjectIdentifier._hierarchyPath : null;
                if (!string.IsNullOrEmpty(path))
                {
                    int slash = path.LastIndexOf('/');
                    string last = slash >= 0 ? path.Substring(slash + 1) : path;
                    var match = LightAlphaRegex.Match(last);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
                        _lightContainerIndex = parsed;
                }
                return _lightContainerIndex;
            }
        }
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightMaskData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TransmittedLightMask;

        // 0 = MaskScaleArray, 1 = _maskScaleKeyValueList; any other value yields 0 (native GetMaskScale).
        public int _currentVersion;
        public int _lightContainerCount;
        public float[] MaskScaleArray;
        public List<LiveTransmittedLightMaskScaleKeyValue> _maskScaleKeyValueList;
        // Blink-light colour power link (false in every key of son1093; also no path of that live matches the
        // container regex, so it has no effect there).
        public bool _isBlinkLightColorPowerLinked;

        public int LightContainerCount => _lightContainerCount;

        // Native GetMaskScale(index, groupId): version 0 reads the float array; version 1 the key/value list and,
        // when the key is linked to the blink light colour power and the entry has a container index, multiplies the
        // scale by LiveTimelineControl.TryGetBlinkLightColorPower(group, container) (the scale alone if that fails).
        public float GetMaskScale(int index, string groupName = null)
        {
            if (_currentVersion == 0)
                return MaskScaleArray != null && index >= 0 && index < MaskScaleArray.Length ? MaskScaleArray[index] : 0f;
            if (_currentVersion == 1)
            {
                if (_maskScaleKeyValueList == null || index < 0 || index >= _maskScaleKeyValueList.Count
                    || _maskScaleKeyValueList[index] == null)
                    return 0f;
                var entry = _maskScaleKeyValueList[index];
                float scale = entry._maskScale;
                if (!_isBlinkLightColorPowerLinked)
                    return scale;
                int container = entry.LightContainerIndex;
                if (container < 0)
                    return scale;
                return Gallop.Live.StageBlinkLightDriver.TryGetContainerColorPower(groupName, container, out float power)
                    ? scale * power : scale;
            }
            return 0f;
        }

        public string GetHierarchyPath(int index)
        {
            if (_maskScaleKeyValueList == null || index < 0 || index >= _maskScaleKeyValueList.Count)
                return null;
            var item = _maskScaleKeyValueList[index];
            return item != null && item._gameObjectIdentifier != null ? item._gameObjectIdentifier._hierarchyPath : null;
        }
    }

    [Serializable]
    public class LiveTimelineKeyTransmittedLightMaskDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTransmittedLightMaskData>
    {
    }

    [Serializable]
    public class LiveTimelineTransmittedLightMaskData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "TransmittedLightMask";

        public LiveTimelineKeyTransmittedLightMaskDataList keys;

        public override ILiveTimelineKeyDataList GetKeyList()
        {
            return keys;
        }

        public LiveTimelineTransmittedLightMaskData()
            : base(default_name)
        {
            keys = new LiveTimelineKeyTransmittedLightMaskDataList();
        }
    }

    // Per-frame state of one mask group: the group (its name is the root object) plus the current scales.
    public struct TransmittedLightMaskUpdateInfo
    {
        public LiveTimelineTransmittedLightMaskData Group;
        public LiveTimelineKeyTransmittedLightMaskData Key; // current key: gives the renderer paths
        public float[] Scales;
    }

    public delegate void TransmittedLightMaskUpdateInfoDelegate(ref TransmittedLightMaskUpdateInfo updateInfo);
}
