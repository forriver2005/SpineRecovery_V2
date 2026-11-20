using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    /// <summary>
    /// Training home: greeting, the AR training entry and a list of
    /// secondary entries with status badges. Laid out in the same 390-unit
    /// design space as the questionnaires; icons are drawn from shapes so
    /// they stay sharp at any resolution.
    /// </summary>
    internal sealed class HomeScreen
    {
        internal enum MonthlyEntryStatus
        {
            /// <summary>No assignment issued: row is greyed out and not tappable.</summary>
            Disabled,
            /// <summary>Pending assignment the patient has not opened yet: 「新」 hint.</summary>
            New,
            /// <summary>Pending assignment already opened: still fillable this month.</summary>
            Open,
        }

        internal struct State
        {
            public string Name;
            public bool Activated;
            public bool Calibrated;
            public MonthlyEntryStatus MonthlyStatus;
        }

        internal sealed class Actions
        {
            public UnityAction EnterTraining;
            public UnityAction OpenTracker;
            public UnityAction OpenActivation;
            public UnityAction OpenHistory;
            public UnityAction OpenMonthly;
            public UnityAction Logout;
        }

        private enum BadgeKind
        {
            Pending,
            Done,
            Info,
            New,
        }

        private sealed class Badge
        {
            public Image Fill;
            public Text Caption;

            public void Set(string caption, BadgeKind kind)
            {
                Caption.text = caption;
                switch (kind)
                {
                    case BadgeKind.Done:
                        Fill.color = DoneFill;
                        Caption.color = DoneText;
                        break;
                    case BadgeKind.Info:
                        Fill.color = TileFill;
                        Caption.color = InfoText;
                        break;
                    case BadgeKind.New:
                        Fill.color = QuestionnaireUi.DangerFill;
                        Caption.color = QuestionnaireUi.DangerText;
                        break;
                    default:
                        Fill.color = QuestionnaireUi.BadgeFill;
                        Caption.color = QuestionnaireUi.BadgeText;
                        break;
                }
            }

            public void Hide()
            {
                Caption.text = "";
                Fill.color = Color.clear;
            }
        }

        private sealed class RowRefs
        {
            public Button Button;
            public Text Title;
            public Text Subtitle;
            public Image Tile;
            public Transform Glyph;
        }

        private const float RowHeight = 64f;
        private const float RowsTop = 6.5f;
        private const float CardWidth = 350f;
        private const float BadgeRight = 304f;

        private static readonly Color ArFill = new Color(0.369f, 0.627f, 0.933f);
        private static readonly Color GlyphBlue = new Color(0.235f, 0.447f, 0.769f);
        private static readonly Color TileFill = new Color(0.90f, 0.94f, 0.99f);
        private static readonly Color RowPressed = new Color(0.90f, 0.94f, 1f, 0.9f);
        private static readonly Color ChevronGrey = new Color(0.55f, 0.58f, 0.65f);
        private static readonly Color DoneFill = new Color(0.886f, 0.969f, 0.914f);
        private static readonly Color DoneText = new Color(0.09f, 0.44f, 0.24f);
        private static readonly Color InfoText = new Color(0.17f, 0.36f, 0.70f);
        private static readonly Color DisabledInk = new Color(0.60f, 0.64f, 0.70f);
        private static readonly Color DisabledTile = new Color(0.90f, 0.92f, 0.95f);

        private Func<State> getState;
        private Text avatarLetter;
        private Text greeting;
        private Badge trackerBadge;
        private Badge monthlyBadge;
        private RowRefs monthlyRow;
        private Image monthlyNewDot;
        private string shownName = "";

        /// <summary>Free-form status line under the list (training launch errors, save confirmations).</summary>
        public Text Message { get; private set; }

        public GameObject Build(Transform canvasParent, Func<State> stateProvider, Actions actions)
        {
            getState = stateProvider;
            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, "HomePanel", Refresh, out GameObject panel);
            // Keep the header below the phone camera cutout while preserving the design spacing.
            root.anchoredPosition = new Vector2(0f, -18f);

            Image avatar = QuestionnaireUi.Box(root, "Avatar", QuestionnaireUi.SecondaryButton, 26f, 20f, 81.5f, 52f, 52f);
            avatarLetter = QuestionnaireUi.Label(avatar.transform, "?", 24, GlyphBlue, TextAnchor.MiddleCenter,
                FontStyle.Bold, 0f, 0f, 52f, 52f);
            greeting = QuestionnaireUi.Label(root, "你好，", 24, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 86f, 86f, 284f, 36f);

            BuildTrainingButton(root, actions.EnterTraining);

            RectTransform card = QuestionnaireUi.Card(root, 20f, 267f, CardWidth, 204f);
            BuildRow(card, 0, "追踪器连接与校准", "训练前先连接并校准传感器", DrawTracker, actions.OpenTracker, 56f, out trackerBadge);
            BuildRow(card, 1, "训练历史", "查看每次训练的记录", DrawClock, actions.OpenHistory, 0f, out _);
            monthlyRow = BuildRow(card, 2, "月度问卷", "每月一次，了解恢复情况", DrawClipboard, actions.OpenMonthly, 68f, out monthlyBadge);
            for (int i = 0; i < 2; i++)
            {
                QuestionnaireUi.Box(card, "Divider", QuestionnaireUi.Divider, 0.5f, 0f,
                    RowsTop + RowHeight * (i + 1) - 0.5f, CardWidth, 1f);
            }
            monthlyNewDot = QuestionnaireUi.Box(card, "MonthlyNewDot", QuestionnaireUi.DangerText, 6f,
                333f, RowsTop + RowHeight * 2f + 6f, 12f, 12f);
            monthlyNewDot.gameObject.SetActive(false);

            Message = QuestionnaireUi.Label(root, "", 13, QuestionnaireUi.Muted, TextAnchor.UpperLeft,
                FontStyle.Normal, 20f, 487f, 350f, 60f);

            RectTransform footer = QuestionnaireUi.Footer(root, 68f);
            Button logout = QuestionnaireUi.MakeButton(footer, "Logout", Color.clear, 0.5f, 115f, 12f, 160f, 44f,
                actions.Logout);
            QuestionnaireUi.Label(logout.transform, "退出登录", 15, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 0f, 0f, 160f, 44f);

            QuestionnaireUi.Header(root, "训练首页", null);
            return panel;
        }

        /// <summary>Re-reads the account state; runs every time the home page is shown.</summary>
        public void Refresh()
        {
            if (getState == null || greeting == null) return;
            State state = getState();
            string name = state.Name ?? "";
            if (name != shownName)
            {
                // A different account signed in: drop the previous one's status line.
                shownName = name;
                if (Message != null) Message.text = "";
            }

            avatarLetter.text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
            greeting.text = "你好，" + name;
            trackerBadge.Set(state.Calibrated ? "已校准" : "未校准", state.Calibrated ? BadgeKind.Done : BadgeKind.Pending);
            ApplyMonthlyStatus(state.MonthlyStatus);
        }

        private void ApplyMonthlyStatus(MonthlyEntryStatus status)
        {
            if (monthlyRow == null || monthlyBadge == null || monthlyNewDot == null) return;
            switch (status)
            {
                case MonthlyEntryStatus.Disabled:
                    SetMonthlyEnabled(false);
                    monthlyNewDot.gameObject.SetActive(false);
                    monthlyBadge.Hide();
                    break;
                case MonthlyEntryStatus.New:
                    SetMonthlyEnabled(true);
                    monthlyNewDot.gameObject.SetActive(true);
                    monthlyBadge.Set("新", BadgeKind.New);
                    break;
                default:
                    SetMonthlyEnabled(true);
                    monthlyNewDot.gameObject.SetActive(false);
                    monthlyBadge.Set("本月待填", BadgeKind.Info);
                    break;
            }
        }

        private void SetMonthlyEnabled(bool enabled)
        {
            monthlyRow.Button.interactable = enabled;
            monthlyRow.Title.color = enabled ? QuestionnaireUi.Ink : DisabledInk;
            monthlyRow.Subtitle.color = enabled ? QuestionnaireUi.Muted : DisabledInk;
            monthlyRow.Tile.color = enabled ? TileFill : DisabledTile;
            foreach (Image part in monthlyRow.Glyph.GetComponentsInChildren<Image>())
            {
                part.color = enabled ? GlyphBlue : DisabledInk;
            }
        }

        // ---- layout -------------------------------------------------------------

        private static void BuildTrainingButton(RectTransform root, UnityAction onClick)
        {
            Button button = QuestionnaireUi.MakeButton(root, "EnterTraining", ArFill, 20f, 20f, 151f, 350f, 100f, onClick);
            Image tile = QuestionnaireUi.Box(button.transform, "IconTile", new Color(1f, 1f, 1f, 0.2f), 16f,
                20f, 22f, 56f, 56f);
            DrawGoggles(GlyphBox(tile.transform, 10f, 16f, 36f, 24f));
            QuestionnaireUi.Label(button.transform, "进入 AR 训练", 22, Color.white, TextAnchor.MiddleLeft,
                FontStyle.Bold, 89f, 36f, 200f, 28f);
            Chevron(button.transform, 319f, 50f, Color.white, 2.4f, 1.15f);
        }

        private static RowRefs BuildRow(RectTransform card, int index, string title, string subtitle,
            Action<Transform> drawGlyph, UnityAction onClick, float badgeWidth, out Badge badge)
        {
            Image row = QuestionnaireUi.Box(card, "Row" + index, Color.white, 0.5f, 0f,
                RowsTop + RowHeight * index, CardWidth, RowHeight);
            row.raycastTarget = true;
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            button.transition = Selectable.Transition.ColorTint;
            // The row is a white image tinted fully transparent when idle, so
            // only the pressed state shows (no square corners over the card).
            Color idle = new Color(1f, 1f, 1f, 0f);
            ColorBlock colors = button.colors;
            colors.normalColor = idle;
            colors.highlightedColor = idle;
            colors.selectedColor = idle;
            colors.disabledColor = idle;
            colors.pressedColor = RowPressed;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);

            Image tile = QuestionnaireUi.Box(row.transform, "IconTile", TileFill, 12f, 18f, 12f, 40f, 40f);
            RectTransform glyph = GlyphBox(tile.transform, 8f, 8f, 24f, 24f);
            drawGlyph(glyph);

            float titleWidth = (badgeWidth > 0f ? BadgeRight - badgeWidth - 6f : 270f) - 72.5f;
            Text titleLabel = QuestionnaireUi.Label(row.transform, title, 17, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 72.5f, 12f, titleWidth, 22f);
            Text subtitleLabel = QuestionnaireUi.Label(row.transform, subtitle, 13, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 72.5f, 36f, titleWidth, 18f);

            badge = null;
            if (badgeWidth > 0f)
            {
                Image fill = QuestionnaireUi.Box(row.transform, "Badge", QuestionnaireUi.BadgeFill, 11f,
                    BadgeRight - badgeWidth, 21f, badgeWidth, 22f);
                Text caption = QuestionnaireUi.Label(fill.transform, "", 13, QuestionnaireUi.BadgeText,
                    TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, badgeWidth, 22f);
                badge = new Badge { Fill = fill, Caption = caption };
            }

            Chevron(row.transform, 326f, 32f, ChevronGrey, 2f, 1f);
            return new RowRefs { Button = button, Title = titleLabel, Subtitle = subtitleLabel, Tile = tile, Glyph = glyph };
        }

        // ---- drawing helpers --------------------------------------------------------

        private static RectTransform GlyphBox(Transform parent, float x, float y, float width, float height)
        {
            GameObject go = new GameObject("Glyph", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return QuestionnaireUi.Place((RectTransform)go.transform, x, y, width, height);
        }

        /// <summary>A rectangle that clips its children, used to show half of a ring.</summary>
        private static RectTransform Clip(Transform parent, float x, float y, float width, float height)
        {
            GameObject go = new GameObject("Clip", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            return QuestionnaireUi.Place((RectTransform)go.transform, x, y, width, height);
        }

        private static void Outline(Transform parent, float x, float y, float width, float height, float radius,
            float thickness, Color color)
        {
            Image ring = QuestionnaireUi.Ring(parent, "Outline", color, radius, thickness);
            QuestionnaireUi.Place(ring.rectTransform, x, y, width, height);
        }

        /// <summary>A line segment with round ends, between two points in the parent's design units.</summary>
        private static void Stroke(Transform parent, float x1, float y1, float x2, float y2, float thickness,
            Color color)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            Image bar = QuestionnaireUi.Box(parent, "Stroke", color, thickness * 0.5f, x1, y1,
                Mathf.Sqrt(dx * dx + dy * dy), thickness);
            RectTransform rect = bar.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x1, -y1);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg);
        }

        private static void Chevron(Transform parent, float centreX, float centreY, Color color, float thickness,
            float scale)
        {
            float tipX = centreX + 3f * scale;
            Stroke(parent, tipX, centreY, centreX - 3f * scale, centreY - 5.5f * scale, thickness, color);
            Stroke(parent, tipX, centreY, centreX - 3f * scale, centreY + 5.5f * scale, thickness, color);
        }

        // ---- icons (24x24 glyph boxes, 36x24 for the goggles) -----------------------

        private static void DrawGoggles(Transform glyph)
        {
            Outline(glyph, 1f, 3f, 34f, 19f, 8f, 2.5f, Color.white);
            QuestionnaireUi.Box(glyph, "EyeLeft", Color.white, 2.25f, 11.5f, 10.5f, 4.5f, 4.5f);
            QuestionnaireUi.Box(glyph, "EyeRight", Color.white, 2.25f, 20f, 10.5f, 4.5f, 4.5f);
        }

        // "(▢)": a sensor between two radio-wave arcs, each arc being the edge of a circle seen through a clip.
        private static void DrawTracker(Transform glyph)
        {
            Outline(glyph, 7.5f, 5f, 9f, 14f, 3.5f, 2f, GlyphBlue);
            Outline(Clip(glyph, 0f, 3f, 4f, 18f), 0f, 0f, 18f, 18f, 9f, 2f, GlyphBlue);
            Outline(Clip(glyph, 20f, 3f, 4f, 18f), -14f, 0f, 18f, 18f, 9f, 2f, GlyphBlue);
        }

        private static void DrawPersonCard(Transform glyph)
        {
            Outline(glyph, 3f, 1f, 18f, 22f, 4.5f, 2f, GlyphBlue);
            Outline(glyph, 8.5f, 5.5f, 7f, 7f, 3.5f, 2f, GlyphBlue);
            Outline(Clip(glyph, 6.5f, 14f, 11f, 5f), 0f, 0f, 11f, 11f, 5.5f, 2f, GlyphBlue);
        }

        private static void DrawClock(Transform glyph)
        {
            Outline(glyph, 1f, 1f, 22f, 22f, 11f, 2f, GlyphBlue);
            Stroke(glyph, 12f, 12f, 12f, 6.5f, 2f, GlyphBlue);
            Stroke(glyph, 12f, 12f, 15.5f, 14f, 2f, GlyphBlue);
        }

        private static void DrawClipboard(Transform glyph)
        {
            Outline(glyph, 3f, 3.5f, 18f, 20.5f, 4f, 2f, GlyphBlue);
            QuestionnaireUi.Box(glyph, "Clip", GlyphBlue, 2f, 8f, 1f, 8f, 4.5f);
            Stroke(glyph, 8f, 13f, 10.8f, 15.8f, 2f, GlyphBlue);
            Stroke(glyph, 10.8f, 15.8f, 16f, 10.5f, 2f, GlyphBlue);
            Stroke(glyph, 8.5f, 19.5f, 15.5f, 19.5f, 2f, GlyphBlue);
        }
    }
}
