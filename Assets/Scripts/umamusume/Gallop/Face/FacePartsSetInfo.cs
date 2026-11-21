using System.Collections.Generic;

namespace Gallop
{
    // Port of FacePartsSetInfo (dump_jp, global namespace, TypeDefIndex 33561771).
    // FacialEarDic (static +0x0) is the table LiveFaceController.AlterUpdateFacialNew (RVA 0x4EC20E0) uses to
    // turn LiveTimelineKeyFacialEarData.facialEarIdL/R into an EarType when the key has IsEnabledFacialEarId set.
    // FacialEyebrowDic (static +0x8) / FacialEyeDic (static +0x10) / FacialMouthDic (static +0x18) are read by
    // LiveFaceController.UpdateFacePartsSetFromFacialPartsDataArray (RVA 0x4EC58E0) to turn a
    // FacialPartsData.FacialPartsId into the FaceParts._faceParts id of the target parts set.
    // Values read from FacePartsSetInfo..cctor (RVA 0x58F8180, decompiled in Ghidra UmaDumpGHIDRA @0x7ffd59c78180):
    //   FacialEarDic:     Add(i, (EarType)i), i = 0..0x10 (17 entries, identity)
    //   FacialEyebrowDic: Add(i, i), i = 0..0x18 (25 entries, identity)
    //   FacialEyeDic:     Add(i, i) for the keys 0,1,3,4,2,5..0x1c (29 entries, identity)
    //   FacialMouthDic:   Add(i, i), i = 0..0x38 (57 entries, identity)
    // All four are identity maps, so the viewer's "morph index = FacialPartsId - 1" mapping matches the game's
    // FaceParts._faceParts values. EYE_HIGHLIGHT_ANIMATION_ARRAY (static +0x20, {NoAnim, Teary, Twinkle}) is not
    // kept here: the effect key passes tearyType straight to EyeHighlightController.PlayAnimation (FaceDrivenKeyTarget).
    public static class FacePartsSetInfo
    {
        public static readonly Dictionary<int, EarType> FacialEarDic = new Dictionary<int, EarType>()
        {
            { 0, EarType.Base },
            { 1, EarType.Base_N },
            { 2, EarType.Kanasi },
            { 3, EarType.Dere_N },
            { 4, EarType.Dere },
            { 5, EarType.Yure },
            { 6, EarType.Biku_N },
            { 7, EarType.Biku },
            { 8, EarType.Ikari },
            { 9, EarType.Tanosi },
            { 10, EarType.Up_N },
            { 11, EarType.Up },
            { 12, EarType.Down },
            { 13, EarType.Front },
            { 14, EarType.Side },
            { 15, EarType.Back },
            { 16, EarType.Roll },
        };

        // Identity maps (see class comment): FacialPartsId -> FaceParts._faceParts.
        public static readonly Dictionary<int, int> FacialEyebrowDic = BuildIdentityDic(0, 0x18);
        public static readonly Dictionary<int, int> FacialEyeDic = BuildIdentityDic(0, 0x1c);
        public static readonly Dictionary<int, int> FacialMouthDic = BuildIdentityDic(0, 0x38);

        private static Dictionary<int, int> BuildIdentityDic(int from, int to)
        {
            var dic = new Dictionary<int, int>();
            for (int i = from; i <= to; i++)
            {
                dic[i] = i;
            }
            return dic;
        }
    }
}
