using UnityEngine;
using UnityEngine.UI;

public class LiveCharacterSelect : MonoBehaviour
{
    // PJ del slot: cara, pelo y cola (y el icono del slot).
    public CharaEntry CharaEntry;
    // (Other Characters) PJ del que se toma el cuerpo/traje. null = el propio CharaEntry.
    // NonSerialized: Unity nunca deja a null un campo serializado de una clase [Serializable] (crea
    // una instancia vacía, Id 0), y eso se leería como "cuerpo de otro PJ".
    [System.NonSerialized]
    public CharaEntry BodyCharaEntry;
    // Traje del CUERPO, es decir, de BodyChara.
    public string CostumeId;
    public string HeadCostumeId;
    // (Color) Color del traje comun elegido para este slot (0 = el de siempre). Los trajes comunes
    // con varios colores (p.ej. la camisa negra del 3.5周年) se cargan con este indice.
    public int CostumeColorIndex;
    public Image CharaImage;
    public Image CostumeImage;
    public Text IndexText;

    // PJ cuyo cuerpo se carga realmente.
    public CharaEntry BodyChara { get { return BodyCharaEntry != null ? BodyCharaEntry : CharaEntry; } }

    public void SelectChara(UmaViewerUI ui)
    {
        if (!ui) return;
        ui.CurrentSeletChara = this;
        if (!ui.SelectCharacterPannel.activeInHierarchy)
            ui.ToggleUIPanel(ui.SelectCharacterPannel);
        //El panel se pone en el PJ de este slot (y en su cuerpo, si viene de otro PJ).
        ui.ShowLiveSlotInCostumePanel(this);
    }

    public void SetValue(CharaEntry charaentry, string costumeId, Sprite costumeSprite, string headCostumeId, int costumeColorIndex = 0, CharaEntry bodyChara = null)
    {
        CharaEntry = charaentry;
        //El cuerpo de otro PJ sólo vale entre personajes normales; el mismo PJ o un mob = sin override.
        BodyCharaEntry = (bodyChara != null && charaentry != null && bodyChara.Id != charaentry.Id
                          && !bodyChara.IsMob && !charaentry.IsMob) ? bodyChara : null;
        CostumeId = costumeId;
        HeadCostumeId = headCostumeId;
        CostumeColorIndex = costumeColorIndex < 0 ? 0 : costumeColorIndex;
        CharaImage.enabled = true;
        CharaImage.sprite = charaentry.Icon;
        CostumeImage.sprite = costumeSprite;
    }
}

