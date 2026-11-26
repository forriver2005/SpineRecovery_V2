using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    public sealed partial class AppShellUi
    {
        private HistoryScreen historyScreen;
        private Slider intakePainSlider;

        private GameObject CreateArTrainingPanel(Transform parent)
        {
            var root = QuestionnaireUi.CreateScreen(parent, "ArTrainingPanel", null, out var panel);
            var content = QuestionnaireUi.ScrollArea(root, 56, 124, 660);
            MobileUi.Text(content, "开始本次 AR 训练", 20, 24, 350, 36, 24, true, QuestionnaireUi.Accent);
            MobileUi.Text(content, "连接投屏眼镜与追踪器，准备开始训练", 20, 76, 350, 42, 14, false, QuestionnaireUi.Muted);
            var card = QuestionnaireUi.Card(content, 20, 138, 350, 226);
            MobileUi.Text(card, "训练前的准备", 20, 20, 310, 28, 18, true);
            MobileUi.Text(card, "1  连接投屏眼镜\n\n2  佩戴并校准 IMU 追踪器\n\n3  留出足够的活动空间", 20, 62, 310, 144, 15);
            QuestionnaireUi.PillButton(content, "追踪器连接与校准", 20, 390, 350, 52, false, OpenTrackingCalibrationFromHome);
            QuestionnaireUi.PillButton(content, "动作示范库", 20, 458, 350, 52, false, OpenActionLibrary);
            var footer = QuestionnaireUi.Footer(root, 124);
            QuestionnaireUi.PillButton(footer, "开始训练", 20, 12, 350, 52, true, () => StartTraining("SpineV2Minimal", "dead_bug"));
            arTrainingMessage = MobileUi.Text(footer, "建议先完成追踪器校准，再开始训练。", 20, 72, 350, 44, 12, false, QuestionnaireUi.Muted);
            MobileUi.Header(root, "AR 训练", () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private GameObject CreateHistoryPanel(Transform parent)
        {
            historyScreen = new HistoryScreen();
            var panel = historyScreen.Build(parent, () => bootstrap.State.Session,
                () => bootstrap.Router.Navigate(AppRoute.Home), ShowMonthlyQuestionnaire, OpenReplayById, LatestImportedReplayId);
            historyMessage = historyScreen.Message;
            return panel;
        }

        private void RefreshHistorySummary() => historyScreen?.Refresh();

        private string LatestImportedReplayId()
        {
            if (!NativeMobileService.Available || bootstrap.State.Session == null) return null;
            try
            {
                string ids = NativeMobileService.Call<string>("importedReplayIds", bootstrap.State.Session.accountId);
                var imported = MobileUi.ReadJson<ImportedReplayList>("{\"items\":" + ids + "}");
                return imported?.items != null && imported.items.Length > 0 ? imported.items[imported.items.Length - 1] : null;
            }
            catch (AndroidJavaException) { return null; }
        }

        private GameObject CreateFormPanel(Transform parent, string title, string subtitle, Action<Transform> build)
        {
            AppRoute route = title == "基础问卷" ? AppRoute.BasicSurvey : title == "疼痛记录" ? AppRoute.PainSelect :
                title == "首次动作评估" ? AppRoute.MobileAdmission : AppRoute.AdmissionDecision;
            var root = QuestionnaireUi.CreateScreen(parent, route + "Panel", null, out var panel);
            var content = QuestionnaireUi.ScrollArea(root, 56, 124, 800);
            MobileUi.Text(content, title == "基础问卷" ? "先了解一下你的情况" : title == "疼痛记录" ? "哪里感到疼痛？" : title,
                20, 20, 350, 34, 24, true, QuestionnaireUi.Accent);
            MobileUi.Text(content, subtitle, 20, 64, 350, 48, 13, false, QuestionnaireUi.Muted);
            var footer = QuestionnaireUi.Footer(root, 124);
            flowMessage = MobileUi.Text(footer, "", 20, 70, 350, 48, 12, false, QuestionnaireUi.DangerText, TextAnchor.UpperLeft);
            flowMessages[route] = flowMessage;
            string caption = route == AppRoute.BasicSurvey ? "保存并继续" : route == AppRoute.PainSelect ? "记录并继续" :
                route == AppRoute.MobileAdmission ? "完成首次评估" : "退出登录";
            QuestionnaireUi.PillButton(footer, caption, 20, 12, 350, 52, true, () =>
            {
                flowMessages[route].text = "";
                if (route == AppRoute.BasicSurvey) SaveSurvey();
                else if (route == AppRoute.PainSelect) SavePain();
                else if (route == AppRoute.MobileAdmission) FinishAdmission();
                else bootstrap.State.ClearSession();
            });
            build(content);
            MobileUi.Header(root, title, () =>
            {
                if (route == AppRoute.AdmissionDecision) bootstrap.State.ClearSession();
                else bootstrap.Router.Navigate(route == AppRoute.BasicSurvey ? AppRoute.BasicProfile :
                    route == AppRoute.PainSelect ? AppRoute.BasicSurvey : AppRoute.PainSelect);
            });
            return panel;
        }

        private void CreateSurveyForm(Transform parent)
        {
            var first = QuestionnaireUi.Card(parent, 20, 128, 350, 238);
            MobileUi.Text(first, "01  当前情况", 20, 16, 310, 28, 17, true);
            string[] situations = { "预防 / 保持健康", "曾有相关问题", "目前急性不适" };
            situationButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                situationButtons[i] = MobileUi.Choice(first, situations[i], 20, 58 + i * 56, 310, () => SelectSituation(index));
            }
            var second = QuestionnaireUi.Card(parent, 20, 382, 350, 250);
            MobileUi.Text(second, "02  当前症状", 20, 16, 310, 28, 17, true);
            string[] symptoms = { "没感觉", "腰疼 / 腿疼 / 腿麻 / 放射痛", "无力（暂停流程）" };
            symptomButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                symptomButtons[i] = MobileUi.Choice(second, symptoms[i], 20, 58 + i * 58, 310, () => SelectSymptom(index));
            }
            surveySelectionLabel = MobileUi.Text(parent, "", 24, 650, 342, 52, 13, false, QuestionnaireUi.Muted);
            MobileUi.Height((RectTransform)parent, 726);
            RefreshSurveySelection();
        }

        private void CreatePainForm(Transform parent)
        {
            var card = QuestionnaireUi.Card(parent, 20, 128, 350, 364);
            MobileUi.Text(card, "选择疼痛部位", 20, 16, 310, 28, 17, true);
            MobileUi.Text(card, "可多选，再为当前部位设置疼痛程度", 20, 46, 310, 24, 12, false, QuestionnaireUi.Muted);
            string[] labels = { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" };
            string[] keys = { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" };
            painPartButtons = new Button[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                painPartButtons[i] = MobileUi.Choice(card, labels[i], 20 + i % 2 * 160, 84 + i / 2 * 54, 150, () => TogglePainPart(key));
            }
            noPainButton = MobileUi.Choice(card, "没有疼痛", 20, 304, 310, SelectNoPain);
            var rating = QuestionnaireUi.Card(parent, 20, 508, 350, 254);
            MobileUi.Text(rating, "疼痛程度", 20, 16, 310, 28, 17, true);
            selectedPainPartLabel = MobileUi.Text(rating, "", 20, 52, 310, 52, 13, false, QuestionnaireUi.Muted);
            painScoreLabel = MobileUi.Text(rating, "", 20, 106, 310, 46, 14, true, QuestionnaireUi.Accent);
            intakePainSlider = QuestionnaireUi.PainSlider(rating, 20, 164, 310, null);
            intakePainSlider.minValue = 1;
            intakePainSlider.SetValueWithoutNotify(selectedPain);
            intakePainSlider.onValueChanged.AddListener(value => ChangePainScore((int)value - selectedPain));
            MobileUi.Text(rating, "1 轻微", 24, 208, 145, 24, 12, false, QuestionnaireUi.Muted);
            MobileUi.Text(rating, "10 剧痛", 181, 208, 145, 24, 12, false, QuestionnaireUi.Muted, TextAnchor.MiddleRight);
            MobileUi.Height((RectTransform)parent, 786);
            RefreshPainSelectionUi(keys, labels);
        }

        private void CreateAdmissionForm(Transform parent)
        {
            flexionResponseButtons = CreateMovementAssessment(parent, true, 128, "站立位前屈测试（FIS）", "双腿伸直，连续尝试用手摸脚尖 10 次", QuestionnaireUi.Accent);
            flexionPhaseButtons = lastCreatedPhaseButtons;
            extensionResponseButtons = CreateMovementAssessment(parent, false, 896, "站立位后伸测试（EIS）", "双手托腰，以手为支点连续后仰 10 次", new Color(.04f, .59f, .54f));
            extensionPhaseButtons = lastCreatedPhaseButtons;
            MobileUi.Height((RectTransform)parent, 1664);
        }

        private Button[] CreateMovementAssessment(Transform parent, bool flexion, float top, string title, string instruction, Color accent)
        {
            var card = QuestionnaireUi.Card(parent, 20, top, 350, 752);
            MobileUi.Text(card, title, 20, 16, 310, 28, 18, true, accent);
            MobileUi.Text(card, instruction, 20, 50, 310, 36, 13, false, QuestionnaireUi.Muted);
            var picture = QuestionnaireUi.Box(card, "MovementIllustration", QuestionnaireUi.OptionBackground, 14, 20, 96, 310, 176);
            MobileUi.Asset(picture.transform, flexion ? "Flexion" : "Extension", 75, 8, 160, 160);
            Text count = MobileUi.Text(card, "实际完成：0 / 10 次", 20, 286, 188, 44, 15, true);
            Action<int> changeCount = delta =>
            {
                ShowFlowMessage("");
                int value = Mathf.Clamp((flexion ? flexionCompletedCount : extensionCompletedCount) + delta, 0, 10);
                if (flexion) flexionCompletedCount = value; else extensionCompletedCount = value;
                count.text = "实际完成：" + value + " / 10 次";
            };
            MobileUi.Choice(card, "−", 222, 286, 48, () => changeCount(-1), false, 44);
            MobileUi.Choice(card, "+", 282, 286, 48, () => changeCount(1), false, 44);
            MobileUi.Text(card, "重复动作后，原有疼痛是否缓解？", 20, 344, 310, 26, 15, true);
            string[] responses = { "是，明显缓解", "否，无变化", "加重" };
            var buttons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                buttons[i] = MobileUi.Choice(card, responses[i], 20, 382 + i * 54, 310, () => SelectAdmissionResponse(flexion, index));
            }
            MobileUi.Text(card, "疼痛最先出现在哪个阶段？（可选）", 20, 548, 310, 26, 13, false, QuestionnaireUi.Muted);
            string[] phases = { "起始阶段", "中间阶段", "末端阶段", "全程无痛" };
            var phaseButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                phaseButtons[i] = MobileUi.Choice(card, phases[i], 20 + (i % 2) * 160, 584 + i / 2 * 50, 150, () => SelectAdmissionPhase(flexion, index), false, 44);
            }
            // Keep the existing 1–10 admission scale; explicit confirmation also permits its initial value.
            var score = MobileUi.Text(card, "疼痛程度：未评分 / 10", 20, 686, 210, 28, 14, true, accent);
            var slider = QuestionnaireUi.PainSlider(card, 20, 714, 210, null);
            slider.minValue = 1;
            slider.SetValueWithoutNotify(1);
            slider.onValueChanged.AddListener(value => SetAdmissionPain(flexion, (int)value));
            MobileUi.Choice(card, "确认评分", 240, 692, 90, () => SetAdmissionPain(flexion, (int)slider.value), false, 44);
            if (flexion) flexionScoreLabel = score; else extensionScoreLabel = score;
            lastCreatedPhaseButtons = phaseButtons;
            return buttons;
        }

        private void CreateReviewForm(Transform parent)
        {
            var card = QuestionnaireUi.Card(parent, 20, 136, 350, 260);
            var notice = QuestionnaireUi.Box(card, "ReviewNotice", QuestionnaireUi.DangerFill, 14, 20, 20, 310, 70);
            MobileUi.Text(notice.transform, "!", 16, 15, 30, 40, 24, true, QuestionnaireUi.DangerText, TextAnchor.MiddleCenter);
            MobileUi.Text(notice.transform, "请先暂停自助训练", 58, 15, 236, 40, 16, true, QuestionnaireUi.DangerText);
            MobileUi.Text(card, "根据刚才填写的信息，建议先联系医疗专业人员评估，再决定如何训练。", 20, 110, 310, 66, 16, true);
            MobileUi.Text(card, "当前记录已保留。你可以退出登录，按照专业人员的建议继续。", 20, 190, 310, 50, 14, false, QuestionnaireUi.Muted);
            MobileUi.Height((RectTransform)parent, 430);
        }

        private RectTransform adminContent;

        private GameObject CreateAdminPanel(Transform parent)
        {
            var root = QuestionnaireUi.CreateScreen(parent, "AdminPanel", RefreshAdminSummary, out var panel);
            MobileUi.Header(root, "管理员工作台", null);
            adminContent = QuestionnaireUi.ScrollArea(root, 56, 0, 480);
            RefreshAdminSummary();
            return panel;
        }

        private void RefreshAdminSummary()
        {
            if (adminContent == null) return;
            MobileUi.Clear(adminContent);
            var repo = bootstrap?.AdminConfigs;
            var config = repo?.CurrentPublished();
            bool locked = config != null && config.trackerBindingsLocked && !config.trackerBindingsDisabled;
            var device = QuestionnaireUi.Card(adminContent, 20, 20, 350, 186);
            MobileUi.Text(device, "IMU 追踪器", 20, 17, 200, 32, 20, true);
            var chip = QuestionnaireUi.Box(device, "BindingStatus", QuestionnaireUi.SecondaryButton, 10, 242, 19, 88, 28);
            MobileUi.Text(chip.transform, locked ? "已绑定" : config == null ? "状态未知" : "未绑定",
                0, 0, 88, 28, 13, true, QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
            MobileUi.Text(device, locked ? "查看五台设备的佩戴部位、连接状态和电量。" :
                "为五台追踪器分配佩戴部位，再绑定到患者端。", 20, 58, 310, 54, 15, false, QuestionnaireUi.Muted);
            AdminButton(device, locked ? "管理已绑定设备" : "管理 IMU 绑定", 20, 120, 310, true, OpenAdminImuBinding);

            var plan = QuestionnaireUi.Card(adminContent, 20, 222, 350, 156);
            MobileUi.Text(plan, "已发布训练方案", 20, 16, 310, 34, 20, true);
            float y = 62;
            void Detail(string value, bool bold)
            {
                var label = MobileUi.Text(plan, value, 20, y, 310, 24, bold ? 15 : 14, bold,
                    bold ? QuestionnaireUi.Ink : QuestionnaireUi.Muted, TextAnchor.UpperLeft);
                float h = Mathf.Max(24, label.preferredHeight * label.rectTransform.localScale.y);
                label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h / label.rectTransform.localScale.y);
                y += h + 8;
            }
            if (config == null) Detail("暂无已发布的训练配置", false);
            else
            {
                Detail("当前版本   " + config.versionName, true);
                var names = new System.Collections.Generic.List<string>();
                foreach (var item in config.actions ?? Array.Empty<AdminActionDefinition>())
                    if (item != null && !string.IsNullOrWhiteSpace(item.displayName)) names.Add(item.displayName);
                Detail("训练动作   " + (names.Count == 0 ? "暂无" : string.Join("、", names)), false);
                foreach (var rule in config.recommendationRules ?? Array.Empty<AdminRecommendationRule>())
                {
                    if (rule == null || !rule.enabled) continue;
                    var targets = Array.ConvertAll(rule.ResolveActions(config.actions), action => action.displayName);
                    if (targets.Length > 0) Detail(rule.userTag + "：推荐" + string.Join("、", targets), false);
                }
            }
            var versions = repo?.ListConfigurations();
            if (versions != null && repo is JsonAdminConfigRepository editable)
            {
                AdminTrainingConfiguration ready = null, archived = null;
                foreach (var v in versions)
                {
                    if (v.state == AdminConfigState.ArTestPassed && ready == null) ready = v;
                    if (v.state == AdminConfigState.Archived &&
                        (archived == null || string.CompareOrdinal(v.updatedAt, archived.updatedAt) > 0)) archived = v;
                }
                void VersionAction(string caption, Func<bool> apply)
                {
                    AdminButton(plan, caption, 20, y, 310, false, () => {
                        bool ok = apply(); RefreshAdminSummary();
                        adminMessage.text = ok ? "配置已更新" : "配置未更新，请检查版本状态";
                    });
                    y += 60;
                }
                if (ready != null) VersionAction("发布已验证版本 " + ready.versionName, () => editable.Publish(ready.configId));
                if (archived != null) VersionAction("回滚至 " + archived.versionName, () => editable.RollbackTo(archived.configId));
            }
            plan.sizeDelta = new Vector2(350, y + 12);
            var shadow = (RectTransform)adminContent.GetChild(plan.GetSiblingIndex() - 1);
            shadow.sizeDelta = new Vector2(352, y + 16);
            float bottom = 222 + y + 28;
            adminMessage = MobileUi.Text(adminContent, "", 22, bottom, 346, 26, 13, false, QuestionnaireUi.Accent);
            AdminButton(adminContent, "退出登录", 20, bottom + 32, 350, false, () => bootstrap.State.ClearSession());
            MobileUi.Height(adminContent, bottom + 104);
        }

        private static void AdminButton(Transform parent, string caption, float x, float y, float width,
            bool primary, UnityEngine.Events.UnityAction action)
        {
            var b = QuestionnaireUi.MakeButton(parent, caption, primary ? QuestionnaireUi.Accent : QuestionnaireUi.SecondaryButton,
                12, x, y, width, 48, action);
            MobileUi.Text(b.transform, caption, 8, 0, width - 16, 48, 16, true,
                primary ? Color.white : QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
        }
    }
}
