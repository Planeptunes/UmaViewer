using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Mini character tracks (a chibi copy of a live character, e.g. the plush held in son1156).
    // Field names and types follow the cutt TypeTree (son1156_camera); the runtime layout was
    // verified against LiveTimelineControl.AlterUpdate_MiniCharaMotion @0x7ffd592a84f0, which
    // reads the key fields at: clip +0x18, headFrame +0x20, playSpeed +0x24 (the same offsets for
    // facial +0x28/+0x30/+0x34, ear +0x38/+0x40/+0x44 and position +0x48/+0x50/+0x54) and Loop
    // at +0x58. The viewer fields match that order.
    //
    // MiniCharaCamera track (game only, not ported): the worksheet carries a
    // MiniCharaCameraDataList at +0x3b8 (LiveTimelineKeyMiniCharaCameraData) driving
    // Director.CreateMiniCharaCamera @0x7ffd591eb500: a dedicated orthographic camera on culling
    // layer 20 (GraphicSettings.GetCullingLayer(0x14)) that renders the mini into a
    // GallopFrameBuffer and composites it with a CommandBuffer Blit (Director +0x188 material)
    // in OnMiniCharaCameraAfterRendering @0x7ffd591f4be0; the key animates the camera's enabled
    // flag (+0x30) and orthographicSize (+0x34) in AlterUpdate_MiniCharaCamera @0x7ffd592a7be0.
    // Not ported: the composite material/shader and the GallopFrameBuffer semantics (see the
    // report of the mini-character work).

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
