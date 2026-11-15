using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Native 0x1ada690 result layout; Transform remains a managed reference.
    public struct Spotlight3dUpdateInfo
    {
        public int timelineIndex;
        public int characterFlag;
        public bool isActive;
        public bool IsEnabledBillboard;
        public Color color;
        public float colorPower;
        public float localHeight;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Transform TargetCameraTransform;
        // JP build (dump_jp.cs Spotlight3dUpdateInfo +0x0A / +0x0C / +0x58): taken from the CURRENT key, never interpolated.
        public bool EnableLookAt;
        public int PositionType;        // LiveTimelineKeySpotlight3dData.PositionType: 0 Key, 1 Character, 2 World
        public string TargetNodeName;
    }

    public delegate void Spotlight3dUpdateInfoDelegate(ref Spotlight3dUpdateInfo updateInfo);
}
