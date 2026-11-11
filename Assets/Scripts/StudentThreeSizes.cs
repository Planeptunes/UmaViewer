using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Three sizes (bust / waist / hip, cm) per character, read from student_threesizes.json (next to Config.json).
/// Keys are the English names; values look like "B71 W54 H76" or null. Entries whose numbers do not parse or are
/// implausible (hip below waist) are ignored, so the character falls back to the DB index system.
/// </summary>
public static class StudentThreeSizes
{
    public struct Sizes
    {
        public float B, W, H;
    }

    private const string FileName = "student_threesizes.json";
    private static readonly Regex ThreeNumbers = new Regex(@"(\d+)\D+(\d+)\D+(\d+)", RegexOptions.Compiled);
    private static Dictionary<string, Sizes> table;

    public static bool TryGet(CharaEntry chara, out Sizes sizes)
    {
        sizes = default;
        if (chara == null) return false;
        EnsureLoaded();
        string name = !string.IsNullOrEmpty(chara.EnName) ? chara.EnName : LocalizeEn.GetCharaName(chara.Id, chara.Name);
        return !string.IsNullOrEmpty(name) && table.TryGetValue(Normalize(name), out sizes);
    }

    private static void EnsureLoaded()
    {
        if (table != null) return;
        table = new Dictionary<string, Sizes>();
        string path = Path.GetFullPath(Application.dataPath + "/../" + FileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[StudentThreeSizes] {path} not found: every character uses the DB index system.");
            return;
        }
        try
        {
            var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
            foreach (var kv in raw)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                var m = ThreeNumbers.Match(kv.Value);
                if (!m.Success) continue;
                var s = new Sizes { B = float.Parse(m.Groups[1].Value), W = float.Parse(m.Groups[2].Value), H = float.Parse(m.Groups[3].Value) };
                if (s.B < 40f || s.W < 30f || s.H < 40f || s.H < s.W) continue;
                table[Normalize(kv.Key)] = s;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[StudentThreeSizes] Could not read {path}: {ex.Message}");
        }
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
