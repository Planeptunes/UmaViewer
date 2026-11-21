using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Gallop.Live
{
    /// <summary>
    /// Port of Gallop.Live.RubyText (UpdateText RVA 0x592DC520 in the dump listing, SetRubyPos,
    /// Initialize, Clear). Tags have the form "[base]&lt;ruby&gt;": the tag is replaced by "base" in
    /// the label text and "ruby" is drawn on its own Text centered above the base characters, using
    /// the positions given by a TextGenerator populated with the label's generation settings.
    ///
    /// Data not recovered from the game code (they live in the game's prefab): the ruby Text
    /// prefab style (font size / color / outline) and the serialized _offsetHeight. The ruby Text
    /// copies the label's font, color and Shadow effects, scaled by <see cref="RubyFontScale"/>.
    /// </summary>
    public class RubyText : MonoBehaviour
    {
        // RubyText..cctor: RUBY_TAG_REGEX. Group 1 = base text, group 2 = ruby text.
        private static readonly Regex RUBY_TAG_REGEX = new Regex(@"\[(.*?)\]<(.*?)>");

        // RubyText.Initialize creates 8 ruby slots up front.
        private const int RUBY_LIST_CAPACITY = 8;

        // RubyText._offsetHeight (serialized field, prefab value not available).
        public float _offsetHeight = 0f;

        // Ruby font size relative to the label font size (prefab value not available).
        public float RubyFontScale = 0.5f;

        private struct RubyPos
        {
            public string Ruby;
            public int Start;
            public int End;
        }

        private Text _text;
        private RectTransform _rectTransform;
        private TextGenerator _textGenerator;
        private readonly List<RubyPos> _rubyPosList = new List<RubyPos>(RUBY_LIST_CAPACITY);
        private readonly List<Text> _rubyTextInstanceList = new List<Text>(RUBY_LIST_CAPACITY);
        private string _originalText;
        private bool _isInitialized;

        /// <summary>Text with the ruby tags, as set by <see cref="SetLyrics"/>.</summary>
        public string RawText { get; private set; } = "";

        /// <summary>Gets (or adds) the RubyText of a label.</summary>
        public static RubyText For(Text label)
        {
            if (label == null) return null;
            var ruby = label.GetComponent<RubyText>();
            if (ruby == null) ruby = label.gameObject.AddComponent<RubyText>();
            return ruby;
        }

        /// <summary>LyricsController.UpdateTime: label.text = lyrics; rubyText.UpdateText(true).</summary>
        public void SetLyrics(string lyrics)
        {
            if (lyrics == null) lyrics = "";
            Initialize();
            if (!_isInitialized) return;
            // Unchanged line and nobody touched the label meanwhile: nothing to rebuild.
            if (lyrics == RawText && _text.text == _shownText) return;
            RawText = lyrics;
            _text.text = lyrics;
            UpdateText(true);
            _shownText = _text.text;
        }

        private string _shownText;

        // RubyText.Initialize
        private void Initialize()
        {
            if (_isInitialized) return;
            _text = GetComponent<Text>();
            if (_text == null) return;
            _rectTransform = _text.rectTransform;
            _textGenerator = new TextGenerator();
            for (int i = 0; i < RUBY_LIST_CAPACITY; i++)
            {
                _rubyPosList.Add(default(RubyPos));
                CreateRubyTextInstance();
            }
            _isInitialized = true;
        }

        // RubyText.CreateRubyTextInstance
        private Text CreateRubyTextInstance()
        {
            var go = new GameObject("Ruby", typeof(RectTransform), typeof(Text));
            go.layer = gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_text.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;

            var ruby = go.GetComponent<Text>();
            ruby.font = _text.font;
            ruby.color = _text.color;
            ruby.alignment = TextAnchor.MiddleCenter;
            ruby.horizontalOverflow = HorizontalWrapMode.Overflow;
            ruby.verticalOverflow = VerticalWrapMode.Overflow;
            ruby.raycastTarget = false;
            ruby.supportRichText = false;
            ruby.text = "";
            ruby.enabled = false;

            foreach (var sourceEffect in _text.GetComponents<Shadow>())
            {
                var effect = go.AddComponent(sourceEffect.GetType()) as Shadow;
                if (effect == null) continue;
                effect.effectColor = sourceEffect.effectColor;
                effect.effectDistance = sourceEffect.effectDistance * RubyFontScale;
                effect.useGraphicAlpha = sourceEffect.useGraphicAlpha;
            }

            _rubyTextInstanceList.Add(ruby);
            return ruby;
        }

        // RubyText.GetRubyTextInstance
        private Text GetRubyTextInstance(int index)
        {
            if (index >= 0 && index < _rubyTextInstanceList.Count) return _rubyTextInstanceList[index];
            return CreateRubyTextInstance();
        }

        // RubyText.SetRubyPos
        private void SetRubyPos(int index, string ruby, int start, int end)
        {
            var pos = new RubyPos { Ruby = ruby, Start = start, End = end };
            if (index >= 0 && index < _rubyPosList.Count) _rubyPosList[index] = pos;
            else _rubyPosList.Add(pos);
        }

        // RubyText.Clear / the reset at the top of UpdateText
        private void ResetRubies()
        {
            for (int i = 0; i < _rubyPosList.Count; i++)
                _rubyPosList[i] = default(RubyPos);
            for (int i = 0; i < _rubyTextInstanceList.Count; i++)
            {
                _rubyTextInstanceList[i].text = "";
                _rubyTextInstanceList[i].enabled = false;
            }
        }

        // RubyText.UpdateText
        private void UpdateText(bool getTextFromLabel)
        {
            if (!_isInitialized) return;
            if (getTextFromLabel) _originalText = _text.text;

            ResetRubies();
            if (string.IsNullOrEmpty(_originalText)) return;

            MatchCollection matches = RUBY_TAG_REGEX.Matches(_originalText);
            if (matches.Count <= 0) return;

            string str = _originalText;
            int removed = 0;
            int index = 0;
            foreach (Match match in matches)
            {
                if (match.Groups.Count <= 2) continue;
                Group baseGroup = match.Groups[1];
                Group rubyGroup = match.Groups[2];

                str = str.Remove(match.Index - removed, match.Length);
                str = str.Insert(match.Index - removed, baseGroup.Value);

                int start = baseGroup.Index - removed - 1;
                SetRubyPos(index, rubyGroup.Value, start, start + baseGroup.Length - 1);
                index++;
                removed += rubyGroup.Length + 4;
            }

            _text.text = str;
            TextGenerationSettings settings = _text.GetGenerationSettings(_rectTransform.sizeDelta);
            _textGenerator.Populate(_text.text, settings);
            IList<UICharInfo> chars = _textGenerator.characters;
            if (chars == null || chars.Count == 0) return;

            float pixelsPerUnit = settings.scaleFactor;
            if (pixelsPerUnit <= 1e-6f) pixelsPerUnit = 1f;

            int rubyFontSize = Mathf.Max(1, Mathf.RoundToInt(_text.fontSize * RubyFontScale));
            for (int i = 0; i < _rubyPosList.Count; i++)
            {
                RubyPos pos = _rubyPosList[i];
                if (pos.Start < 0 || pos.Start >= chars.Count || pos.End < 0 || pos.End >= chars.Count) continue;

                Text instance = GetRubyTextInstance(i);
                instance.fontSize = rubyFontSize;
                instance.text = pos.Ruby;
                instance.enabled = true;

                UICharInfo first = chars[pos.Start];
                UICharInfo last = chars[pos.End];
                float x = ((last.cursorPos.x + last.charWidth - first.cursorPos.x) * 0.5f + first.cursorPos.x) / pixelsPerUnit;
                float y = first.cursorPos.y / pixelsPerUnit + instance.preferredHeight * 0.5f + _offsetHeight;
                instance.rectTransform.SetParent(_text.transform, false);
                instance.rectTransform.localPosition = new Vector3(x, y, 0f);
            }
        }
    }
}
