using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Gallop.Live.Cutt.LiveTimelineKeyCameraLayerData (TypeDefIndex 33583381): the main camera's layer offset range.
    // Fields offsetMaxPosition (+0x30) and offsetMinPosition (+0x3C); the worksheet field is cameraLayerKeys (+0x160).
    // The game's LiveTimelineKeyMonitorCameraLayerData / LiveTimelineKeyMultiCameraLayerData derive from this class;
    // the viewer keeps them as separate classes with the same two fields.
    [Serializable]
    public class LiveTimelineKeyCameraLayerData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CameraLayer;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
    }

    [Serializable]
    public class LiveTimelineKeyCameraLayerDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCameraLayerData> { }
}
