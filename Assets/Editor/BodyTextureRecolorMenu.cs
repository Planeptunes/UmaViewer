// Herramienta local (no forma parte del visor publicado).
// Opens the M_Body texture recolour tool (BodyTextureRecolor) on the character loaded in Play Mode.
using UnityEditor;
using UnityEngine;

public static class BodyTextureRecolorMenu
{
    // %#r = Ctrl+Shift+R
    const string MenuPath = "Tools/Recolor de texturas de M_Body (personaje cargado) %#r";

    [MenuItem(MenuPath)]
    static void Open()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Recolor de M_Body",
                "Entra en Play Mode y carga un personaje antes de usar la herramienta.", "Vale");
            return;
        }

        UmaContainerCharacter character = UmaViewerBuilder.Instance != null ? UmaViewerBuilder.Instance.CurrentUMAContainer : null;
        if (character == null) character = Object.FindObjectOfType<UmaContainerCharacter>();
        if (character == null)
        {
            Debug.LogWarning("[BodyTextureRecolor] No hay ningún personaje cargado en la escena.");
            return;
        }

        BodyTextureRecolor tool = character.GetComponent<BodyTextureRecolor>();
        if (tool == null) tool = character.gameObject.AddComponent<BodyTextureRecolor>();
        tool.Open();
        Debug.Log("[BodyTextureRecolor] Abierto para " + character.name + ".");
    }
}
