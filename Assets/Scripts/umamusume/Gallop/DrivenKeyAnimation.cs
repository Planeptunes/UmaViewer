using UnityEngine;

namespace Gallop
{
    // Port of Gallop.EarTypeSet (dump_jp.cs TypeDefIndex 33580404 / global dump.cs 15092):
    //   EarType[] faceType // 0x10  (the ear types used by a driven-key ear clip)
    //   int count          // 0x18  (number of used entries; DrivenKeyComponent.PlayEar appends to it)
    [System.Serializable]
    public class EarTypeSet
    {
        public EarType[] faceType;
        public int count;
    }

    // Port of Gallop.DrivenKeyAnimation (ScriptableObject, assembly umamusume).
    // Serialized layout from the game dump (dump_jp.cs TypeDefIndex 33580405 / global dump.cs 15093):
    //   AnimationClip clip                  // 0x18
    //   FaceTypeSet[] animationFaceType     // 0x20  (not ported: no FaceTypeSet type in the viewer)
    //   EarTypeSet[]  animationEarType      // 0x28
    //   [SerializeField] bool _isCheckEyeRange // 0x30
    // The class exists so that DrivenKeyAnimation assets referenced by the live cutt
    // (LiveTimelineKeyMiniCharaMotionData.FacialDrivenKeyAnimation / EarDrivenKeyAnimation)
    // deserialize to a typed object and expose their clip, which
    // MiniAnimationController.PlayFacialMotion (0x7ffd59599090) reads at +0x18 and
    // PlayEarMotion (0x7ffd59598f40) passes to DrivenKeyComponent.PlayEar (0x7ffd58e6fa20),
    // which reads animationEarType at +0x28.
    [System.Serializable]
    public class DrivenKeyAnimation : ScriptableObject
    {
        public const int FaceNumGroupDiff = 2;
        public const int FaceNum = 5;
        public const int EyeNum = 2;
        public const int EyebrowNum = 2;
        public const int MouthNum = 1;
        public const int EarNum = 2;

        public AnimationClip clip;

        // FaceTypeSet[] animationFaceType (+0x20) is not ported: the mini ear path does not read it.
        public EarTypeSet[] animationEarType;

        [SerializeField]
        private bool _isCheckEyeRange;

        // DrivenKeyAnimation.get_IsCheckEyeRange @0x7ffd5495c820
        public bool IsCheckEyeRange => _isCheckEyeRange;
    }
}
