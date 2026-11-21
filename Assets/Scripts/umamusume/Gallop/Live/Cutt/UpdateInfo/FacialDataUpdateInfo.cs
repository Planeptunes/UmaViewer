using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Port of FacialDataUpdateInfo (dump_jp, TypeDefIndex 33583587, class layout 0x10..0x84; the struct is passed
    // by ref, so the slot offsets below are the class offsets minus the 0x10 object header). One key per slot for
    // mouth / eyebrow / eyeTrack / ear / tail / effect, cur+next only for face and eye, then the key indexes and
    // tailMotionSec. The previous viewer-side prev/cur/next triples were invented (AGENTS.md 8.2) and are gone.
    //
    // Slot writes are done by LiveTimelineControl.SetupFacialUpdateInfo_* (each one mirrors its game counterpart
    // at the RVA documented there) and consumed by LiveFaceController.AlterUpdateFacialNew (RVA 0x4EC20E0) and
    // Director.<InitializeTimeline>b__476_3 (RVA 0x4E84230) for the ear/tail random motion and eye trace extras.
    public struct FacialDataUpdateInfo // TypeDefIndex: 33583587
    {
        // Fields
        public LiveTimelineKeyFacialFaceData faceCur; // 0x00 (dump 0x10)
        public LiveTimelineKeyFacialFaceData faceNext; // 0x08 (dump 0x18)
        public LiveTimelineKeyFacialMouthData mouth; // 0x10 (dump 0x20)
        public LiveTimelineKeyFacialEyeData eyeCur; // 0x18 (dump 0x28)
        public LiveTimelineKeyFacialEyeData eyeNext; // 0x20 (dump 0x30)
        public LiveTimelineKeyFacialEyebrowData eyebrow; // 0x28 (dump 0x38)
        public LiveTimelineKeyFacialEyeTrackData eyeTrack; // 0x30 (dump 0x40)
        public LiveTimelineKeyFacialEarData ear; // 0x38 (dump 0x48)
        public LiveTimelineKeyTailMotionData tail; // 0x40 (dump 0x50)
        public LiveTimelineKeyFacialEffectData effect; // 0x48 (dump 0x58)
        public int faceKeyIndex; // 0x50 (dump 0x60)
        public int mouthKeyIndex; // 0x54 (dump 0x64)
        public int eyeKeyIndex; // 0x58 (dump 0x68)
        public int eyebrowKeyIndex; // 0x5C (dump 0x6C)
        public int eyeTrackKeyIndex; // 0x60 (dump 0x70)
        public int earKeyIndex; // 0x64 (dump 0x74)
        public int effectKeyIndex; // 0x68 (dump 0x78)
        public int tailKeyIndex; // 0x6C (dump 0x7C)
        public float tailMotionSec; // 0x70 (dump 0x80)
    }
}
