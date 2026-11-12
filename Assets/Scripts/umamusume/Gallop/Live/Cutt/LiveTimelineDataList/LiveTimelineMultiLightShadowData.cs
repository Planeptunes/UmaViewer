using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // MultiLightShadowKeys track: parameters of the stage "DefaultShadow" materials (floor / backdrop shadow
    // of the AdditionalLight shadows). Field names and types follow the cutt TypeTree (son1093); the offsets
    // (+0x30 IsUseParam ... +0x7C IsDarkestShadowOnly) follow LiveTimelineKeyData_MultiLightShadow of the JP client.
    [Serializable]
    public class LiveTimelineKeyData_MultiLightShadow : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiLightShadow;

        public bool IsUseParam;
        public Vector3 ShadowCenterPosition;
        public Vector3 ShadowForward; // Euler angles in degrees
        public float ShadowFadeStart = 100f;
        public float ShadowFadeLength = 100f;
        public bool IsUseShadowFront;
        public bool IsShadowNearStart = true;
        public Color ShadowStartColor = Color.black;
        public Color ShadowEndColor = Color.black;
        public float ShadowDistance = 15f;
        public bool IsDarkestShadowOnly;
    }

    [Serializable]
    public class LiveTimelineKeyMultiLightShadowDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeyData_MultiLightShadow>
    {
    }

    // Layout mirrors the client's MultiLightShadowUpdateInfo. Booleans come from the current key; every float and
    // colour is lerped towards the next key when the NEXT key IsInterpolateKey (native AlterUpdate_MultiLightShadow).
    public struct MultiLightShadowUpdateInfo
    {
        public bool IsUseParam;
        public Vector3 ShadowCenterPosition;
        public Vector3 ShadowForward;
        public float ShadowFadeStart;
        public float ShadowFadeLength;
        public bool IsUseShadowFront;
        public bool IsShadowNearStart;
        public Color ShadowStartColor;
        public Color ShadowEndColor;
        public float ShadowDistance;
        public bool IsDarkestShadowOnly;
    }

    public delegate void MultiLightShadowParamInfoDelegate(ref MultiLightShadowUpdateInfo updateInfo);
}
