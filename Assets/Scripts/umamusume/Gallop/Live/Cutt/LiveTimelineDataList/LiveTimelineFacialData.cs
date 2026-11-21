using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [System.Serializable]
	public class LiveTimelineKeyFacialFaceData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialFace; }
    	}
		public int facialId;
		public int weight;
		public int speed;
		public int time;
		public DrivenKeyComponent.InterpolateType interpolateType;

		// Gallop.Live.Cutt.LiveTimelineKeyFacialFaceData (dump_jp) attribute bits. The getters are ICF-folded
		// bodies shared with the other key classes (RVA -> bit verified in
		// code_JP Gallop.Live.Cutt.LiveTimelineKeyFacialFaceData.c and symbols.csv):
		//   0x4F70610 = 0x10000 (IsNop), 0x4F70650 = 0x20000 (IsBlink),
		//   0x4F70130 = 0x40000 (IsCheek), 0x4F70150 = 0x80000 (IsEyeNop),
		//   0x4F700A0 = 0x100000 (IsDisableEyePupli, FlagUtil.HasAnyFlag(attribute, 0x100000)).
		public const int kAttrNop = 0x10000;
		public const int kAttrBlink = 0x20000;
		public const int kAttrCheek = 0x40000;
		public const int kAttrEyeNop = 0x80000;
		public const int kAttrDisableEyePupli = 0x100000;

		public bool IsNop => ((int)attribute & kAttrNop) != 0;
		public bool IsBlink => ((int)attribute & kAttrBlink) != 0;
		public bool IsCheek => ((int)attribute & kAttrCheek) != 0;
		public bool IsEyeNop => ((int)attribute & kAttrEyeNop) != 0;

		// get_IsDisableEyePupli (RVA 0x4F700A0), read by LiveFaceController.UpdateEye (RVA 0x4EC5030).
		public bool IsDisableEyePupli => ((int)attribute & kAttrDisableEyePupli) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialFaceDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialFaceData>
	{
		
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialMouthData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialMouth; }
    	}
		public int facialId;
		public int weight;
		public int speed;
		public int time;
		public FacialPartsData[] facialPartsDataArray;
		public DrivenKeyComponent.InterpolateType interpolateType;
		public int type;

		// Gallop.Live.Cutt.LiveTimelineKeyFacialMouthData (dump_jp): get_IsNop RVA 0x4F70610 (bit 0x10000),
		// get_IsEnabledFaceParts RVA 0x4F70650 (bit 0x20000). IsEnabledFaceParts selects the
		// UpdateFacePartsSetFromFacialPartsDataArray path in LiveFaceController.AlterUpdateFacialNew (0x4EC20E0).
		public const int kAttrNop = 0x10000;
		public const int kAttrEnableFaceParts = 0x20000;

		public bool IsNop => ((int)attribute & kAttrNop) != 0;
		public bool IsEnabledFaceParts => ((int)attribute & kAttrEnableFaceParts) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialMouthDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialMouthData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyeData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialEye; }
    	}
		public int facialId;
		public int weight;
		public int speed;
		public int time;
		public FacialPartsData[] facialPartsDataArrayL;
		public FacialPartsData[] facialPartsDataArrayR;
		public DrivenKeyComponent.InterpolateType interpolateType;

		// Gallop.Live.Cutt.LiveTimelineKeyFacialEyeData (dump_jp) attribute bits (RVA -> bit verified in
		// code_JP Gallop.Live.Cutt.LiveTimelineKeyFacialFaceData.c / LiveTimelineKeyEffectData.c):
		//   get_IsNop 0x4F70610 = 0x10000, get_IsBlink 0x4F70650 = 0x20000,
		//   get_IsEnabledFaceParts 0x4F70130 = 0x40000 (NOTE: different bit than the mouth/eyebrow/ear keys),
		//   get_IsDisableEyePupli 0x4F70150 = 0x80000.
		// Read by LiveFaceController.UpdateEye (RVA 0x4EC5030).
		public const int kAttrNop = 0x10000;
		public const int kAttrBlink = 0x20000;
		public const int kAttrEnableFaceParts = 0x40000;
		public const int kAttrDisableEyePupli = 0x80000;

		public bool IsNop => ((int)attribute & kAttrNop) != 0;
		public bool IsBlink => ((int)attribute & kAttrBlink) != 0;
		public bool IsEnabledFaceParts => ((int)attribute & kAttrEnableFaceParts) != 0;
		public bool IsDisableEyePupli => ((int)attribute & kAttrDisableEyePupli) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyeDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialEyeData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyebrowData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialEyebrow; }
    	}
		public int facialId;
		public int weight;
		public int speed;
		public int time;
		public FacialPartsData[] facialPartsDataArrayL;
		public FacialPartsData[] facialPartsDataArrayR;
		public DrivenKeyComponent.InterpolateType interpolateType;
		public bool usePartsScale;

		// Gallop.Live.Cutt.LiveTimelineKeyFacialEyebrowData (dump_jp): get_IsNop RVA 0x4F70610 (bit 0x10000),
		// get_IsEnabledFaceParts RVA 0x4F70650 (bit 0x20000). IsEnabledFaceParts selects the
		// UpdateFacePartsSetFromFacialPartsDataArray path in LiveFaceController.AlterUpdateFacialNew (0x4EC20E0).
		public const int kAttrNop = 0x10000;
		public const int kAttrEnableFaceParts = 0x20000;

		public bool IsNop => ((int)attribute & kAttrNop) != 0;
		public bool IsEnabledFaceParts => ((int)attribute & kAttrEnableFaceParts) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyebrowDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialEyebrowData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyeTrackData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialEyeTrack; }
    	}
		public LiveTimelineDefine.FacialEyeTrackTargetType targetType;
		public int verticalRatePer;
		public int horizontalRatePer;
		public int speedRatePer;
		public int speed;
		public int time;
		public DrivenKeyComponent.InterpolateType interpolateType;
		public Vector3 DirectPosition;

		// Gallop.Live.Cutt.LiveTimelineKeyFacialEyeTrackData (dump_jp): IsNop RVA 0x4F70610 (bit 0x10000),
		// get_IsEnabledDurationTime RVA 0x4F70650 (bit 0x20000 = kAttrDurationTime). The delegate
		// Director.<InitializeTimeline>b__476_3 (RVA 0x4E84230) uses IsEnabledDurationTime to choose between
		// EyeTraceController.ClearDurationTime and SetDurationTime(time / 60).
		public const int kAttrNop = 0x10000;
		public const int kAttrDurationTime = 0x20000;

		public bool IsNop => ((int)attribute & kAttrNop) != 0;
		public bool IsEnabledDurationTime => ((int)attribute & kAttrDurationTime) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEyeTrackDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialEyeTrackData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEarData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialEar; }
    	}
		public int facialId;
		public int weight;
		public int speed;
		public int time;
		public int facialEarIdL; // 0x28
		public int facialEarIdR; // 0x2C
		// Gallop.Live.Cutt.LiveTimelineKeyFacialEarData (dump_jp): random ear motion fields.
		// Read by LiveFaceController.AlterUpdateFacialNew (0x4EC20E0) to skip the ear key when set.
		public bool useEarRandomMotion; // 0x30
		public EarType randomMotionEarType; // 0x34
		public int randomMotionIndex; // 0x38
		public DrivenKeyComponent.InterpolateType interpolateType; // 0x4C

		// LiveTimelineKeyFacialEarData.kAttrNop / kAttrEnableFacialId.
		public const int kAttrNop = 0x10000;
		public const int kAttrEnableFacialId = 0x20000;

		// get_IsNop (RVA 0x4F70610): FlagUtil.HasAnyFlag(attribute, 0x10000).
		public bool IsNop => ((int)attribute & kAttrNop) != 0;

		// get_IsEnabledFacialEarId (RVA 0x4F70650): FlagUtil.HasAnyFlag(attribute, 0x20000).
		public bool IsEnabledFacialEarId => ((int)attribute & kAttrEnableFacialId) != 0;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEarDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialEarData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEffectData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.FacialEffect; }
    	}
		
		[System.Serializable]
		public class TeardropConfig
		{
			public int teardropIndex;
			public int teardropSlot;
			public bool isReversed;
			public float Alpha;
			public float Speed;
			public Color Color;
		}

		public int cheekType;
		public int tearyType;
		public int tearfulType;
		public TeardropConfig[] teardropConfigs;
		public int mangameIndex;
			// +0x34 in the game (EyeHighlightController.TimeComparisonType: GreaterThan 0, ApproximatelyGreaterThan 1),
			// passed to ModelController.PlayEyeHighlightAnimation by LiveFaceController.AlterUpdateFacialNew.
			public int TwinkleTimeComparisonType;

		public const int kAttrCheek = 65536;
		public const int kAttrTeary = 131072;
		public const int kAttrTearful = 262144;
		public const int kAttrTeardrop = 524288;
		public const int kAttrMangame = 2097152;
		public const int kAttrFaceShadow = 4194304;
		public const int kAttrFaceShadowVisible = 8388608;
	}

	[System.Serializable]
	public class LiveTimelineKeyFacialEffectDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialEffectData>
	{

	}

	[System.Serializable]
	public class LiveTimelineKeyTailMotionData : LiveTimelineKey
	{
		public override LiveTimelineKeyDataType dataType
    	{
        	get { return LiveTimelineKeyDataType.TailMotion; } // get_dataType returns 0x76 (RVA 0x4F73F60)
    	}

		// Gallop.Live.Cutt.LiveTimelineKeyTailMotionData (dump_jp TypeDefIndex 33583526). The statics
		// TAIL_MOTION_STARTINDEX / TAIL_MOTION_ENDINDEX are not per-instance state and are not stored here.
		public bool UseTailRandomMotion; // 0x18
		public int MotionIndex; // 0x1C
		public float PlaySpeed; // 0x20 (ctor default 1.0, LiveTimelineKeyTailMotionData..ctor 0x4F73F50)
		public bool IsTimescaleDisabled; // 0x24

		public LiveTimelineKeyTailMotionData()
		{
			PlaySpeed = 1f;
		}
	}

	[System.Serializable]
	public class LiveTimelineKeyTailMotionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTailMotionData>
	{
		// LiveTimelineKeyTailMotionDataList (dump_jp TypeDefIndex 33583795): TailMotionPrevFrame (+0x40) and
		// TailMotionPrevFrameAnimationTime (+0x44) are the animation-time cache LiveTimelineControl.
		// SetupFacialUpdateInfo_Tail (RVA 0x4F86EB0) reads and writes through LiveTimelineUtil.GetAnimationTime.
		public int TailMotionPrevFrame; // 0x40
		public float TailMotionPrevFrameAnimationTime; // 0x44

		// LiveTimelineKeyTailMotionDataList.GetRandomTailMotionIndex RVA 0x4F7FBF0: the MotionIndex of every key
		// with UseTailRandomMotion. Consumed by Director.UpdateRandomTailMotion (pending Director-side wiring).
		public HashSet<int> GetRandomTailMotionIndex()
		{
			var result = new HashSet<int>();
			int count = Count;
			for (int i = 0; i < count; i++)
			{
				var key = At(i) as LiveTimelineKeyTailMotionData;
				if (key != null && key.UseTailRandomMotion)
				{
					result.Add(key.MotionIndex);
				}
			}
			return result;
		}
	}

	[System.Serializable]
	public class LiveTimelineFacialData
    {
		public LiveTimelineKeyFacialFaceDataList faceKeys; // 0x10
		public LiveTimelineKeyFacialMouthDataList mouthKeys; // 0x18
		public LiveTimelineKeyFacialEyeDataList eyeKeys; // 0x20
		public LiveTimelineKeyFacialEyebrowDataList eyebrowKeys; // 0x28
		public LiveTimelineKeyFacialEyeTrackDataList eyeTrackKeys; // 0x30
		public LiveTimelineKeyFacialEarDataList earKeys; // 0x38
		public LiveTimelineKeyFacialEffectDataList effectKeys; // 0x40
		// Game worksheet offset +0x48 (dump_jp LiveTimelineFacialData; GetKeyListArray @0x7ffd592fcae0 reads the
		// eighth list at +0x48). Fills FacialDataUpdateInfo.tail via LiveTimelineControl.SetupFacialUpdateInfo_Tail.
		public LiveTimelineKeyTailMotionDataList tailKeys; // 0x48
	}
}