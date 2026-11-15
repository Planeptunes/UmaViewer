using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using UnityEngine;

public static class LiveAutoSelection
{
    private sealed class Candidate
    {
        public CharaEntry Character;
        public string CostumeId;
        public Sprite Icon;
        public int BodyTypeSub;
    }

    // Empty slots get defaults (existing character, body costume and head costume choices are not replaced), then the
    // extra characters of the song are forced like the game does (Live3DSettings.ApplyCharaExpansion).
    public static bool FillMissing(UmaViewerMain main, LiveEntry live,
        IList<LiveCharacterSelect> slots, Sprite fallbackIcon = null)
    {
        if (!FillEmptySlots(main, live, slots, fallbackIcon)) return false;
        ForceCharacterExpansions(main, live, slots, fallbackIcon);
        return true;
    }

    private static bool FillEmptySlots(UmaViewerMain main, LiveEntry live,
        IList<LiveCharacterSelect> slots, Sprite fallbackIcon)
    {
        if (main == null || live == null || slots == null) return false;
        bool needsDefaults = false;
        var used = new HashSet<int>();
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            if (slot.CharaEntry != null && slot.CharaEntry.Id > 0) used.Add(slot.CharaEntry.Id);
            if (slot.CharaEntry == null || slot.CharaEntry.Id <= 0 || string.IsNullOrEmpty(slot.CostumeId)) needsDefaults = true;
        }
        if (!needsDefaults) return true;

        var defaults = GetWinningCostumes(main, fallbackIcon);
        var singerIds = GetSingerIds(main, live.MusicId);
        var singers = new List<Candidate>();
        var others = new List<Candidate>();
        foreach (var character in main.Characters)
        {
            if (character == null || character.IsMob || !defaults.TryGetValue(character.Id, out var candidate)) continue;
            candidate.Character = character;
            (singerIds.Contains(character.Id) ? singers : others).Add(candidate);
        }
        Shuffle(singers);
        Shuffle(others);

        for (int index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot == null) continue;
            bool missingCharacter = slot.CharaEntry == null || slot.CharaEntry.Id <= 0;
            bool missingCostume = string.IsNullOrEmpty(slot.CostumeId);
            if (!missingCharacter && !missingCostume) continue;

            Candidate selected;
            if (missingCharacter)
            {
                selected = FindCandidate(main, singers, used, slot.CostumeId, true)
                    ?? FindCandidate(main, others, used, slot.CostumeId, true)
                    ?? FindCandidate(main, singers, used, slot.CostumeId, false)
                    ?? FindCandidate(main, others, used, slot.CostumeId, false);
            }
            else
            {
                defaults.TryGetValue(slot.CharaEntry.Id, out selected);
                if (slot.CharaEntry.IsMob) selected = null;
            }
            if (selected == null) return false;

            if (missingCharacter) slot.CharaEntry = selected.Character;
            if (missingCostume) slot.CostumeId = selected.CostumeId;
            used.Add(slot.CharaEntry.Id);
            if (slot.CharaImage != null)
            {
                slot.CharaImage.enabled = true;
                slot.CharaImage.sprite = slot.CharaEntry.Icon;
            }
            if (missingCostume && slot.CostumeImage != null)
                slot.CostumeImage.sprite = selected.Icon;
        }
        return true;
    }

    // Full mode keeps only MemberCount UI slots, so the extra characters do not exist there as slots: they are appended to the load
    // data (UmaViewerBuilder.LoadLive) as the game builds them. Returns null when `index` is not an expansion of this live, its
    // source is missing or its dress has no body for that character (the caller then keeps the old identity cycling).
    public static LiveCharacterLoadData CreateExpansionCharacter(UmaViewerMain main, LiveEntry live,
        IList<LiveCharacterLoadData> characters, int index, Sprite fallbackIcon)
    {
        if (main == null || live == null || characters == null) return null;
        var expansions = live.GetCharacterExpansions();
        int e = index - live.MemberCount;
        if (e < 0 || e >= expansions.Count) return null;
        int sourceIndex = expansions[e].SourceSlot;
        if (sourceIndex < 0 || sourceIndex >= characters.Count || sourceIndex >= index) return null;
        var source = characters[sourceIndex];
        if (source == null || source.CharaEntry == null || source.CharaEntry.Id <= 0 || source.CharaEntry.IsMob) return null;
        if (!TryGetDressCostume(main, source.CharaEntry, expansions[e].DressId, fallbackIcon, out string costumeId, out Sprite _))
            return null;
        return new LiveCharacterLoadData
        {
            CharaEntry = source.CharaEntry,
            BodyCharaEntry = null,
            CostumeId = costumeId,
            HeadCostumeId = "",
            CostumeColorIndex = 0
        };
    }

    // Live3DSettings.ApplyCharaExpansion (probe 1794756197, live 1092): extra character j sits at slot MemberCount + j and is a
    // copy of slot CharacterExpansionArray[j] (livesettings row 13, column 2) wearing the live's dress (column 3; the game
    // gives it colour -1, head -1). It overrides whatever the slot had, as in the game; the source slot is not changed.
    // A copy whose dress has no body for that character is left as it was.
    private static void ForceCharacterExpansions(UmaViewerMain main, LiveEntry live,
        IList<LiveCharacterSelect> slots, Sprite fallbackIcon)
    {
        if (main == null || live == null || slots == null) return;
        var expansions = live.GetCharacterExpansions();
        for (int e = 0; e < expansions.Count; e++)
        {
            int index = live.MemberCount + e;
            if (index >= slots.Count) break;
            var slot = slots[index];
            int sourceIndex = expansions[e].SourceSlot;
            if (slot == null || sourceIndex < 0 || sourceIndex >= index) continue;
            var source = slots[sourceIndex];
            if (source == null || source.CharaEntry == null || source.CharaEntry.Id <= 0 || source.CharaEntry.IsMob) continue;
            if (!TryGetDressCostume(main, source.CharaEntry, expansions[e].DressId, fallbackIcon,
                    out string costumeId, out Sprite icon)) continue;

            slot.CharaEntry = source.CharaEntry;
            slot.BodyCharaEntry = null;
            slot.CostumeId = costumeId;
            slot.HeadCostumeId = "";
            slot.CostumeColorIndex = 0;
            if (slot.CharaImage != null)
            {
                slot.CharaImage.enabled = true;
                slot.CharaImage.sprite = slot.CharaEntry.Icon;
            }
            if (slot.CostumeImage != null) slot.CostumeImage.sprite = icon;
        }
    }

    // dress_data id -> costume id the viewer loads. Common dresses (chara_id 0) are "<body_type:0000>_<sub:00>_<variant:00>"
    // (first variant that has a body for this character); character-owned dresses use the body_type_sub, as in
    // GetWinningCostumes.
    private static bool TryGetDressCostume(UmaViewerMain main, CharaEntry character, int dressId,
        Sprite fallbackIcon, out string costumeId, out Sprite icon)
    {
        costumeId = null;
        icon = fallbackIcon;
        var rows = UmaDatabaseController.Instance.DressData;
        if (rows == null) return false;
        foreach (DataRow row in rows)
        {
            if (Convert.ToInt32(row["id"]) != dressId) continue;
            int owner = Convert.ToInt32(row["chara_id"]);
            int bodyType = Convert.ToInt32(row["body_type"]);
            int sub = Convert.ToInt32(row["body_type_sub"]);
            if (owner > 0)
            {
                if (owner != character.Id || Convert.ToInt32(row["costume_type"]) != 1) return false;
                string owned = sub.ToString("D2", CultureInfo.InvariantCulture);
                if (!HasBody(main, character, owned)) return false;
                costumeId = owned;
            }
            else
            {
                for (int variant = 0; variant < 10 && costumeId == null; variant++)
                {
                    string common = string.Format(CultureInfo.InvariantCulture, "{0:0000}_{1:00}_{2:00}", bodyType, sub, variant);
                    if (HasBody(main, character, common)) costumeId = common;
                }
                if (costumeId == null) return false;
            }
            foreach (var costume in main.Costumes)
            {
                if (owner > 0 ? (costume.CharaId != owner || costume.BodyTypeSub != sub)
                              : (costume.BodyType != bodyType || costume.BodyTypeSub != sub)) continue;
                if (costume.Icon != null) icon = costume.Icon;
                break;
            }
            return true;
        }
        return false;
    }

    private static Dictionary<int, Candidate> GetWinningCostumes(UmaViewerMain main, Sprite fallbackIcon)
    {
        var result = new Dictionary<int, Candidate>();
        var rows = UmaDatabaseController.Instance.DressData;
        if (rows == null) return result;
        foreach (DataRow row in rows)
        {
            if (Convert.ToInt32(row["costume_type"]) != 1) continue;
            int characterId = Convert.ToInt32(row["chara_id"]);
            int subtype = Convert.ToInt32(row["body_type_sub"]);
            string costumeId = subtype.ToString("D2", CultureInfo.InvariantCulture);
            string body = UmaDatabaseController.BodyPath + $"bdy{characterId}_{costumeId}/pfb_bdy{characterId}_{costumeId}";
            if (characterId <= 0 || !main.AbList.TryGetValue(body, out var entry) || entry == null) continue;
            if (result.TryGetValue(characterId, out var previous) && previous.BodyTypeSub <= subtype) continue;
            Sprite icon = fallbackIcon;
            foreach (var costume in main.Costumes)
            {
                if (costume.CharaId != characterId || costume.BodyTypeSub != subtype) continue;
                if (costume.Icon != null) icon = costume.Icon;
                break;
            }
            result[characterId] = new Candidate { CostumeId = costumeId, Icon = icon, BodyTypeSub = subtype };
        }
        return result;
    }

    private static HashSet<int> GetSingerIds(UmaViewerMain main, int musicId)
    {
        string song = musicId.ToString("D4", CultureInfo.InvariantCulture);
        string prefix = $"sound/l/{song}/snd_bgm_live_{song}_chara_";
        var result = new HashSet<int>();
        foreach (var sound in main.AbSounds)
        {
            string name = sound.Name;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith("awb", StringComparison.OrdinalIgnoreCase)) continue;
            int separator = name.IndexOf('_', prefix.Length);
            if (separator < 0 || !name.Substring(separator).StartsWith("_01", StringComparison.Ordinal)) continue;
            if (int.TryParse(name.Substring(prefix.Length, separator - prefix.Length), out int characterId))
                result.Add(characterId);
        }
        return result;
    }

    private static Candidate FindCandidate(UmaViewerMain main, List<Candidate> candidates,
        HashSet<int> used, string costumeId, bool distinct)
    {
        if (candidates.Count == 0) return null;
        int start = distinct ? 0 : UnityEngine.Random.Range(0, candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[(start + i) % candidates.Count];
            if (distinct && used.Contains(candidate.Character.Id)) continue;
            if (string.IsNullOrEmpty(costumeId) || HasBody(main, candidate.Character, costumeId)) return candidate;
        }
        return null;
    }

    private static bool HasBody(UmaViewerMain main, CharaEntry character, string costumeId)
    {
        if (costumeId.Length < 4)
            return main.AbList.ContainsKey(UmaDatabaseController.BodyPath + $"bdy{character.Id}_{costumeId}/pfb_bdy{character.Id}_{costumeId}");
        int separator = costumeId.LastIndexOf('_');
        if (separator < 0) return false;
        var rows = UmaDatabaseController.Instance.CharaData;
        if (rows == null) return false;
        foreach (var row in rows)
        {
            if (Convert.ToInt32(row["id"]) != character.Id) continue;
            string shortId = costumeId.Substring(0, separator);
            string body = UmaDatabaseController.BodyPath + $"bdy{shortId}/pfb_bdy{costumeId}_{row["height"]}_{row["shape"]}_{row["bust"]}";
            return main.AbList.ContainsKey(body);
        }
        return false;
    }

    private static void Shuffle(List<Candidate> candidates)
    {
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int other = UnityEngine.Random.Range(0, i + 1);
            var candidate = candidates[i];
            candidates[i] = candidates[other];
            candidates[other] = candidate;
        }
    }
}
