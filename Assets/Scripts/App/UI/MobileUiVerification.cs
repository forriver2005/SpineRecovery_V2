#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    // Batch-only UI acceptance; fixtures never touch a patient account or the network.
    public static class MobileUiVerification
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        private static readonly List<string> checks = new List<string>();
        private static string output;
        private static Canvas canvas;
        private static Camera camera;
        private static AppShellUi shell;
        private static AppState state;
        private static MemoryUsers users;

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
            checks.Clear();
            output = Path.GetFullPath("../tmp/mobile-ui-20261124/previews");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("UI verification"); host.SetActive(false);
            var app = host.AddComponent<AppBootstrap>();
            shell = host.AddComponent<AppShellUi>();
            var router = host.AddComponent<AppRouter>();
            var native = host.AddComponent<NativeMobileService>();
            state = new AppState();
            state.SetSession(new UserSession { accountId = "ui-fixture" });
            users = new MemoryUsers();
            SetAuto(typeof(AppBootstrap), null, "Instance", app);
            SetAuto(typeof(NativeMobileService), null, "Instance", native);
            SetAuto(typeof(AppBootstrap), app, "State", state);
            SetAuto(typeof(AppBootstrap), app, "Router", router);
            SetAuto(typeof(AppBootstrap), app, "Users", users);
            Set(shell, "bootstrap", app);
            router.Initialize(state);
            camera = new GameObject("RenderCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QuestionnaireUi.PageBackground;
            canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera; canvas.planeDistance = 10;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -20);
            try
            {
                VerifyAssets();
                VerifyHistory();
                VerifyIntake();
                VerifyRoutine();
                var ar = (GameObject)Call(shell, "CreateArTrainingPanel", canvas.transform);
                Render(ar, "ar-training"); ar.SetActive(false);
                var admin = (GameObject)Call(shell, "CreateAdminPanel", canvas.transform);
                Get<Text>(shell, "adminMessage").text = "当前版本：康复训练配置\n状态：已发布\n\n训练动作：鸟狗、死虫、臀桥、侧桥、卷腹、猫式\n\n固定追踪器（5 个）\n左前臂：离线\n右前臂：离线\n左小腿：离线\n右小腿：离线\n腰部：离线\n\n患者端读取当前发布的训练配置。";
                Render(admin, "admin"); admin.SetActive(false);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
                Debug.Log("MOBILE_UI_VERIFICATION_PASS " + checks.Count + " checks; " + output);
            }
            finally
            {
                SetAuto(typeof(AppBootstrap), null, "Instance", null);
                SetAuto(typeof(NativeMobileService), null, "Instance", null);
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void VerifyAssets()
        {
            foreach (string name in new[] { "Back", "HistoryClock", "Chevron", "Flexion", "Extension" })
            {
                var path = "Assets/Resources/AppUi/Refresh/" + name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var texture = Resources.Load<Texture2D>("AppUi/Refresh/" + name);
                Check(texture != null && texture.width > 0 && texture.height > 0, "Local Figma asset: " + name);
            }
        }

        private static void VerifyHistory()
        {
            var panel = (GameObject)Call(shell, "CreateHistoryPanel", canvas.transform);
            Render(panel, "history-empty");
            Check(!Button(panel, "查看最近完成的回放").interactable, "Empty history disables replay");
            var user = state.Session;
            user.painHistory = Enumerable.Range(0, 5).Select(i => new PainHistoryRecord
            { id = i.ToString(), recordedAt = "2026-11-" + (24 - i) + "T12:12:00+08:00", painLevel = i + 1, painParts = new[] { "left_waist", "left_leg" } }).ToArray();
            user.trainingHistory = new[]
            {
                new TrainingHistoryRecord { sessionId = "complete", actionId = "birddog", completed = true, completedAt = "2026-11-24T12:30:00+08:00", resultJson = "{\"overallScore\":86}", replaySessionId = "fixture-replay", planJson = "{\"startedAt\":\"2026-11-24T12:12:00+08:00\",\"actions\":[{\"actionName\":\"鸟狗\"},{\"actionName\":\"臀桥\"}]}" },
                new TrainingHistoryRecord { sessionId = "stopped", actionId = "deadbug", completedAt = "2026-11-23T12:30:00+08:00", endReason = "USER_EXIT", resultJson = "malformed legacy json" },
                new TrainingHistoryRecord { sessionId = "legacy", actionId = "hipthrust", completedAt = "legacy", endReason = "LEGACY_IMPORT" }
            };
            Call(shell, "RefreshHistorySummary");
            Render(panel, "history-records");
            Check(Button(panel, "查看最近完成的回放").interactable, "Committed completed record enables replay");
            Check(Texts(panel).Any(t => t.text == "18 分钟"), "History derives elapsed time from recorded timestamps");
            Check(!Texts(panel).Any(t => t.text.Contains("left_waist")), "Pain history uses patient-facing body labels");
            Button(panel, "RecordDetails").onClick.Invoke();
            Check(Button(panel, "查看本次回放").interactable, "Record expansion exposes its own replay");
            Render(panel, "history-detail");
            Button(panel, "AllPainRecords").onClick.Invoke();
            Check(Texts(panel).Count(t => t.text.Contains("左腰部")) == 5, "All pain records can be expanded");
            Render(panel, "history-small", 320, 568);
            panel.SetActive(false);
        }

        private static void VerifyIntake()
        {
            var survey = Form("基础问卷", "选择最符合当前情况的一项", "CreateSurveyForm");
            state.Navigate(AppRoute.BasicSurvey);
            Button(survey, "保存并继续").onClick.Invoke();
            Check(state.Route == AppRoute.BasicSurvey && users.Saves == 0, "Unanswered survey cannot advance or save");
            Button(survey, "预防 / 保持健康").onClick.Invoke();
            Button(survey, "没感觉").onClick.Invoke();
            Button(survey, "保存并继续").onClick.Invoke();
            Check(state.Route == AppRoute.PainSelect && state.Session.basicSituation == "prevention", "Survey saves original values and navigation");
            Render(survey, "intake-survey"); survey.SetActive(false);
            var pain = Form("疼痛记录", "选择疼痛部位并记录 1–10 分；没有疼痛可选择“没有疼痛”。", "CreatePainForm");
            Check(!pain.GetComponentInChildren<Slider>().interactable, "Pain slider requires a selected body part");
            Button(pain, "左腰部").onClick.Invoke();
            var slider = pain.GetComponentInChildren<Slider>(); slider.value = 5;
            Button(pain, "右腿").onClick.Invoke(); slider.value = 7;
            Button(pain, "左腰部").onClick.Invoke();
            Check(slider.value == 5, "Switching the active pain region restores its individual score");
            Render(pain, "pain-record");
            Button(pain, "没有疼痛").onClick.Invoke();
            Check(!slider.interactable, "No-pain selection disables rating");
            Button(pain, "记录并继续").onClick.Invoke();
            Check(state.Session.painFree && state.Session.latestPainLevel == 0 && state.Route == AppRoute.MobileAdmission, "No-pain intake keeps existing storage contract");
            pain.SetActive(false);
            var admission = Form("首次动作评估", "仅记录体验，不用于诊断。若疼痛明显加重或达到 8/10，请停止并寻求专业评估。", "CreateAdmissionForm");
            Button(admission, "完成首次评估").onClick.Invoke();
            Check(!state.Session.admissionCompleted, "Unanswered admission is blocked");
            var counts = admission.GetComponentsInChildren<Button>().Where(b => b.name == "+").ToArray();
            foreach (var count in counts) for (int i = 0; i < 5; i++) count.onClick.Invoke();
            foreach (var b in admission.GetComponentsInChildren<Button>().Where(b => b.name == "否，无变化")) b.onClick.Invoke();
            foreach (var b in admission.GetComponentsInChildren<Button>().Where(b => b.name == "确认评分")) b.onClick.Invoke();
            Render(admission, "admission-flexion");
            Render(admission, "admission-extension", 390, 844, true);
            foreach (var image in admission.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Figma:")))
                Check(Mathf.Abs(image.rectTransform.rect.width / image.rectTransform.rect.height - image.sprite.rect.width / image.sprite.rect.height) < .01f,
                    "Original asset aspect ratio: " + image.name);
            Button(admission, "完成首次评估").onClick.Invoke();
            Check(state.Session.admissionCompleted && state.Session.flexionPainLevel == 1 && state.Session.flexionCompletions == 5 && state.Route == AppRoute.Home,
                "Initial rating can be confirmed without moving slider; admission rules preserved");
            admission.SetActive(false);
            var review = Form("需要专业评估", "根据当前记录，系统暂停自助训练。请联系医疗专业人员。", "CreateReviewForm");
            Render(review, "professional-review"); review.SetActive(false);
        }

        private static void VerifyRoutine()
        {
            var panel = (GameObject)Call(shell, "CreateServerRoutinePanel", canvas.transform);
            Render(panel, "questionnaire-empty");
            Type listType = typeof(AppShellUi).GetNestedType("RoutineList", BindingFlags.NonPublic);
            string json = "{\"items\":[{\"taskId\":1,\"name\":\"最近一个月，腰怎么样？\",\"availability\":\"available\",\"questions\":[" +
                "{\"id\":\"pain\",\"type\":\"single_choice\",\"prompt\":\"最近腰疼的情况？\",\"required\":true,\"options\":[{\"value\":\"none\",\"label\":\"不疼\"},{\"value\":\"sometimes\",\"label\":\"偶尔轻度疼痛\"},{\"value\":\"often\",\"label\":\"经常轻度疼痛，或偶尔很疼\"}]} ," +
                "{\"id\":\"scale\",\"type\":\"scale\",\"prompt\":\"疼痛程度\",\"required\":true,\"min\":3,\"max\":9,\"step\":2}," +
                "{\"id\":\"bool\",\"type\":\"boolean\",\"prompt\":\"最近小便正常吗？\"}," +
                "{\"id\":\"text\",\"type\":\"short_text\",\"prompt\":\"其他想告诉医生的情况\",\"maxLength\":120}" +
                "]},{\"taskId\":2,\"name\":\"已完成的月度评估\",\"completedAt\":\"2026-11-01T10:00:00+08:00\",\"questions\":[]}] }";
            Set(shell, "routineTasks", JsonUtility.FromJson(json, listType));
            Call(shell, "RenderRoutineQuestion");
            Render(panel, "questionnaire-list");
            Button(panel, "开始 / 继续填写").onClick.Invoke();
            Render(panel, "questionnaire-form");
            Call(shell, "SubmitServerRoutine", 1L);
            Check(Get<Text>(shell, "routineMessage").text.Contains("必填"), "Required unanswered questions block submission");
            Button(panel, "+").onClick.Invoke();
            Check(Texts(panel).Any(t => t.text == "5"), "Scale starts at server minimum and respects step");
            Check(Get<Text>(shell, "routineMessage").text.Contains("保存失败"), "Failed native save leaves editable draft with retry state");
            var textInput = panel.GetComponentInChildren<InputField>();
            textInput.onEndEdit.Invoke("测试备注");
            Check(Get<Text>(shell, "routineProgressLabel").text == "2 / 4", "Text answer updates progress without recreating the focused form");
            Render(panel, "questionnaire-failure", 360, 640);
            Call(shell, "ResetRoutineSession");
            Check(Get<object>(shell, "routineTasks") == null && !Get<bool>(shell, "routineSaving"), "Account switch clears questionnaire data and request state");
            Set(shell, "routineTasks", JsonUtility.FromJson(json, listType));
            Set(shell, "routineTaskIndex", 1);
            Call(shell, "RenderRoutineQuestion");
            Check(Get<Text>(shell, "routinePrimaryLabel").text == "返回问卷列表", "Completed questionnaire is read-only with a return action");
            Render(panel, "questionnaire-completed"); panel.SetActive(false);
            var symptoms = new[] { "跳痛", "刺痛", "刀割痛", "锐痛", "疼痛牵扯感", "绞痛", "热灼痛", "持续固定痛", "胀痛", "触痛", "撕裂痛", "软弱无力", "厌烦", "害怕", "受罪、惩罚感" };
            string severity = "[{\"value\":\"none\",\"label\":\"无\"},{\"value\":\"mild\",\"label\":\"轻\"},{\"value\":\"medium\",\"label\":\"中\"},{\"value\":\"severe\",\"label\":\"重\"}]";
            string sf = "{\"items\":[{\"taskId\":3,\"name\":\"最近的腰疼，像哪种感觉？\",\"availability\":\"available\",\"questions\":[" +
                string.Join(",", symptoms.Select((s, i) => "{\"id\":\"q" + i + "\",\"type\":\"single_choice\",\"prompt\":\"" + s + "\",\"options\":" + severity + "}")) + "]}]}";
            Set(shell, "routineTasks", JsonUtility.FromJson(sf, listType)); Set(shell, "routineTaskIndex", 0);
            Call(shell, "RenderRoutineQuestion");
            Render(panel, "questionnaire-severity");
            Check(panel.GetComponentsInChildren<Button>().Count(b => b.name == "无") == 15, "All SF-MPQ-style severity rows retain independent answer controls");
            Render(panel, "questionnaire-severity-bottom", 390, 844, true); panel.SetActive(false);
        }

        private static GameObject Form(string title, string subtitle, string builder)
        {
            Action<Transform> build = parent => Call(shell, builder, parent);
            return (GameObject)Call(shell, "CreateFormPanel", canvas.transform, title, subtitle, build);
        }

        private static void Render(GameObject panel, string name, int width = 390, int height = 844, bool bottom = false)
        {
            panel.SetActive(true);
            var texture = new RenderTexture(width * 2, height * 2, 24);
            camera.targetTexture = texture;
            camera.aspect = (float)width / height;
            camera.orthographicSize = height / 2f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            canvas.transform.position = Vector3.zero;
            canvas.transform.localScale = Vector3.one;
            // Slider.Rebuild invokes callbacks in edit mode; rendering must not answer forms.
            var sliders = panel.GetComponentsInChildren<Slider>();
            var sliderEvents = sliders.Select(s => s.onValueChanged).ToArray();
            for (int i = 0; i < sliders.Length; i++) sliders[i].onValueChanged = new Slider.SliderEvent();
            Canvas.ForceUpdateCanvases();
            foreach (var root in panel.GetComponentsInChildren<QuestionnaireDesignRoot>()) Call(root, "Apply");
            foreach (var layout in panel.GetComponentsInChildren<MobileSummaryLayout>()) layout.Apply();
            Canvas.ForceUpdateCanvases();
            var scroll = panel.GetComponentInChildren<ScrollRect>();
            if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = bottom ? 0 : 1; }
            Canvas.ForceUpdateCanvases();
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            for (int i = 0; i < sliders.Length; i++) sliders[i].onValueChanged = sliderEvents[i];
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var png = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); png.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), png.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(png); UnityEngine.Object.DestroyImmediate(texture);
            Check(panel.GetComponentInChildren<QuestionnaireDesignRoot>() != null, "Responsive design root: " + name);
            var design = panel.GetComponentInChildren<QuestionnaireDesignRoot>().GetComponent<RectTransform>();
            Check(Mathf.Abs(design.rect.width - 390) < 1 && Mathf.Abs(design.rect.height - height * 390f / width) < 1,
                "Design root fits phone ratio: " + name);
            foreach (var button in panel.GetComponentsInChildren<Button>().Where(b => b.transform.parent.name == "Footer"))
            {
                var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
                Check(corners.All(p => p.x >= -width / 2f - 1 && p.x <= width / 2f + 1 && p.y >= -height / 2f - 1 && p.y <= height / 2f + 1),
                    "Footer action stays inside viewport: " + name + "/" + button.name);
            }
            foreach (var image in panel.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Figma:")))
                Check(image.sprite != null && image.sprite.texture != null, "Rendered asset slot: " + name + "/" + image.name);
            foreach (var slider in sliders) Check(Mathf.Abs(slider.handleRect.rect.height - 28) < .1f, "Circular pain-slider thumb: " + name);
        }

        private static IEnumerable<Text> Texts(GameObject panel) => panel.GetComponentsInChildren<Text>();
        private static Button Button(GameObject panel, string name) => panel.GetComponentsInChildren<Button>().First(b => b.name == name);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add("PASS " + message); }
        private static object Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Private).Invoke(instance, args);
        private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
        private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, Private).GetValue(instance);
        private static void SetAuto(Type type, object instance, string property, object value) => type.GetField("<" + property + ">k__BackingField", Private).SetValue(instance, value);

        private sealed class MemoryUsers : IUserRepository
        {
            public int Saves;
            public bool TryAuthenticate(string a, string p, out UserSession s) { s = null; return false; }
            public bool TryAuthenticateAdministrator(string a, string p, out UserSession s) { s = null; return false; }
            public AccountRegistrationResult TryRegister(string a, string p, out UserSession s) { s = null; return AccountRegistrationResult.InvalidInput; }
            public void Save(UserSession session) { Saves++; }
            public UserSession Load(string account) => state.Session;
            public void AppendPainRecord(UserSession session, PainHistoryRecord record) { session.painHistory = session.painHistory.Concat(new[] { record }).ToArray(); Saves++; }
            public void AppendTrainingRecord(UserSession session, TrainingHistoryRecord record) { Saves++; }
        }
    }
}
#endif
