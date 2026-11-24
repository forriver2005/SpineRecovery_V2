using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    internal sealed class MobileSummaryLayout : MonoBehaviour
    {
        public Text Summary;
        public RectTransform Card, Shadow, Actions;
        private string previous;

        private void LateUpdate() => Apply();

        internal void Apply()
        {
            if (Summary == null || Summary.text == previous) return;
            previous = Summary.text;
            float height = Mathf.Max(220, Summary.preferredHeight * Summary.rectTransform.localScale.y + 48);
            Card.sizeDelta = new Vector2(350, height);
            if (Shadow != null) Shadow.sizeDelta = new Vector2(352, height + 4);
            Summary.rectTransform.sizeDelta = new Vector2(930, (height - 40) * 3);
            Actions.anchoredPosition = new Vector2(0, -136 - height);
            MobileUi.Height((RectTransform)transform, 136 + height + 484, false);
        }
    }

    // Shared 390-unit presentation for the remaining mobile screens.
    internal static class MobileUi
    {
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> AssetSprites =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        public static Text Text(Transform parent, string value, float x, float y, float width = 310,
            float height = 24, float size = 15, bool bold = false, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            return QuestionnaireUi.Label(parent, value, size, color ?? QuestionnaireUi.Ink, align,
                bold ? FontStyle.Bold : FontStyle.Normal, x, y, width, height);
        }

        public static Button Choice(Transform parent, string caption, float x, float y, float width,
            UnityAction action, bool selected = false, float height = 48)
        {
            var button = QuestionnaireUi.MakeButton(parent, caption, Color.white, 12, x, y, width, height, action);
            Text(button.transform, caption, 14, 0, width - 28, height);
            Select(button, selected);
            return button;
        }

        public static void Select(Button button, bool selected)
        {
            if (button == null) return;
            // White base graphic: ColorBlock owns tint, including legacy selection handlers.
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = selected ? QuestionnaireUi.Accent : QuestionnaireUi.OptionBackground;
            colors.highlightedColor = colors.normalColor;
            colors.pressedColor = new Color(.76f, .85f, .97f);
            button.colors = colors;
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? Color.white : QuestionnaireUi.Ink;
        }

        public static void Header(Transform root, string title, UnityAction back)
        {
            var bar = QuestionnaireUi.Box(root, "Header", QuestionnaireUi.HeaderBackground, .5f, 0, 0, 390, 56);
            Text(bar.transform, title, 56, 0, 278, 56, 17, true, QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
            if (back == null) return;
            var button = QuestionnaireUi.MakeButton(bar.transform, "Back", Color.clear, 1, 6, 6, 44, 44, back);
            Asset(button.transform, "Back", 14, 10, 24, 24);
        }

        public static void Asset(Transform parent, string name, float x, float y, float width, float height)
        {
            var texture = Resources.Load<Texture2D>("AppUi/Refresh/" + name);
            if (texture == null) { Debug.LogError("Missing mobile UI asset: " + name); return; }
            var go = new GameObject("Figma:" + name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            // Fit without stretching the source image.
            float scale = Mathf.Min(width / texture.width, height / texture.height);
            float w = texture.width * scale, h = texture.height * scale;
            QuestionnaireUi.Place((RectTransform)go.transform, x + (width - w) / 2, y + (height - h) / 2, w, h);
            if (!AssetSprites.TryGetValue(name, out var sprite) || sprite == null)
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                sprite.name = "Figma:" + name;
                AssetSprites[name] = sprite;
            }
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child.gameObject);
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        public static void Height(RectTransform content, float height, bool reset = true)
        {
            content.sizeDelta = new Vector2(0, height);
            if (!reset) return;
            content.anchoredPosition = Vector2.zero;
            var scroll = content.parent.GetComponent<ScrollRect>();
            if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
        }

        public static string Date(string value)
        {
            return DateTimeOffset.TryParse(value, out var parsed)
                ? parsed.ToLocalTime().ToString("MM月dd日 HH:mm") : string.IsNullOrWhiteSpace(value) ? "时间未记录" : value;
        }

        public static T ReadJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }
    }
}
