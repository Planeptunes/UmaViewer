using System;

namespace Gallop.Live.Cutt
{
    // Native Gallop.Live.Cutt.LiveTimelineKeyRendererDataList / LiveTimelineKeyRendererData (dataType 32 = Renderer;
    // ctor sets renderEnable = true, field at 0x30) and LiveTimelineRendererData (worksheet.rendererList @0xB8).
    [Serializable]
    public class LiveTimelineKeyRendererDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyRendererData>
    {
    }

    [Serializable]
    public class LiveTimelineKeyRendererData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get { return LiveTimelineKeyDataType.Renderer; }
        }

        public bool renderEnable = true;
    }

    [Serializable]
    public class LiveTimelineRendererData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "Renderer";
        public LiveTimelineKeyRendererDataList keys;

        public override ILiveTimelineKeyDataList GetKeyList()
        {
            return keys;
        }
    }

    // Native Gallop.Live.Cutt.RendererUpdateInfo: TimelineNameHash @0x10, renderEnable @0x14.
    public struct RendererUpdateInfo
    {
        public int TimelineNameHash;
        public bool renderEnable;
    }

    public delegate void RendererUpdateInfoDelegate(ref RendererUpdateInfo updateInfo);
}
