using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// English lyrics written by the community translation team UmaTL
/// (https://github.com/UmaTL/hachimi-tl-en, folder localized_data/assets/lyrics).
/// All the credit for these translations goes to them and their contributors; see the
/// translation progress / credits pages of that project.
///
/// Format of each file `m{songId}_lyrics.json`: { "milliseconds": "text" }. The translated text
/// replaces the text of the line of the game's own lyric table that has the same time; time, type
/// and variation of the line are not touched.
///
/// The files are downloaded once into `{persistentDataPath}/TranslatedLyrics` and refreshed at most
/// once a week. Songs without a file keep the lyrics of the game client (see UmaViewerBuilder.LoadLiveLyrics).
/// </summary>
public static class TranslatedLyrics
{
    public const string SourceName = "UmaTL translation team";
    public const string SourceUrl = "https://github.com/UmaTL/hachimi-tl-en";

    private const string ListUrl = "https://api.github.com/repos/UmaTL/hachimi-tl-en/contents/localized_data/assets/lyrics";
    private const string RawBaseUrl = "https://raw.githubusercontent.com/UmaTL/hachimi-tl-en/refs/heads/main/localized_data/assets/lyrics/";
    private const string StampFileName = "refresh.stamp";
    private const int RefreshDays = 7;

    private static readonly Regex FileNameRegex = new Regex(@"^m\d+_lyrics\.json$");

    private static string CacheDir => Path.Combine(Application.persistentDataPath, "TranslatedLyrics");

    /// <summary>Downloads the missing / outdated translated lyric files. Never throws; offline keeps the cache.</summary>
    public static IEnumerator Prefetch()
    {
        string dir = CacheDir;
        string stampPath = Path.Combine(dir, StampFileName);
        if (IsFresh(stampPath)) yield break;

        string listJson = null;
        yield return UmaViewerDownload.DownloadText(ListUrl, text => listJson = text);
        if (string.IsNullOrEmpty(listJson)) yield break;

        var names = new List<string>();
        try
        {
            foreach (var item in JArray.Parse(listJson))
            {
                string name = item["name"] != null ? item["name"].ToString() : null;
                if (name != null && FileNameRegex.IsMatch(name)) names.Add(name);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TranslatedLyrics] Could not read the file list: {ex.Message}");
            yield break;
        }

        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TranslatedLyrics] Could not create the cache folder: {ex.Message}");
            yield break;
        }

        int saved = 0;
        foreach (string name in names)
        {
            string json = null;
            yield return UmaViewerDownload.DownloadText(RawBaseUrl + name, text => json = text);
            if (string.IsNullOrEmpty(json)) continue;
            try
            {
                JObject.Parse(json);   // only keep valid files
                File.WriteAllText(Path.Combine(dir, name), json, new UTF8Encoding(false));
                saved++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TranslatedLyrics] Skipped '{name}': {ex.Message}");
            }
        }

        try { File.WriteAllText(stampPath, DateTime.UtcNow.Ticks.ToString()); } catch { }
        Debug.Log($"[TranslatedLyrics] {saved}/{names.Count} lyric files updated. English lyrics by {SourceName} ({SourceUrl}).");
    }

    /// <summary>
    /// Replaces the text of the lines of `lyrics` that have a translation (same time in ms).
    /// Returns false when the song has no translated file or no line matched.
    /// </summary>
    public static bool TryApply(int songId, List<UmaLyricsData> lyrics)
    {
        if (lyrics == null || lyrics.Count == 0) return false;

        string path = Path.Combine(CacheDir, $"m{songId}_lyrics.json");
        if (!File.Exists(path)) return false;

        try
        {
            var byTime = new Dictionary<float, string>();
            foreach (var pair in JObject.Parse(File.ReadAllText(path)))
            {
                int ms;
                if (!int.TryParse(pair.Key, out ms)) continue;
                // Same computation as the lyric table (LoadLyrics) so the float keys are identical.
                byTime[ms / 1000f] = pair.Value.ToString();
            }

            int matched = 0;
            foreach (var line in lyrics)
            {
                string text;
                if (byTime.TryGetValue(line.time, out text))
                {
                    line.text = text;
                    matched++;
                }
            }
            return matched > 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TranslatedLyrics] Could not read '{path}': {ex.Message}");
            return false;
        }
    }

    private static bool IsFresh(string stampPath)
    {
        try
        {
            if (!File.Exists(stampPath)) return false;
            long ticks;
            if (!long.TryParse(File.ReadAllText(stampPath), out ticks)) return false;
            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalDays < RefreshDays;
        }
        catch
        {
            return false;
        }
    }
}
