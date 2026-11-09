using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Favorite" star pinned to the top-right corner of a costume item, using the game's own star icons:
/// `utx_ico_strong_star_00` (gold star) for a favorite and `utx_ico_strong_star_01` (plain star) for a
/// costume that is not in the list. Clicking toggles it and saves at once.
///
/// The star is created at runtime as the last child of the item and has its OWN Button, so the click
/// toggles the favorite and never triggers the item's "load costume" button underneath it.
/// </summary>
public static class CostumeFavoriteStar
{
    private const string StarName = "FavoriteStar";
    private const float Margin = 6f;
    private const float Size = 30f;
    private const string OnIconPath = "UI/Common/utx_ico_strong_star_00";    //gold star: favorite
    private const string OffIconPath = "UI/Common/utx_ico_strong_star_01";   //plain star: not favorite

    private static Sprite _onSprite;
    private static Sprite _offSprite;
    private static bool _missingLogged;

    /// <summary>Adds (or refreshes) the star on a costume item. `onChanged` runs after each toggle.</summary>
    public static void Attach(UmaUIContainer container, int charaId, string costumeId, Action onChanged)
    {
        if (container == null) return;
        var old = container.transform.Find(StarName);
        if (old != null) UnityEngine.Object.Destroy(old.gameObject);

        LoadSprites();
        if (_onSprite == null || _offSprite == null) return;

        var go = new GameObject(StarName, typeof(RectTransform));
        go.transform.SetParent(container.transform, false);

        var image = go.AddComponent<Image>();
        image.raycastTarget = true;     //also the Button's hit area
        image.preserveAspect = true;

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(Size, Size);
        rect.anchoredPosition = new Vector2(-Margin, -Margin);

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            bool nowFavorite = CostumeFavorites.Toggle(charaId, costumeId, container.Name);
            SetState(image, nowFavorite);
            if (onChanged != null) onChanged();
        });

        SetState(image, CostumeFavorites.IsFavorite(charaId, costumeId));
    }

    //Sprites built once from the game icons exported under Resources (same pattern as CustomCostumeIcons).
    private static void LoadSprites()
    {
        if (_onSprite != null && _offSprite != null) return;
        _onSprite = LoadSprite(OnIconPath);
        _offSprite = LoadSprite(OffIconPath);
        if ((_onSprite == null || _offSprite == null) && !_missingLogged)
        {
            _missingLogged = true;
            Debug.LogError($"[CostumeFavoriteStar] Missing star icons at Resources/{OnIconPath} and Resources/{OffIconPath}.");
        }
    }

    private static Sprite LoadSprite(string path)
    {
        var texture = Resources.Load<Texture2D>(path);
        return texture == null
            ? null
            : Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    private static void SetState(Image image, bool favorite)
    {
        image.sprite = favorite ? _onSprite : _offSprite;
    }
}
