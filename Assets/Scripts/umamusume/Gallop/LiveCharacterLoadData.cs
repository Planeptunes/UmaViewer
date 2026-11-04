using System;
using System.Collections.Generic;
/// <summary>
/// //把角色选择界面中的角色数据，复制成一份专门用于 Live 加载的轻量数据
/// </summary>
///
[Serializable]
public sealed class LiveCharacterLoadData
{
    // PJ del slot: cara, pelo y cola.
    public CharaEntry CharaEntry;
    // (Other Characters) PJ del cuerpo/traje, igual que en LiveCharacterSelect. null = el propio CharaEntry.
    public CharaEntry BodyCharaEntry;
    public string CostumeId;
    public string HeadCostumeId;
    // (Color) Color del traje comun (0 = el de siempre), igual que en LiveCharacterSelect.
    public int CostumeColorIndex;

    // PJ cuyo cuerpo se carga realmente.
    public CharaEntry BodyChara { get { return BodyCharaEntry != null ? BodyCharaEntry : CharaEntry; } }

    public static LiveCharacterLoadData Capture(LiveCharacterSelect source)
    {
        if (source == null)
            return null;

        return new LiveCharacterLoadData
        {
            CharaEntry = CopyChara(source.CharaEntry),
            BodyCharaEntry = CopyChara(source.BodyCharaEntry),
            CostumeId = source.CostumeId,
            HeadCostumeId = source.HeadCostumeId,
            CostumeColorIndex = source.CostumeColorIndex
        };
    }

    static CharaEntry CopyChara(CharaEntry sourceChara)
    {
        if (sourceChara == null)
            return null;

        return new CharaEntry
        {
            Name = sourceChara.Name,
            EnName = sourceChara.EnName,
            Id = sourceChara.Id,
            ThemeColor = sourceChara.ThemeColor,
            IsMob = sourceChara.IsMob,
            Icon = null
        };
    }

    public static List<LiveCharacterLoadData> CaptureAll(IList<LiveCharacterSelect> sources)
    {
        var result = new List<LiveCharacterLoadData>();
        if (sources == null)
            return result;

        for (int i = 0; i < sources.Count; i++)
        {
            LiveCharacterLoadData item = Capture(sources[i]);
            if (item != null)
                result.Add(item);
        }

        return result;
    }
}
