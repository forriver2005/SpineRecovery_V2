using System;
using System.Collections.Generic;
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
            public string name;
            public string completedAt;
            public string availability;
            public string problem;
            public RoutineQuestion[] questions;
        }
        [Serializable] private sealed class RoutineQuestion
        {
            public string id;
            public string type;
            public string prompt;
            public bool required;
            public int min, max, step, maxLength, minSelections, maxSelections;
            public int numberValue;
            public bool boolValue, answerSet;
            public string textValue;
            public string[] values;
            public RoutineOption[] options;
        }
        [Serializable] private sealed class RoutineOption { public string value, label; }

        private Transform routineBody;
        private RoutineList routineTasks;
        private int routineTaskIndex, routineQuestionIndex;

        private GameObject CreateServerRoutinePanel(Transform parent)
        {
            GameObject panel = CreatePanel("RoutineQuestionnairePanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "例行问卷", new Vector2(0, 750), 30);
            routineMessage = CreateLabel(panel.transform, "请刷新医生安排的问卷。", new Vector2(0, 650), 17);
            routineMessage.rectTransform.sizeDelta = new Vector2(850, 130);
            var body = new GameObject("Question", typeof(RectTransform));
            body.transform.SetParent(panel.transform, false);
            routineBody = body.transform;
            CreateButton(panel.transform, "刷新问卷", new Vector2(-220, -700), RefreshServerRoutine, 370);
            CreateButton(panel.transform, "返回首页", new Vector2(220, -700), () => bootstrap.Router.Navigate(AppRoute.Home), 370);
            return panel;
        }

        private void RefreshServerRoutine()
        {
            if (bootstrap.State.Session == null || nativeBusy) return;
            nativeBusy = true;
            string account = bootstrap.State.Session.accountId;
            routineMessage.text = "正在获取问卷，已保存的草稿会保留。";
            NativeMobileService.Instance.Request("routineRefresh", new NativeRequest { accountId = account }, reply =>
            {
                nativeBusy = false;
                if (bootstrap.State.Session?.accountId != account) return;
                if (!reply.ok) { routineMessage.text = reply.error; return; }
                routineTasks = JsonUtility.FromJson<RoutineList>(reply.dataJson);
                routineTaskIndex = routineQuestionIndex = 0;
                routineMessage.text = routineTasks.message;
                RenderRoutineQuestion();
            });
        }

        private void RenderRoutineQuestion()
        {
            foreach (Transform child in routineBody) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (routineTasks?.items == null || routineTasks.items.Length == 0)
            { CreateLabel(routineBody, "暂无待填写问卷。", new Vector2(0, 300), 24); return; }
            RoutineTask task = routineTasks.items[routineTaskIndex];
            CreateLabel(routineBody, task.name, new Vector2(0, 480), 26);
            bool editable = string.IsNullOrEmpty(task.completedAt) && string.IsNullOrEmpty(task.problem) && task.availability == "available";
            if (task.questions != null && task.questions.Length > 0)
            {
                routineQuestionIndex = Mathf.Clamp(routineQuestionIndex, 0, task.questions.Length - 1);
                RoutineQuestion q = task.questions[routineQuestionIndex];
                Text prompt = CreateLabel(routineBody, (routineQuestionIndex + 1) + "/" + task.questions.Length + "  " + q.prompt + (q.required ? "（必填）" : ""), new Vector2(0, 370), 23);
                prompt.rectTransform.sizeDelta = new Vector2(850, 160);
                if (editable) RenderRoutineAnswer(task, q);
                CreateButton(routineBody, "上一题", new Vector2(-220, -420), () => { routineQuestionIndex--; RenderRoutineQuestion(); }, 370);
                CreateButton(routineBody, "下一题", new Vector2(220, -420), () => { routineQuestionIndex++; RenderRoutineQuestion(); }, 370);
            }
            CreateButton(routineBody, "上一份问卷", new Vector2(-220, -510), () => { routineTaskIndex = (routineTaskIndex + routineTasks.items.Length - 1) % routineTasks.items.Length; routineQuestionIndex = 0; RenderRoutineQuestion(); }, 370);
            CreateButton(routineBody, "下一份问卷", new Vector2(220, -510), () => { routineTaskIndex = (routineTaskIndex + 1) % routineTasks.items.Length; routineQuestionIndex = 0; RenderRoutineQuestion(); }, 370);
            if (editable) CreateButton(routineBody, "提交这份问卷", new Vector2(0, -600), () => SubmitServerRoutine(task.taskId));
            else CreateLabel(routineBody, !string.IsNullOrEmpty(task.completedAt) ? "已完成：" + task.completedAt : task.problem + "  草稿已保留，请在线刷新。", new Vector2(0, 180), 20);
        }

        private void RenderRoutineAnswer(RoutineTask task, RoutineQuestion q)
        {
            if (q.type == "single_choice" || q.type == "multi_choice")
            {
                var selected = new List<string>(q.values ?? Array.Empty<string>());
                for (int i = 0; i < (q.options?.Length ?? 0); i++)
                {
                    RoutineOption option = q.options[i];
                    bool chosen = q.type == "single_choice" ? q.textValue == option.value : selected.Contains(option.value);
                    Button button = CreateButton(routineBody, (chosen ? "✓ " : "") + option.label,
                        new Vector2(i % 2 == 0 ? -220 : 220, 245 - i / 2 * 68), () =>
                        {
                            if (q.type == "single_choice") q.textValue = option.value;
                            else
                            {
                                if (selected.Contains(option.value)) selected.Remove(option.value);
                                else if (selected.Count < q.maxSelections) selected.Add(option.value);
                                q.values = selected.ToArray();
                            }
                            SaveRoutineAnswer(task, q);
                        }, 400);
                    button.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 60);
                }
            }
            else if (q.type == "scale")
            {
                CreateLabel(routineBody, q.answerSet ? q.numberValue.ToString() : "请选择数值", new Vector2(0, 130), 28);
                CreateButton(routineBody, "−", new Vector2(-180, 40), () => { q.numberValue = Mathf.Max(q.min, q.numberValue - q.step); SaveRoutineAnswer(task, q); }, 260);
                CreateButton(routineBody, "+", new Vector2(180, 40), () => { q.numberValue = Mathf.Min(q.max, q.numberValue + q.step); SaveRoutineAnswer(task, q); }, 260);
                CreateButton(routineBody, "确认当前数值", new Vector2(0, -60), () => SaveRoutineAnswer(task, q));
            }
            else if (q.type == "boolean")
            {
                CreateButton(routineBody, (q.answerSet && q.boolValue ? "✓ " : "") + "是", new Vector2(-200, 180), () => { q.boolValue = true; SaveRoutineAnswer(task, q); }, 350);
                CreateButton(routineBody, (q.answerSet && !q.boolValue ? "✓ " : "") + "否", new Vector2(200, 180), () => { q.boolValue = false; SaveRoutineAnswer(task, q); }, 350);
            }
            else if (q.type == "short_text")
            {
                InputField input = CreateInput(routineBody, "请输入答案", new Vector2(0, 180), false);
                input.characterLimit = q.maxLength;
                input.text = q.textValue;
                input.onEndEdit.AddListener(value => { q.textValue = value; SaveRoutineAnswer(task, q); });
            }
        }

        private void SaveRoutineAnswer(RoutineTask task, RoutineQuestion q)
        {
            string account = bootstrap.State.Session.accountId;
            q.answerSet = true;
            NativeMobileService.Instance.Request("routineAnswer", new NativeRequest
            {
                accountId = account, taskId = task.taskId, questionId = q.id, answerType = q.type,
                numberValue = q.numberValue, boolValue = q.boolValue, textValue = q.textValue, values = q.values ?? Array.Empty<string>()
            }, reply => { if (!reply.ok && bootstrap.State.Session?.accountId == account) routineMessage.text = reply.error; });
            if (q.type != "short_text") RenderRoutineQuestion();
        }

        private void SubmitServerRoutine(long taskId)
        {
            if (nativeBusy) return;
            nativeBusy = true;
            string account = bootstrap.State.Session.accountId;
            NativeMobileService.Instance.Request("routineSubmit", new NativeRequest { accountId = account, taskId = taskId }, reply =>
            {
                nativeBusy = false;
                if (bootstrap.State.Session?.accountId != account) return;
                if (!reply.ok) { routineMessage.text = reply.error; return; }
                routineTasks = JsonUtility.FromJson<RoutineList>(reply.dataJson);
                routineMessage.text = routineTasks.message;
                RenderRoutineQuestion();
            });
        }
    }
}
