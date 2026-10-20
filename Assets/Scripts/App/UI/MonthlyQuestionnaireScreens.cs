using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    /// <summary>
    /// Monthly assessment, two pages: 1) JOA-style self assessment (symptoms,
    /// daily activities, urination) and 2) SF-MPQ pain descriptors. The user
    /// can step back from page 2 to page 1 without losing answers; call
    /// <see cref="Begin"/> to start a fresh form.
    /// </summary>
    internal sealed class MonthlyQuestionnaireScreens
    {
        private sealed class RadioGroup
        {
            public Button[] Rows;
            public GameObject[] Checks;
            public int Selected = -1;
        }

        private const int TotalSteps = 2;
        private const float FooterHeight = 100f;

        private static readonly Color RadioSelected = new Color(0.357f, 0.612f, 0.925f);
        private static readonly Color SegmentSelected = new Color(0.267f, 0.498f, 0.831f);

        private static readonly string[] BackPainOptions =
            { "不疼", "偶尔轻微疼", "经常轻微疼，或偶尔很疼", "经常或一直很疼" };
        private static readonly string[] LegPainOptions =
            { "没有", "偶尔轻微疼或麻", "经常轻微疼或麻，或偶尔很严重", "经常或一直很严重" };
        private static readonly string[] WalkingOptions =
            { "正常", "会疼或麻，但能走 500 米以上", "因腿疼、麻或没力，走不到 500 米", "因腿疼、麻或没力，走不到 100 米" };
        private static readonly string[] UrinationOptions =
            { "正常", "有点排尿困难", "排尿明显困难（漏尿或尿不出）" };
        private static readonly string[] ActivityNames =
            { "平躺时翻身", "站立", "弯腰洗漱", "向前弯腰", "坐着约 1 小时", "提或拿重物", "走路" };
        private static readonly string[] LimitLabels = { "不受限", "有点受限", "很受限" };
        private static readonly string[] RatingLabels = { "无", "轻", "中", "重" };

        // name, hint
        private static readonly string[][] SensoryWords =
        {
            new[] { "跳痛", "一跳一跳地疼" }, new[] { "刺痛", "像针扎" }, new[] { "刀割痛", "像刀割" },
            new[] { "锐痛", "尖锐、剧烈" }, new[] { "痉挛牵扯痛", "抽筋、被扯着疼" }, new[] { "绞痛", "像被拧着疼" },
            new[] { "热灼痛", "火辣辣的" }, new[] { "持续固定痛", "一直在同一处疼" }, new[] { "胀痛", "发胀" },
            new[] { "触痛", "一碰就疼" }, new[] { "撕裂痛", "像被撕开" },
        };
        private static readonly string[][] AffectiveWords =
        {
            new[] { "软弱无力", "疼得没力气" }, new[] { "厌烦", "疼得心烦" },
            new[] { "害怕", "疼得让人担心" }, new[] { "受罪、惩罚感", "疼得像在受罪" },
        };

        // groups: 0 back pain, 1 leg pain, 2 walking, 3 urination
        private readonly RadioGroup[] groups = new RadioGroup[4];
        private readonly int[] activities = new int[ActivityNames.Length];
        private readonly Button[][] activityButtons = new Button[ActivityNames.Length][];
        private readonly int[] sensory = new int[SensoryWords.Length];
        private readonly int[] affective = new int[AffectiveWords.Length];
        private readonly Button[][] sensoryButtons = new Button[SensoryWords.Length][];
        private readonly Button[][] affectiveButtons = new Button[AffectiveWords.Length][];

        private RectTransform joaContent;
        private RectTransform sfmpqContent;
        private Text joaMessage;
        private UnityAction onJoaNext;

        // ---- page 1: JOA self assessment -------------------------------------

        public GameObject BuildJoa(Transform canvasParent, UnityAction onBack, UnityAction onNext)
        {
            onJoaNext = onNext;
            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, "MonthlyJoaPanel",
                () => { if (joaContent != null) joaContent.anchoredPosition = Vector2.zero; }, out GameObject panel);
            joaContent = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, FooterHeight, 2114f);
            BuildStepHeader(joaContent, 1, "最近一个月，腰怎么样？", "按最近一个月的一般情况选");

            RectTransform symptoms = QuestionnaireUi.Card(joaContent, 20f, 159f, 350f, 891f);
            QuestionnaireUi.Label(symptoms, "症状", 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 21f, 310f, 22f);
            groups[0] = BuildRadioGroup(symptoms, "最近腰疼的情况？", 76f, BackPainOptions);
            groups[1] = BuildRadioGroup(symptoms, "最近腿疼或腿麻的情况？", 352f, LegPainOptions);
            groups[2] = BuildRadioGroup(symptoms, "走路的情况？", 628f, WalkingOptions);

            RectTransform daily = QuestionnaireUi.Card(joaContent, 20f, 1072f, 350f, 710f);
            QuestionnaireUi.Label(daily, "日常活动", 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 20f, 310f, 22f);
            QuestionnaireUi.Label(daily, "因为腰的问题，做下面这些事受限吗？", 12.5f, QuestionnaireUi.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 48f, 310f, 18f);
            for (int i = 0; i < ActivityNames.Length; i++)
            {
                int activity = i;
                QuestionnaireUi.Label(daily, ActivityNames[i], 14, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                    FontStyle.Bold, 20f, 82f + 89f * i, 310f, 22f);
                activityButtons[i] = new Button[LimitLabels.Length];
                for (int c = 0; c < LimitLabels.Length; c++)
                {
                    int choice = c;
                    Button button = QuestionnaireUi.MakeButton(daily, "Limit" + i + "_" + c,
                        QuestionnaireUi.OptionBackground, 12f, 20f + c * 105f, 111.5f + 89f * i, 100f, 44f,
                        () => { activities[activity] = choice; RefreshActivities(); ClearJoaMessage(); });
                    QuestionnaireUi.Label(button.transform, LimitLabels[c], 15, QuestionnaireUi.Ink,
                        TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, 100f, 44f);
                    activityButtons[i][c] = button;
                }
            }

            RectTransform urination = QuestionnaireUi.Card(joaContent, 20f, 1802f, 350f, 288f);
            QuestionnaireUi.Label(urination, "排尿情况", 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 21f, 310f, 22f);
            groups[3] = BuildRadioGroup(urination, "最近小便正常吗？", 72.5f, UrinationOptions);

            // Nothing is pre-selected on the self assessment.
            for (int i = 0; i < activities.Length; i++) activities[i] = -1;
            RefreshActivities();

            RectTransform footer = QuestionnaireUi.Footer(root, FooterHeight);
            joaMessage = QuestionnaireUi.Label(footer, "", 12.5f, QuestionnaireUi.DangerText, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, 4f, 350f, 16f);
            QuestionnaireUi.PillButton(footer, "下一题", 20f, 24f, 350f, 52f, true, HandleJoaNext);
            QuestionnaireUi.Header(root, "腰椎康复系统", onBack);
            return panel;
        }

        // ---- page 2: SF-MPQ ---------------------------------------------------

        public GameObject BuildSfMpq(Transform canvasParent, UnityAction onBack, UnityAction onFinish)
        {
            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, "MonthlySfmpqPanel",
                () => { if (sfmpqContent != null) sfmpqContent.anchoredPosition = Vector2.zero; }, out GameObject panel);
            sfmpqContent = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, FooterHeight, 1270f);
            BuildStepHeader(sfmpqContent, 2, "最近的腰腿疼，像哪种感觉？", "每个词都选一下，没有这种感觉就选「无」");

            BuildRatingCard(sfmpqContent, 159f, 748f, "疼痛的感觉", SensoryWords, sensory, sensoryButtons);
            BuildRatingCard(sfmpqContent, 928f, 314f, "疼痛带来的感受", AffectiveWords, affective, affectiveButtons);

            RectTransform footer = QuestionnaireUi.Footer(root, FooterHeight);
            QuestionnaireUi.PillButton(footer, "完成", 20f, 24f, 350f, 52f, true, onFinish);
            QuestionnaireUi.Header(root, "腰椎康复系统", onBack);
            return panel;
        }

        // ---- state ------------------------------------------------------------

        /// <summary>Clears every answer so the form starts empty (SF-MPQ defaults to 无).</summary>
        public void Begin()
        {
            foreach (RadioGroup group in groups)
            {
                if (group == null) continue;
                group.Selected = -1;
                RefreshRadio(group);
            }
            for (int i = 0; i < activities.Length; i++) activities[i] = -1;
            for (int i = 0; i < sensory.Length; i++) sensory[i] = 0;
            for (int i = 0; i < affective.Length; i++) affective[i] = 0;
            RefreshActivities();
            RefreshRatings(sensoryButtons, sensory);
            RefreshRatings(affectiveButtons, affective);
            ClearJoaMessage();
        }

        /// <summary>Snapshot of the current answers; the caller fills in id and timestamp.</summary>
        public MonthlyQuestionnaireRecord CollectRecord()
        {
            return new MonthlyQuestionnaireRecord
            {
                backPain = groups[0].Selected,
                legPain = groups[1].Selected,
                walking = groups[2].Selected,
                activityLimits = (int[])activities.Clone(),
                urination = groups[3].Selected,
                sfmpqSensory = (int[])sensory.Clone(),
                sfmpqAffective = (int[])affective.Clone(),
            };
        }

        // ---- building blocks --------------------------------------------------

        private static void BuildStepHeader(RectTransform content, int step, string title, string subtitle)
        {
            QuestionnaireUi.Label(content, "月度评估", 13, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 24f, 200f, 20f);
            QuestionnaireUi.Label(content, step + " / " + TotalSteps, 13, QuestionnaireUi.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold, 270f, 24f, 100f, 20f);
            QuestionnaireUi.Box(content, "ProgressTrack", new Color(0.88f, 0.90f, 0.93f), 3f, 20f, 54f, 350f, 6f);
            QuestionnaireUi.Box(content, "ProgressFill", QuestionnaireUi.Accent, 3f, 20f, 54f,
                350f * step / TotalSteps, 6f);
            QuestionnaireUi.Label(content, title, 22, QuestionnaireUi.Accent, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 81f, 350f, 32f);
            QuestionnaireUi.Label(content, subtitle, 13.5f, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 20f, 119f, 350f, 20f);
        }

        /// <summary>A question title plus one tappable row per option, selecting a single answer.</summary>
        private RadioGroup BuildRadioGroup(RectTransform card, string question, float titleCentre, string[] options)
        {
            QuestionnaireUi.Label(card, question, 14, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, titleCentre - 11f, 310f, 22f);
            RadioGroup group = new RadioGroup
            {
                Rows = new Button[options.Length],
                Checks = new GameObject[options.Length],
            };
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Button row = QuestionnaireUi.MakeButton(card, "Option" + i, QuestionnaireUi.OptionBackground, 14f,
                    20f, titleCentre + 23f + 58f * i, 310f, 46f,
                    () => { group.Selected = index; RefreshRadio(group); ClearJoaMessage(); });
                QuestionnaireUi.Label(row.transform, options[i], 14, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                    FontStyle.Normal, 16f, 0f, 245f, 46f);

                Image circle = QuestionnaireUi.Box(row.transform, "Radio", Color.white, 11f, 272f, 12f, 22f, 22f);
                Image ring = QuestionnaireUi.Ring(circle.transform, "RadioRing", new Color(0.74f, 0.78f, 0.84f), 11f, 1.5f);
                QuestionnaireUi.Place(ring.rectTransform, 0f, 0f, 22f, 22f);

                GameObject check = new GameObject("Check", typeof(RectTransform));
                check.transform.SetParent(circle.transform, false);
                QuestionnaireUi.Place((RectTransform)check.transform, 0f, 0f, 22f, 22f);
                Bar(check.transform, 6f, 11.5f, 5.2f, -45f, RadioSelected);
                Bar(check.transform, 9.5f, 15f, 9.5f, 45f, RadioSelected);
                group.Rows[i] = row;
                group.Checks[i] = check;
            }
            RefreshRadio(group);
            return group;
        }

        /// <summary>A thin bar anchored by its left end, used to draw the check mark.</summary>
        private static void Bar(Transform parent, float x, float y, float length, float angle, Color color)
        {
            Image bar = QuestionnaireUi.Box(parent, "Bar", color, 1f, x, y, length, 2f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.anchoredPosition = new Vector2(x, -y);
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static void BuildRatingCard(RectTransform content, float y, float height, string title,
            string[][] words, int[] values, Button[][] buttons)
        {
            const float firstColumn = 162f;
            const float columnPitch = 50f;
            const float buttonWidth = 43.5f;

            RectTransform card = QuestionnaireUi.Card(content, 20f, y, 350f, height);
            QuestionnaireUi.Label(card, title, 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 20f, 125f, 24f);
            for (int c = 0; c < RatingLabels.Length; c++)
            {
                QuestionnaireUi.Label(card, RatingLabels[c], 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                    FontStyle.Normal, firstColumn + c * columnPitch - 20f, 22f, 40f, 20f);
            }
            for (int i = 0; i < words.Length; i++)
            {
                int word = i;
                float centre = 84f + 62f * i;
                QuestionnaireUi.Label(card, words[i][0], 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                    FontStyle.Bold, 20f, centre - 19.5f, 135f, 20f);
                QuestionnaireUi.Label(card, words[i][1], 11.5f, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                    FontStyle.Normal, 20f, centre + 5f, 135f, 16f);
                buttons[i] = new Button[RatingLabels.Length];
                for (int c = 0; c < RatingLabels.Length; c++)
                {
                    int rating = c;
                    Button button = QuestionnaireUi.MakeButton(card, "Rating" + i + "_" + c,
                        QuestionnaireUi.OptionBackground, 12f, firstColumn + c * columnPitch - buttonWidth * 0.5f,
                        centre - 22f, buttonWidth, 44f, () =>
                        {
                            values[word] = rating;
                            RefreshRatings(buttons, values);
                        });
                    QuestionnaireUi.Label(button.transform, RatingLabels[c], 15, QuestionnaireUi.Ink,
                        TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, buttonWidth, 44f);
                    buttons[i][c] = button;
                }
            }
            RefreshRatings(buttons, values);
        }

        // ---- refresh ------------------------------------------------------------

        private static void RefreshRadio(RadioGroup group)
        {
            for (int i = 0; i < group.Rows.Length; i++)
            {
                bool selected = group.Selected == i;
                QuestionnaireUi.SetChoice(group.Rows[i], selected, RadioSelected, QuestionnaireUi.OptionBackground,
                    Color.white, QuestionnaireUi.Ink);
                group.Checks[i].SetActive(selected);
            }
        }

        private void RefreshActivities()
        {
            for (int i = 0; i < activityButtons.Length; i++)
            {
                if (activityButtons[i] == null) continue;
                for (int c = 0; c < activityButtons[i].Length; c++)
                {
                    QuestionnaireUi.SetChoice(activityButtons[i][c], activities[i] == c, SegmentSelected,
                        QuestionnaireUi.OptionBackground, Color.white, QuestionnaireUi.Ink);
                }
            }
        }

        private static void RefreshRatings(Button[][] buttons, int[] values)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                for (int c = 0; c < buttons[i].Length; c++)
                {
                    QuestionnaireUi.SetChoice(buttons[i][c], values[i] == c, SegmentSelected,
                        QuestionnaireUi.OptionBackground, Color.white, QuestionnaireUi.Ink);
                }
            }
        }

        private void ClearJoaMessage()
        {
            if (joaMessage != null) joaMessage.text = "";
        }

        private void HandleJoaNext()
        {
            int missing = 0;
            foreach (RadioGroup group in groups)
            {
                if (group.Selected < 0) missing++;
            }
            foreach (int limit in activities)
            {
                if (limit < 0) missing++;
            }
            if (missing > 0)
            {
                joaMessage.text = "还有 " + missing + " 项没回答，请全部选完再继续。";
                return;
            }
            joaMessage.text = "";
            if (onJoaNext != null) onJoaNext();
        }
    }
}
