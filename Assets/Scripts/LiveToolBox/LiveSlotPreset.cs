using System;

// One saved slot of the Live CharaSelect panel (stored in Config.json, see Config.LiveSlotPreset).
// Only plain ids are stored; the CharaEntry objects and the costume icon are resolved on load.
[Serializable]
public class LiveSlotPresetEntry
{
    // CharaEntry.Id of the slot character (face/hair/tail). -1 = the slot was empty when saved.
    public int CharaId = -1;
    public bool CharaIsMob;
    // CharaEntry.Id of the body owner ("Other Characters"). 0 = same character as CharaId.
    public int BodyCharaId;
    public bool BodyCharaIsMob;
    public string CostumeId = "";
    public string HeadCostumeId = "";
    public int CostumeColorIndex;
}
