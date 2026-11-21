using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gallop.Live
{
    /// <summary>
    /// Port of the data side of Gallop.Live.LyricsController: LoadLyrics (RVA 0x4ED0290),
    /// IsVisibleLyric (0x4ED0110) and UpdateTime (0x4ED08D0), plus Gallop.GallopUtil.ConvertCSV
    /// (RVA 0x58FDC6D0 in the dump listing). The MonoBehaviour part (label objects) is not ported:
    /// the viewer draws the current line on its own Text (see RubyText).
    /// </summary>
    public static class LyricsController
    {
        // LyricsController.AdditionalSetting
        public const int ADDITIONAL_SETTING_NONE = 0;
        public const int ADDITIONAL_SETTING_SHEET_VARIATION_ID = 1;

        /// <summary>LyricsController.LoadLyrics: CSV text (header row first) to the lyric table.</summary>
        public static List<UmaLyricsData> LoadLyrics(string csvText)
        {
            var result = new List<UmaLyricsData>();
            if (string.IsNullOrEmpty(csvText)) return result;

            List<List<string>> rows = ConvertCSV(csvText, true);
            for (int i = 0; i < rows.Count; i++)
            {
                List<string> row = rows[i];
                var data = new UmaLyricsData { time = 0f, text = "" };
                if (row.Count > 1)
                {
                    // TimeUtil.ConvertMilliSecondsToSeconds(int.Parse(column 0))
                    data.time = int.Parse(row[0], CultureInfo.InvariantCulture) / 1000f;
                    data.text = ReplaceText(row[1]);
                    if (row.Count > 3)
                    {
                        int type, value;
                        if (int.TryParse(row[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out type) &&
                            int.TryParse(row[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        {
                            data.additionalSettingType = type;
                            data.additionalSettingValue = value;
                        }
                    }
                }
                result.Add(data);
            }
            return result;
        }

        /// <summary>TextUtil.ReplaceComma(TextUtil.Replace(text, "\\n", "\n")) as applied by LoadLyrics.</summary>
        public static string ReplaceText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\\n", "\n");
            return text.Replace("[COMMA]", ",");
        }

        /// <summary>LyricsController.IsVisibleLyric (0x4ED0110).</summary>
        public static bool IsVisibleLyric(UmaLyricsData data, int sheetVariationId)
        {
            if (data.additionalSettingType != ADDITIONAL_SETTING_SHEET_VARIATION_ID) return true;
            return data.additionalSettingValue == sheetVariationId;
        }

        /// <summary>
        /// LyricsController.UpdateTime (0x4ED08D0): index of the line shown at time t, or -1 when
        /// there are no lines. The line is the last visible one whose time is &lt;= t.
        /// </summary>
        public static int GetDisplayIndex(List<UmaLyricsData> data, float t, int sheetVariationId)
        {
            int count = data != null ? data.Count : 0;
            if (count == 0) return -1;

            int i = 0;
            for (; i < count; i++)
            {
                if (!IsVisibleLyric(data[i], sheetVariationId)) continue;
                if (data[i].time > t) continue;

                int next = i;
                do
                {
                    next++;
                    if (next >= count) break;
                } while (!IsVisibleLyric(data[next], sheetVariationId));

                if (next < count && data[next].time <= t) continue;
                break;
            }
            if (i >= count) i = count - 1;
            return i;
        }

        /// <summary>Text of the line returned by <see cref="GetDisplayIndex"/> ("" when none).</summary>
        public static string GetCurrentLyrics(List<UmaLyricsData> data, float t, int sheetVariationId)
        {
            int index = GetDisplayIndex(data, t, sheetVariationId);
            if (index < 0) return "";
            return data[index].text ?? "";
        }

        /// <summary>
        /// GallopUtil.ConvertCSV(text, skipFirstLine): state machine CSV reader (RFC style quotes:
        /// a quoted field may hold commas and line breaks, "" is an escaped quote). Leading and
        /// trailing blanks of a field are trimmed. Throws FormatException on malformed quotes,
        /// like the original ApplicationException.
        /// </summary>
        public static List<List<string>> ConvertCSV(string s, bool skipFirstLine)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            int n = s.Length;
            int j = 0;
            while (j < n && s[j] == '﻿') j++;

            int state = 0;       // 0 = field start, 1 = plain field, 2 = inside quotes, 3 = after closing quote
            int start = j;
            int end = j;
            bool escaped = false;
            bool skip = skipFirstLine;

            for (; j < n; j++)
            {
                char c = s[j];
                if (c == ' ' || c == '\t')
                {
                    if (state == 2) end = j;
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && j < n - 1 && s[j + 1] == '\n') j++;

                    if (state == 2)
                    {
                        end = j;
                    }
                    else if (state == 0)
                    {
                        if (row.Count > 0)
                        {
                            row.Add("");
                            EndRow(rows, ref row, ref skip);
                        }
                    }
                    else
                    {
                        row.Add(ReadField(s, start, end, escaped));
                        escaped = false;
                        state = 0;
                        EndRow(rows, ref row, ref skip);
                    }
                }
                else if (c == '"')
                {
                    if (state == 0)
                    {
                        state = 2;
                        start = j + 1;
                        end = j;
                    }
                    else if (state == 2)
                    {
                        if (j < n - 1 && s[j + 1] == '"')
                        {
                            j++;
                            escaped = true;
                            end = j;
                        }
                        else
                        {
                            state = 3;
                        }
                    }
                    else
                    {
                        throw new FormatException("CSV: unexpected quote");
                    }
                }
                else if (c == ',')
                {
                    if (state == 0)
                    {
                        row.Add("");
                    }
                    else if (state == 2)
                    {
                        end = j;
                    }
                    else
                    {
                        row.Add(ReadField(s, start, end, escaped));
                        escaped = false;
                        state = 0;
                    }
                }
                else
                {
                    if (state == 0)
                    {
                        state = 1;
                        start = j;
                    }
                    else if (state == 3)
                    {
                        throw new FormatException("CSV: unexpected character after closing quote");
                    }
                    end = j;
                }
            }

            if (state != 0)
            {
                if (state == 2) throw new FormatException("CSV: unterminated quote");
                if (skip) return rows;
                // Original: the last field is taken as is (no "" unescape here).
                row.Add(s.Substring(start, end - start + 1));
                rows.Add(row);
                return rows;
            }

            if (row.Count > 0 && !skip)
            {
                row.Add("");
                rows.Add(row);
            }
            return rows;
        }

        private static string ReadField(string s, int start, int end, bool escaped)
        {
            string field = s.Substring(start, end - start + 1);
            if (escaped) field = field.Replace("\"\"", "\"");
            return field;
        }

        private static void EndRow(List<List<string>> rows, ref List<string> row, ref bool skip)
        {
            if (!skip) rows.Add(row);
            else skip = false;
            row = new List<string>(row.Count);
        }
    }
}
