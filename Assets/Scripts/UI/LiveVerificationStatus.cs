using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//Lista manual del estado de verificación de cada live: qué canciones se han comprobado y se ven igual
//(o lo más parecido posible) al juego original. Las que están en VerifiedLives salen con un tick verde
//en "Shows/Live List"; el resto sale como WIP.
public static class LiveVerificationStatus
{
    //MusicId de los lives comprobados y verificados. Añadir aquí los nuevos.
    public static readonly HashSet<int> VerifiedLives = new HashSet<int>
    {
        1001,
        1083,
        1156,
    };

    private const string BadgeName = "StatusBadge";
    private const float Margin = 6f;
    private const float Height = 35.9f;
    private static readonly Color VerifiedColor = new Color(0.20f, 0.72f, 0.25f, 1f);
    private static readonly Color WipColor = new Color(0.93f, 0.55f, 0.10f, 1f);

    public static bool IsVerified(int musicId) => VerifiedLives.Contains(musicId);

    //Adds the status indicator (green tick or WIP) to the list item, pinned to the right edge of the
    //row. It sits in the TOP-right corner on purpose: the bottom-right one is where the row's
    //selection tick is drawn, so the badge used to cover it.
    public static void AttachBadge(UmaUIContainer container, int musicId)
    {
        if (container == null) return;
        var old = container.transform.Find(BadgeName);
        if (old != null) Object.Destroy(old.gameObject);

        var go = new GameObject(BadgeName, typeof(RectTransform));
        go.transform.SetParent(container.transform, false);

        var label = go.AddComponent<Text>();
        label.raycastTarget = false;
        label.alignment = TextAnchor.MiddleRight;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.fontStyle = FontStyle.Bold;
        label.font = container.Text != null ? container.Text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (IsVerified(musicId))
        {
            label.text = "✔";
            label.fontSize = 24;
            label.color = VerifiedColor;
        }
        else
        {
            label.text = "WIP";
            label.fontSize = 16;
            label.color = WipColor;
        }

        var rect = (RectTransform)go.transform;
        //Top-right corner of the row (mirror of the old bottom-right anchor).
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(label.preferredWidth, Height);
        rect.anchoredPosition = new Vector2(-Margin, 0f);
    }
}
