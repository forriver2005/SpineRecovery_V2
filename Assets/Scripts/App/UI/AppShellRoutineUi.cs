using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    public sealed partial class AppShellUi
    {
        [Serializable] private sealed class RoutineList { public RoutineTask[] items; public string message; }
        [Serializable] private sealed class RoutineTask
        {
            public long taskId;
            public string name, completedAt, availability, problem;
            public RoutineQuestion[] questions;
        }
        [Serializable] private sealed class RoutineQuestion
        {
            public string id, type, prompt;
            public bool required;
            public int min, max, step, maxLength, minSelections, maxSelections;
            public int numberValue;
            public bool boolValue, answerSet;
            public string textValue;
            public string[] values;
            public RoutineOption[] options;
        }
        [Serializable] private sealed class RoutineOption { public string value, label; }
        private sealed class PendingRoutineAnswer
        {
            public RoutineQuestion Question;
            public NativeRequest Request;
        }

        private RectTransform routineBody;
        private RoutineList routineTasks;
        private int routineTaskIndex = -1;
        private int routineEpoch;
        private bool routineLoading, routineSaving, routineSubmitting;
        private Button routinePrimary;
        private Text routinePrimaryLabel;
        private Text routineProgressLabel;
        private Image routineProgressBar;
        private readonly Queue<PendingRoutineAnswer> routineSaves = new Queue<PendingRoutineAnswer>();
        private readonly Dictionary<RoutineQuestion, RoutineTask> routineSaveErrors = new Dictionary<RoutineQuestion, RoutineTask>();

        private GameObject CreateServerRoutinePanel(Transform parent)
        {
            var root = QuestionnaireUi.CreateScreen(parent, "RoutineQuestionnairePanel", null, out var panel);
            routineBody = QuestionnaireUi.ScrollArea(root, 56, 128, 650);
            var footer = QuestionnaireUi.Footer(root, 128);
            routinePrimary = QuestionnaireUi.PillButton(footer, "刷新问卷", 20, 12, 350, 52, true, RoutinePrimaryAction);
            routinePrimaryLabel = routinePrimary.GetComponentInChildren<Text>();
            routineMessage = MobileUi.Text(footer, "", 20, 72, 350, 48, 12, false, QuestionnaireUi.Muted, TextAnchor.UpperLeft);
            MobileUi.Header(root, "月度问卷", () =>
            {
                if (routineTaskIndex >= 0) { routineTaskIndex = -1; RenderRoutineQuestion(); }
                else bootstrap.Router.Navigate(AppRoute.Home);
            });
            RenderRoutineQuestion();
            return panel;
        }

        private void ResetRoutineSession()
        {
            routineEpoch++;
            routineSaves.Clear(); routineSaveErrors.Clear();
            routineSaving = routineLoading = routineSubmitting = false;
            routineTasks = null; routineTaskIndex = -1;
            if (routineMessage != null) routineMessage.text = "";
            if (routineBody != null) RenderRoutineQuestion();
        }

        private void RefreshServerRoutine()
        {
            if (bootstrap.State.Session == null || routineLoading || routineSubmitting) return;
            if (routineSaving || routineSaves.Count > 0 || routineSaveErrors.Count > 0)
            { routineMessage.text = "请先保存当前答案；保存失败时可点击重试。"; return; }
            routineLoading = true;
            int epoch = routineEpoch;
            string account = bootstrap.State.Session.accountId;
            routineMessage.text = "正在获取医生安排的问卷…";
            UpdateRoutinePrimary();
            NativeMobileService.Instance.Request("routineRefresh", new NativeRequest { accountId = account }, reply =>
            {
                if (epoch != routineEpoch || bootstrap.State.Session?.accountId != account) return;
                routineLoading = false;
                if (!reply.ok) { routineMessage.text = reply.error; RenderRoutineQuestion(); return; }
                var list = MobileUi.ReadJson<RoutineList>(reply.dataJson);
                if (list == null) { routineMessage.text = "问卷暂时无法读取，请重试。"; UpdateRoutinePrimary(); return; }
                routineTasks = list; routineTaskIndex = -1;
                routineMessage.text = list.message ?? "";
                RenderRoutineQuestion();
            });
        }

        private RoutineTask CurrentRoutineTask => routineTasks?.items != null && routineTaskIndex >= 0 && routineTaskIndex < routineTasks.items.Length
            ? routineTasks.items[routineTaskIndex] : null;

        private static bool RoutineEditable(RoutineTask task) => task != null && string.IsNullOrEmpty(task.completedAt) &&
            string.IsNullOrEmpty(task.problem) && task.availability == "available";

        private void RoutinePrimaryAction()
        {
            if (routineSaveErrors.Count > 0)
            {
                foreach (var pair in routineSaveErrors.ToArray()) SaveRoutineAnswer(pair.Value, pair.Key, false);
                return;
            }
            var current = CurrentRoutineTask;
            if (current == null) RefreshServerRoutine();
            else if (RoutineEditable(current)) SubmitServerRoutine(current.taskId);
            else { routineTaskIndex = -1; RenderRoutineQuestion(); }
        }

        private void UpdateRoutinePrimary()
        {
            if (routinePrimary == null) return;
            bool busy = routineLoading || routineSaving || routineSubmitting || routineSaves.Count > 0;
            routinePrimary.interactable = !busy;
            routinePrimaryLabel.text = routineLoading ? "正在加载…" : routineSubmitting ? "正在提交…" : busy ? "正在保存答案…" :
                routineSaveErrors.Count > 0 ? "重试保存答案" : CurrentRoutineTask == null ? "刷新问卷" :
                RoutineEditable(CurrentRoutineTask) ? "完成并提交" : "返回问卷列表";
        }

        private void RenderRoutineQuestion()
        {
            if (routineBody == null) return;
            MobileUi.Clear(routineBody);
            var task = CurrentRoutineTask;
            if (task == null) RenderRoutineList();
            else RenderRoutineForm(task);
            UpdateRoutinePrimary();
        }

        private void RenderRoutineList()
        {
            MobileUi.Text(routineBody, "每月了解恢复情况", 20, 20, 350, 34, 24, true, QuestionnaireUi.Accent);
            MobileUi.Text(routineBody, "按照医生的安排填写，答案会自动保存为草稿", 20, 65, 350, 44, 13, false, QuestionnaireUi.Muted);
            var tasks = routineTasks?.items ?? Array.Empty<RoutineTask>();
            float y = 128;
            if (tasks.Length == 0)
            {
                var card = QuestionnaireUi.Card(routineBody, 20, y, 350, 216);
                MobileUi.Asset(card, "HistoryClock", 147, 24, 56, 56);
                MobileUi.Text(card, routineTasks == null ? "查看医生安排的问卷" : "暂无待填写问卷", 20, 100, 310, 30, 18, true, null, TextAnchor.MiddleCenter);
                MobileUi.Text(card, "医生安排后，点击下方刷新即可查看。", 20, 145, 310, 48, 14, false, QuestionnaireUi.Muted, TextAnchor.MiddleCenter);
                y += 240;
            }
            for (int i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (task == null) continue;
                int index = i;
                var card = QuestionnaireUi.Card(routineBody, 20, y, 350, 156);
                bool done = !string.IsNullOrEmpty(task.completedAt), editable = RoutineEditable(task);
                MobileUi.Text(card, task.name, 20, 16, 310, 48, 17, true);
                MobileUi.Text(card, done ? "已完成 · " + MobileUi.Date(task.completedAt) : editable ? "待填写 · " + (task.questions?.Length ?? 0) + " 题" : "暂不可填写 · 刷新后查看状态", 20, 68, 310, 24, 13, false, QuestionnaireUi.Muted);
                QuestionnaireUi.PillButton(card, done ? "查看已填答案" : editable ? "开始 / 继续填写" : "查看详情", 20, 104, 310, 40, false,
                    () => { routineTaskIndex = index; routineMessage.text = ""; RenderRoutineQuestion(); });
                y += 172;
            }
            MobileUi.Height(routineBody, y + 20);
        }

        private void RenderRoutineForm(RoutineTask task)
        {
            bool editable = RoutineEditable(task);
            bool completed = !string.IsNullOrEmpty(task.completedAt);
            var questions = (task.questions ?? Array.Empty<RoutineQuestion>()).Where(q => q != null).ToArray();
            int count = questions.Count(HasRoutineAnswer);
            MobileUi.Text(routineBody, "月度评估", 20, 20, 250, 24, 14, true, QuestionnaireUi.Accent);
            routineProgressLabel = MobileUi.Text(routineBody, completed ? "已完成" : count + " / " + questions.Length, 280, 20, 90, 24, 14, true, QuestionnaireUi.Accent, TextAnchor.MiddleRight);
            QuestionnaireUi.Box(routineBody, "ProgressTrack", QuestionnaireUi.Divider, 4, 20, 54, 350, 8);
            routineProgressBar = QuestionnaireUi.Box(routineBody, "Progress", QuestionnaireUi.Accent, 4, 20, 54, completed ? 350 : 350f * count / Math.Max(1, questions.Length), 8);
            routineProgressBar.gameObject.SetActive(completed || count > 0);
            MobileUi.Text(routineBody, task.name, 20, 80, 350, 62, 22, true, QuestionnaireUi.Accent);
            MobileUi.Text(routineBody, editable ? "请按实际情况选择，标有 * 的项目为必填" : !string.IsNullOrEmpty(task.completedAt) ? "已完成 · " + MobileUi.Date(task.completedAt) : task.problem ?? "暂不可填写，请返回列表刷新。",
                20, 148, 350, 48, 13, false, QuestionnaireUi.Muted);
            float y = 212;
            if (completed)
            {
                var success = QuestionnaireUi.Card(routineBody, 20, y, 350, 146);
                MobileUi.Text(success, "已完成本次评估", 20, 20, 310, 32, 19, true, QuestionnaireUi.Accent);
                MobileUi.Text(success, "你的回答已提交。感谢认真记录恢复情况，可返回列表查看其他问卷。", 20, 65, 310, 60, 14, false, QuestionnaireUi.Muted);
                y += 162;
            }
            for (int questionIndex = 0; questionIndex < questions.Length; questionIndex++)
            {
                var q = questions[questionIndex];
                if (CompactRoutineOptions(q) && (q.prompt?.Length ?? 0) <= 10)
                {
                    int end = questionIndex + 1;
                    while (end < questions.Length && CompactRoutineOptions(questions[end]) && (questions[end].prompt?.Length ?? 0) <= 10) end++;
                    var group = QuestionnaireUi.Card(routineBody, 20, y, 350, 60 + (end - questionIndex) * 64);
                    MobileUi.Text(group, "选择符合你的情况", 20, 14, 310, 28, 17, true);
                    for (int index = questionIndex; index < end; index++)
                    {
                        var row = new GameObject("SeverityRow", typeof(RectTransform)).GetComponent<RectTransform>();
                        row.SetParent(group, false);
                        QuestionnaireUi.Place(row, 0, 54 + (index - questionIndex) * 64, 350, 64);
                        var item = questions[index];
                        MobileUi.Text(row, item.prompt + (item.required ? " *" : ""), 20, 0, 98, 50, 14, true);
                        RenderRoutineAnswer(task, item, row, 0, editable, 126, 204);
                    }
                    y += 76 + (end - questionIndex) * 64;
                    questionIndex = end - 1;
                    continue;
                }
                float promptHeight = Math.Max(44, 24 * (float)Math.Ceiling(((q.prompt?.Length ?? 0) + 5) / 18f));
                bool options = q.type == "single_choice" || q.type == "multi_choice";
                float answerHeight = CompactRoutineOptions(q) ? 56 : options ? (q.options ?? Array.Empty<RoutineOption>()).Sum(o => RoutineOptionHeight(o) + 8) : q.type == "short_text" ? 132 : 120;
                var card = QuestionnaireUi.Card(routineBody, 20, y, 350, promptHeight + answerHeight + 42);
                MobileUi.Text(card, q.prompt + (q.required ? " *" : ""), 20, 16, 310, promptHeight, 16, true);
                RenderRoutineAnswer(task, q, card, promptHeight + 28, editable);
                y += promptHeight + answerHeight + 58;
            }
            if (questions.Length == 0 && !completed) MobileUi.Text(routineBody, "这份问卷暂无题目，请刷新后再试。", 20, y, 350, 60, 14, false, QuestionnaireUi.Muted);
            MobileUi.Height(routineBody, y + 80);
        }

        private static float RoutineOptionHeight(RoutineOption option) => Math.Max(50, 22 * (float)Math.Ceiling(((option?.label?.Length ?? 0) + 3) / 17f) + 16);

        private static bool CompactRoutineOptions(RoutineQuestion q) => q.type == "single_choice" && q.options != null &&
            q.options.Length >= 3 && q.options.Length <= 4 && q.options.All(o => o != null && (o.label?.Length ?? 0) <= 4);

        private static bool HasRoutineAnswer(RoutineQuestion q)
        {
            if (!q.answerSet) return false;
            if (q.type == "short_text") return !string.IsNullOrWhiteSpace(q.textValue);
            if (q.type == "single_choice") return (q.options ?? Array.Empty<RoutineOption>()).Any(o => o != null && o.value == q.textValue);
            if (q.type == "multi_choice") return (q.values?.Length ?? 0) >= q.minSelections;
            return q.type != "scale" || q.numberValue >= q.min && q.numberValue <= q.max;
        }

        private void RenderRoutineAnswer(RoutineTask task, RoutineQuestion q, Transform card, float y, bool editable,
            float answerX = 20, float answerWidth = 310)
        {
            if (q.type == "single_choice" || q.type == "multi_choice")
            {
                var selected = new List<string>(q.values ?? Array.Empty<string>());
                bool compact = CompactRoutineOptions(q);
                int optionIndex = 0;
                foreach (var option in q.options ?? Array.Empty<RoutineOption>())
                {
                    if (option == null) continue;
                    bool chosen = q.type == "single_choice" ? q.answerSet && q.textValue == option.value : selected.Contains(option.value);
                    float height = RoutineOptionHeight(option);
                    float width = compact ? (answerWidth - (q.options.Length - 1) * 8) / q.options.Length : 310;
                    var button = MobileUi.Choice(card, (chosen && !compact ? "✓  " : "") + option.label,
                        compact ? answerX + optionIndex * (width + 8) : 20, y, width, () =>
                    {
                        if (q.type == "single_choice") q.textValue = option.value;
                        else
                        {
                            if (selected.Contains(option.value)) selected.Remove(option.value);
                            else if (q.maxSelections <= 0 || selected.Count < q.maxSelections) selected.Add(option.value);
                            else { routineMessage.text = "最多可选择 " + q.maxSelections + " 项。"; return; }
                            q.values = selected.ToArray();
                        }
                        SaveRoutineAnswer(task, q);
                    }, chosen, height);
                    button.interactable = editable && !routineSubmitting;
                    if (compact)
                    {
                        var label = button.GetComponentInChildren<Text>();
                        label.alignment = TextAnchor.MiddleCenter;
                        QuestionnaireUi.Place(label.rectTransform, 4, 0, (width - 8) * 3, height * 3);
                        label.fontSize = 42;
                    }
                    else y += height + 8;
                    optionIndex++;
                }
            }
            else if (q.type == "scale")
            {
                int value = q.answerSet ? Mathf.Clamp(q.numberValue, q.min, q.max) : q.min;
                MobileUi.Text(card, q.answerSet ? value.ToString() : "请选择数值", 76, y, 198, 44, 22, true, QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
                var minus = MobileUi.Choice(card, "−", 20, y, 48, () => { q.numberValue = Mathf.Max(q.min, value - Math.Max(1, q.step)); SaveRoutineAnswer(task, q); });
                var plus = MobileUi.Choice(card, "+", 282, y, 48, () => { q.numberValue = Mathf.Min(q.max, value + Math.Max(1, q.step)); SaveRoutineAnswer(task, q); });
                minus.interactable = plus.interactable = editable && !routineSubmitting;
                var confirm = MobileUi.Choice(card, "确认数值  " + value + "（" + q.min + "–" + q.max + "）", 20, y + 62, 310,
                    () => { q.numberValue = value; SaveRoutineAnswer(task, q); });
                confirm.interactable = editable && !routineSubmitting;
            }
            else if (q.type == "boolean")
            {
                var yes = MobileUi.Choice(card, "是", 20, y, 150, () => { q.boolValue = true; SaveRoutineAnswer(task, q); }, q.answerSet && q.boolValue);
                var no = MobileUi.Choice(card, "否", 180, y, 150, () => { q.boolValue = false; SaveRoutineAnswer(task, q); }, q.answerSet && !q.boolValue);
                yes.interactable = no.interactable = editable && !routineSubmitting;
            }
            else if (q.type == "short_text")
            {
                var field = QuestionnaireUi.Box(card, "AnswerInput", QuestionnaireUi.OptionBackground, 12, 20, y, 310, 108);
                field.raycastTarget = true;
                var input = field.gameObject.AddComponent<InputField>();
                input.targetGraphic = field;
                var text = MobileUi.Text(field.transform, "", 12, 10, 286, 88, 15, false, null, TextAnchor.UpperLeft);
                text.verticalOverflow = VerticalWrapMode.Truncate;
                var placeholder = MobileUi.Text(field.transform, "请输入答案", 12, 10, 286, 88, 15, false, QuestionnaireUi.Muted, TextAnchor.UpperLeft);
                input.textComponent = text; input.placeholder = placeholder;
                input.lineType = InputField.LineType.MultiLineSubmit;
                input.characterLimit = Math.Max(0, q.maxLength);
                input.text = q.textValue ?? "";
                input.interactable = editable && !routineSubmitting;
                input.onEndEdit.AddListener(value => { if (q.textValue == value && q.answerSet) return; q.textValue = value.Replace("\r", " ").Replace("\n", " "); SaveRoutineAnswer(task, q, false); });
            }
            else MobileUi.Text(card, "当前版本暂不支持此题，请联系医生调整问卷。", 20, y, 310, 80, 14, false, QuestionnaireUi.Muted);
        }

        private void SaveRoutineAnswer(RoutineTask task, RoutineQuestion q, bool render = true)
        {
            if (bootstrap.State.Session == null || !RoutineEditable(task) || routineSubmitting) return;
            q.answerSet = true;
            // Text fields save on end-edit without rebuilding the form, preserving
            // focus when the patient moves directly into the next text field.
            if (CurrentRoutineTask == task && routineProgressLabel != null)
            {
                var questions = (task.questions ?? Array.Empty<RoutineQuestion>()).Where(item => item != null).ToArray();
                int answered = questions.Count(HasRoutineAnswer);
                routineProgressLabel.text = answered + " / " + questions.Length;
                if (routineProgressBar != null)
                {
                    routineProgressBar.rectTransform.sizeDelta = new Vector2(350f * answered / Math.Max(1, questions.Length), 8);
                    routineProgressBar.gameObject.SetActive(answered > 0);
                }
            }
            routineSaves.Enqueue(new PendingRoutineAnswer { Question = q, Request = new NativeRequest
            {
                accountId = bootstrap.State.Session.accountId, taskId = task.taskId, questionId = q.id, answerType = q.type,
                numberValue = q.numberValue, boolValue = q.boolValue, textValue = q.textValue, values = (q.values ?? Array.Empty<string>()).ToArray()
            }});
            if (render)
            {
                float offset = routineBody.anchoredPosition.y;
                RenderRoutineQuestion(); routineBody.anchoredPosition = new Vector2(0, offset);
            }
            DrainRoutineSaves();
        }

        // Serialize writes so a slower earlier request cannot overwrite the latest choice.
        private void DrainRoutineSaves()
        {
            UpdateRoutinePrimary();
            if (routineSaving || routineSaves.Count == 0) return;
            var pending = routineSaves.Dequeue();
            routineSaving = true;
            int epoch = routineEpoch;
            routineMessage.text = "正在保存草稿…";
            UpdateRoutinePrimary();
            NativeMobileService.Instance.Request("routineAnswer", pending.Request, reply =>
            {
                if (epoch != routineEpoch || bootstrap.State.Session?.accountId != pending.Request.accountId) return;
                routineSaving = false;
                if (reply.ok) routineSaveErrors.Remove(pending.Question);
                else
                {
                    var owner = (routineTasks?.items ?? Array.Empty<RoutineTask>()).FirstOrDefault(t => t != null && t.taskId == pending.Request.taskId);
                    if (owner != null) routineSaveErrors[pending.Question] = owner;
                }
                routineMessage.text = routineSaveErrors.Count > 0 ? "部分答案保存失败，请点击重试。" : "草稿已保存，可稍后继续填写。";
                DrainRoutineSaves();
            });
        }

        private void SubmitServerRoutine(long taskId)
        {
            var task = CurrentRoutineTask;
            if (!RoutineEditable(task) || routineLoading || routineSaving || routineSubmitting || routineSaves.Count > 0 || routineSaveErrors.Count > 0) return;
            if (task.questions == null || task.questions.Length == 0) { routineMessage.text = "问卷暂无题目，请先刷新。"; return; }
            var missing = task.questions.FirstOrDefault(q => q != null && q.required && !HasRoutineAnswer(q));
            if (missing != null) { routineMessage.text = "请先完成必填题：" + missing.prompt; return; }
            routineSubmitting = true;
            int epoch = routineEpoch;
            string account = bootstrap.State.Session.accountId;
            routineMessage.text = "正在提交问卷…";
            float offset = routineBody.anchoredPosition.y;
            RenderRoutineQuestion(); routineBody.anchoredPosition = new Vector2(0, offset);
            NativeMobileService.Instance.Request("routineSubmit", new NativeRequest { accountId = account, taskId = taskId }, reply =>
            {
                if (epoch != routineEpoch || bootstrap.State.Session?.accountId != account) return;
                routineSubmitting = false;
                if (!reply.ok) { routineMessage.text = reply.error; RenderRoutineQuestion(); routineBody.anchoredPosition = new Vector2(0, offset); return; }
                var list = MobileUi.ReadJson<RoutineList>(reply.dataJson);
                if (list == null) { routineMessage.text = "提交结果暂时无法读取，请刷新确认。"; UpdateRoutinePrimary(); return; }
                routineTasks = list;
                routineTaskIndex = Array.FindIndex(list.items ?? Array.Empty<RoutineTask>(), t => t != null && t.taskId == taskId);
                routineMessage.text = list.message ?? "问卷已提交。";
                RenderRoutineQuestion();
            });
        }
    }
}
