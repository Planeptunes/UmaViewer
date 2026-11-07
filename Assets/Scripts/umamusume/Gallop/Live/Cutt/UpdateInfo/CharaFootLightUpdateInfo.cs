using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Per-character foot light state (charaFootLightKeys track): a height gradient light that
    // rises from the feet up to hightMax. Layout mirrors the client's CharaFootLightUpdateInfo
    // (header 0x10).
    public struct CharaFootLightUpdateInfo
    {
        // Fields
        public int CharacterIndex; // 0x10
        public float hightMax; // 0x14 glow height above the feet
        public Color lightColor; // 0x18
        public LiveDefine.HightLightBlendMode LightBlendMode; // 0x28
        public LiveDefine.Easing Easing; // 0x2C
    }

    public delegate void CharaFootLightUpdateInfoDelegate(ref CharaFootLightUpdateInfo updateInfo);
}
