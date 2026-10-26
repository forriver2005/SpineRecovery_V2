using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace SpineRecovery.App
{
    public sealed class AppShellUi : MonoBehaviour
    {
        private AppBootstrap bootstrap;
        private Canvas appCanvas;
        private Camera shellCamera;
        private bool nativeBusy;
        private string previewActivationCode;
        private int loginGeneration;
        private int sessionIdentityGeneration;
        private int directiveRefreshGeneration;
        private bool directiveRefreshPending;
        private int questionnaireRefreshGeneration;
        private bool questionnaireRefreshPending;
        private NativeQuestionnaireTask[] nativeQuestionnaires = System.Array.Empty<NativeQuestionnaireTask>();
        private NativeQuestionnaireTask activeNativeQuestionnaire;
        private bool questionnaireOpenRequested;
        private bool questionnaireSaving;
        private bool questionnaireDraftDirty;
        private bool questionnaireBusy;
        private System.Action questionnaireAfterSave;
        private int questionnaireStep;
        private int questionnaireTotal;
        private bool auxiliaryPageOpen;
        private AppRoute auxiliaryReturnRoute;
        private RectTransform safeContent;
        private Rect lastSafeArea;
        private Text loginMessage;
        private Text homeMessage;
        private Text trackingMessage;
        private Text adminMessage;
        private Text historyMessage;
        private Text activationMessage;
        private Text arTrainingMessage;
        private Text trainingMenuMessage;
        private EventSystem appEventSystem;
        private Button trackingCompleteButton;
        private InputField accountInput;
        private InputField passwordInput;
        private InputField registerAccountInput;
        private InputField registerPasswordInput;
        private InputField registerConfirmInput;
        private InputField activationCodeInput;
        private InputField[] profileInputs;
        private AppRoute profileContinueRoute = AppRoute.Home;
        private Text flowMessage;
        private Text painScoreLabel;
        private Text surveySelectionLabel;
        private Text selectedPainPartLabel;
        private Button[] situationButtons;
        private Button[] symptomButtons;
        private Button[] genderButtons;
        private Button[] painPartButtons;
        private Button noPainButton;
        private Button[] flexionResponseButtons;
        private Button[] extensionResponseButtons;
        private Button[] flexionPhaseButtons;
        private Button[] extensionPhaseButtons;
        private readonly Dictionary<AppRoute, Text> flowMessages = new Dictionary<AppRoute, Text>();
        private int selectedPain = 3;
        private int flexionAdmissionPain;
        private int extensionAdmissionPain;
        private bool painFree;
        private bool flexionPainRated;
        private bool extensionPainRated;
        private int flexionCompletedCount, extensionCompletedCount;
        private string selectedGender = "";
        private string selectedSituation = "";
        private string selectedSymptom = "";
        private string selectedPainPart = "";
        private string flexionResponse;
        private string extensionResponse;
        private string flexionPainPhase;
        private string extensionPainPhase;
        private readonly List<string> selectedPainParts = new List<string>();
        private readonly Dictionary<string, int> painLevelsByPart = new Dictionary<string, int>();
        private PreTrainingScreen preTrainingScreen;
        private NoticeScreen preTrainingBlockedScreen;
        private PostActionScreen postActionScreen;
        private NoticeScreen postTrainingStopScreen;
        // Set when a training button sent the user to the pre-training check,
        // so passing the check can carry on into that training session.
        private string pendingTrainingScene = "";
        private string pendingTrainingActionId = "";
        private string[] lastPreTrainingParts = System.Array.Empty<string>();
        private int postActionNumber = 1;
        private int postActionCount = 1;
        private MonthlyQuestionnaireScreens monthlyScreens;
        private HomeScreen homeScreen;
        /// <summary>Pages of the monthly assignment currently being filled (1, 2 or 3).</summary>
        private int activeMonthlyModules = MonthlyQuestionnaireAssignment.ModuleJoa;

        private void Start()
        {
            AppBootstrap sceneBootstrap = GetComponent<AppBootstrap>();
            if (AppBootstrap.Instance != null && AppBootstrap.Instance != sceneBootstrap)
            {
                return;
            }

            bootstrap = AppBootstrap.Instance ?? GetComponent<AppBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = gameObject.AddComponent<AppBootstrap>();
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            bootstrap.State.SessionChanged += HandleSessionChanged;
            EnsureEventSystem();
            EnsureCameraIfNeeded();
            ScreenOrientationService.SetPortrait();
            appCanvas = CreateCanvas(transform);
            GameObject safe = new GameObject("SafeArea", typeof(RectTransform));
            safe.transform.SetParent(appCanvas.transform, false);
            safeContent = safe.GetComponent<RectTransform>();
            ApplySafeArea();
            GameObject loginPanel = CreateLoginPanel(safeContent);
            GameObject registerPanel = CreateRegisterPanel(safeContent);
            GameObject activationPanel = CreateActivationPanel(safeContent);
            GameObject homePanel = CreateHomePanel(safeContent);
            GameObject arTrainingPanel = CreateArTrainingPanel(safeContent);
            GameObject trainingMenuPanel = CreateTrainingMenuPanel(safeContent);
            GameObject trackingPanel = CreateTrackingCalibrationPanel(safeContent);
            GameObject profilePanel = CreateFormPanel(safeContent, "资料登记", "请填写基本资料（年龄 10–100，身高 120–220 cm，体重 35–200 kg）", CreateProfileForm);
            GameObject surveyPanel = CreateFormPanel(safeContent, "基础问卷", "选择最符合当前情况的一项；如出现危险症状请暂停并联系专业人员。", CreateSurveyForm);
            GameObject painPanel = CreateFormPanel(safeContent, "疼痛记录", "选择疼痛部位并记录 1–10 分；没有疼痛可选择“无疼痛”。", CreatePainForm);
            GameObject admissionPanel = CreateFormPanel(safeContent, "首次动作评估", "仅记录体验，不用于诊断。若疼痛明显加重或达到 8/10，请停止并寻求专业评估。", CreateAdmissionForm);
            GameObject reviewPanel = CreateFormPanel(safeContent, "需要专业评估", "根据当前记录，系统暂停自助训练。请联系医疗专业人员；该结果不是诊断。", CreateReviewForm);
            preTrainingScreen = new PreTrainingScreen();
            GameObject preTrainingPanel = preTrainingScreen.Build(safeContent, HandlePreTrainingBack,
                HandlePreTrainingSubmitted,
                () => string.IsNullOrEmpty(pendingTrainingScene) ? "确认并返回首页" : "开始训练");
            GameObject preTrainingBlockedPanel = CreatePreTrainingBlockedPanel(safeContent);
            GameObject adminPanel = CreateAdminPanel(safeContent);
            GameObject historyPanel = CreateHistoryPanel(safeContent);
            postActionScreen = new PostActionScreen();
            GameObject postTrainingPanel = postActionScreen.Build(safeContent, HandlePostActionBack,
                HandlePostActionSubmitted);
            GameObject postTrainingStopPanel = CreatePostTrainingStopPanel(safeContent);
            monthlyScreens = new MonthlyQuestionnaireScreens();
            GameObject monthlyJoaPanel = monthlyScreens.BuildJoa(safeContent,
                HandleMonthlyBack,
                HandleMonthlyJoaNext);
            GameObject monthlySfmpqPanel = monthlyScreens.BuildSfMpq(safeContent,
                HandleMonthlySfmpqBack,
                FinishMonthlyQuestionnaire);
            monthlyScreens.AnswersChanged += () =>
            {
                if (activeNativeQuestionnaire != null && !questionnaireBusy)
                    SaveNativeQuestionnaireDraft();
            };

            bootstrap.Router.Initialize(bootstrap.State);
            bootstrap.Router.RegisterPanel(AppRoute.Login, loginPanel);
            bootstrap.Router.RegisterPanel(AppRoute.Register, registerPanel);
            bootstrap.Router.RegisterPanel(AppRoute.Activation, activationPanel);
            bootstrap.Router.RegisterPanel(AppRoute.Home, homePanel);
            bootstrap.Router.RegisterPanel(AppRoute.Training, arTrainingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.TrainingPreparation, trainingMenuPanel);
            bootstrap.Router.RegisterPanel(AppRoute.TrackingCalibration, trackingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.BasicProfile, profilePanel);
            bootstrap.Router.RegisterPanel(AppRoute.BasicSurvey, surveyPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PainSelect, painPanel);
            bootstrap.Router.RegisterPanel(AppRoute.MobileAdmission, admissionPanel);
            bootstrap.Router.RegisterPanel(AppRoute.AdmissionDecision, reviewPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PreTraining, preTrainingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PreTrainingBlocked, preTrainingBlockedPanel);
            bootstrap.Router.RegisterPanel(AppRoute.Admin, adminPanel);
            bootstrap.Router.RegisterPanel(AppRoute.History, historyPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PostTrainingSurvey, postTrainingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PostTrainingStop, postTrainingStopPanel);
            bootstrap.Router.RegisterPanel(AppRoute.MonthlyJoa, monthlyJoaPanel);
            bootstrap.Router.RegisterPanel(AppRoute.MonthlySfmpq, monthlySfmpqPanel);
        }

        public void ShowAppShell()
        {
            if (appCanvas != null)
            {
                appCanvas.gameObject.SetActive(true);
            }
            EnsureEventSystem();
            EnsureCameraIfNeeded();
            if (shellCamera != null) shellCamera.enabled = true;
            SetAppEventSystemEnabled(true);

            if (bootstrap != null && bootstrap.State.Session != null && homeMessage != null)
            {
                RefreshHomeSummary();
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            if (scene.name != "AppShell") { HideAppShell(); return; }
            ShowAppShell();
            AppRoute route = bootstrap.State.Session == null ? AppRoute.Login :
                auxiliaryPageOpen ? auxiliaryReturnRoute : AppRoute.Home;
            bool auxiliary = auxiliaryPageOpen;
            auxiliaryPageOpen = false;
            bootstrap.Router.Navigate(route);
            if (!auxiliary) bootstrap.Training.RestoreForCurrentUser();
        }

        private void Update()
        {
            if (safeContent != null && Screen.safeArea != lastSafeArea) ApplySafeArea();
            // Wait for profile/unfinished training pages to finish before opening a login task.
            if (questionnaireOpenRequested && !questionnaireRefreshPending &&
                activeNativeQuestionnaire == null && bootstrap?.State?.Route == AppRoute.Home)
            {
                questionnaireOpenRequested = false;
                OpenNextNativeQuestionnaire(true);
            }
        }

        private void ApplySafeArea()
        {
            if (safeContent == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect area = Screen.safeArea;
            lastSafeArea = area;
            safeContent.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safeContent.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safeContent.offsetMin = safeContent.offsetMax = Vector2.zero;
        }

        private void HideAppShell()
        {
            if (appCanvas != null) appCanvas.gameObject.SetActive(false);
            if (shellCamera != null) shellCamera.enabled = false;
            SetAppEventSystemEnabled(false);
        }

        private void HandleSessionChanged(UserSession session)
        {
            sessionIdentityGeneration++;
            directiveRefreshGeneration++;
            directiveRefreshPending = false;
            questionnaireRefreshGeneration++;
            questionnaireRefreshPending = false;
            nativeQuestionnaires = System.Array.Empty<NativeQuestionnaireTask>();
            activeNativeQuestionnaire = null;
            questionnaireOpenRequested = false;
            questionnaireSaving = questionnaireDraftDirty = questionnaireBusy = false;
            questionnaireAfterSave = null;
            if (session == null)
            {
                nativeBusy = false;
                loginGeneration++;
                NativeMobileService.Call("logout");
            }
        }

        private void OnDestroy()
        {
            directiveRefreshGeneration++;
            questionnaireRefreshGeneration++;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (bootstrap != null) bootstrap.State.SessionChanged -= HandleSessionChanged;
        }

        private void RefreshHomeSummary()
        {
            if (homeScreen != null) homeScreen.Refresh();
        }


        private GameObject CreateLoginPanel(Transform parent)
        {
            GameObject panel = CreatePanel("LoginPanel", parent, new Color(0.973f, 0.980f, 1f));
            GameObject designRoot = new GameObject("LoginDesign390x884", typeof(RectTransform));
            designRoot.transform.SetParent(panel.transform, false);
            RectTransform designRect = designRoot.GetComponent<RectTransform>();
            designRect.anchorMin = new Vector2(0.5f, 0.5f);
            designRect.anchorMax = new Vector2(0.5f, 0.5f);
            designRect.pivot = new Vector2(0.5f, 0.5f);
            designRect.sizeDelta = new Vector2(390f, 884f);
            designRect.anchoredPosition = Vector2.zero;
            float designScale = Mathf.Min(1080f / 390f, 1920f / 884f);
            designRoot.transform.localScale = new Vector3(designScale, designScale, 1f);

            // Figma exports use top-left coordinates in a 390x884 frame. The
            // RectTransform above uses the same local frame, so convert every
            // component through one mapping instead of mixing center and top-
            // left coordinates (the source of the old overlap/drift).
            // LoginCover.png is the approved complete 390x884 Figma export.
            // Keep it as one image so every logo, label, border, shadow and
            // spacing remains pixel-identical to the source design. The
            // controls below are transparent interaction overlays only.
            CreateRasterImage(designRoot.transform, "AppUi/LoginCover", Vector2.zero,
                new Vector2(LoginDesignWidth, LoginDesignHeight));

            accountInput = CreateInput(designRoot.transform, "手机号 / 邮箱", LoginDesignPosition(45.5f, 317.5f, 299f, 51f), false);
            passwordInput = CreateInput(designRoot.transform, "密码", LoginDesignPosition(45.5f, 385.5f, 299f, 51f), true);
            accountInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(299f, 51f);
            passwordInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(299f, 51f);
            MakeOverlayInput(accountInput);
            MakeOverlayInput(passwordInput);

            Button loginButton = CreateButton(designRoot.transform, "登录", LoginDesignPosition(45f, 491f, 300f, 52f), HandleLoginClicked, 300f, 52f);
            Button registerButton = CreateButton(designRoot.transform, "注册新账号", LoginDesignPosition(214.489f, 610.271f, 59.106f, 14.677f),
                () => bootstrap.Router.Navigate(AppRoute.Register), 59.106f, 28f);
            MakeOverlayButton(loginButton);
            MakeOverlayButton(registerButton);

            loginMessage = CreateLabel(designRoot.transform, "", LoginDesignPosition(45f, 550f, 300f, 28f), 12);
            loginMessage.rectTransform.sizeDelta = LoginDesignSize(300f, 28f);
            return panel;
        }

        private GameObject CreateRegisterPanel(Transform parent)
        {
            GameObject panel = CreatePanel("RegisterPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "注册账号", new Vector2(0, 250), 30);
            CreateLabel(panel.transform, "创建账号后可以绑定激活档案。", new Vector2(0, 195), 16);
            registerAccountInput = CreateInput(panel.transform, "设置账号", new Vector2(0, 115), false);
            registerPasswordInput = CreateInput(panel.transform, "设置密码", new Vector2(0, 40), true);
            registerConfirmInput = CreateInput(panel.transform, "再次输入密码", new Vector2(0, -35), true);
            CreateButton(panel.transform, "注册并继续激活", new Vector2(0, -125), HandleRegisterClicked);
            CreateButton(panel.transform, "返回登录", new Vector2(0, -195), () => bootstrap.Router.Navigate(AppRoute.Login));
            return panel;
        }

        private GameObject CreateActivationPanel(Transform parent)
        {
            GameObject panel = CreatePanel("ActivationPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "激活档案", new Vector2(0, 220), 30);
            CreateLabel(panel.transform, "请输入医生建档时提供的 11 位手机号激活码。", new Vector2(0, 165), 16);
            activationCodeInput = CreateInput(panel.transform, "激活码（11位手机号）", new Vector2(0, 80), false);
            activationMessage = CreateLabel(panel.transform, "本地演示会保存绑定状态；正式环境可替换为服务器预览/核销。", new Vector2(0, 15), 14);
            activationMessage.rectTransform.sizeDelta = new Vector2(760, 70);
            CreateButton(panel.transform, "确认绑定", new Vector2(0, -75), HandleActivationClicked);
            CreateButton(panel.transform, "跳过，稍后激活", new Vector2(0, -145), SkipActivation);
            CreateButton(panel.transform, "返回登录", new Vector2(0, -215), () => bootstrap.State.ClearSession());
            return panel;
        }

        private GameObject CreateHomePanel(Transform parent)
        {
            homeScreen = new HomeScreen();
            GameObject panel = homeScreen.Build(parent, BuildHomeState, new HomeScreen.Actions
            {
                EnterTraining = () => bootstrap.Router.Navigate(AppRoute.Training),
                OpenTracker = () => bootstrap.Router.Navigate(AppRoute.TrackingCalibration),
                OpenActivation = () => bootstrap.Router.Navigate(AppRoute.Activation),
                OpenHistory = ShowHistory,
                OpenMonthly = ShowMonthlyQuestionnaire,
                Logout = () => bootstrap.State.ClearSession(),
            });
            // Status lines (launch errors, save confirmations) still go through homeMessage.
            homeMessage = homeScreen.Message;
            return panel;
        }

        private HomeScreen.State BuildHomeState()
        {
            UserSession session = bootstrap != null && bootstrap.State != null ? bootstrap.State.Session : null;
            if (session == null) return new HomeScreen.State { Name = "" };

            MonthlyQuestionnaireAssignment assignment = !NativeMobileService.Available && bootstrap.MonthlyAssignments != null
                ? bootstrap.MonthlyAssignments.GetActive(session.accountId)
                : null;
            HomeScreen.MonthlyEntryStatus monthlyStatus = HomeScreen.MonthlyEntryStatus.Disabled;
            if (assignment != null)
            {
                monthlyStatus = assignment.opened
                    ? HomeScreen.MonthlyEntryStatus.Open
                    : HomeScreen.MonthlyEntryStatus.New;
            }
            if (NativeMobileService.Available && nativeQuestionnaires.Any(IsPendingNativeQuestionnaire))
                monthlyStatus = HomeScreen.MonthlyEntryStatus.Open;

            return new HomeScreen.State
            {
                Name = session.accountId,
                Activated = session.profile != null && session.profile.activationBound,
                Calibrated = !string.IsNullOrEmpty(session.trackingCalibratedAt),
                MonthlyStatus = monthlyStatus,
            };
        }

        private GameObject CreateArTrainingPanel(Transform parent)
        {
            GameObject panel = CreatePanel("ArTrainingPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "AR 训练", new Vector2(0, 250), 34);
            CreateLabel(panel.transform, "连接投屏眼镜与 IMU 追踪器，进入本次训练流程。", new Vector2(0, 190), 17);
            arTrainingMessage = CreateLabel(panel.transform, "建议先完成追踪器校准，再开始训练。", new Vector2(0, 130), 16);
            arTrainingMessage.rectTransform.sizeDelta = new Vector2(820, 70);
            // The Unity V2 scene owns the AR preparation screen and its action
            // menu.  Enter it directly so the phone flow matches SpineFlowMobile.
            CreateButton(panel.transform, "开始训练", new Vector2(0, 40), () => StartTraining("SpineV2Minimal", "dead_bug"));
            CreateButton(panel.transform, "追踪器连接与校准", new Vector2(0, -50), () => bootstrap.Router.Navigate(AppRoute.TrackingCalibration));
            CreateButton(panel.transform, "动作示范库", new Vector2(0, -140), OpenActionLibrary);
            CreateButton(panel.transform, "返回训练首页", new Vector2(0, -230), () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private GameObject CreateTrainingMenuPanel(Transform parent)
        {
            GameObject panel = CreatePanel("TrainingMenuPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "选择训练内容", new Vector2(0, 250), 32);
            CreateLabel(panel.transform, "开始训练前请选择一种模式或测试项目。", new Vector2(0, 195), 17);
            trainingMenuMessage = CreateLabel(panel.transform, "训练模式会按当前处方进入指导训练。", new Vector2(0, 135), 16);
            trainingMenuMessage.rectTransform.sizeDelta = new Vector2(820, 70);
            CreateButton(panel.transform, "训练模式", new Vector2(0, 55), () => StartTraining("SpineV2Minimal", "dead_bug"));
            CreateButton(panel.transform, "动作示范库", new Vector2(0, -35), OpenActionLibrary);
            CreateButton(panel.transform, "返回 AR 训练", new Vector2(0, -305), () => bootstrap.Router.Navigate(AppRoute.Training));
            CreateButton(panel.transform, "回到主页", new Vector2(0, -395), () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private void ImportLegacyArchive()
        {
            if (!NativeMobileService.Available || bootstrap.State.Session == null) return;
            NativeMobileService.Call("openLegacyImport", bootstrap.State.Session.accountId);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused || !NativeMobileService.Available || bootstrap?.State?.Session == null) return;
            var user = bootstrap.Users.Load(bootstrap.State.Session.accountId);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(user), bootstrap.State.Session);
            RefreshHomeSummary();
            RefreshHistorySummary();
            RefreshDirectiveSilently();
            RefreshQuestionnairesSilently(true);
        }

        private void RefreshQuestionnairesSilently(bool openPending = false)
        {
            UserSession session = bootstrap?.State?.Session;
            if (!NativeMobileService.Available || NativeMobileService.Instance == null ||
                session?.profile?.activationBound != true) return;
            if (openPending && activeNativeQuestionnaire == null) questionnaireOpenRequested = true;
            if (questionnaireRefreshPending) return;
            string account = session.accountId;
            int sessionGeneration = loginGeneration;
            int generation = ++questionnaireRefreshGeneration;
            questionnaireRefreshPending = true;
            NativeMobileService.Instance.Request("routineRefresh", new NativeRequest { accountId = account }, reply =>
            {
                if (this == null || generation != questionnaireRefreshGeneration ||
                    sessionGeneration != loginGeneration || bootstrap?.State?.Session?.accountId != account) return;
                questionnaireRefreshPending = false;
                if (!reply.ok)
                {
                    Debug.LogWarning("[MobileBridge] Background questionnaire refresh did not complete.");
                    return;
                }
                ReadNativeQuestionnaires(reply);
                if (activeNativeQuestionnaire != null && !questionnaireBusy && !questionnaireSaving)
                {
                    var refreshed = nativeQuestionnaires.FirstOrDefault(task => task.taskId == activeNativeQuestionnaire.taskId);
                    if (refreshed != null)
                    {
                        activeNativeQuestionnaire = refreshed;
                        if (!string.IsNullOrEmpty(refreshed.completedAt)) CompleteNativeQuestionnaire();
                        else monthlyScreens.SetInteraction(!refreshed.pendingSubmission, true);
                    }
                }
                RefreshHomeSummary();
            });
        }

        private void RefreshDirectiveSilently()
        {
            UserSession session = bootstrap?.State?.Session;
            if (!NativeMobileService.Available || NativeMobileService.Instance == null ||
                session?.profile?.activationBound != true || nativeBusy || directiveRefreshPending) return;

            string account = session.accountId;
            int sessionGeneration = loginGeneration;
            int generation = ++directiveRefreshGeneration;
            directiveRefreshPending = true;
            // Native code persists the full snapshot. Refreshing it neither creates a
            // training session nor changes the existing page's messages or authorization.
            NativeMobileService.Instance.Request("refreshDirective", new NativeRequest { accountId = account }, reply =>
            {
                if (this == null || generation != directiveRefreshGeneration ||
                    sessionGeneration != loginGeneration || bootstrap?.State?.Session?.accountId != account) return;
                directiveRefreshPending = false;
                if (!reply.ok)
                    Debug.LogWarning("[MobileBridge] Background directive refresh did not complete.");
            });
        }

        [System.Serializable] private sealed class ImportedReplayList { public string[] items; }

        private void RequestSync()
        {
            NativeMobileService.Instance.Request("sync", new NativeRequest { accountId = bootstrap.State.Session?.accountId },
                reply => { if (homeMessage != null) homeMessage.text = reply.ok ? JsonUtility.FromJson<NativeMessage>(reply.dataJson).message : reply.error; });
        }

        private void RequestConfigUpdate()
        {
            NativeMobileService.Instance.Request("configUpdate", new NativeRequest { accountId = bootstrap.State.Session?.accountId },
                reply => { if (homeMessage != null) homeMessage.text = reply.ok ? JsonUtility.FromJson<NativeMessage>(reply.dataJson).message : reply.error; });
        }

        private void OpenActionLibrary()
        {
            if (!Application.CanStreamedLevelBeLoaded("CoachingChoose")) return;
            auxiliaryReturnRoute = bootstrap.State.Route;
            auxiliaryPageOpen = true;
            HideAppShell();
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene("CoachingChoose", LoadSceneMode.Single);
        }

        private void OpenReplay()
        {
            if (!Application.CanStreamedLevelBeLoaded("PlayBack")) return;
            var record = (bootstrap.State.Session?.trainingHistory ?? System.Array.Empty<TrainingHistoryRecord>())
                .Reverse().FirstOrDefault(row => !string.IsNullOrEmpty(row.replaySessionId));
            string replayId = record?.replaySessionId;
            if (string.IsNullOrEmpty(replayId) && NativeMobileService.Available)
            {
                string ids = NativeMobileService.Call<string>("importedReplayIds", bootstrap.State.Session.accountId);
                var imported = JsonUtility.FromJson<ImportedReplayList>("{\"items\":" + ids + "}");
                replayId = imported.items?.LastOrDefault();
            }
            if (string.IsNullOrEmpty(replayId))
            { historyMessage.text = "暂无本账号已完成的回放。"; return; }
            ReplaySessionContext.SetCommitted(replayId);
            auxiliaryReturnRoute = AppRoute.History;
            auxiliaryPageOpen = true;
            HideAppShell();
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene("PlayBack", LoadSceneMode.Single);
        }

        private GameObject CreateAdminPanel(Transform parent)
        {
            GameObject panel = CreatePanel("AdminPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "管理员配置（只读）", new Vector2(0, 220), 28);
            adminMessage = CreateLabel(panel.transform, "", new Vector2(0, 80), 16);
            adminMessage.rectTransform.sizeDelta = new Vector2(900, 260);
            CreateButton(panel.transform, "刷新已发布配置", new Vector2(0, -80), RefreshAdminSummary);
            CreateButton(panel.transform, "从当前版本创建草稿", new Vector2(0, -155), CreateAdminDraft);
            CreateButton(panel.transform, "标记 AR 测试通过", new Vector2(0, -230), MarkAdminArTestPassed);
            CreateButton(panel.transform, "发布当前草稿", new Vector2(0, -305), PublishAdminDraft);
            CreateButton(panel.transform, "回滚到最近归档版本", new Vector2(0, -380), RollbackAdminVersion);
            CreateButton(panel.transform, "返回登录", new Vector2(0, -455), () => bootstrap.State.ClearSession());
            return panel;
        }

        private GameObject CreateHistoryPanel(Transform parent)
        {
            GameObject panel = CreatePanel("HistoryPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "训练历史", new Vector2(0, 700), 28);
            var scrollObject = new GameObject("HistoryScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(panel.transform, false);
            var rect = scrollObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900, 850);
            rect.anchoredPosition = new Vector2(0, 170);
            scrollObject.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            historyMessage = CreateLabel(viewport, "", Vector2.zero, 16);
            historyMessage.rectTransform.anchorMin = historyMessage.rectTransform.anchorMax = new Vector2(0.5f, 1);
            historyMessage.rectTransform.pivot = new Vector2(0.5f, 1);
            historyMessage.rectTransform.sizeDelta = new Vector2(840, 0);
            historyMessage.alignment = TextAnchor.UpperLeft;
            historyMessage.verticalOverflow = VerticalWrapMode.Overflow;
            historyMessage.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = historyMessage.rectTransform;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            CreateButton(panel.transform, "查看最近完成的回放", new Vector2(0, -350), OpenReplay);
            CreateButton(panel.transform, "返回训练首页", new Vector2(0, -450), () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private const string TherapistContactPending = "暂未配置治疗师联系方式，请通过医院渠道联系你的治疗师。";

        private GameObject CreatePreTrainingBlockedPanel(Transform parent)
        {
            preTrainingBlockedScreen = new NoticeScreen();
            return preTrainingBlockedScreen.Build(parent, "PreTrainingBlockedPanel", "今天先不练了", "今天的记录",
                new[]
                {
                    new NoticeScreen.Row("静息疼痛", NoticeScreen.RowStyle.Badge),
                    new NoticeScreen.Row("疼痛部位", NoticeScreen.RowStyle.Muted),
                    new NoticeScreen.Row("训练状态", NoticeScreen.RowStyle.Muted),
                },
                "联系治疗师", "返回首页",
                () => bootstrap.Router.Navigate(AppRoute.Home),
                () => preTrainingBlockedScreen.SetNotice(TherapistContactPending),
                () => bootstrap.Router.Navigate(AppRoute.Home));
        }

        private GameObject CreatePostTrainingStopPanel(Transform parent)
        {
            postTrainingStopScreen = new NoticeScreen();
            return postTrainingStopScreen.Build(parent, "PostTrainingStopPanel", "这次先练到这里", "本次记录",
                new[]
                {
                    new NoticeScreen.Row("已完成动作", NoticeScreen.RowStyle.Strong),
                    new NoticeScreen.Row("训练前疼痛", NoticeScreen.RowStyle.Strong),
                    new NoticeScreen.Row("本次最高疼痛", NoticeScreen.RowStyle.Badge),
                    new NoticeScreen.Row("触发动作", NoticeScreen.RowStyle.Muted),
                    new NoticeScreen.Row("疼痛部位", NoticeScreen.RowStyle.Muted),
                },
                "结束训练", "联系治疗师",
                FinishAfterPainStop,
                FinishAfterPainStop,
                () => postTrainingStopScreen.SetNotice(TherapistContactPending));
        }

        private static string PartsText(string[] parts)
        {
            return parts == null || parts.Length == 0 ? "未选择" : string.Join("、", parts);
        }

        private static string DisplayActionName(TrainingPlan plan)
        {
            if (plan == null) return "本次动作";
            if (plan.actionId == "dead_bug") return "死虫式";
            if (plan.actionId == "bird_dog") return "鸟狗式";
            return string.IsNullOrEmpty(plan.actionId) ? "本次动作" : plan.actionId;
        }

        private void HandlePreTrainingBack()
        {
            pendingTrainingScene = "";
            pendingTrainingActionId = "";
            bootstrap.Router.Navigate(bootstrap.State.Session != null ? AppRoute.Home : AppRoute.Login);
        }

        private void HandlePreTrainingSubmitted(PreTrainingAnswers answers)
        {
            UserSession user = bootstrap.State.Session;
            if (user == null)
            {
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }

            string[] painParts = answers.ToPainParts();
            user.latestPainLevel = answers.Pain;
            user.preTrainingCheckPassed = answers.Pain < QuestionnaireUi.PainStopThreshold;
            user.preTrainingRecordedAt = System.DateTimeOffset.UtcNow.ToString("O");
            user.preTrainingEventId = "pre:" + user.preTrainingRecordedAt;
            HealthEventRecord.Append(user, "preTrainingQuestionnaire", user.preTrainingRecordedAt,
                new PreTrainingSnapshot
                {
                    prePainLevel = answers.Pain, allowedToTrain = user.preTrainingCheckPassed,
                    recordedAt = user.preTrainingRecordedAt,
                }, user.preTrainingEventId, answers.Regions, answers.Leg);
            bootstrap.Users.AppendPainRecord(user, new PainHistoryRecord
            {
                id = user.preTrainingEventId,
                recordedAt = user.preTrainingRecordedAt,
                painLevel = answers.Pain,
                source = "DailyQuestionnaire",
                painParts = painParts,
                pointsCaptured = false,
                painFree = answers.Pain == 0,
            });
            lastPreTrainingParts = painParts;

            if (!user.preTrainingCheckPassed)
            {
                pendingTrainingScene = "";
                pendingTrainingActionId = "";
                preTrainingBlockedScreen.Show(
                    "腰椎疼痛达到停训标准（≥ " + QuestionnaireUi.PainStopThreshold + " 分）\n先休息，疼痛缓解后再来",
                    new[] { answers.Pain + " / 10", PartsText(painParts), "未开始" });
                bootstrap.Router.Navigate(AppRoute.PreTrainingBlocked);
                return;
            }

            // Arrived from a training button: carry on into the session the
            // user asked for. Otherwise (e.g. the check right after login) go
            // back to the home screen like before.
            if (!string.IsNullOrEmpty(pendingTrainingScene))
            {
                string scene = pendingTrainingScene;
                string actionId = pendingTrainingActionId;
                pendingTrainingScene = "";
                pendingTrainingActionId = "";
                StartTraining(scene, actionId);
                return;
            }
            homeMessage.text = "训练前检查已记录。训练场景仍需设备校准与服务端授权后才能安全接入。";
            bootstrap.Router.Navigate(AppRoute.Home);
        }

        public void ShowPostTrainingSurvey()
        {
            TrainingPlan plan = bootstrap != null && bootstrap.Training != null ? bootstrap.Training.CurrentPlan : null;
            // The AR scene runs one action per launch today, so the survey that
            // follows it always belongs to the last (or only) action of the plan.
            postActionCount = plan != null && plan.actions != null ? Mathf.Max(1, plan.actions.Length) : 1;
            postActionNumber = postActionCount;
            if (postActionScreen != null) postActionScreen.Show(postActionNumber, postActionCount, DisplayActionName(plan));
            if (bootstrap != null && bootstrap.Router != null)
                bootstrap.Router.Navigate(AppRoute.PostTrainingSurvey);
        }

        private void HandlePostActionBack()
        {
            // Keep the durable session result; the next login resumes this survey.
            if (bootstrap.Training != null) bootstrap.Training.DeferFeedback();
            bootstrap.Router.Navigate(AppRoute.Home);
        }

        private void HandlePostActionSubmitted(int intensity, int pain)
        {
            UserSession user = bootstrap.State.Session;
            TrainingPlan plan = bootstrap.Training != null ? bootstrap.Training.CurrentPlan : null;
            bool stop = pain >= QuestionnaireUi.PainStopThreshold;
            string actionName = DisplayActionName(plan);
            // The training service owns the session record; hand the answers to
            // it so the result stays durable and replayable.
            if (bootstrap.Training != null)
            {
                bootstrap.Training.SubmitFeedback("运动强度 " + intensity + " / 5",
                    "疼痛 " + pain + " / 10" + (stop ? "（超标停训）" : ""));
            }

            if (!stop)
            {
                RefreshHistorySummary();
                bootstrap.Router.Navigate(AppRoute.History);
                return;
            }

            // Pain is over the limit: the next session needs a fresh pre-training check.
            int prePain = 0;
            if (user != null)
            {
                prePain = user.latestPainLevel;
                user.preTrainingCheckPassed = false;
                bootstrap.Users.Save(user);
            }
            postTrainingStopScreen.Show(
                "做完「" + actionName + "」后疼痛达到停训标准（≥ " + QuestionnaireUi.PainStopThreshold + " 分）\n后面的动作已取消，已完成的部分都记录好了",
                new[]
                {
                    postActionNumber + " / " + postActionCount,
                    prePain + " / 10",
                    Mathf.Max(prePain, pain) + " / 10",
                    actionName,
                    PartsText(lastPreTrainingParts),
                });
            bootstrap.Router.Navigate(AppRoute.PostTrainingStop);
        }

        private void FinishAfterPainStop()
        {
            RefreshHistorySummary();
            bootstrap.Router.Navigate(AppRoute.History);
        }

        private void ShowMonthlyQuestionnaire()
        {
            UserSession user = bootstrap.State.Session;
            if (user == null)
            {
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }

            if (NativeMobileService.Available)
            {
                RefreshQuestionnairesSilently(true);
                return;
            }

            MonthlyQuestionnaireAssignment assignment = bootstrap.MonthlyAssignments.GetActive(user.accountId);
            if (assignment == null)
            {
                return;
            }
            if (!assignment.opened)
            {
                assignment.opened = true;
                bootstrap.MonthlyAssignments.Save(user.accountId, assignment);
            }

            activeMonthlyModules = assignment.modules;
            monthlyScreens.SetJoaNextCaption(assignment.Includes(MonthlyQuestionnaireAssignment.ModuleSfMpq)
                ? "下一题"
                : "完成");
            monthlyScreens.Begin();
            if (assignment.Includes(MonthlyQuestionnaireAssignment.ModuleJoa))
            {
                bootstrap.Router.Navigate(AppRoute.MonthlyJoa);
            }
            else
            {
                bootstrap.Router.Navigate(AppRoute.MonthlySfmpq);
            }
        }

        private void HandleMonthlyJoaNext()
        {
            if (NativeMobileService.Available) { FinishMonthlyQuestionnaire(); return; }
            if ((activeMonthlyModules & MonthlyQuestionnaireAssignment.ModuleSfMpq) != 0)
            {
                bootstrap.Router.Navigate(AppRoute.MonthlySfmpq);
            }
            else
            {
                FinishMonthlyQuestionnaire();
            }
        }

        private void HandleMonthlySfmpqBack()
        {
            if (NativeMobileService.Available) { HandleMonthlyBack(); return; }
            if ((activeMonthlyModules & MonthlyQuestionnaireAssignment.ModuleJoa) != 0)
            {
                bootstrap.Router.Navigate(AppRoute.MonthlyJoa);
            }
            else
            {
                bootstrap.Router.Navigate(AppRoute.Home);
            }
        }

        private void FinishMonthlyQuestionnaire()
        {
            if (NativeMobileService.Available)
            {
                if (activeNativeQuestionnaire == null || questionnaireBusy) return;
                questionnaireBusy = true;
                monthlyScreens.SetInteraction(false, false);
                SaveNativeQuestionnaireDraft(SubmitNativeQuestionnaire);
                return;
            }
            UserSession user = bootstrap.State.Session;
            if (user == null)
            {
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }

            MonthlyQuestionnaireRecord record = monthlyScreens.CollectRecord();
            record.id = "monthly:" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            record.completedAt = System.DateTimeOffset.UtcNow.ToString("O");
            record.modules = activeMonthlyModules;

            MonthlyQuestionnaireAssignment assignment = bootstrap.MonthlyAssignments.GetActive(user.accountId);
            if (assignment != null)
            {
                record.assignmentId = assignment.assignmentId;
                assignment.status = MonthlyQuestionnaireAssignment.StatusCompleted;
                assignment.completedAt = record.completedAt;
                bootstrap.MonthlyAssignments.Save(user.accountId, assignment);
            }
            bootstrap.Users.AppendMonthlyRecord(user, record);
            homeMessage.text = "月度问卷已保存到本地。";
            bootstrap.Router.Navigate(AppRoute.Home);
        }

        private static bool IsPendingNativeQuestionnaire(NativeQuestionnaireTask task)
        {
            return task.taskId > 0 && string.IsNullOrEmpty(task.completedAt) &&
                string.IsNullOrEmpty(task.problem) && (task.availability == "available" || task.pendingSubmission) &&
                MonthlyQuestionnaireTaskAdapter.Supports(task);
        }

        private void ReadNativeQuestionnaires(NativeReply reply)
        {
            nativeQuestionnaires = JsonUtility.FromJson<NativeQuestionnaireTasks>(reply.dataJson)?.items
                ?? System.Array.Empty<NativeQuestionnaireTask>();
        }

        private bool OpenNextNativeQuestionnaire(bool first)
        {
            var pending = nativeQuestionnaires.Where(IsPendingNativeQuestionnaire)
                .OrderBy(task => task.availableAt, System.StringComparer.Ordinal).ThenBy(task => task.taskId).ToArray();
            if (pending.Length == 0) return false;
            if (first) { questionnaireStep = 1; questionnaireTotal = pending.Length; }
            activeNativeQuestionnaire = pending[0];
            activeMonthlyModules = MonthlyQuestionnaireTaskAdapter.Module(activeNativeQuestionnaire);
            monthlyScreens.Begin();
            monthlyScreens.Restore(MonthlyQuestionnaireTaskAdapter.Restore(activeNativeQuestionnaire));
            monthlyScreens.SetTaskProgress(activeMonthlyModules, questionnaireStep, questionnaireTotal);
            monthlyScreens.SetJoaNextCaption(pending.Length > 1 ? "下一题" : "完成");
            monthlyScreens.SetInteraction(!activeNativeQuestionnaire.pendingSubmission, true);
            bootstrap.Router.Navigate(activeMonthlyModules == MonthlyQuestionnaireAssignment.ModuleJoa
                ? AppRoute.MonthlyJoa : AppRoute.MonthlySfmpq);
            return true;
        }

        private void HandleMonthlyBack()
        {
            if (!NativeMobileService.Available) { bootstrap.Router.Navigate(AppRoute.Home); return; }
            if (activeNativeQuestionnaire == null || questionnaireBusy) return;
            questionnaireBusy = true;
            monthlyScreens.SetInteraction(false, false);
            SaveNativeQuestionnaireDraft(() =>
            {
                activeNativeQuestionnaire = null;
                questionnaireBusy = false;
                questionnaireOpenRequested = false;
                bootstrap.Router.Navigate(AppRoute.Home);
                RefreshHomeSummary();
            });
        }

        // Keep one local save in flight. Rapid choices coalesce into the latest complete draft.
        private void SaveNativeQuestionnaireDraft(System.Action afterSave = null)
        {
            if (activeNativeQuestionnaire == null) return;
            if (afterSave != null) questionnaireAfterSave = afterSave;
            questionnaireDraftDirty = true;
            if (questionnaireSaving) return;
            if (activeNativeQuestionnaire.pendingSubmission)
            {
                questionnaireDraftDirty = false;
                var next = questionnaireAfterSave;
                questionnaireAfterSave = null;
                next?.Invoke();
                return;
            }
            questionnaireDraftDirty = false;
            questionnaireSaving = true;
            long taskId = activeNativeQuestionnaire.taskId;
            int identity = sessionIdentityGeneration;
            string answers = MonthlyQuestionnaireTaskAdapter.AnswersJson(activeNativeQuestionnaire, monthlyScreens.CollectRecord());
            NativeMobileService.Instance.Request("routineSave", new NativeRequest
            { accountId = bootstrap.State.Session.accountId, taskId = taskId, answersJson = answers }, reply =>
            {
                if (this == null || identity != sessionIdentityGeneration || activeNativeQuestionnaire?.taskId != taskId) return;
                questionnaireSaving = false;
                if (!reply.ok)
                {
                    questionnaireAfterSave = null;
                    questionnaireBusy = false;
                    monthlyScreens.SetInteraction(!activeNativeQuestionnaire.pendingSubmission, true);
                    Debug.LogWarning("[MobileBridge] Questionnaire draft save did not complete.");
                    return;
                }
                ReadNativeQuestionnaires(reply);
                activeNativeQuestionnaire = nativeQuestionnaires.First(task => task.taskId == taskId);
                if (questionnaireDraftDirty) { SaveNativeQuestionnaireDraft(); return; }
                var next = questionnaireAfterSave;
                questionnaireAfterSave = null;
                next?.Invoke();
            });
        }

        private void SubmitNativeQuestionnaire()
        {
            long taskId = activeNativeQuestionnaire.taskId;
            int identity = sessionIdentityGeneration;
            NativeMobileService.Instance.Request("routineSubmit", new NativeRequest
            { accountId = bootstrap.State.Session.accountId, taskId = taskId }, reply =>
            {
                if (this == null || identity != sessionIdentityGeneration || activeNativeQuestionnaire?.taskId != taskId) return;
                questionnaireBusy = false;
                if (reply.ok)
                {
                    ReadNativeQuestionnaires(reply);
                    activeNativeQuestionnaire = nativeQuestionnaires.First(task => task.taskId == taskId);
                    if (!string.IsNullOrEmpty(activeNativeQuestionnaire.completedAt))
                    {
                        CompleteNativeQuestionnaire();
                        return;
                    }
                }
                // The native queue retains the exact request until the server confirms it.
                monthlyScreens.SetInteraction(!activeNativeQuestionnaire.pendingSubmission, true);
                Debug.LogWarning("[MobileBridge] Questionnaire submission is not confirmed yet.");
            });
        }

        private void CompleteNativeQuestionnaire()
        {
            activeNativeQuestionnaire = null;
            questionnaireBusy = false;
            questionnaireStep++;
            if (OpenNextNativeQuestionnaire(false)) return;
            questionnaireOpenRequested = false;
            bootstrap.Router.Navigate(AppRoute.Home);
            RefreshHomeSummary();
        }

        private void ShowHistory()
        {
            RefreshHistorySummary();
            bootstrap.Router.Navigate(AppRoute.History);
        }

        private void HandleRegisterClicked()
        {
            if (registerAccountInput == null || registerPasswordInput == null || registerConfirmInput == null) return;
            if (string.IsNullOrWhiteSpace(registerAccountInput.text) || string.IsNullOrWhiteSpace(registerPasswordInput.text))
            {
                loginMessage.text = "注册失败：账号和密码不能为空。";
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }
            if (registerPasswordInput.text != registerConfirmInput.text)
            {
                loginMessage.text = "注册失败：两次输入的密码不一致。";
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }
            AccountRegistrationResult result = bootstrap.Users.TryRegister(registerAccountInput.text, registerPasswordInput.text, out UserSession session);
            if (result != AccountRegistrationResult.Success)
            {
                loginMessage.text = result == AccountRegistrationResult.AccountAlreadyExists
                    ? "注册失败：账号已存在。" : "注册失败：请检查账号和密码。";
                bootstrap.Router.Navigate(AppRoute.Login);
                return;
            }
            bootstrap.State.SetSession(session);
            bootstrap.Training.RestoreForCurrentUser();
            activationCodeInput.text = "";
            activationMessage.text = "账号已创建，请绑定医生激活码，或选择稍后激活。";
            bootstrap.Router.Navigate(AppRoute.Activation);
        }

        private void HandleActivationClicked()
        {
            UserSession session = bootstrap.State.Session;
            string code = activationCodeInput == null ? "" : activationCodeInput.text.Trim();
            if (session == null) return;
            if (NativeMobileService.Available)
            {
                if (nativeBusy) return;
                string account = session.accountId;
                bool confirmed = previewActivationCode == code;
                nativeBusy = true;
                activationMessage.text = confirmed ? "正在绑定医生档案…" : "正在核对档案…";
                NativeMobileService.Instance.Request(confirmed ? "activate" : "previewActivation",
                    new NativeRequest { accountId = account, code = code }, reply =>
                    {
                        nativeBusy = false;
                        if (bootstrap.State.Session?.accountId != account) return;
                        if (!reply.ok) { activationMessage.text = reply.error; return; }
                        if (!confirmed)
                        {
                            previewActivationCode = code;
                            activationMessage.text = JsonUtility.FromJson<NativeMessage>(reply.dataJson).message + "；确认是本人后再次点击绑定。";
                            return;
                        }
                        previewActivationCode = null;
                        bootstrap.State.SetSession(JsonUtility.FromJson<UserSession>(reply.dataJson));
                        activationMessage.text = "医生档案绑定成功。";
                        NavigateWithProfile(AppRoute.Home);
                        RefreshDirectiveSilently();
                        RefreshQuestionnairesSilently(true);
                    });
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^1[3-9][0-9]{9}$"))
            {
                if (activationMessage != null) activationMessage.text = "激活失败：请输入有效的 11 位手机号。";
                return;
            }
            if (session.profile == null) session.profile = new UserProfile();
            session.profile.activationCode = code;
            session.profile.patientId = "local-patient-" + session.accountId;
            session.profile.activationBound = true;
            session.profile.activationBoundAt = System.DateTimeOffset.UtcNow.ToString("O");
            bootstrap.Users.Save(session);
            if (activationMessage != null) activationMessage.text = "激活成功，已绑定本地演示档案。";
            NavigateWithProfile(AppRoute.Home);
        }

        [System.Serializable]
        private sealed class NativeMessage { public string message; }

        private void SkipActivation()
        {
            NavigateWithProfile(AppRoute.Home);
        }

        [System.Serializable] private sealed class HistoryScore { public int overallScore; }

        private void RefreshHistorySummary()
        {
            UserSession session = bootstrap.State.Session;
            if (session == null || historyMessage == null) return;
            var pain = session.painHistory ?? System.Array.Empty<PainHistoryRecord>();
            var training = session.trainingHistory ?? System.Array.Empty<TrainingHistoryRecord>();
            var monthly = session.monthlyHistory ?? System.Array.Empty<MonthlyQuestionnaireRecord>();
            string monthlyLine = "月度问卷：" + monthly.Length + " 次" +
                (monthly.Length == 0 ? "" : "，最近一次 " + monthly[monthly.Length - 1].completedAt);
            List<string> trainingLines = new List<string>();
            foreach (TrainingHistoryRecord record in training)
            {
                string feedback = string.IsNullOrWhiteSpace(record.feedback) ? "" : "，反馈：" + record.feedback;
                var result = string.IsNullOrEmpty(record.resultJson) ? null : JsonUtility.FromJson<HistoryScore>(record.resultJson);
                string state = record.endReason == "LEGACY_IMPORT" ? "导入记录" : record.completed ? "完成" : string.IsNullOrEmpty(record.endReason) ? "进行中" : "中断";
                trainingLines.Add(record.completedAt + "  " + record.actionId + "  " + state +
                    (record.completed && result != null ? "  得分 " + result.overallScore : "") + feedback);
            }
            List<string> painLines = pain
                .OrderByDescending(record => record.recordedAt)
                .Take(8)
                .Select(record => record.recordedAt + "  疼痛 " + record.painLevel + "/10" +
                    (record.painParts == null || record.painParts.Length == 0 ? "" : "（" + string.Join("、", record.painParts) + "）"))
                .ToList();
            historyMessage.text = "疼痛记录：" + pain.Length + " 条\n" +
                (painLines.Count == 0 ? "暂无疼痛记录。" : string.Join("\n", painLines)) + "\n\n" +
                "训练记录：" + training.Length + " 条\n" +
                (trainingLines.Count == 0 ? "完成训练并提交结果后，这里会显示训练记录。" :
                    string.Join("\n", trainingLines)) + "\n\n" + monthlyLine;
        }

        private void RefreshAdminSummary()
        {
            if (adminMessage == null || bootstrap.AdminConfigs == null) return;
            var config = bootstrap.AdminConfigs.CurrentPublished();
            var actions = config.actions ?? System.Array.Empty<AdminActionDefinition>();
            List<string> actionLines = new List<string>();
            foreach (AdminActionDefinition action in actions)
            {
                actionLines.Add(action.displayName + " [" + action.actionId + "]");
            }
            var ruleLines = (config.recommendationRules ?? System.Array.Empty<AdminRecommendationRule>())
                .Where(rule => rule != null && rule.enabled)
                .OrderByDescending(rule => rule.priority)
                .Select(rule => rule.userTag + " → " + string.Join("、", rule.actionIds ?? System.Array.Empty<string>()))
                .ToArray();
            adminMessage.text = "当前版本：" + config.versionName + "（" + config.state + "）\n" +
                "配置 ID：" + config.configId + "\n" +
                "动作：" + string.Join("、", actionLines) + "\n" +
                "推荐规则：" + (ruleLines.Length == 0 ? "暂无" : string.Join("；", ruleLines)) + "\n" +
                "说明：患者侧只读取 Published 配置；发布前必须完成 AR 测试。";
        }

        private AdminTrainingConfiguration LatestAdminDraft()
        {
            return bootstrap.AdminConfigs?.ListConfigurations()
                .FirstOrDefault(config => config.state == AdminConfigState.Draft || config.state == AdminConfigState.ArTestPassed);
        }

        private void CreateAdminDraft()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            repository.CreateDraftFromPublished();
            RefreshAdminSummary();
            if (adminMessage != null) adminMessage.text += "\n已创建草稿，可先执行 AR 测试。";
        }

        private void MarkAdminArTestPassed()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            AdminTrainingConfiguration draft = LatestAdminDraft();
            bool passed = draft != null && repository.MarkArTestPassed(draft.configId, "unity-demo-ar-proof-" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            RefreshAdminSummary();
            if (adminMessage != null) adminMessage.text += passed ? "\nAR 测试已通过，可发布。" : "\n没有可测试的草稿。请先创建草稿。";
        }

        private void PublishAdminDraft()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            AdminTrainingConfiguration draft = LatestAdminDraft();
            bool published = draft != null && repository.Publish(draft.configId);
            RefreshAdminSummary();
            if (adminMessage != null) adminMessage.text += published ? "\n配置已发布，患者侧将读取此版本。" : "\n发布失败：必须先完成 AR 测试。";
        }

        private void RollbackAdminVersion()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            AdminTrainingConfiguration archived = bootstrap.AdminConfigs.ListConfigurations()
                .Where(config => config.state == AdminConfigState.Archived)
                .OrderByDescending(config => config.updatedAt)
                .FirstOrDefault();
            bool rolledBack = archived != null && repository.RollbackTo(archived.configId);
            RefreshAdminSummary();
            if (adminMessage != null) adminMessage.text += rolledBack ? "\n已回滚到归档版本。" : "\n没有可回滚的归档版本。";
        }

        private GameObject CreateTrackingCalibrationPanel(Transform parent)
        {
            GameObject panel = CreatePanel("TrackingCalibrationPanel", parent, new Color(0.973f, 0.980f, 1f));
            CreateLabel(panel.transform, "IMU 追踪器校准", new Vector2(0, 185), 28);
            CreateLabel(panel.transform,
                "启动内嵌 SlimeVR，按引导连接追踪器并完成校准。返回后请确认校准状态。",
                new Vector2(0, 115), 15);
            trackingMessage = CreateLabel(panel.transform, "校准尚未开始", new Vector2(0, 55), 15);
            CreateButton(panel.transform, "打开 SlimeVR 校准清单", new Vector2(0, -15), OpenEmbeddedSlimeVrCalibration);
            trackingCompleteButton = CreateButton(panel.transform, "我已完成校准，继续", new Vector2(0, -85), ConfirmTrackingCalibration);
            trackingCompleteButton.interactable = false;
            CreateButton(panel.transform, "返回训练首页", new Vector2(0, -155), () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private void OpenEmbeddedSlimeVrCalibration()
        {
            int height = bootstrap.State.Session != null ? bootstrap.State.Session.profile.heightCm : 0;
            string error;
            if (!EmbeddedSlimeVrBridge.TryOpenCalibration(height, out error))
            {
                trackingMessage.text = error;
                return;
            }

            trackingCompleteButton.interactable = true;
            trackingMessage.text = "SlimeVR 已打开。完成校准后返回此页面，再点击确认。";
        }

        private void ConfirmTrackingCalibration()
        {
            UserSession session = bootstrap.State.Session;
            if (session != null)
            {
                session.trackingCalibratedAt = System.DateTimeOffset.UtcNow.ToString("O");
                bootstrap.Users.Save(session);
            }
            if (trackingMessage != null)
                trackingMessage.text = EmbeddedSlimeVrBridge.TryReadState(out var tracking)
                    ? "SlimeVR：" + tracking.state + "；在线追踪器：" + tracking.trackers.Count(t => t.connected) +
                        "；骨骼输出：" + (tracking.vmcSkeletonOutputFresh ? "正在更新" : "等待数据")
                    : "暂时无法读取追踪器状态，可以重新打开校准页检查。";
            bootstrap.Router.Navigate(AppRoute.Home);
        }

        private void HandleLoginClicked()
        {
            if (nativeBusy) return;
            if (NativeMobileService.Available)
            {
                nativeBusy = true;
                int generation = ++loginGeneration;
                loginMessage.text = "正在登录…";
                NativeMobileService.Instance.Request("login", new NativeRequest
                { accountId = accountInput.text, password = passwordInput.text }, reply =>
                {
                    nativeBusy = false;
                    if (generation != loginGeneration) return;
                    if (!reply.ok) { loginMessage.text = reply.error; return; }
                    HandleAuthenticated(JsonUtility.FromJson<UserSession>(reply.dataJson));
                });
                return;
            }
            if (bootstrap.Users.TryAuthenticateAdministrator(accountInput.text, passwordInput.text, out UserSession admin))
            {
                bootstrap.State.SetSession(admin);
                RefreshAdminSummary();
                bootstrap.Router.Navigate(AppRoute.Admin);
                return;
            }
            if (!bootstrap.Users.TryAuthenticate(accountInput.text, passwordInput.text, out UserSession session))
            {
                loginMessage.text = "登录失败：患者账号为 new1–new10 / old1–old10，密码 123；原管理员为 admin / admin。";
                loginMessage.color = new Color(1f, 0.55f, 0.45f);
                return;
            }
            HandleAuthenticated(session);
        }

        private void HandleAuthenticated(UserSession session)
        {
            bootstrap.State.SetSession(session);
            RefreshHomeSummary();
            if (session.IsReturningUser)
            {
                if (!NativeMobileService.Available && session.profile.ageYears <= 0)
                {
                    session.profile.gender = "男"; session.profile.ageYears = 35; session.profile.heightCm = 175; session.profile.weightKg = 70;
                    if (session.latestPainLevel <= 0) session.latestPainLevel = 3;
                    bootstrap.Users.Save(session);
                }
                session.preTrainingCheckPassed = false;
                bootstrap.Users.Save(session);
                pendingTrainingScene = "";
                pendingTrainingActionId = "";
                NavigateWithProfile(AppRoute.PreTraining);
            }
            else
            {
                NavigateWithProfile(AppRoute.Home);
            }
            if (bootstrap.State.Route != AppRoute.BasicProfile)
                bootstrap.Training.RestoreForCurrentUser();
            RefreshDirectiveSilently();
            RefreshQuestionnairesSilently(true);
        }

        private void NavigateWithProfile(AppRoute destination)
        {
            UserProfile profile = bootstrap.State.Session.profile;
            string gender = profile?.gender;
            bool male = gender == "男" || gender == "male";
            bool female = gender == "女" || gender == "female";
            if (profile != null && (male || female) &&
                profile.ageYears >= 10 && profile.ageYears <= 100 &&
                profile.heightCm >= 120 && profile.heightCm <= 220 &&
                profile.weightKg >= 35 && profile.weightKg <= 200)
            {
                bootstrap.Router.Navigate(destination);
                return;
            }

            profileContinueRoute = destination;
            profileInputs[0].text = profile != null && profile.ageYears > 0 ? profile.ageYears.ToString() : "";
            profileInputs[1].text = profile != null && profile.heightCm > 0 ? profile.heightCm.ToString() : "";
            profileInputs[2].text = profile != null && profile.weightKg > 0 ? profile.weightKg.ToString() : "";
            selectedGender = male ? "男" : female ? "女" : "";
            SetSelectedChoice(genderButtons, male ? 0 : female ? 1 : -1);
            flowMessages[AppRoute.BasicProfile].text = "";
            bootstrap.Router.Navigate(AppRoute.BasicProfile);
        }

        private GameObject CreateFormPanel(Transform parent, string title, string subtitle, System.Action<Transform> build)
        {
            GameObject panel = CreatePanel(title + "Panel", parent, new Color(0.973f, 0.980f, 1f));
            bool admission = title == "首次动作评估";
            Text heading = CreateLabel(panel.transform, title, new Vector2(0, admission ? 700 : 330), admission ? 36 : 28);
            if (admission) heading.fontStyle = FontStyle.Bold;
            Text description = CreateLabel(panel.transform, subtitle, new Vector2(0, admission ? 640 : 275), admission ? 18 : 15);
            if (admission) description.fontStyle = FontStyle.Bold;
            flowMessage = CreateLabel(panel.transform, "", new Vector2(0, admission ? -680 : -360), admission ? 18 : 14);
            AppRoute route = title == "资料登记" ? AppRoute.BasicProfile : title == "基础问卷" ? AppRoute.BasicSurvey :
                title == "疼痛记录" ? AppRoute.PainSelect : title == "首次动作评估" ? AppRoute.MobileAdmission :
                title == "需要专业评估" ? AppRoute.AdmissionDecision : AppRoute.PreTraining;
            flowMessages[route] = flowMessage;
            build(panel.transform);
            return panel;
        }

        private void CreateProfileForm(Transform parent)
        {
            profileInputs = new[] {
                CreateInput(parent, "年龄（岁）", new Vector2(0, 180), false),
                CreateInput(parent, "身高（cm）", new Vector2(0, 105), false),
                CreateInput(parent, "体重（kg）", new Vector2(0, 30), false),
            };
            genderButtons = new[]
            {
                CreateButton(parent, "性别：男", new Vector2(-145, -50), () => SelectGender("男", 0), 250),
                CreateButton(parent, "性别：女", new Vector2(145, -50), () => SelectGender("女", 1), 250),
            };
            SetSelectedChoice(genderButtons, -1);
            CreateButton(parent, "保存并继续", new Vector2(0, -145), SaveProfile);
        }

        private void SelectGender(string gender, int selectedIndex)
        {
            selectedGender = gender;
            SetSelectedChoice(genderButtons, selectedIndex);
        }

        private void SaveProfile()
        {
            if (!int.TryParse(profileInputs[0].text, out int age) || age < 10 || age > 100 ||
                !int.TryParse(profileInputs[1].text, out int height) || height < 120 || height > 220 ||
                !int.TryParse(profileInputs[2].text, out int weight) || weight < 35 || weight > 200 || string.IsNullOrEmpty(selectedGender))
            { ShowFlowMessage("资料无效：请检查年龄、身高、体重范围并选择性别。"); return; }
            UserSession user = bootstrap.State.Session;
            if (user.profile == null) user.profile = new UserProfile();
            user.profile.ageYears = age; user.profile.heightCm = height; user.profile.weightKg = weight; user.profile.gender = selectedGender;
            bootstrap.Users.Save(user);
            RefreshHomeSummary();
            bootstrap.Router.Navigate(profileContinueRoute);
            bootstrap.Training.RestoreForCurrentUser();
        }

        private void CreateSurveyForm(Transform parent)
        {
            situationButtons = new[]
            {
                CreateButton(parent, "情况：预防 / 保持健康", new Vector2(0, 195), () => SelectSituation(0)),
                CreateButton(parent, "情况：曾有相关问题", new Vector2(0, 125), () => SelectSituation(1)),
                CreateButton(parent, "情况：目前急性不适", new Vector2(0, 55), () => SelectSituation(2)),
            };
            symptomButtons = new[]
            {
                CreateButton(parent, "症状：没感觉", new Vector2(0, -25), () => SelectSymptom(0)),
                CreateButton(parent, "症状：腰疼 / 腿疼 / 腿麻 / 放射痛", new Vector2(0, -95), () => SelectSymptom(1)),
                CreateButton(parent, "症状：无力（暂停流程）", new Vector2(0, -165), () => SelectSymptom(2)),
            };
            surveySelectionLabel = CreateLabel(parent, "当前选择：尚未选择", new Vector2(0, -315), 14);
            CreateButton(parent, "保存并继续", new Vector2(0, -245), SaveSurvey);
            RefreshSurveySelection();
        }

        private void SelectSituation(int index)
        {
            selectedSituation = index == 0 ? "prevention" : index == 1 ? "history" : "acute";
            RefreshSurveySelection();
        }

        private void SelectSymptom(int index)
        {
            selectedSymptom = index == 0 ? "无感觉" : index == 1 ? "疼痛或麻木" : "无力";
            RefreshSurveySelection();
        }

        private void RefreshSurveySelection()
        {
            SetSelectedChoice(situationButtons, selectedSituation == "prevention" ? 0 : selectedSituation == "history" ? 1 : selectedSituation == "acute" ? 2 : -1);
            SetSelectedChoice(symptomButtons, selectedSymptom == "无感觉" ? 0 : selectedSymptom == "疼痛或麻木" ? 1 : selectedSymptom == "无力" ? 2 : -1);
            if (surveySelectionLabel != null)
                surveySelectionLabel.text = "当前选择：" + GetSituationLabel(selectedSituation) + "；" + (string.IsNullOrEmpty(selectedSymptom) ? "未选症状" : selectedSymptom);
        }

        private static string GetSituationLabel(string situation)
        {
            switch (situation)
            {
                case "prevention": return "预防 / 保持健康";
                case "history": return "曾有相关问题";
                case "acute": return "目前急性不适";
                default: return "未选情况";
            }
        }

        private static void SetSelectedChoice(Button[] choices, int selectedIndex)
        {
            if (choices == null) return;
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null && choices[i].targetGraphic != null)
                {
                    Color normal = i == selectedIndex ? new Color(0.12f, 0.39f, 0.78f) : new Color(0.22f, 0.74f, 0.97f);
                    ColorBlock colors = choices[i].colors;
                    colors.normalColor = normal;
                    colors.highlightedColor = i == selectedIndex ? new Color(0.18f, 0.47f, 0.86f) : new Color(0.30f, 0.78f, 0.98f);
                    colors.pressedColor = new Color(0.10f, 0.32f, 0.68f);
                    colors.selectedColor = normal;
                    choices[i].colors = colors;
                }
            }
        }

        private void SaveSurvey()
        {
            if (string.IsNullOrEmpty(selectedSituation) || string.IsNullOrEmpty(selectedSymptom)) { ShowFlowMessage("请先选择当前情况和症状。"); return; }
            UserSession user = bootstrap.State.Session;
            user.basicSituation = selectedSituation; user.symptoms = new[] { selectedSymptom };
            user.userTags = BuildUserTags(user);
            user.intakeReviewRequired = selectedSymptom == "无力" || selectedSituation == "acute";
            HealthEventRecord.Append(user, "admissionIntake", System.DateTimeOffset.UtcNow.ToString("O"),
                new AdmissionIntakeSnapshot
                {
                    situation = selectedSituation, symptom = selectedSymptom,
                    goals = (string[])(user.goals ?? System.Array.Empty<string>()).Clone(),
                });
            if (selectedSymptom == "无力" || selectedSituation == "acute")
            {
                user.intakeReviewRequired = true;
                bootstrap.Users.Save(user);
                bootstrap.Router.Navigate(AppRoute.AdmissionDecision);
                return;
            }
            bootstrap.Users.Save(user); bootstrap.Router.Navigate(AppRoute.PainSelect);
        }

        private void CreatePainForm(Transform parent)
        {
            string[] parts = { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" };
            string[] keys = { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" };
            painPartButtons = new Button[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string key = keys[i];
                painPartButtons[i] = CreateButton(parent, parts[i], new Vector2((i % 2 == 0 ? -145 : 145), 185 - (i / 2) * 65), () => TogglePainPart(key), 250);
            }
            noPainButton = CreateButton(parent, "没有疼痛", new Vector2(0, -72), SelectNoPain);
            selectedPainPartLabel = CreateLabel(parent, "已选部位：未选择（可多选）", new Vector2(0, -130), 15);
            selectedPainPartLabel.rectTransform.sizeDelta = new Vector2(760, 38);
            painScoreLabel = CreateLabel(parent, "选中部位后设置疼痛程度：3 / 10", new Vector2(0, -180), 16);
            CreateButton(parent, "疼痛评分 −", new Vector2(-145, -238), () => ChangePainScore(-1), 250);
            CreateButton(parent, "疼痛评分 +", new Vector2(145, -238), () => ChangePainScore(1), 250);
            CreateButton(parent, "记录并继续", new Vector2(0, -305), SavePain);
            flowMessage.rectTransform.anchoredPosition = new Vector2(0, -390);
            RefreshPainSelectionUi(keys, parts);
        }

        private void SavePain()
        {
            if (selectedPainParts.Count == 0 && !painFree)
            {
                ShowFlowMessage("请选择疼痛部位，或选择“没有疼痛”。");
                return;
            }

            UserSession user = bootstrap.State.Session;
            user.painParts = selectedPainParts.ToArray();
            user.painLevels = new int[selectedPainParts.Count];
            for (int i = 0; i < selectedPainParts.Count; i++)
                user.painLevels[i] = painLevelsByPart[selectedPainParts[i]];
            user.painFree = painFree;
            user.latestPainLevel = user.painLevels.Length > 0 ? Mathf.Max(user.painLevels) : 0;
            user.userTags = BuildUserTags(user);
            user.intakeCompleted = true;
            string recordedAt = System.DateTimeOffset.UtcNow.ToString("O");
            HealthEventRecord.Append(user, "admissionSurvey", recordedAt, new AdmissionSurveySnapshot
            {
                painLevel = user.latestPainLevel, requiresClinicalReview = user.intakeReviewRequired,
            });
            string[] pointKeys = { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" };
            string[] pointLabels = { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" };
            var points = new PainPointSnapshot[selectedPainParts.Count];
            for (int i = 0; i < points.Length; i++)
            {
                int labelIndex = System.Array.IndexOf(pointKeys, selectedPainParts[i]);
                points[i] = new PainPointSnapshot
                {
                    bodyPart = selectedPainParts[i], bodyPartLabel = pointLabels[labelIndex],
                    painLevel = painLevelsByPart[selectedPainParts[i]],
                };
            }
            bootstrap.Users.AppendPainRecord(user, new PainHistoryRecord
            {
                id = "intake:" + System.Guid.NewGuid().ToString("N"),
                recordedAt = recordedAt,
                painLevel = user.latestPainLevel,
                source = "AdmissionQuestionnaire",
                painParts = (string[])user.painParts.Clone(),
                points = points, pointsCaptured = true, painFree = user.painFree,
            });
            bootstrap.Router.Navigate(AppRoute.MobileAdmission);
        }

        private static string[] BuildUserTags(UserSession user)
        {
            if (user == null) return System.Array.Empty<string>();
            var tags = new List<string>();
            if (user.latestPainLevel >= 7) tags.Add("准入疼痛较高");
            else if (user.latestPainLevel > 0 && user.latestPainLevel <= 3) tags.Add("腰部疼痛指数低");
            if (user.flexionResponse == "relieved") tags.Add("屈曲方向偏好");
            if (user.extensionResponse == "relieved") tags.Add("伸展方向偏好");
            return tags.Distinct().ToArray();
        }

        private void TogglePainPart(string partKey)
        {
            painFree = false;
            if (selectedPainParts.Contains(partKey) && selectedPainPart == partKey)
            {
                selectedPainParts.Remove(partKey);
                painLevelsByPart.Remove(partKey);
                selectedPainPart = selectedPainParts.Count > 0 ? selectedPainParts[selectedPainParts.Count - 1] : "";
            }
            else if (selectedPainParts.Contains(partKey))
            {
                selectedPainPart = partKey;
                selectedPain = painLevelsByPart[partKey];
            }
            else
            {
                selectedPainParts.Add(partKey);
                painLevelsByPart[partKey] = selectedPain;
                selectedPainPart = partKey;
            }

            if (!string.IsNullOrEmpty(selectedPainPart) && painLevelsByPart.ContainsKey(selectedPainPart))
                selectedPain = painLevelsByPart[selectedPainPart];
            RefreshPainSelectionUi(
                new[] { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" },
                new[] { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" });
        }

        private void SelectNoPain()
        {
            selectedPainParts.Clear();
            painLevelsByPart.Clear();
            selectedPainPart = "";
            painFree = true;
            RefreshPainSelectionUi(
                new[] { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" },
                new[] { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" });
        }

        private void RefreshPainSelectionUi(string[] keys, string[] labels)
        {
            List<string> selectedLabels = new List<string>();
            for (int i = 0; i < painPartButtons.Length; i++)
            {
                bool selected = selectedPainParts.Contains(keys[i]);
                bool active = selected && keys[i] == selectedPainPart;
                Color normal = active ? new Color(0.10f, 0.37f, 0.78f) : selected ? new Color(0.20f, 0.58f, 0.88f) : new Color(0.91f, 0.95f, 0.99f);
                Color highlighted = active ? new Color(0.14f, 0.43f, 0.84f) : selected ? new Color(0.30f, 0.67f, 0.92f) : new Color(0.83f, 0.91f, 0.98f);
                SetButtonColors(painPartButtons[i], normal, highlighted, active ? Color.white : selected ? Color.white : new Color(0.10f, 0.18f, 0.30f));
                if (selected)
                    selectedLabels.Add(labels[i] + "（" + painLevelsByPart[keys[i]] + "/10）");
            }

            selectedPainPartLabel.text = selectedLabels.Count == 0
                ? painFree ? "已选部位：没有疼痛" : "已选部位：未选择（可多选）"
                : "已选部位：" + string.Join("、", selectedLabels);
            painScoreLabel.text = string.IsNullOrEmpty(selectedPainPart)
                ? painFree ? "没有疼痛，无需设置评分" : "选中部位后设置疼痛程度：" + selectedPain + " / 10"
                : "当前编辑：" + labels[System.Array.IndexOf(keys, selectedPainPart)] + "，疼痛程度：" + selectedPain + " / 10";
            if (noPainButton != null)
                SetButtonColors(noPainButton, painFree ? new Color(0.10f, 0.37f, 0.78f) : new Color(0.91f, 0.95f, 0.99f), new Color(0.30f, 0.67f, 0.92f), painFree ? Color.white : new Color(0.10f, 0.18f, 0.30f));
        }

        private void ChangePainScore(int delta)
        {
            if (string.IsNullOrEmpty(selectedPainPart))
            {
                ShowFlowMessage("请先选择要记录疼痛程度的部位。");
                return;
            }
            selectedPain = Mathf.Clamp(selectedPain + delta, 1, 10);
            painLevelsByPart[selectedPainPart] = selectedPain;
            RefreshPainSelectionUi(
                new[] { "left_waist", "right_waist", "left_upper_arm", "right_upper_arm", "left_hip", "right_hip", "left_leg", "right_leg" },
                new[] { "左腰部", "右腰部", "左大臂", "右大臂", "左臀部", "右臀部", "左腿", "右腿" });
        }

        private static void SetButtonColors(Button button, Color normal, Color highlighted, Color textColor)
        {
            if (button == null) return;
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = new Color(0.08f, 0.30f, 0.66f);
            colors.selectedColor = normal;
            button.colors = colors;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = textColor;
        }

        private void CreateAdmissionForm(Transform parent)
        {
            const float verticalShift = 180f;
            flexionResponseButtons = CreateMovementAssessment(parent, true, 225f + verticalShift, "测试项 1 · 站立位前屈（FIS）", "双腿伸直，连续尝试用手摸脚尖；按产品流程完成 10 次。", new Color(0.22f, 0.55f, 0.91f));
            flexionPhaseButtons = lastCreatedPhaseButtons;
            extensionResponseButtons = CreateMovementAssessment(parent, false, -285f + verticalShift, "测试项 2 · 站立位后伸（EIS）", "双手托腰，以手为支点连续后仰；按产品流程完成 10 次。", new Color(0.08f, 0.61f, 0.56f));
            extensionPhaseButtons = lastCreatedPhaseButtons;
            Button finishButton = CreateButton(parent, "完成首次评估", new Vector2(0, -645), FinishAdmission, 560, 64);
            Text finishLabel = finishButton.GetComponentInChildren<Text>();
            if (finishLabel != null) { finishLabel.fontSize = 26; finishLabel.fontStyle = FontStyle.Bold; }
            flowMessage.rectTransform.anchoredPosition = new Vector2(0, -720);
        }

        private Button[] lastCreatedPhaseButtons;

        private Button[] CreateMovementAssessment(Transform parent, bool flexion, float top, string title, string instruction, Color accent)
        {
            Text countLabel = CreateLabel(parent, "实际完成：0 次", new Vector2(-100, top + 52), 18);
            countLabel.rectTransform.sizeDelta = new Vector2(340, 34);
            System.Action<int> updateCount = delta =>
            {
                int count = Mathf.Clamp((flexion ? flexionCompletedCount : extensionCompletedCount) + delta, 0, 10);
                if (flexion) flexionCompletedCount = count; else extensionCompletedCount = count;
                countLabel.text = "实际完成：" + count + " 次";
            };
            CreateButton(parent, "−", new Vector2(170, top + 52), () => updateCount(-1), 80, 38);
            CreateButton(parent, "+", new Vector2(270, top + 52), () => updateCount(1), 80, 38);
            Text sectionTitle = CreateLabel(parent, title, new Vector2(0, top), 25);
            sectionTitle.fontStyle = FontStyle.Bold;
            sectionTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            Text repetitions = CreateLabel(parent, instruction, new Vector2(0, top - 46), 18);
            repetitions.fontStyle = FontStyle.Bold;
            repetitions.rectTransform.sizeDelta = new Vector2(760, 34);
            Text responseQuestion = CreateLabel(parent, "动作后的疼痛反应", new Vector2(0, top - 82), 20);
            responseQuestion.fontStyle = FontStyle.Bold;
            responseQuestion.rectTransform.sizeDelta = new Vector2(760, 34);

            string[] responses = { "是，明显缓解", "否，无变化", "加重" };
            Button[] responseButtons = new Button[responses.Length];
            for (int i = 0; i < responses.Length; i++)
            {
                int responseIndex = i;
                responseButtons[i] = CreateButton(parent, responses[i], new Vector2(0, top - 120 - i * 44),
                    () => SelectAdmissionResponse(flexion, responseIndex), 560, 40);
                Text responseLabel = responseButtons[i].GetComponentInChildren<Text>();
                if (responseLabel != null) { responseLabel.fontSize = 22; responseLabel.fontStyle = FontStyle.Bold; }
                SetButtonColors(responseButtons[i], new Color(0.95f, 0.97f, 0.99f), new Color(0.88f, 0.93f, 0.98f), new Color(0.16f, 0.20f, 0.26f));
            }

            Text phaseTitle = CreateLabel(parent, "补充观察（可选）：疼痛最先出现阶段", new Vector2(0, top - 266), 18);
            phaseTitle.fontStyle = FontStyle.Bold;
            phaseTitle.rectTransform.sizeDelta = new Vector2(760, 30);
            string[] phases = { "起始阶段", "中间阶段", "末端阶段", "全程无痛" };
            Button[] phaseButtons = new Button[phases.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                int phaseIndex = i;
                Vector2 pos = new Vector2(i % 2 == 0 ? -145 : 145, top - 302 - (i / 2) * 42);
                phaseButtons[i] = CreateButton(parent, phases[i], pos,
                    () => SelectAdmissionPhase(flexion, phaseIndex), 250, 38);
                Text phaseLabel = phaseButtons[i].GetComponentInChildren<Text>();
                if (phaseLabel != null) { phaseLabel.fontSize = 20; phaseLabel.fontStyle = FontStyle.Bold; }
                SetButtonColors(phaseButtons[i], new Color(0.95f, 0.97f, 0.99f), new Color(0.88f, 0.93f, 0.98f), new Color(0.16f, 0.20f, 0.26f));
            }

            Text scoreLabel = CreateLabel(parent, "疼痛程度：未评分 / 10", new Vector2(0, top - 394), 20);
            scoreLabel.fontStyle = FontStyle.Bold;
            scoreLabel.rectTransform.sizeDelta = new Vector2(760, 32);
            CreatePainSlider(parent, new Vector2(0, top - 438), accent,
                value => SetAdmissionPain(flexion, value));
            Text lowLabel = CreateLabel(parent, "1 轻微", new Vector2(-250, top - 468), 16);
            lowLabel.rectTransform.sizeDelta = new Vector2(100, 28);
            lowLabel.alignment = TextAnchor.MiddleLeft;
            lowLabel.color = new Color(0.42f, 0.48f, 0.56f);
            Text highLabel = CreateLabel(parent, "10 剧痛", new Vector2(250, top - 468), 16);
            highLabel.rectTransform.sizeDelta = new Vector2(100, 28);
            highLabel.alignment = TextAnchor.MiddleRight;
            highLabel.color = new Color(0.42f, 0.48f, 0.56f);
            if (flexion)
            {
                flexionScoreLabel = scoreLabel;
            }
            else
            {
                extensionScoreLabel = scoreLabel;
            }

            lastCreatedPhaseButtons = phaseButtons;
            return responseButtons;
        }

        private Text flexionScoreLabel;
        private Text extensionScoreLabel;

        private void FinishAdmission()
        {
            if (string.IsNullOrEmpty(flexionResponse) || string.IsNullOrEmpty(extensionResponse) ||
                !flexionPainRated || !extensionPainRated)
            {
                ShowFlowMessage("请分别完成 FIS 和 EIS 的疼痛反应选择与疼痛评分。疼痛最先出现阶段为可选项。");
                return;
            }

            if (flexionCompletedCount < 5 || extensionCompletedCount < 5)
            {
                ShowFlowMessage("请记录两个测试实际完成的次数（每项5至10次），未完成时可以稍后继续。");
                return;
            }
            UserSession user = bootstrap.State.Session;
            user.flexionCompletions = flexionCompletedCount;
            user.extensionCompletions = extensionCompletedCount;
            user.flexionPainLevel = flexionAdmissionPain;
            user.extensionPainLevel = extensionAdmissionPain;
            user.flexionResponse = flexionResponse;
            user.extensionResponse = extensionResponse;
            user.flexionPainPhase = flexionPainPhase;
            user.extensionPainPhase = extensionPainPhase;
            user.admissionCompletedAt = System.DateTimeOffset.UtcNow.ToString("O");
            List<string> tags = new List<string>(user.userTags ?? System.Array.Empty<string>());
            tags.RemoveAll(tag => tag == "屈曲方向偏好" || tag == "伸展方向偏好");
            if (extensionResponse == "relieved" && flexionResponse != "relieved") tags.Add("伸展方向偏好");
            else if (flexionResponse == "relieved" && extensionResponse != "relieved") tags.Add("屈曲方向偏好");
            user.userTags = tags.ToArray();
            user.admissionReviewRequired = Mathf.Max(flexionAdmissionPain, extensionAdmissionPain) >= 8;
            user.admissionCompleted = true;
            HealthEventRecord.Append(user, "mobileAdmissionAssessment", user.admissionCompletedAt,
                new AdmissionAssessmentSnapshot
                {
                    completedAt = user.admissionCompletedAt,
                    flexion = new AdmissionMovementSnapshot
                    {
                        movementType = "fis", completionCount = flexionCompletedCount,
                        painResponse = flexionResponse, firstPainPhase = flexionPainPhase,
                        painLevel = flexionAdmissionPain,
                    },
                    extension = new AdmissionMovementSnapshot
                    {
                        movementType = "eis", completionCount = extensionCompletedCount,
                        painResponse = extensionResponse, firstPainPhase = extensionPainPhase,
                        painLevel = extensionAdmissionPain,
                    },
                });
            bootstrap.Users.Save(user);
            if (user.admissionReviewRequired) { bootstrap.Router.Navigate(AppRoute.AdmissionDecision); return; }
            bootstrap.Router.Navigate(AppRoute.Home);
        }

        private void SelectAdmissionResponse(bool flexion, int index)
        {
            string value = index == 0 ? "relieved" : index == 1 ? "unchanged" : "worsened";
            if (flexion) flexionResponse = value; else extensionResponse = value;
            RefreshAdmissionChoiceColors();
        }

        private void SelectAdmissionPhase(bool flexion, int index)
        {
            string value = index == 0 ? "start" : index == 1 ? "middle" : index == 2 ? "end" : "pain_free";
            if (flexion) flexionPainPhase = flexionPainPhase == value ? null : value;
            else extensionPainPhase = extensionPainPhase == value ? null : value;
            RefreshAdmissionChoiceColors();
        }

        private Slider CreatePainSlider(Transform parent, Vector2 position, Color accent, System.Action<int> onValueChanged)
        {
            GameObject root = new GameObject("PainScoreSlider", typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(560, 32);
            rootRect.anchoredPosition = position;

            Image background = CreateSliderImage(root.transform, "Background", new Color(0.87f, 0.90f, 0.94f));
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0, 0.38f);
            backgroundRect.anchorMax = new Vector2(1, 0.62f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(root.transform, false);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(0, 10);
            fillAreaRect.offsetMax = new Vector2(0, -10);
            Image fill = CreateSliderImage(fillArea.transform, "Fill", accent);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(16, 0);
            handleAreaRect.offsetMax = new Vector2(-16, 0);
            Image handle = CreateSliderImage(handleArea.transform, "Handle", accent);
            RectTransform handleRect = handle.rectTransform;
            handleRect.sizeDelta = new Vector2(30, 30);
            handleRect.anchorMin = new Vector2(0, 0.5f);
            handleRect.anchorMax = new Vector2(0, 0.5f);

            Slider slider = root.GetComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 1;
            slider.maxValue = 10;
            slider.wholeNumbers = true;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            ColorBlock sliderColors = slider.colors;
            sliderColors.normalColor = accent;
            sliderColors.highlightedColor = Color.Lerp(accent, Color.white, 0.15f);
            sliderColors.pressedColor = Color.Lerp(accent, Color.black, 0.15f);
            sliderColors.selectedColor = accent;
            slider.colors = sliderColors;
            slider.value = 1;
            slider.onValueChanged.AddListener(value => onValueChanged(Mathf.RoundToInt(value)));
            root.AddComponent<PainSliderInteractionFeedback>().Initialize(slider, onValueChanged);
            return slider;
        }

        private static Image CreateSliderImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private void SetAdmissionPain(bool flexion, int value)
        {
            if (flexion)
            {
                flexionPainRated = true;
                flexionAdmissionPain = value;
                flexionScoreLabel.text = "疼痛程度：" + value + " / 10";
            }
            else
            {
                extensionPainRated = true;
                extensionAdmissionPain = value;
                extensionScoreLabel.text = "疼痛程度：" + value + " / 10";
            }
        }

        private void RefreshAdmissionChoiceColors()
        {
            RefreshChoiceColors(flexionResponseButtons, flexionResponse == "relieved" ? 0 : flexionResponse == "unchanged" ? 1 : flexionResponse == "worsened" ? 2 : -1, new Color(0.22f, 0.55f, 0.91f));
            RefreshChoiceColors(extensionResponseButtons, extensionResponse == "relieved" ? 0 : extensionResponse == "unchanged" ? 1 : extensionResponse == "worsened" ? 2 : -1, new Color(0.08f, 0.61f, 0.56f));
            RefreshChoiceColors(flexionPhaseButtons, PhaseIndex(flexionPainPhase), new Color(0.22f, 0.55f, 0.91f));
            RefreshChoiceColors(extensionPhaseButtons, PhaseIndex(extensionPainPhase), new Color(0.08f, 0.61f, 0.56f));
        }

        private static int PhaseIndex(string phase)
        {
            return phase == "start" ? 0 : phase == "middle" ? 1 : phase == "end" ? 2 : phase == "pain_free" ? 3 : -1;
        }

        private static void RefreshChoiceColors(Button[] buttons, int selectedIndex, Color accent)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
            {
                Color background = i == selectedIndex ? accent : new Color(0.95f, 0.97f, 0.99f);
                SetButtonColors(buttons[i], background, Color.Lerp(background, accent, 0.25f), i == selectedIndex ? Color.white : new Color(0.16f, 0.20f, 0.26f));
            }
        }

        private void CreateReviewForm(Transform parent)
        {
            CreateLabel(parent, "本地演示不会自动联网通知医生。请先遵循专业人员的建议，不要通过重新登录绕过此提示。", new Vector2(0, 110), 16);
            CreateButton(parent, "退出登录", new Vector2(0, -5), () => bootstrap.State.ClearSession());
        }

        private void ShowFlowMessage(string message)
        {
            if (flowMessages.TryGetValue(bootstrap.State.Route, out Text messageText) && messageText != null)
                messageText.text = message;
        }

        private void StartTraining(string sceneName, string actionId)
        {
            UserSession user = bootstrap.State.Session;
            if (user == null) { bootstrap.Router.Navigate(AppRoute.Login); return; }
            if (bootstrap.Training.HasPendingFeedback && bootstrap.Training.CurrentPlan.accountId == user.accountId)
            { ShowPostTrainingSurvey(); return; }
            if (!user.preTrainingCheckPassed)
            {
                // Remember which training button sent the user here so passing
                // the check can carry on into that session.
                pendingTrainingScene = sceneName;
                pendingTrainingActionId = actionId;
                bootstrap.Router.Navigate(AppRoute.PreTraining);
                return;
            }
            if (nativeBusy) return;
            if (NativeMobileService.Available)
            {
                nativeBusy = true;
                arTrainingMessage.text = "正在获取本次医生处方…";
                string account = user.accountId;
                int generation = loginGeneration;
                int identityGeneration = sessionIdentityGeneration;
                NativeMobileService.Instance.Request("plan", new NativeRequest { accountId = account }, reply =>
                {
                    if (this == null || generation != loginGeneration || identityGeneration != sessionIdentityGeneration ||
                        bootstrap.State.Session?.accountId != account) return;
                    nativeBusy = false;
                    if (!reply.ok) { arTrainingMessage.text = reply.error; return; }
                    TrainingPlan plan;
                    try { plan = JsonUtility.FromJson<TrainingPlan>(reply.dataJson); }
                    catch (System.ArgumentException) { plan = null; }
                    if (plan == null || plan.accountId != account || plan.patientId != bootstrap.State.Session.profile?.patientId ||
                        !plan.CanConsumePrescription())
                    {
                        arTrainingMessage.text = "无法启动训练，请检查处方和场景配置。";
                        return;
                    }
                    nativeBusy = true;
                    NativeMobileService.Instance.Request("confirmPlan", new NativeRequest
                    { accountId = account, planToken = plan.planToken, consumerConfirmed = true }, confirmed =>
                    {
                        if (this == null || generation != loginGeneration || identityGeneration != sessionIdentityGeneration ||
                            bootstrap.State.Session?.accountId != account) return;
                        nativeBusy = false;
                        if (!confirmed.ok) { arTrainingMessage.text = confirmed.error; return; }
                        LaunchPlan(plan);
                    });
                });
                return;
            }
            string coach = actionId == "bird_dog" ? "BirdDogPractice" : "DeadBugPractice";
            bool male = user.profile.gender == "男" || user.profile.gender == "male";
            coach = SpineFlowTrainingSession.ResolveSceneForGender(coach, male);
            LaunchPlan(new TrainingPlan
            {
                accountId = user.accountId, patientId = user.profile.patientId ?? user.accountId,
                gender = male ? "male" : "female", actionId = actionId, targetScene = coach,
                preTrainingEventId = user.preTrainingEventId,
                actions = new[]
                {
                    new TrainingAction { actionId = actionId + "_coach", arTaskType = "coach_training", sets = 1, reps = 4, sceneName = coach },
                    new TrainingAction { actionId = actionId + "_game", arTaskType = "game_training", sceneName = sceneName }
                }
            });
        }

        private void LaunchPlan(TrainingPlan plan)
        {
            HideAppShell();
            if (bootstrap.Training.StartTraining(plan)) return;
            ShowAppShell();
            if (arTrainingMessage != null) arTrainingMessage.text = "无法启动训练，请检查处方和场景配置。";
        }

        private static Canvas CreateCanvas(Transform persistentParent)
        {
            GameObject canvasObject = new GameObject("AppCanvas");
            canvasObject.transform.SetParent(persistentParent, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private const float LoginDesignWidth = 390f;
        private const float LoginDesignHeight = 884f;

        private static Vector2 LoginDesignSize(float width, float height)
        {
            return new Vector2(width, height);
        }

        private static Vector2 LoginDesignPosition(float x, float y, float width, float height)
        {
            return new Vector2(
                x + width * 0.5f - LoginDesignWidth * 0.5f,
                LoginDesignHeight * 0.5f - (y + height * 0.5f));
        }

        private static void CreateRasterImage(Transform parent, string resourcePath, Vector2 position, Vector2 size)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return;
            GameObject imageObject = new GameObject("Raster:" + resourcePath, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.GetComponent<Image>();
            image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        private static void MakeOverlayInput(InputField input)
        {
            if (input == null) return;
            Image image = input.GetComponent<Image>();
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            RoundedBorderGraphic border = input.GetComponentInChildren<RoundedBorderGraphic>(true);
            if (border != null) border.SetBorderColor(new Color(1f, 1f, 1f, 0f));
            if (input.textComponent != null)
            {
                input.textComponent.color = new Color(0.25f, 0.30f, 0.38f, 1f);
                // The login design root is scaled up for the portrait canvas;
                // generate the glyphs at a larger size so typed characters do
                // not become soft after the transform scale.
                // The login artwork is authored at 390 design pixels (the
                // source PNG is now imported at 2x). Keep live glyphs at the
                // same visual size as the artwork instead of the old 30px
                // value, which rendered typed text oversized and soft.
                input.textComponent.fontSize = 18;
                input.textComponent.alignByGeometry = false;
                input.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (input.placeholder is Text placeholder)
                placeholder.color = new Color(1f, 1f, 1f, 0f);
        }

        private static void MakeOverlayButton(Button button)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = new Color(1f, 1f, 1f, 0f);
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            button.colors = colors;
        }

        private void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            appEventSystem = eventSystem;
            eventSystem.enabled = true;
            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // Ensure the active EventSystem has a complete set of pointer and click actions.
            if (inputModule.point == null || inputModule.leftClick == null)
            {
                inputModule.AssignDefaultActions();
            }

            BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
            for (int i = 0; i < modules.Length; i++)
            {
                modules[i].enabled = modules[i] == inputModule;
            }
        }

        private void SetAppEventSystemEnabled(bool enabled)
        {
            if (appEventSystem == null) return;
            appEventSystem.enabled = enabled;
            BaseInputModule[] modules = appEventSystem.GetComponents<BaseInputModule>();
            for (int i = 0; i < modules.Length; i++)
                modules[i].enabled = enabled;
        }

        private void EnsureCameraIfNeeded()
        {
            if (shellCamera != null) return;
            GameObject cameraObject = new GameObject("AppShell Camera");
            shellCamera = cameraObject.AddComponent<Camera>();
            shellCamera.clearFlags = CameraClearFlags.SolidColor;
            shellCamera.backgroundColor = new Color(0.973f, 0.980f, 1f);
            shellCamera.cullingMask = 0;
            shellCamera.depth = -10;
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            color.a = 1f;
            panel.GetComponent<Image>().color = color;
            panel.GetComponent<Image>().raycastTarget = false;
            return panel;
        }

        private static Text CreateLabel(Transform parent, string value, Vector2 position, int fontSize)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(760, 55);
            rect.anchoredPosition = position;
            Text label = labelObject.GetComponent<Text>();
            label.text = value;
            label.font = AppBootstrap.Instance != null && AppBootstrap.Instance.uiFont != null
                ? AppBootstrap.Instance.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // The original shell was authored with desktop-sized coordinates
            // but very small mobile typography.  Keep call sites readable and
            // apply one consistent accessibility multiplier here.
            label.fontSize = Mathf.RoundToInt(fontSize * 1.28f);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.08f, 0.10f, 0.14f);
            label.raycastTarget = false;
            return label;
        }

        private static InputField CreateInput(Transform parent, string placeholder, Vector2 position, bool password)
        {
            GameObject inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(680, 78);
            rect.anchoredPosition = position;
            Image fieldImage = inputObject.GetComponent<Image>();
            fieldImage.color = Color.white;
            // The SVG card supplies the normal gray input border. Do not use
            // Unity's Outline on this transparent Image: Outline paints the
            // whole rectangular graphic when its alpha is zero, creating the
            // large blue block seen while typing. A dedicated rounded ring
            // draws only the border and keeps the field interior clear.
            RoundedBorderGraphic focusBorder = CreateRoundedBorder(inputObject.transform);
            InputField input = inputObject.GetComponent<InputField>();
            input.targetGraphic = fieldImage;
            input.transition = Selectable.Transition.None;
            ColorBlock colors = input.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.96f, 0.98f, 1f);
            colors.pressedColor = new Color(0.90f, 0.95f, 1f);
            colors.selectedColor = new Color(0.96f, 0.98f, 1f);
            input.colors = colors;
            InputFieldFocusFeedback focusFeedback = inputObject.AddComponent<InputFieldFocusFeedback>();
            focusFeedback.Initialize(fieldImage, focusBorder);

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(48f, 4f);
            textRect.offsetMax = new Vector2(-16f, -4f);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.alignByGeometry = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.supportRichText = false;
            input.textComponent = text;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            // Match the mobile reference state: the focused field uses a
            // blue outline, a visible blue caret and the standard blinking
            // cadence. Keep Unity's caret visible when the Android keyboard
            // is displayed instead of delegating all feedback to the IME.
            input.customCaretColor = true;
            input.caretColor = new Color(0.34f, 0.61f, 0.91f, 1f);
            input.caretBlinkRate = 0.85f;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.34f, 0.61f, 0.91f, 0.28f);
            input.shouldHideMobileInput = false;
            Text placeholderText = CreateLabel(inputObject.transform, placeholder, Vector2.zero, 21);
            placeholderText.color = new Color(0.35f, 0.35f, 0.35f);
            input.placeholder = placeholderText;
            RectTransform placeholderRect = placeholderText.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(48f, 4f);
            placeholderRect.offsetMax = new Vector2(-16f, -4f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            inputObject.GetComponent<Image>().raycastTarget = true;

            // Group.svg contains the authored placeholder as vector artwork.
            // Once the user types, cover only the text lane so that the live
            // InputField value never renders on top of that static placeholder
            // (or the left/right icons). The mask stays disabled while empty.
            GameObject valueMaskObject = new GameObject("ValueMask", typeof(RectTransform), typeof(Image));
            valueMaskObject.transform.SetParent(inputObject.transform, false);
            valueMaskObject.transform.SetSiblingIndex(0);
            RectTransform maskRect = valueMaskObject.GetComponent<RectTransform>();
            maskRect.anchorMin = new Vector2(0.5f, 0.5f);
            maskRect.anchorMax = new Vector2(0.5f, 0.5f);
            maskRect.pivot = new Vector2(0.5f, 0.5f);
            maskRect.anchoredPosition = new Vector2(-2.5f, 0f);
            maskRect.sizeDelta = new Vector2(235f, 40f);
            Image valueMask = valueMaskObject.GetComponent<Image>();
            valueMask.color = Color.white;
            valueMask.raycastTarget = false;
            valueMask.enabled = false;
            focusFeedback.SetPlaceholderMask(valueMask, input);
            input.onValueChanged.AddListener(value => valueMask.enabled = input.isFocused || !string.IsNullOrEmpty(value));
            return input;
        }

        private static RoundedBorderGraphic CreateRoundedBorder(Transform parent)
        {
            GameObject borderObject = new GameObject("FocusBorder", typeof(RectTransform));
            borderObject.transform.SetParent(parent, false);
            RectTransform rect = borderObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();
            RoundedBorderGraphic border = borderObject.AddComponent<RoundedBorderGraphic>();
            border.cornerRadius = 12f;
            border.borderThickness = 2.2f;
            border.raycastTarget = false;
            border.SetBorderColor(new Color(1f, 1f, 1f, 0f));
            return border;
        }

        private static Button CreateButton(Transform parent, string caption, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            return CreateButton(parent, caption, position, action, 560);
        }

        private static Button CreateButton(Transform parent, string caption, Vector2 position, UnityEngine.Events.UnityAction action, float width)
        {
            return CreateButton(parent, caption, position, action, width, 76);
        }

        private static Button CreateButton(Transform parent, string caption, Vector2 position, UnityEngine.Events.UnityAction action, float width, float height)
        {
            GameObject buttonObject = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = position;
            buttonObject.GetComponent<Image>().color = Color.white;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            buttonObject.GetComponent<Image>().raycastTarget = true;
            ColorBlock buttonColors = button.colors;
            buttonColors.normalColor = new Color(0.22f, 0.74f, 0.97f);
            buttonColors.highlightedColor = new Color(0.30f, 0.78f, 0.98f);
            buttonColors.pressedColor = new Color(0.12f, 0.39f, 0.78f);
            buttonColors.selectedColor = buttonColors.normalColor;
            buttonColors.disabledColor = new Color(0.62f, 0.68f, 0.76f);
            button.colors = buttonColors;
            button.onClick.AddListener(action);
            Text label = CreateLabel(buttonObject.transform, caption, Vector2.zero, 23);
            label.color = Color.white;
            label.raycastTarget = false;
            return button;
        }
    }

    /// <summary>
    /// Draws a real rounded rectangle ring. Unity's built-in Outline is not
    /// suitable here because the input field intentionally has a transparent
    /// Image over the authored SVG; Outline then fills the whole rectangle.
    /// </summary>
    internal sealed class RoundedBorderGraphic : Graphic
    {
        public float cornerRadius = 12f;
        public float borderThickness = 2.2f;

        public void SetBorderColor(Color borderColor)
        {
            color = borderColor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (color.a <= 0f || borderThickness <= 0f) return;

            Rect rect = rectTransform.rect;
            float thickness = Mathf.Min(borderThickness, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (thickness <= 0f) return;

            float outerRadius = Mathf.Clamp(cornerRadius, thickness, Mathf.Min(rect.width, rect.height) * 0.5f);
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            const int segmentsPerCorner = 8;
            int pointCount = segmentsPerCorner * 4;

            for (int i = 0; i < pointCount; i++)
            {
                Vector2 outer = RoundedPoint(rect, outerRadius, i, segmentsPerCorner);
                Vector2 inner = RoundedPoint(rect, innerRadius, i, segmentsPerCorner);
                Vector2 nextOuter = RoundedPoint(rect, outerRadius, (i + 1) % pointCount, segmentsPerCorner);
                Vector2 nextInner = RoundedPoint(rect, innerRadius, (i + 1) % pointCount, segmentsPerCorner);
                int start = vertexHelper.currentVertCount;
                vertexHelper.AddVert(outer, color, Vector2.zero);
                vertexHelper.AddVert(nextOuter, color, Vector2.zero);
                vertexHelper.AddVert(nextInner, color, Vector2.zero);
                vertexHelper.AddVert(inner, color, Vector2.zero);
                vertexHelper.AddTriangle(start, start + 1, start + 2);
                vertexHelper.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static Vector2 RoundedPoint(Rect rect, float radius, int index, int segmentsPerCorner)
        {
            int corner = index / segmentsPerCorner;
            int segment = index % segmentsPerCorner;
            float angle = (corner * 90f + 180f + segment * (90f / segmentsPerCorner)) * Mathf.Deg2Rad;
            Vector2 center;
            switch (corner)
            {
                case 0:
                    center = new Vector2(rect.xMin + radius, rect.yMin + radius);
                    break;
                case 1:
                    center = new Vector2(rect.xMax - radius, rect.yMin + radius);
                    break;
                case 2:
                    center = new Vector2(rect.xMax - radius, rect.yMax - radius);
                    break;
                default:
                    center = new Vector2(rect.xMin + radius, rect.yMax - radius);
                    break;
            }
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }

    internal sealed class InputFieldFocusFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Image image;
        private RoundedBorderGraphic border;
        private Image placeholderMask;
        private InputField input;
        private bool focusStateInitialized;
        private bool lastFocused;

        public void Initialize(Image targetImage, RoundedBorderGraphic targetBorder)
        {
            image = targetImage;
            border = targetBorder;
        }

        public void SetPlaceholderMask(Image mask, InputField owner)
        {
            placeholderMask = mask;
            input = owner;
        }

        private void Update()
        {
            // Some Android input modules do not dispatch ISelectHandler on the
            // first touch, even though the InputField receives the keystroke.
            // Poll the authoritative InputField focus state as a fallback so
            // the blue frame is always synchronized with the caret.
            if (input == null) return;
            bool focused = input.isFocused;
            if (!focusStateInitialized || focused != lastFocused)
            {
                ApplyFocusVisual(focused);
            }
        }

        private void ApplyFocusVisual(bool focused)
        {
            focusStateInitialized = true;
            lastFocused = focused;
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            if (border != null)
            {
                // Keep the focus ring above the authored PNG and all input
                // helper graphics; otherwise the gray raster border can
                // cover the straight sections and leave only blue corners.
                if (focused)
                    border.transform.SetAsLastSibling();
                // Draw a distinct, thicker focus ring over the authored PNG.
                // When focus leaves, hide the overlay completely so the
                // original gray border from LoginCover.png is visible again.
                border.SetBorderColor(focused
                    ? new Color(0.08f, 0.32f, 0.72f, 1f)
                    : new Color(1f, 1f, 1f, 0f));
                border.borderThickness = focused ? 5.5f : 0f;
            }
            if (placeholderMask != null)
                placeholderMask.enabled = focused || (input != null && !string.IsNullOrEmpty(input.text));
        }

        public void OnSelect(BaseEventData eventData)
        {
            ApplyFocusVisual(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            ApplyFocusVisual(false);
        }
    }

    internal sealed class PainSliderInteractionFeedback : MonoBehaviour, IPointerUpHandler
    {
        private Slider slider;
        private System.Action<int> onValueChanged;

        public void Initialize(Slider targetSlider, System.Action<int> callback)
        {
            slider = targetSlider;
            onValueChanged = callback;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (slider != null && onValueChanged != null)
                onValueChanged(Mathf.RoundToInt(slider.value));
        }
    }
}
