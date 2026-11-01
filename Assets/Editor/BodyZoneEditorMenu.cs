// Herramienta local (no forma parte del visor publicado).
// Añade el editor de zonas (BodyZoneEditor) al personaje cargado en Play Mode.
using UnityEditor;
using UnityEngine;

public static class BodyZoneEditorMenu
{
    const string MenuPath = "Tools/Editor de zonas de M_Body (añadir al personaje)";

    [MenuItem(MenuPath)]
    static void AddToCharacter()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Editor de zonas",
                "Entra en Play Mode y carga un personaje antes de usar la herramienta.",
                "Vale");
            return;
        }

        UmaContainerCharacter character = Object.FindObjectOfType<UmaContainerCharacter>();
        if (character == null)
        {
            Debug.LogWarning("[BodyZoneEditor] No hay ningún personaje cargado en la escena.");
            return;
        }

        BodyZoneEditor editor = character.GetComponent<BodyZoneEditor>();
        if (editor == null) editor = character.gameObject.AddComponent<BodyZoneEditor>();
        if (editor == null)
        {
            Debug.LogError("[BodyZoneEditor] No se pudo añadir el componente.");
            return;
        }

        editor.SetEditMode(true);
        Selection.activeGameObject = character.gameObject;
        Debug.Log("[BodyZoneEditor] Añadido a " + character.name + ". F2 para entrar/salir del modo edición.");
    }
}
