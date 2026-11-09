using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// User's favorite costumes: the ones starred with the star of the Uniques list, shown by the
/// "Favorites" entry of the Other Characters dropdown. Only character-owned costumes are stored,
/// and the pair (charaId, costumeId) is enough to rebuild an item anywhere.
///
/// The list lives in `favorite_costumes.json` next to `Config.json` (same convention as `Poses` or
/// `Recordings`), so it survives between sessions and is per-user (git-ignored).
/// </summary>
public static class CostumeFavorites
{
    /// <summary>One starred costume. Serialized as-is; the array order is the starring order.</summary>
    [Serializable]
    public class Entry
    {
        public int charaId;
        public string costumeId;
        public string name;
    }

    [Serializable]
    private class SaveData
    {
        public string generated;
        public List<Entry> favorites = new List<Entry>();
    }

    private static SaveData _data;
    private static bool _loaded;

    private static string FilePath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "favorite_costumes.json"));

    /// <summary>Reads the file once (a missing file just means "no favorites yet").</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _data = new SaveData();

        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                var parsed = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (parsed != null && parsed.favorites != null) _data = parsed;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CostumeFavorites] Could not read the favorites file: {ex.Message}");
        }

        if (_data.favorites == null) _data.favorites = new List<Entry>();
    }

    /// <summary>Re-reads the file so the UI shows whatever is on disk right now.</summary>
    public static void Reload()
    {
        _loaded = false;
        EnsureLoaded();
    }

    /// <summary>True if the costume is in the user's favorites.</summary>
    public static bool IsFavorite(int charaId, string costumeId)
    {
        if (string.IsNullOrEmpty(costumeId)) return false;
        EnsureLoaded();
        return FindIndex(charaId, costumeId) >= 0;
    }

    /// <summary>Adds or removes the costume and saves at once. Returns the new state.</summary>
    public static bool Toggle(int charaId, string costumeId, string displayName)
    {
        if (string.IsNullOrEmpty(costumeId)) return false;
        EnsureLoaded();

        int index = FindIndex(charaId, costumeId);
        bool nowFavorite;
        if (index >= 0)
        {
            _data.favorites.RemoveAt(index);
            nowFavorite = false;
        }
        else
        {
            _data.favorites.Add(new Entry { charaId = charaId, costumeId = costumeId, name = displayName });
            nowFavorite = true;
        }

        Save();
        return nowFavorite;
    }

    /// <summary>Starred costumes, in the order they were starred.</summary>
    public static IReadOnlyList<Entry> GetAll()
    {
        EnsureLoaded();
        return _data.favorites;
    }

    private static int FindIndex(int charaId, string costumeId)
    {
        for (int i = 0; i < _data.favorites.Count; i++)
        {
            var entry = _data.favorites[i];
            if (entry != null && entry.charaId == charaId &&
                string.Equals(entry.costumeId, costumeId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static void Save()
    {
        try
        {
            _data.generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string path = FilePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(_data, true), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CostumeFavorites] Could not save the favorites file: {ex.Message}");
        }
    }
}
