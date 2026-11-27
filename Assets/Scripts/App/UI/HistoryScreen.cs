using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    internal sealed class HistoryScreen
    {
        [Serializable] private sealed class Result { public int overallScore; public int durationSeconds; }
        private RectTransform content;
        private Func<UserSession> getSession;
        private UnityAction monthly;
        private Action<string> replay;
        private Func<string> importedReplay;
        private Button replayButton;
        private Text replayCaption;
        private string latestReplay;
        private string expandedSession;
        private bool allPain;
        private string account;
        public Text Message { get; private set; }

        public GameObject Build(Transform parent, Func<UserSession> session, UnityAction back,
            UnityAction onMonthly, Action<string> onReplay, Func<string> getImportedReplay = null)
        {
            getSession = session; monthly = onMonthly; replay = onReplay;
            importedReplay = getImportedReplay;
            var root = QuestionnaireUi.CreateScreen(parent, "HistoryPanel", Refresh, out var panel);
            content = QuestionnaireUi.ScrollArea(root, 56, 112, 700);
            var footer = QuestionnaireUi.Footer(root, 112);
            replayButton = QuestionnaireUi.PillButton(footer, "查看最近完成的回放", 20, 12, 350, 52, true,
                () => replay(latestReplay));
            replayCaption = replayButton.GetComponentInChildren<Text>();
            Message = MobileUi.Text(footer, "", 20, 70, 350, 32, 12, false, QuestionnaireUi.Muted, TextAnchor.MiddleCenter);
            MobileUi.Header(root, "训练历史", back);
            Refresh();
            return panel;
        }

        public void Refresh()
        {
            if (content == null) return;
            var session = getSession();
            if (session?.accountId != account) { expandedSession = null; allPain = false; account = session?.accountId; }
            MobileUi.Clear(content);
            var pain = (session?.painHistory ?? Array.Empty<PainHistoryRecord>()).Where(r => r != null)
                .OrderByDescending(r => Timestamp(r.recordedAt)).ToArray();
            var training = (session?.trainingHistory ?? Array.Empty<TrainingHistoryRecord>()).Where(r => r != null)
                .OrderByDescending(r => Timestamp(r.completedAt)).ToArray();
            latestReplay = training.FirstOrDefault(r => r.completed && !string.IsNullOrEmpty(r.replaySessionId))?.replaySessionId;
            if (string.IsNullOrEmpty(latestReplay)) latestReplay = importedReplay?.Invoke();
            bool hasReplay = !string.IsNullOrEmpty(latestReplay);
            replayButton.interactable = hasReplay;
            ((Image)replayButton.targetGraphic).color = hasReplay ? QuestionnaireUi.Primary : QuestionnaireUi.Divider;
            replayCaption.color = hasReplay ? Color.white : QuestionnaireUi.Muted;
            Message.text = hasReplay ? "可查看本账号最近完成的训练" : "完成训练并保存回放后可查看";
            string completedAt = session?.routineQuestionnaireCompletedAt;
            // The session exposes the latest questionnaire date, not an all-time count.
            Stat(20, pain.Length.ToString(), "疼痛记录");
            Stat(140, training.Length.ToString(), "训练记录");
            Stat(260, string.IsNullOrEmpty(completedAt) ? "—" : "已填", "月度问卷");
            float y = 114;
            if (training.Length > 0) y = Training(training, y);
            y = Pain(pain, y);
            if (training.Length == 0) y = Training(training, y);
            var card = QuestionnaireUi.Card(content, 20, y, 350, 96);
            MobileUi.Text(card, "月度问卷", 20, 16, 210, 24, 17, true);
            MobileUi.Text(card, string.IsNullOrEmpty(completedAt) ? "按医生安排填写" : "最近完成：" + MobileUi.Date(completedAt),
                20, 46, 225, 34, 12, false, QuestionnaireUi.Muted);
            var monthlyButton = QuestionnaireUi.PillButton(card, "查看", 260, 26, 74, 40, false, monthly);
            monthlyButton.interactable = session != null && !session.IsAdministrator;
            MobileUi.Height(content, y + 116);
        }

        private static DateTimeOffset Timestamp(string value) => DateTimeOffset.TryParse(value, out var date) ? date : DateTimeOffset.MinValue;

        private void Stat(float x, string value, string label)
        {
            var card = QuestionnaireUi.Card(content, x, 20, 110, 78);
            MobileUi.Text(card, value, 12, 10, 86, 34, 26, true, QuestionnaireUi.Accent);
            MobileUi.Text(card, label, 12, 47, 86, 20, 12, false, QuestionnaireUi.Muted);
        }

        private float Training(TrainingHistoryRecord[] records, float y)
        {
            float height = 56 + (records.Length == 0 ? 92 : records.Sum(r => r.sessionId == expandedSession ? 262 + FeedbackHeight(r) : 146));
            var card = QuestionnaireUi.Card(content, 20, y, 350, height);
            MobileUi.Text(card, "训练记录", 20, 16, 200, 26, 17, true);
            MobileUi.Text(card, "共 " + records.Length + " 次", 230, 16, 100, 26, 13, false, QuestionnaireUi.Muted, TextAnchor.MiddleRight);
            if (records.Length == 0)
            {
                var empty = QuestionnaireUi.Box(card, "EmptyTraining", QuestionnaireUi.OptionBackground, 14, 20, 54, 310, 74);
                MobileUi.Asset(empty.transform, "HistoryClock", 17, 23, 28, 28);
                MobileUi.Text(empty.transform, "完成训练并提交结果后，这里会显示训练记录", 57, 10, 238, 54, 13, false, QuestionnaireUi.Muted);
            }
            float top = 54;
            foreach (var row in records)
            {
                bool expanded = row.sessionId == expandedSession;
                var tile = QuestionnaireUi.Box(card, "TrainingRecord", QuestionnaireUi.OptionBackground, 14, 20, top, 310, expanded ? 248 + FeedbackHeight(row) : 132);
                var hit = QuestionnaireUi.MakeButton(tile.transform, "RecordDetails", Color.clear, 12, 0, 0, 310, 132,
                    () => { expandedSession = expanded ? null : row.sessionId; float scroll = content.anchoredPosition.y; Refresh(); content.anchoredPosition = new Vector2(0, scroll); });
                MobileUi.Text(hit.transform, MobileUi.Date(row.completedAt), 14, 9, 220, 26, 15, true);
                MobileUi.Asset(hit.transform, "Chevron", 282, 16, 16, 16);
                string state = row.endReason == "LEGACY_IMPORT" ? "导入记录" : row.completed ? "已完成" : string.IsNullOrEmpty(row.endReason) ? "进行中" : "中途停止";
                MobileUi.Text(hit.transform, state + (row.feedbackPending ? " · 待反馈" : ""), 14, 36, 280, 22, 12, true,
                    row.completed ? new Color(.04f, .51f, .45f) : QuestionnaireUi.BadgeText);
                var plan = MobileUi.ReadJson<TrainingPlan>(row.planJson);
                var score = ReadResult(row.resultJson);
                bool hasScore = score != null && score.overallScore >= 0 && score.overallScore <= 100;
                string actionName = plan?.actions?.FirstOrDefault()?.actionName;
                Metric(hit.transform, 14, "训练动作", string.IsNullOrEmpty(actionName) ? ActionName(row.actionId) : actionName, 116);
                string duration = "未记录";
                if (score != null && score.durationSeconds > 0) duration = Math.Max(1, (int)Math.Ceiling(score.durationSeconds / 60f)) + " 分钟";
                else if (DateTimeOffset.TryParse(plan?.startedAt, out var started) && DateTimeOffset.TryParse(row.completedAt, out var ended) && ended >= started)
                    duration = Math.Max(1, (int)Math.Ceiling((ended - started).TotalMinutes)) + " 分钟";
                Metric(hit.transform, 138, "时长", duration, 76);
                Metric(hit.transform, 222, "动作得分", hasScore ? score.overallScore.ToString() : "—", 74);
                if (expanded)
                {
                    string actions = plan?.actions == null ? ActionName(row.actionId) : string.Join("、", plan.actions.Where(a => a != null).Select(a => string.IsNullOrEmpty(a.actionName) ? ActionName(a.actionId) : a.actionName));
                    MobileUi.Text(tile.transform, "本次动作：" + actions, 14, 140, 282, 48, 13);
                    string feedback = row.actionFeedbacks != null && row.actionFeedbacks.Length > 0 ?
                        string.Join("\n", row.actionFeedbacks.OrderBy(a => a.actionIndex).Select(a =>
                            (a.actionIndex + 1) + ". " + a.actionName + "\n" + a.difficulty + " · " + a.feeling)) :
                        "训练反馈：" + (string.IsNullOrWhiteSpace(row.feedback) ? "暂无反馈" : row.feedback);
                    MobileUi.Text(tile.transform, feedback, 14, 192, 282, FeedbackHeight(row), 13, false, QuestionnaireUi.Muted);
                    var view = QuestionnaireUi.PillButton(tile.transform, "查看本次回放", 14, 200 + FeedbackHeight(row), 282, 36, false, () => replay(row.replaySessionId));
                    view.interactable = row.completed && !string.IsNullOrEmpty(row.replaySessionId);
                }
                top += expanded ? 262 + FeedbackHeight(row) : 146;
            }
            return y + height + 16;
        }

        private static int FeedbackHeight(TrainingHistoryRecord row) => Math.Max(44, (row.actionFeedbacks?.Length ?? 0) * 64);

        private static void Metric(Transform tile, float x, string label, string value, float width)
        {
            var box = QuestionnaireUi.Box(tile, label, Color.white, 10, x, 68, width, 52);
            MobileUi.Text(box.transform, label, 8, 5, width - 16, 18, 11, false, QuestionnaireUi.Muted);
            MobileUi.Text(box.transform, value, 8, 25, width - 16, 22, 13, true);
        }

        private static Result ReadResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            // Overwrite preserves the sentinel for older payloads without a total score.
            var result = new Result { overallScore = -1, durationSeconds = -1 };
            try { JsonUtility.FromJsonOverwrite(json, result); return result; }
            catch (ArgumentException) { return null; }
        }

        internal static string ActionName(string id)
        {
            switch (id)
            {
                case "birddog": return "鸟狗"; case "deadbug": return "死虫";
                case "hipthrust": return "臀桥"; case "sidebridge": return "侧桥";
                case "curlup": case "crunch": return "卷腹"; case "catcamel": case "lyingcat": return "猫式";
                default: return string.IsNullOrEmpty(id) ? "康复训练" : id;
            }
        }

        private float Pain(PainHistoryRecord[] records, float y)
        {
            var shown = allPain ? records : records.Take(2).ToArray();
            float height = 56 + Math.Max(1, shown.Length) * 84;
            var card = QuestionnaireUi.Card(content, 20, y, 350, height);
            MobileUi.Text(card, "疼痛记录", 20, 16, 170, 26, 17, true);
            if (records.Length > 2)
            {
                var more = QuestionnaireUi.MakeButton(card, "AllPainRecords", Color.clear, 1, 205, 8, 125, 44,
                    () => { allPain = !allPain; Refresh(); });
                MobileUi.Text(more.transform, allPain ? "收起" : "查看全部 " + records.Length + " 条", 0, 0, 125, 44, 12, true, QuestionnaireUi.Accent, TextAnchor.MiddleRight);
            }
            if (records.Length == 0) MobileUi.Text(card, "还没有疼痛记录\n完成疼痛评估后可在这里查看", 20, 56, 310, 64, 13, false, QuestionnaireUi.Muted);
            for (int i = 0; i < shown.Length; i++)
            {
                var row = shown[i];
                var tile = QuestionnaireUi.Box(card, "PainRecord", QuestionnaireUi.OptionBackground, 14, 20, 54 + i * 84, 310, 72);
                var score = QuestionnaireUi.Box(tile.transform, "PainScore", Color.white, 12, 14, 12, 48, 48);
                MobileUi.Text(score.transform, row.painLevel.ToString(), 0, 0, 48, 30, 18, true, QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
                MobileUi.Text(score.transform, "/ 10", 0, 29, 48, 16, 10, false, QuestionnaireUi.Muted, TextAnchor.MiddleCenter);
                MobileUi.Text(tile.transform, MobileUi.Date(row.recordedAt), 74, 10, 222, 24, 14, true);
                MobileUi.Text(tile.transform, row.painLevel == 0 ? "没有疼痛" : string.Join(" · ", (row.painParts ?? Array.Empty<string>()).Select(PartName)), 74, 36, 222, 30, 12, false, QuestionnaireUi.Muted);
            }
            return y + height + 16;
        }

        private static string PartName(string key)
        {
            switch (key)
            {
                case "left_waist": return "左腰部"; case "right_waist": return "右腰部";
                case "left_upper_arm": return "左大臂"; case "right_upper_arm": return "右大臂";
                case "left_hip": return "左臀部"; case "right_hip": return "右臀部";
                case "left_leg": return "左腿"; case "right_leg": return "右腿";
                default: return key;
            }
        }
    }
}
