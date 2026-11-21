using System;
using System.Globalization;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeySpotlight3dData : LiveTimelineKeyWithInterpolate
    {
        public const int ATTR_DISABLE_BILLBOARD = 0x10000;
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Spotlight3d;
        public bool isActive;
        public Color color = Color.white;
        public float colorPower = 1f;
        public float localHeight;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
        public Vector3 characterPosition;
        public int targetCameraType;
        public int targetCameraIndex;

        // JP build keys (dump_jp.cs, LiveTimelineKeySpotlight3dData +0x84 / +0x88 / +0x90). Names match the bundle's TypeTree.
        // Key = `position` (+ characterPosition) is authored in the stage; Character = relative to the character's position
        // (or to the model node TargetNodeName); World = absolute. EnableLookAt makes the light face that position.
        public enum PositionType { Key = 0, Character = 1, World = 2 }
        public PositionType PositionTypeValue;
        public bool EnableLookAt;
        public string TargetNodeName;

        // Native 0x1af80e0/0x1af80f0: bit 16 disables billboarding.
        public bool IsEnabledBillboard
        {
            get => ((int)attribute & ATTR_DISABLE_BILLBOARD) == 0;
            set => attribute = (LiveTimelineKeyAttribute)(value
                ? (int)attribute & ~ATTR_DISABLE_BILLBOARD
                : (int)attribute | ATTR_DISABLE_BILLBOARD);
        }
    }

    [Serializable]
    public class LiveTimelineKeySpotlight3dDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeySpotlight3dData>
    {
    }

    [Serializable]
    public class LiveTimelineSpotlight3dData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeySpotlight3dDataList keys = new LiveTimelineKeySpotlight3dDataList();
        // JP LiveTimelineSpotlight3dData (dump_jp.cs): the binding is serialized on the GROUP, not on the keys:
        // _characterIndex +0x28 (ctor sets -1), _assetId +0x2C (default 0 = "spotlight3d000"). The game's
        // UpdateStatus @0x7ffd592f7d10 only recomputes the name hash.
        [SerializeField] private int _characterIndex = -1;  // 0x28
        [SerializeField] private int _assetId;               // 0x2C

        public int CharacterIndex { get => _characterIndex; set => _characterIndex = value; }
        public int AssetId { get => _assetId; set => _assetId = value; }

        public LiveTimelineSpotlight3dData() : base("Spotlight3d") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;

        // Native GetAssetName 0x1afa8d0, with invariant numeric formatting.
        public static string GetAssetName(int assetId) => assetId < 0
            ? "spotlight3d"
            : "spotlight3d" + assetId.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }
}
