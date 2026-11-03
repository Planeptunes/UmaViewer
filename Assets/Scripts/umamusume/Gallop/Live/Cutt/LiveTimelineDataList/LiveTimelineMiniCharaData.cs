using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Mini character tracks (a chibi copy of a live character, e.g. the plush held in son1156).
    // Field names and types follow the cutt TypeTree (son1156_camera).

    [Serializable]
    public class LiveTimelineKeyMiniCharaTransformData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MiniCharaTransform;

        public bool Visible;
        public Vector3 Position;
        public Vector3 Rotate;
        public float BodyScale = 1f;
        public int LayerIndex;
        public bool IsLayout2d;
        public bool IsAttachChara;
        public int AttachIndex;
        public string AttachNodeName;
        public bool ShouldInheritParentScale;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaTransformDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaTransformData> { }

    [Serializable]
    public class LiveTimelineMiniCharaTransformData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyMiniCharaTransformDataList keys = new LiveTimelineKeyMiniCharaTransformDataList();
        public int _index;
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaMotionData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MiniCharaMotion;

        public AnimationClip BodyAnimationClip;
        public int BodyHeadFrame;
        public float BodyPlaySpeed = 1f;
        // DrivenKeyAnimation assets (facial/ear blend keys); no runtime type here.
        public UnityEngine.Object FacialDrivenKeyAnimation;
        public int FacialHeadFrame;
        public float FacialPlaySpeed = 1f;
        public UnityEngine.Object EarDrivenKeyAnimation;
        public int EarHeadFrame;
        public float EarPlaySpeed = 1f;
        public AnimationClip PositionAnimationClip;
        public int PositionHeadFrame;
        public float PositionPlaySpeed = 1f;
        public bool Loop;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaMotionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaMotionData> { }

    [Serializable]
    public class LiveTimelineMiniCharaMotionData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyMiniCharaMotionDataList keys = new LiveTimelineKeyMiniCharaMotionDataList();
        public int _index;
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaColorData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MiniCharaColor;

        public int TargetFlags;
        public int ColorType;
        public Color Color = Color.white;
        public float ColorPower = 1f;
        public float OutlineWidthPower = 1f;
        public Color OutlineColor;
        // 0 = Blend, 1 = Multiply, 2 = Max (MiniModelController.OutlineColorBlend).
        public int OutlineColorBlend;
    }

    [Serializable]
    public class LiveTimelineKeyMiniCharaColorDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaColorData> { }

    [Serializable]
    public class LiveTimelineMiniCharaColorData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyMiniCharaColorDataList keys = new LiveTimelineKeyMiniCharaColorDataList();
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
}
