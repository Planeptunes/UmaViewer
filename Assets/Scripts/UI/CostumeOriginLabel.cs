using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Second, smaller line under the costume name that tells which character the costume comes from.
/// Only used by the "Favorites" list (costumes of several characters mixed together). The label is a
/// clone of the item's own active text component, so it keeps its font (including the Chinese TMP one).
/// </summary>
public static class CostumeOriginLabel
{
    private const string LabelName = "OriginLabel";
    private const float SubtitleFontSize = 15f;
    private const float NameShiftUp = 12f;
    private const float SubtitleShiftDown = 14f;
    private static readonly Color SubtitleColor = new Color(0.62f, 0.62f, 0.62f, 1f);

    /// <summary>Adds "- {originName}" under the item name (and lifts the name a little).</summary>
    public static void Attach(UmaUIContainer container, string originName)
    {
        if (container == null || string.IsNullOrEmpty(originName)) return;

        var old = container.transform.Find(LabelName);
        if (old != null) Object.Destroy(old.gameObject);

        bool useTmp = container.TextMesh != null && container.TextMesh.gameObject.activeSelf;
        GameObject source = useTmp
            ? container.TextMesh.gameObject
            : (container.Text != null ? container.Text.gameObject : null);
        if (source == null) return;

        var clone = Object.Instantiate(source, source.transform.parent);
        clone.name = LabelName;

        if (useTmp)
        {
            var tmp = clone.GetComponent<TextMeshProUGUI>();
            tmp.text = "-" + originName;
            tmp.enableAutoSizing = false;
            tmp.fontSize = SubtitleFontSize;
            tmp.color = SubtitleColor;
            tmp.raycastTarget = false;
        }
        else
        {
            var text = clone.GetComponent<Text>();
            text.text = "-" + originName;
            text.resizeTextForBestFit = false;
            text.fontSize = Mathf.RoundToInt(SubtitleFontSize);
            text.color = SubtitleColor;
            text.raycastTarget = false;
        }

        ShiftY((RectTransform)source.transform, NameShiftUp);
        ShiftY((RectTransform)clone.transform, -SubtitleShiftDown);
    }

    private static void ShiftY(RectTransform rect, float dy)
    {
        rect.anchoredPosition += new Vector2(0f, dy);
    }
}
