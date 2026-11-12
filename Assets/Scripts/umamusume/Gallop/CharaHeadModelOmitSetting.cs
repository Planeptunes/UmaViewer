using UnityEngine;

namespace Gallop
{
    // Per head-model "omit" flags (asset ast_chrXXXX_YY_chara_head_model_omit_setting, only a few special
    // heads such as chr2008 ship one). Field order and names follow the client class; a true flag means the
    // head does NOT use that feature (ModelController.get_IsOmitSweat reads IsSweat, ...).
    public class CharaHeadModelOmitSetting : ScriptableObject
    {
        public bool IsMayu;
        public bool IsCheek;
        public bool IsEyeHighlightAnimation;
        public bool IsTearModel;
        public bool IsSweat;
        public bool IsTearAnimation;
        public bool IsMangame;
        public bool IsShade;

        public static string GetPath(int charaId, string headSubId)
        {
            string chara = charaId.ToString("0000");
            return $"3d/chara/head/chr{chara}_{headSubId}/extensions/ast_chr{chara}_{headSubId}_chara_head_model_omit_setting";
        }
    }
}
