using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace SpineRecovery.App
{
    public sealed partial class AppShellUi : MonoBehaviour
    {
        private AppBootstrap bootstrap;
        private Canvas appCanvas;
        private Camera shellCamera;
        private bool nativeBusy;
        private string previewActivationCode;
        private int loginGeneration;
        private int sessionIdentityGeneration;
        private Text routineMessage;
        private bool auxiliaryPageOpen;
        private string auxiliarySceneName;
        private bool auxiliaryReturnInProgress;
        private AppRoute auxiliaryReturnRoute;
        private RectTransform safeContent;
        private Rect lastSafeArea;
        private Text loginMessage;
        private Text homeMessage;
        private Text trackingMessage;
        private Text adminMessage;
        private Text adminImuBindingMessage;
        private Text historyMessage;
        private Text activationMessage;
        private Text arTrainingMessage;
        private Text trainingMenuMessage;
        private EventSystem appEventSystem;
        private Button trackingCompleteButton;
        private Button adminImuCommitButton;
        private RectTransform adminImuBindingContent;
        private Text trackingStatusTitle;
        private Text trainingDisplayStatus;
        private Text trainingTrackerStatus;
        private InputField accountInput;
        private InputField passwordInput;
        private InputField registerAccountInput;
        private InputField registerPasswordInput;
        private InputField registerConfirmInput;
        private InputField activationCodeInput;
        private InputField[] profileInputs;
        private AppRoute profileContinueRoute = AppRoute.Home;
        private AppRoute trackingReturnRoute = AppRoute.Home;
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
        private HomeScreen homeScreen;
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
            GameObject profilePanel = CreateProfilePanel(safeContent);
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
            GameObject adminImuBindingPanel = CreateAdminImuBindingPanel(safeContent);
            GameObject historyPanel = CreateHistoryPanel(safeContent);
            postActionScreen = new PostActionScreen();
            GameObject postTrainingPanel = postActionScreen.Build(safeContent, HandlePostActionBack,
                HandlePostActionSubmitted);
            GameObject postTrainingStopPanel = CreatePostTrainingStopPanel(safeContent);
            GameObject routinePanel = CreateServerRoutinePanel(safeContent);

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
            bootstrap.Router.RegisterPanel(AppRoute.AdminImuBinding, adminImuBindingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.History, historyPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PostTrainingSurvey, postTrainingPanel);
            bootstrap.Router.RegisterPanel(AppRoute.PostTrainingStop, postTrainingStopPanel);
            bootstrap.Router.RegisterPanel(AppRoute.RoutineQuestionnaire, routinePanel);
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
            if (scene.name != "AppShell")
            {
                // Starting a practice from the library is no longer auxiliary browsing.
                if (scene.name != auxiliarySceneName) ClearAuxiliaryNavigation();
                HideAppShell();
                return;
            }
            ShowAppShell();
            AppRoute route = bootstrap.State.Session == null ? AppRoute.Login :
                auxiliaryPageOpen ? auxiliaryReturnRoute : AppRoute.Home;
            bool auxiliary = auxiliaryPageOpen;
            ClearAuxiliaryNavigation();
            bootstrap.Router.Navigate(route);
            if (!auxiliary) bootstrap.Training.RestoreForCurrentUser();
        }

        internal bool IsBrowsingAuxiliaryScene => auxiliaryPageOpen &&
            SceneManager.GetActiveScene().name == auxiliarySceneName;

        internal bool TryReturnFromAuxiliaryPage()
        {
            if (!IsBrowsingAuxiliaryScene) return false;
            if (auxiliaryReturnInProgress) return true;
            auxiliaryReturnInProgress = true;
            try
            {
                ScreenOrientationService.SetPortrait();
                SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
                return true;
            }
            catch
            {
                auxiliaryReturnInProgress = false;
                throw;
            }
        }

        private void ClearAuxiliaryNavigation()
        {
            auxiliaryPageOpen = false;
            auxiliarySceneName = null;
            auxiliaryReturnInProgress = false;
        }

        private void Update()
        {
            if (safeContent != null && Screen.safeArea != lastSafeArea) ApplySafeArea();
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
            if (session == null)
            {
                nativeBusy = false;
                loginGeneration++;
                NativeMobileService.Call("logout");
            }
        }

        private void OnDestroy()
        {
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
            GameObject designRoot = new GameObject("RegisterDesign390x884", typeof(RectTransform));
            designRoot.transform.SetParent(panel.transform, false);
            RectTransform designRect = designRoot.GetComponent<RectTransform>();
            designRect.anchorMin = new Vector2(0.5f, 0.5f);
            designRect.anchorMax = new Vector2(0.5f, 0.5f);
            designRect.pivot = new Vector2(0.5f, 0.5f);
            designRect.sizeDelta = new Vector2(LoginDesignWidth, LoginDesignHeight);
            designRect.anchoredPosition = Vector2.zero;
            float designScale = Mathf.Min(1080f / LoginDesignWidth, 1920f / LoginDesignHeight);
            designRoot.transform.localScale = new Vector3(designScale, designScale, 1f);
            CreateRasterImage(designRoot.transform, "AppUi/RegisterScreen", Vector2.zero,
                new Vector2(LoginDesignWidth, LoginDesignHeight));

            registerAccountInput = CreateInput(designRoot.transform, "设置账号", LoginDesignPosition(44f, 312f, 302f, 57f), false);
            registerPasswordInput = CreateInput(designRoot.transform, "设置密码", LoginDesignPosition(44f, 380f, 302f, 57f), true);
            registerConfirmInput = CreateInput(designRoot.transform, "再次输入密码", LoginDesignPosition(44f, 448f, 302f, 57f), true);
            registerAccountInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(302f, 57f);
            registerPasswordInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(302f, 57f);
            registerConfirmInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(302f, 57f);
            MakeOverlayInput(registerAccountInput);
            MakeOverlayInput(registerPasswordInput);
            MakeOverlayInput(registerConfirmInput);

            Button registerButton = CreateButton(designRoot.transform, "注册并继续激活",
                LoginDesignPosition(44f, 530f, 302f, 52f), HandleRegisterClicked, 302f, 52f);
            Button backButton = CreateButton(designRoot.transform, "返回登录",
                LoginDesignPosition(190f, 604f, 110f, 28f), () => bootstrap.Router.Navigate(AppRoute.Login), 110f, 28f);
            MakeOverlayButton(registerButton);
            MakeOverlayButton(backButton);
            return panel;
        }

        private GameObject CreateActivationPanel(Transform parent)
        {
            GameObject panel = CreatePanel("ActivationPanel", parent, new Color(0.973f, 0.980f, 1f));
            GameObject designRoot = new GameObject("ActivationDesign390x884", typeof(RectTransform));
            designRoot.transform.SetParent(panel.transform, false);
            RectTransform designRect = designRoot.GetComponent<RectTransform>();
            designRect.anchorMin = new Vector2(0.5f, 0.5f);
            designRect.anchorMax = new Vector2(0.5f, 0.5f);
            designRect.pivot = new Vector2(0.5f, 0.5f);
            designRect.sizeDelta = new Vector2(LoginDesignWidth, LoginDesignHeight);
            designRect.anchoredPosition = Vector2.zero;
            float designScale = Mathf.Min(1080f / LoginDesignWidth, 1920f / LoginDesignHeight);
            designRoot.transform.localScale = new Vector3(designScale, designScale, 1f);
            CreateRasterImage(designRoot.transform, "AppUi/ActivationScreen", Vector2.zero,
                new Vector2(LoginDesignWidth, LoginDesignHeight));

            activationCodeInput = CreateInput(designRoot.transform, "激活码（11位手机号）",
                LoginDesignPosition(44f, 400f, 302f, 57f), false);
            activationCodeInput.GetComponent<RectTransform>().sizeDelta = LoginDesignSize(302f, 57f);
            MakeOverlayInput(activationCodeInput);
            activationMessage = CreateLabel(designRoot.transform, "",
                LoginDesignPosition(44f, 475f, 302f, 30f), 12);
            activationMessage.rectTransform.sizeDelta = LoginDesignSize(302f, 30f);
            activationMessage.alignment = TextAnchor.MiddleLeft;
            activationMessage.color = new Color(0.35f, 0.40f, 0.48f, 1f);

            Button activateButton = CreateButton(designRoot.transform, "确认绑定",
                LoginDesignPosition(44f, 522f, 302f, 52f), HandleActivationClicked, 302f, 52f);
            Button backButton = CreateButton(designRoot.transform, "返回登录",
                LoginDesignPosition(165f, 598f, 90f, 28f), () => bootstrap.State.ClearSession(), 90f, 28f);
            MakeOverlayButton(activateButton);
            MakeOverlayButton(backButton);
            return panel;
        }

        private GameObject CreateHomePanel(Transform parent)
        {
            homeScreen = new HomeScreen();
            GameObject panel = homeScreen.Build(parent, BuildHomeState, new HomeScreen.Actions
            {
                EnterTraining = () => bootstrap.Router.Navigate(AppRoute.TrainingPreparation),
                OpenTracker = OpenTrackingCalibrationFromHome,
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

            HomeScreen.MonthlyEntryStatus monthlyStatus = NativeMobileService.Available
                ? HomeScreen.MonthlyEntryStatus.Open : HomeScreen.MonthlyEntryStatus.Disabled;
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
            CreateButton(panel.transform, "追踪器连接与校准", new Vector2(0, -50), OpenTrackingCalibrationFromHome);
            CreateButton(panel.transform, "动作示范库", new Vector2(0, -140), OpenActionLibrary);
            CreateButton(panel.transform, "返回训练首页", new Vector2(0, -230), () => bootstrap.Router.Navigate(AppRoute.Home));
            return panel;
        }

        private GameObject CreateTrainingMenuPanel(Transform parent)
        {
            RectTransform root = QuestionnaireUi.CreateScreen(parent, "TrainingMenuPanel", null, out GameObject panel);
            QuestionnaireUi.Header(root, "训练准备", () => bootstrap.Router.Navigate(AppRoute.Home));
            RectTransform content = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, 96f, 690f);
            QuestionnaireUi.Label(content, "开始本次训练", 29, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 36f, 350f, 48f);
            QuestionnaireUi.Label(content, "连接投屏眼镜和 IMU 追踪器后，进入训练流程", 15, QuestionnaireUi.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 91f, 350f, 42f);

            RectTransform deviceCard = QuestionnaireUi.Card(content, 20f, 153f, 350f, 178f);
            trainingDisplayStatus = AddPreparationRow(deviceCard, "投屏眼镜", "未连接", "连接", 12f, 18f,
                OpenDisplayConnection);
            QuestionnaireUi.Box(deviceCard, "Divider", QuestionnaireUi.Divider, 0.5f, 20f, 88f, 310f, 1f);
            trainingTrackerStatus = AddPreparationRow(deviceCard, "IMU 追踪器", "未校准", "去校准", 12f, 96f,
                OpenTrackingCalibrationFromPreparation);

            RectTransform info = QuestionnaireUi.Card(content, 20f, 350f, 350f, 86f);
            QuestionnaireUi.Label(info, "i", 18, QuestionnaireUi.Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, 12f, 0f, 30f, 86f);
            QuestionnaireUi.Label(info, "建议先完成追踪器校准再开始训练，动作识别会更准确", 14,
                QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal, 50f, 0f, 282f, 86f);

            Button library = QuestionnaireUi.MakeButton(content, "ActionLibrary", Color.white, 18f,
                20f, 458f, 350f, 88f, OpenActionLibrary);
            QuestionnaireUi.Label(library.transform, "动作示范库", 19, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 76f, 15f, 240f, 30f);
            QuestionnaireUi.Label(library.transform, "开始前可以先看一遍动作要领", 14, QuestionnaireUi.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 76f, 48f, 240f, 24f);
            QuestionnaireUi.Label(library.transform, ">", 26, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 312f, 0f, 26f, 88f);

            RectTransform footer = QuestionnaireUi.Footer(root, 96f);
            trainingMenuMessage = QuestionnaireUi.Label(footer, "", 12, QuestionnaireUi.DangerText,
                TextAnchor.MiddleCenter, FontStyle.Normal, 20f, 2f, 350f, 18f);
            QuestionnaireUi.PillButton(footer, "开始训练", 20f, 28f, 350f, 52f, true,
                () => StartTraining("SpineV2Minimal", "dead_bug"));
            RefreshTrainingPreparationStatus();
            return panel;
        }

        private static Text AddPreparationRow(Transform parent, string title, string initialStatus,
            string actionCaption, float x, float y, UnityEngine.Events.UnityAction action)
        {
            QuestionnaireUi.Box(parent, title + "Icon", QuestionnaireUi.SecondaryButton, 14f, x, y, 52f, 52f);
            QuestionnaireUi.Label(parent, title, 18, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, x + 66f, y + 1f, 158f, 27f);
            Text status = QuestionnaireUi.Label(parent, initialStatus, 14, QuestionnaireUi.BadgeText,
                TextAnchor.MiddleLeft, FontStyle.Bold, x + 66f, y + 27f, 158f, 22f);
            Button button = QuestionnaireUi.MakeButton(parent, title + "Action", QuestionnaireUi.SecondaryButton,
                18f, 257f, y + 6f, 72f, 40f, action);
            QuestionnaireUi.Label(button.transform, actionCaption, 15, QuestionnaireUi.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, 72f, 40f);
            return status;
        }

        private void RefreshTrainingPreparationStatus()
        {
            if (trainingTrackerStatus == null) return;
            bool confirmed = !string.IsNullOrEmpty(bootstrap?.State?.Session?.trackingCalibratedAt);
            if (EmbeddedSlimeVrBridge.TryReadState(out var state, true) && state.trackers != null)
            {
                int connected = state.trackers.Count(item => item.connected);
                trainingTrackerStatus.text = connected > 0
                    ? (confirmed ? connected + " 个已连接 · 已校准" : connected + " 个已连接")
                    : (confirmed ? "已校准 · 等待连接" : "未校准");
            }
            else trainingTrackerStatus.text = confirmed ? "已校准" : "未校准";
        }

        private void OpenTrackingCalibrationFromHome()
        {
            trackingReturnRoute = AppRoute.Home;
            bootstrap.Router.Navigate(AppRoute.TrackingCalibration);
        }

        private void OpenTrackingCalibrationFromPreparation()
        {
            trackingReturnRoute = AppRoute.TrainingPreparation;
            bootstrap.Router.Navigate(AppRoute.TrackingCalibration);
        }

        private void ReturnFromTrackingCalibration()
        {
            AppRoute destination = trackingReturnRoute == AppRoute.TrainingPreparation
                ? AppRoute.TrainingPreparation : AppRoute.Home;
            bootstrap.Router.Navigate(destination);
        }

        private void OpenDisplayConnection()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.CAST_SETTINGS"))
                {
                    activity.Call("startActivity", intent);
                    if (trainingDisplayStatus != null) trainingDisplayStatus.text = "请在系统中选择设备";
                }
            }
            catch (System.Exception exception)
            {
                if (trainingMenuMessage != null) trainingMenuMessage.text = "无法打开投屏设置：" + exception.Message;
            }
#else
            if (trainingDisplayStatus != null) trainingDisplayStatus.text = "请在 Android 设备上连接";
#endif
        }

        private void ImportLegacyArchive()
        {
            if (!NativeMobileService.Available || bootstrap.State.Session == null) return;
            NativeMobileService.Call("openLegacyImport", bootstrap.State.Session.accountId);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) return;
            if (adminImuBindingContent != null && adminImuBindingContent.gameObject.activeInHierarchy) RefreshAdminImuBindingPage();
            RefreshTrainingPreparationStatus();
            if (!NativeMobileService.Available || bootstrap?.State?.Session == null) return;
            var user = bootstrap.Users.Load(bootstrap.State.Session.accountId);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(user), bootstrap.State.Session);
            RefreshHomeSummary();
            RefreshHistorySummary();
        }

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
            if (!Application.CanStreamedLevelBeLoaded("ActionLibrary")) return;
            auxiliaryReturnRoute = bootstrap.State.Route;
            auxiliaryPageOpen = true;
            auxiliarySceneName = "ActionLibrary";
            auxiliaryReturnInProgress = false;
            HideAppShell();
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene("ActionLibrary", LoadSceneMode.Single);
        }

        [System.Serializable] private sealed class ImportedReplayList { public string[] items; }

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
            auxiliarySceneName = "PlayBack";
            auxiliaryReturnInProgress = false;
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
            CreateButton(panel.transform, "绑定 IMU", new Vector2(0, -155), OpenAdminImuBinding);
            CreateButton(panel.transform, "从当前版本创建草稿", new Vector2(0, -230), CreateAdminDraft);
            CreateButton(panel.transform, "标记 AR 测试通过", new Vector2(0, -305), MarkAdminArTestPassed);
            CreateButton(panel.transform, "发布当前草稿", new Vector2(0, -380), PublishAdminDraft);
            CreateButton(panel.transform, "回滚到最近归档版本", new Vector2(0, -455), RollbackAdminVersion);
            CreateButton(panel.transform, "返回登录", new Vector2(0, -530), () => bootstrap.State.ClearSession());
            return panel;
        }

        private void OpenAdminImuBinding()
        {
            if (bootstrap.State.Session == null || !bootstrap.State.Session.IsAdministrator)
            {
                return;
            }
            bootstrap.Router.Navigate(AppRoute.AdminImuBinding);
        }

        private GameObject CreateAdminImuBindingPanel(Transform parent)
        {
            RectTransform root = QuestionnaireUi.CreateScreen(parent, "AdminImuBindingPanel", RefreshAdminImuBindingPage, out GameObject panel);
            QuestionnaireUi.Header(root, "绑定 IMU", () => bootstrap.Router.Navigate(AppRoute.Admin));
            adminImuBindingContent = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, 172f, 820f);
            RectTransform footer = QuestionnaireUi.Footer(root, 172f);
            QuestionnaireUi.PillButton(footer, "打开完整 SlimeVR 管理界面", 20f, 10f, 350f, 44f, true, OpenAdminSlimeVrInterface);
            QuestionnaireUi.PillButton(footer, "刷新设备状态", 20f, 62f, 350f, 42f, false, RefreshAdminImuBindingPage);
            adminImuCommitButton = QuestionnaireUi.PillButton(footer, "绑定并锁定这 5 个 IMU", 20f, 112f, 350f, 50f, true, ToggleAdminImuBindings);
            return panel;
        }

        private void OpenAdminSlimeVrInterface()
        {
            if (bootstrap.State.Session == null || !bootstrap.State.Session.IsAdministrator) return;
            if (!EmbeddedSlimeVrBridge.TryOpenFullInterface(out string error))
            {
                if (adminImuBindingMessage != null) adminImuBindingMessage.text = error;
                return;
            }
            if (adminImuBindingMessage != null)
                adminImuBindingMessage.text = "完整 SlimeVR 已打开；先批准未知设备，返回后点击“刷新设备状态”。";
        }

        private void RefreshAdminImuBindingPage()
        {
            if (adminImuBindingContent == null) return;
            foreach (Transform child in adminImuBindingContent)
                Destroy(child.gameObject);

            AdminTrainingConfiguration published = bootstrap.AdminConfigs?.CurrentPublished();
            bool locked = published != null && published.trackerBindingsLocked && !published.trackerBindingsDisabled;
            var trackers = ReadLiveImuSnapshots();
            bool ready = AdminTrackerBinding.CanBind(AssignedBindings(trackers), out string readiness);
            if (adminImuCommitButton != null)
            {
                adminImuCommitButton.interactable = locked || ready;
                Text buttonLabel = adminImuCommitButton.GetComponentInChildren<Text>();
                if (buttonLabel != null)
                    buttonLabel.text = locked ? "解除绑定" : "绑定";
            }

            QuestionnaireUi.Label(adminImuBindingContent,
                locked ? "固定 IMU 已绑定" : "绑定 5 台 IMU", 26,
                QuestionnaireUi.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, 20f, 22f, 350f, 42f);
            QuestionnaireUi.Label(adminImuBindingContent,
                locked ? "解除绑定后，可在完整 SlimeVR 界面修改佩戴部位。" : "请在完整 SlimeVR 界面为五台设备分配部位，然后返回绑定。",
                14, QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 68f, 350f, 46f);

            adminImuBindingMessage = QuestionnaireUi.Label(adminImuBindingContent, "", 13,
                locked ? QuestionnaireUi.BadgeText : QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal,
                20f, 121f, 350f, 32f);
            adminImuBindingMessage.text = locked ? "已锁定以下设备和部位。" : readiness;

            if (locked)
            {
                DrawLockedBindingCards(published, trackers, 168f);
                return;
            }

            SlimeVrHapticTrackerSnapshot[] online = trackers.Where(tracker => tracker != null && tracker.connected &&
                !string.IsNullOrWhiteSpace(tracker.hardwareId)).ToArray();
            if (online.Length == 0)
            {
                QuestionnaireUi.WarningBox(adminImuBindingContent, "未发现在线 IMU。请先启动追踪器和 SlimeVR，再点击“刷新设备状态”。",
                    20f, 174f, 350f, 76f);
                return;
            }

            float top = 168f;
            foreach (SlimeVrHapticTrackerSnapshot tracker in online)
            {
                DrawAdminImuCandidateCard(tracker, top);
                top += 126f;
            }
        }

        private SlimeVrHapticTrackerSnapshot[] ReadLiveImuSnapshots()
        {
            if (EmbeddedSlimeVrBridge.TryReadState(out EmbeddedSlimeVrBridge.RuntimeState state, true) && state.trackers != null)
                return state.trackers;
            return System.Array.Empty<SlimeVrHapticTrackerSnapshot>();
        }

        private static AdminTrackerBinding[] AssignedBindings(IEnumerable<SlimeVrHapticTrackerSnapshot> trackers)
        {
            return trackers.Where(t => t != null && t.connected && !string.IsNullOrWhiteSpace(t.bodyPosition))
                .Select(t => new AdminTrackerBinding { hardwareId = t.hardwareId?.Trim(), bodyPosition = t.bodyPosition,
                    online = true, ip = t.ip, batteryPercent = t.batteryPercent }).ToArray();
        }

        private void DrawAdminImuCandidateCard(SlimeVrHapticTrackerSnapshot tracker, float top)
        {
            RectTransform card = QuestionnaireUi.Card(adminImuBindingContent, 20f, top, 350f, 112f);
            string id = tracker.hardwareId.Trim();
            QuestionnaireUi.Label(card, tracker.name ?? "IMU " + tracker.trackerNum, 16, QuestionnaireUi.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold, 16f, 12f, 190f, 25f);
            QuestionnaireUi.Label(card, "MAC: " + id, 12, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 16f, 39f, 310f, 20f);
            string battery = tracker.batteryPercent >= 0 ? "电量 " + tracker.batteryPercent + "%" : "电量暂不可用";
            QuestionnaireUi.Label(card, "在线 · " + battery, 12, new Color(0.12f, 0.55f, 0.30f),
                TextAnchor.MiddleLeft, FontStyle.Bold, 16f, 62f, 160f, 22f);
            QuestionnaireUi.Label(card, AdminTrackerBinding.PositionLabel(tracker.bodyPosition), 15, QuestionnaireUi.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold, 182f, 58f, 150f, 28f);
        }

        private void DrawLockedBindingCards(AdminTrainingConfiguration published, IEnumerable<SlimeVrHapticTrackerSnapshot> liveTrackers, float top)
        {
            foreach (AdminTrackerBinding binding in (published.trackerBindings ?? System.Array.Empty<AdminTrackerBinding>()).OrderBy(item => item.slot))
            {
                SlimeVrHapticTrackerSnapshot live = (liveTrackers ?? System.Array.Empty<SlimeVrHapticTrackerSnapshot>()).FirstOrDefault(tracker =>
                    tracker != null && string.Equals(tracker.hardwareId?.Trim(), binding.hardwareId?.Trim(), System.StringComparison.OrdinalIgnoreCase));
                RectTransform card = QuestionnaireUi.Card(adminImuBindingContent, 20f, top, 350f, 92f);
                QuestionnaireUi.Label(card, binding.DisplayName(), 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                    FontStyle.Bold, 16f, 13f, 120f, 25f);
                QuestionnaireUi.Label(card, "MAC: " + binding.hardwareId, 12, QuestionnaireUi.Muted,
                    TextAnchor.MiddleLeft, FontStyle.Normal, 16f, 42f, 315f, 20f);
                string battery = live != null && live.batteryPercent >= 0 ? " · 电量 " + live.batteryPercent + "%" : " · 电量暂不可用";
                QuestionnaireUi.Label(card, live != null && live.connected ? "在线" + battery : "离线" + battery, 12,
                    live != null && live.connected ? new Color(0.12f, 0.55f, 0.30f) : QuestionnaireUi.BadgeText,
                    TextAnchor.MiddleLeft, FontStyle.Bold, 16f, 65f, 250f, 18f);
                top += 104f;
            }
        }

        private void CommitAdminImuBindings()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            if (repository.BindPublishedTrackers(AssignedBindings(ReadLiveImuSnapshots()), out string message))
                RefreshAdminSummary();
            RefreshAdminImuBindingPage();
            if (adminImuBindingMessage != null) adminImuBindingMessage.text = message;
        }

        private void ToggleAdminImuBindings()
        {
            if (!(bootstrap.AdminConfigs is JsonAdminConfigRepository repository)) return;
            AdminTrainingConfiguration published = repository.CurrentPublished();
            if (published != null && published.trackerBindingsLocked && !published.trackerBindingsDisabled)
            {
                bool saved = repository.SetPublishedTrackerBindingsEnabled(false, out string message);
                RefreshAdminImuBindingPage();
                if (adminImuBindingMessage != null) adminImuBindingMessage.text = message;
                if (saved) RefreshAdminSummary();
                return;
            }
            CommitAdminImuBindings();
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
            user.symptomChange = answers.SymptomChange;
            user.preTrainingCheckPassed = answers.Pain < QuestionnaireUi.PainStopThreshold && answers.SymptomChange != "worse";
            user.preTrainingRecordedAt = System.DateTimeOffset.UtcNow.ToString("O");
            user.preTrainingEventId = "pre:" + user.preTrainingRecordedAt;
            bootstrap.Users.AppendPainRecord(user, new PainHistoryRecord
            {
                id = user.preTrainingEventId,
                recordedAt = user.preTrainingRecordedAt,
                painLevel = answers.Pain,
                source = "DailyQuestionnaire",
                painParts = painParts,
            });
            lastPreTrainingParts = painParts;

            if (!user.preTrainingCheckPassed)
            {
                pendingTrainingScene = "";
                pendingTrainingActionId = "";
                preTrainingBlockedScreen.Show(
                    "本次疼痛达到停训标准或症状明显加重，请先休息并联系专业人员。",
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
            postActionCount = plan != null && plan.actions != null ? Mathf.Max(1, plan.prescriptionContractVersion == 2 ? plan.actions.Length / 2 : plan.actions.Length) : 1;
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
            // Keep the existing feedback transport. Its separator is " / ", so
            // score captions must not contain that separator themselves.
            bootstrap.Training.SubmitFeedback("疼痛 " + pain + "（0–10）", "运动强度 " + intensity + "（0–10）");
            RefreshHistorySummary();
            bootstrap.Router.Navigate(AppRoute.History);
        }

        private void FinishAfterPainStop()
        {
            RefreshHistorySummary();
            bootstrap.Router.Navigate(AppRoute.History);
        }

        private void ShowMonthlyQuestionnaire()
        {
            bootstrap.Router.Navigate(AppRoute.RoutineQuestionnaire);
            RefreshServerRoutine();
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
                    string.Join("\n", trainingLines));
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
            var trackerLines = (config.trackerBindings ?? System.Array.Empty<AdminTrackerBinding>())
                .OrderBy(binding => binding.slot)
                .Select(binding => binding.DisplayName() + "：" +
                    (string.IsNullOrWhiteSpace(binding.hardwareId) ? "未绑定" : binding.hardwareId) +
                    " / " + (binding.online ? "在线" : "离线") +
                    (binding.batteryPercent >= 0 ? " / 电量 " + binding.batteryPercent + "%" : " / 电量未知"))
                .ToArray();
            var liveTrackers = CoachHapticFeedbackController.Instance == null
                ? "状态源未启动"
                : string.Join("\n", CoachHapticFeedbackController.Instance.LatestTrackerSnapshots
                    .Where(tracker => tracker != null && !string.IsNullOrWhiteSpace(tracker.bodyPosition))
                    .Select(tracker => tracker.bodyPosition + "：" + (tracker.connected ? "在线" : "离线") +
                        (tracker.batteryPercent >= 0 ? " / 电量 " + tracker.batteryPercent + "%" : " / 电量未知")));
            adminMessage.text = "当前版本：" + config.versionName + "（" + config.state + "）\n" +
                "配置 ID：" + config.configId + "\n" +
                "动作：" + string.Join("、", actionLines) + "\n" +
                "推荐规则：" + (ruleLines.Length == 0 ? "暂无" : string.Join("；", ruleLines)) + "\n" +
                "IMU 绑定：" + (!config.trackerBindingsLocked ? "未绑定" :
                    config.trackerBindingsDisabled ? "已解除（配置保留）" : "已绑定") + "\n" +
                "固定追踪器（5 个）：\n" + (trackerLines.Length == 0 ? "暂无" : string.Join("\n", trackerLines)) + "\n" +
                "实时状态：\n" + (string.IsNullOrWhiteSpace(liveTrackers) ? "暂无设备状态" : liveTrackers) + "\n" +
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
            RectTransform root = QuestionnaireUi.CreateScreen(parent, "TrackingCalibrationPanel", null, out GameObject panel);
            QuestionnaireUi.Header(root, "追踪器校准", ReturnFromTrackingCalibration);
            RectTransform content = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, 152f, 670f);
            QuestionnaireUi.Label(content, "IMU 追踪器校准", 29, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 36f, 350f, 48f);
            QuestionnaireUi.Label(content, "训练前需要连接并校准追踪器，动作识别才准确", 15,
                QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 91f, 350f, 42f);

            RectTransform state = QuestionnaireUi.Card(content, 20f, 153f, 350f, 104f);
            QuestionnaireUi.Box(state, "StateIcon", QuestionnaireUi.BadgeFill, 16f, 18f, 26f, 52f, 52f);
            trackingStatusTitle = QuestionnaireUi.Label(state, "校准尚未开始", 18, QuestionnaireUi.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold, 84f, 17f, 200f, 30f);
            trackingMessage = QuestionnaireUi.Label(state, "状态：未校准", 14, QuestionnaireUi.BadgeText,
                TextAnchor.MiddleLeft, FontStyle.Bold, 84f, 53f, 230f, 28f);

            RectTransform steps = QuestionnaireUi.Card(content, 20f, 277f, 350f, 325f);
            QuestionnaireUi.Label(steps, "校准步骤", 20, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 18f, 310f, 34f);
            AddCalibrationStep(steps, 1, "佩戴已绑定的追踪器", "确认五个设备已开机并戴在指定部位", 72f);
            AddCalibrationStep(steps, 2, "打开快速校准", "按提示完成校准", 151f);
            AddCalibrationStep(steps, 3, "返回此页确认", "完成后点击下方按钮继续", 230f);

            RectTransform footer = QuestionnaireUi.Footer(root, 152f);
            Button quick = QuestionnaireUi.PillButton(footer, "快速校准", 20f, 12f, 350f, 44f, false,
                OpenEmbeddedSlimeVrQuickCalibration);
            quick.GetComponent<Image>().color = Color.white;
            trackingCompleteButton = QuestionnaireUi.PillButton(footer, "我已完成校准，继续", 20f, 72f,
                350f, 52f, true, ConfirmTrackingCalibration);
            trackingCompleteButton.interactable = false;
            return panel;
        }

        private static void AddCalibrationStep(Transform parent, int number, string title, string detail, float top)
        {
            QuestionnaireUi.Box(parent, "StepNumber", number == 1 ? QuestionnaireUi.Accent : QuestionnaireUi.SecondaryButton,
                18f, 20f, top, 36f, 36f);
            QuestionnaireUi.Label(parent, number.ToString(), 16, number == 1 ? Color.white : QuestionnaireUi.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold, 20f, top, 36f, 36f);
            QuestionnaireUi.Label(parent, title, 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 68f, top - 1f, 260f, 28f);
            QuestionnaireUi.Label(parent, detail, 13, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 68f, top + 28f, 260f, 34f);
        }

        private void OpenEmbeddedSlimeVrQuickCalibration()
        {
            int height = bootstrap.State.Session != null ? bootstrap.State.Session.profile.heightCm : 0;
            if (!EmbeddedSlimeVrBridge.TryOpenCalibration(height, out string error, true))
            {
                trackingStatusTitle.text = "无法启动快速校准";
                trackingMessage.text = error;
                return;
            }
            trackingCompleteButton.interactable = true;
            trackingStatusTitle.text = "快速校准已打开";
            trackingMessage.text = "完成后返回此页面确认";
        }

        private void ConfirmTrackingCalibration()
        {
            UserSession session = bootstrap.State.Session;
            if (session != null)
            {
                session.trackingCalibratedAt = System.DateTimeOffset.UtcNow.ToString("O");
                bootstrap.Users.Save(session);
            }
            RefreshTrainingPreparationStatus();
            if (trackingMessage != null)
            {
                bool available = EmbeddedSlimeVrBridge.TryReadState(out var tracking, true);
                int connected = available && tracking.trackers != null ? tracking.trackers.Count(t => t.connected) : 0;
                trackingStatusTitle.text = connected > 0 ? "追踪器已连接" : "校准确认已记录";
                trackingMessage.text = available ? "在线追踪器：" + connected + " · " +
                    (tracking.vmcSkeletonOutputFresh ? "动作数据正常" : "等待动作数据") : "校准状态已保存";
            }
            ReturnFromTrackingCalibration();
        }

        private void HandleLoginClicked()
        {
            if (nativeBusy) return;
            // The administrator account is local to the Unity app. Check it
            // before dispatching Android login to the patient/server bridge.
            if (string.Equals(accountInput.text?.Trim(), "admin", System.StringComparison.OrdinalIgnoreCase) &&
                passwordInput.text == "admin")
            {
                JsonUserRepository localUsers = new JsonUserRepository();
                if (localUsers.TryAuthenticateAdministrator(accountInput.text, passwordInput.text, out UserSession localAdmin))
                {
                    bootstrap.State.SetSession(localAdmin);
                    RefreshAdminSummary();
                    bootstrap.Router.Navigate(AppRoute.Admin);
                    return;
                }
            }
            if (NativeMobileService.Available)
            {
                nativeBusy = true;
                int generation = ++loginGeneration;
                loginMessage.text = "";
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

        private GameObject CreateProfilePanel(Transform parent)
        {
            RectTransform root = QuestionnaireUi.CreateScreen(parent, "BasicProfilePanel", null, out GameObject panel);
            QuestionnaireUi.Header(root, "资料登记", () => bootstrap.Router.Navigate(profileContinueRoute));
            RectTransform content = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, 96f, 650f);
            QuestionnaireUi.Label(content, "先填一下基本资料", 29, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 36f, 350f, 48f);
            QuestionnaireUi.Label(content, "用来调整训练强度和动作识别，只需填写一次", 15,
                QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 91f, 350f, 42f);

            RectTransform card = QuestionnaireUi.Card(content, 20f, 153f, 350f, 404f);
            QuestionnaireUi.Label(card, "性别", 17, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 20f, 310f, 28f);
            genderButtons = new[]
            {
                CreateProfileChoice(card, "男", 20f, 58f, () => SelectGender("男", 0)),
                CreateProfileChoice(card, "女", 176f, 58f, () => SelectGender("女", 1)),
            };
            SetSelectedChoice(genderButtons, -1);

            profileInputs = new[]
            {
                CreateProfileNumberInput(card, "年龄", "请输入年龄", "岁", "10–100", 122f),
                CreateProfileNumberInput(card, "身高", "请输入身高", "cm", "120–220", 214f),
                CreateProfileNumberInput(card, "体重", "请输入体重", "kg", "35–200", 306f),
            };

            RectTransform footer = QuestionnaireUi.Footer(root, 96f);
            flowMessage = QuestionnaireUi.Label(footer, "", 12, QuestionnaireUi.DangerText,
                TextAnchor.MiddleCenter, FontStyle.Normal, 20f, 2f, 350f, 18f);
            flowMessages[AppRoute.BasicProfile] = flowMessage;
            QuestionnaireUi.PillButton(footer, "保存并继续", 20f, 28f, 350f, 52f, true, SaveProfile);
            return panel;
        }

        private static Button CreateProfileChoice(Transform parent, string caption, float x, float y,
            UnityEngine.Events.UnityAction action)
        {
            Button button = QuestionnaireUi.MakeButton(parent, "Gender" + caption, QuestionnaireUi.OptionBackground,
                12f, x, y, 154f, 48f, action);
            QuestionnaireUi.Label(button.transform, caption, 17, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Bold, 0f, 0f, 154f, 48f);
            return button;
        }

        private static InputField CreateProfileNumberInput(Transform parent, string label, string placeholder,
            string suffix, string range, float y)
        {
            QuestionnaireUi.Label(parent, label, 17, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, y, 120f, 28f);
            QuestionnaireUi.Label(parent, range, 13, QuestionnaireUi.Muted, TextAnchor.MiddleRight,
                FontStyle.Normal, 210f, y, 120f, 28f);

            Image background = QuestionnaireUi.Box(parent, label + "Input", Color.white, 12f,
                20f, y + 34f, 310f, 50f);
            background.raycastTarget = true;
            Image ring = QuestionnaireUi.Ring(background.transform, "InputBorder", new Color(.77f, .80f, .86f),
                12f, 1.2f);
            QuestionnaireUi.Stretch(ring.rectTransform, 0f, 0f, 0f, 0f);
            ring.raycastTarget = false;
            InputField input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.transition = Selectable.Transition.None;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.keyboardType = TouchScreenKeyboardType.NumberPad;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 3;
            input.shouldHideMobileInput = true;
            input.customCaretColor = true;
            input.caretColor = QuestionnaireUi.Accent;
            input.selectionColor = new Color(.29f, .53f, .85f, .25f);

            Text value = QuestionnaireUi.Label(background.transform, "", 17, QuestionnaireUi.Ink,
                TextAnchor.MiddleLeft, FontStyle.Normal, 16f, 0f, 232f, 50f);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            input.textComponent = value;
            Text hint = QuestionnaireUi.Label(background.transform, placeholder, 17, new Color(.58f, .62f, .70f),
                TextAnchor.MiddleLeft, FontStyle.Normal, 16f, 0f, 232f, 50f);
            input.placeholder = hint;
            QuestionnaireUi.Label(background.transform, suffix, 17, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Bold, 252f, 0f, 44f, 50f);
            ProfileInputFocusFeedback feedback = background.gameObject.AddComponent<ProfileInputFocusFeedback>();
            feedback.Initialize(ring);
            return input;
        }

        private void SelectGender(string gender, int selectedIndex)
        {
            selectedGender = gender;
            SetSelectedChoice(genderButtons, selectedIndex);
        }

        private void SaveProfile()
        {
            if (profileInputs != null)
                foreach (InputField input in profileInputs)
                    if (input != null) input.DeactivateInputField();
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
            bootstrap.Users.AppendPainRecord(user, new PainHistoryRecord
            {
                id = "intake:" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                recordedAt = System.DateTimeOffset.UtcNow.ToString("O"),
                painLevel = user.latestPainLevel,
                source = "AdmissionQuestionnaire",
                painParts = user.painParts,
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
            user.admissionCompletedAt = System.DateTimeOffset.Now.ToString("o");
            List<string> tags = new List<string>(user.userTags ?? System.Array.Empty<string>());
            tags.RemoveAll(tag => tag == "屈曲方向偏好" || tag == "伸展方向偏好");
            if (extensionResponse == "relieved" && flexionResponse != "relieved") tags.Add("伸展方向偏好");
            else if (flexionResponse == "relieved" && extensionResponse != "relieved") tags.Add("屈曲方向偏好");
            user.userTags = tags.ToArray();
            user.admissionReviewRequired = Mathf.Max(flexionAdmissionPain, extensionAdmissionPain) >= 8;
            user.admissionCompleted = true;
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
                pendingTrainingScene = sceneName;
                pendingTrainingActionId = actionId;
                bootstrap.Router.Navigate(AppRoute.PreTraining);
                return;
            }
            if (nativeBusy) return;
            if (NativeMobileService.Available && user.profile.activationBound)
            {
                nativeBusy = true;
                arTrainingMessage.text = "正在获取本次医生处方…";
                string account = user.accountId;
                NativeMobileService.Instance.Request("plan", new NativeRequest { accountId = account }, reply =>
                {
                    nativeBusy = false;
                    if (bootstrap.State.Session?.accountId != account) return;
                    if (!reply.ok) { arTrainingMessage.text = reply.error; return; }
                    LaunchPlan(JsonUtility.FromJson<TrainingPlan>(reply.dataJson));
                });
                return;
            }
            string coach = actionId == "bird_dog" ? "BirdDogPractice" : "DeadBugPractice";
            bool male = user.profile.gender == "男" || user.profile.gender == "male";
            coach = SpineFlowTrainingSession.ResolveSceneForGender(coach, male);
            LaunchPlan(new TrainingPlan
            {
                accountId = user.accountId,
                patientId = string.IsNullOrWhiteSpace(user.profile.patientId) ? user.accountId : user.profile.patientId,
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
            if (image != null)
            {
                // This InputField is only an interaction layer over the
                // approved phone PNG. Keep its Image transparent and detach
                // it from Selectable's targetGraphic so Unity cannot tint it
                // white on focus and cover the authored icon/border.
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = true;
            }
            input.targetGraphic = null;
            input.transition = Selectable.Transition.None;
            RoundedBorderGraphic border = input.GetComponentInChildren<RoundedBorderGraphic>(true);
            if (border != null)
            {
                border.SetBorderColor(new Color(1f, 1f, 1f, 0f));
                border.raycastTarget = false;
            }
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
                input.textComponent.font = AppBootstrap.Instance != null && AppBootstrap.Instance.uiFont != null
                    ? AppBootstrap.Instance.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                input.textComponent.fontSize = 17;
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
            // Use the same imported UI font as the authored phone screens.
            // LegacyRuntime.ttf is a bitmap-style fallback and becomes soft
            // after the portrait design root is scaled on Android.
            text.font = AppBootstrap.Instance != null && AppBootstrap.Instance.uiFont != null
                ? AppBootstrap.Instance.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
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
            // Keep the mask inside the field so it never paints over the
            // rounded border. The previous height reached the authored ring
            // and made focus look like a blue/white filled strip.
            maskRect.sizeDelta = new Vector2(235f, 34f);
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
            // Registration and activation pages need a visible neutral field ring.
            // The login page hides this ring after creating its raster overlay.
            border.SetBorderColor(new Color(0.78f, 0.81f, 0.87f, 1f));
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
                border.borderThickness = focused ? 3.2f : 0f;
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

    internal sealed class ProfileInputFocusFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Image border;

        public void Initialize(Image borderImage)
        {
            border = borderImage;
        }

        public void OnSelect(BaseEventData eventData)
        {
            SetFocused(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            SetFocused(false);
        }

        private void SetFocused(bool focused)
        {
            if (border != null)
                border.color = focused ? QuestionnaireUi.Accent : new Color(.77f, .80f, .86f);
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
