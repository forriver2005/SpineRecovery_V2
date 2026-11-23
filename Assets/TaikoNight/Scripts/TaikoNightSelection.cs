using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TaikoNight
{
    // The panel prefab contains the original RingSkinC menu graphics, buttons and layout.
    public sealed class TaikoNightSelection : MonoBehaviour
    {
        static readonly string[] Keys = { "hard", "standard", "simple" };
        static readonly string[] ButtonNames = { "Hard", "Standard", "Simple" };
        static readonly string[] Games = { "HipThrust_TaikoNight_Dynamic", "LyingCatPractice_TaikoNight", "CrunchPractice_TaikoNight" };
        static readonly string[] Titles = { "敲鼓臀桥", "卧位猫式", "卷腹" };
        static readonly Color Ink = new Color(.08f, .14f, .24f);
        readonly Button[] difficulties = new Button[3];
        RectTransform panel, dialog;
        Button start;
        TMP_Text startLabel, times, resultLabel;
        TMP_FontAsset font;
        AvatarMotionSource source;
        Animator patient;
        Sprite selected, normal;
        string title, steps, difficulty;
        float restSecondsRemaining;
        Func<int> readDose;
        Action begin;
        Action<string> change;
        Action workout;
        Func<bool> flowTestWithoutTracker;
        Func<bool> calibrationReady;

        public static TaikoNightSelection Create(Animator patient, TMP_FontAsset font, string title, string steps,
            Action<string> change, Func<int> dose, Action begin, Action workout = null,
            Func<bool> flowTestWithoutTracker = null, Func<bool> calibrationReady = null)
        {
            var host = new GameObject("Taiko Difficulty Selection", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2201;
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var view = host.AddComponent<TaikoNightSelection>();
            view.patient = patient; view.font = font; view.title = title; view.steps = steps;
            view.change = change; view.readDose = dose; view.begin = begin; view.workout = workout;
            view.flowTestWithoutTracker = flowTestWithoutTracker;
            view.calibrationReady = calibrationReady;
            view.Build();
            return view;
        }

        void Build()
        {
            var backdrop = Box(transform, "Backdrop", Color.black);
            Stretch(backdrop);
            panel = (RectTransform)Instantiate(Resources.Load<GameObject>("TaikoNight/TaikoDifficultyPanel"), backdrop, false).transform;
            // Match the reference framing on landscape screens; keep controls inside on 4:3.
            panel.anchorMin = new Vector2(.025f,.04f); panel.anchorMax = new Vector2(.975f,.96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            var identity = panel.Find("DBUI").GetComponent<Image>();
            identity.type = Image.Type.Filled; identity.fillMethod = Image.FillMethod.Vertical;
            identity.fillOrigin = (int)Image.OriginVertical.Bottom; identity.fillAmount = .39f;
            Label(panel, title, -400, 72, 480, 92, 51, Color.black);
            start = Bind("StartTraining", () => { if (HasInput()) begin(); });
            startLabel = start.GetComponentInChildren<TMP_Text>(true);
            selected = start.image.sprite;
            resultLabel = Label(panel, "", -400, -355, 570, 56, 28, Ink);
            times = panel.Find("Times").GetComponentInChildren<TMP_Text>(true);
            for (int i = 0; i < Keys.Length; i++)
            {
                string key = Keys[i];
                difficulties[i] = Bind(ButtonNames[i], () => SetDifficulty(key));
            }
            normal = difficulties[0].image.sprite;
            Bind("guide", OpenGuide);
            Bind("Return", OpenGames);
            var planButton = ExtraButton(panel, "动作串  >\n" + title, new Color(.19f,.29f,.40f), 0, 0, 210, 100, OpenWorkout, 25);
            Corner(planButton, false);
            var gamesButton = ExtraButton(panel, "动作选择", new Color(.25f,.22f,.42f), 0, 0, 225, 75, OpenGames, 29);
            Corner(gamesButton, true);
            SetDifficulty("standard");
            RefreshInput();
        }

        void SetDifficulty(string key)
        {
            change(key);
            SyncDifficulty(key);
        }
        public void SyncDifficulty(string key)
        {
            difficulty = key;
            for (int i = 0; i < difficulties.Length; i++) difficulties[i].image.sprite = Keys[i] == key ? selected : normal;
            times.text = readDose().ToString();
        }
        void OnEnable() { if (start != null) RefreshInput(); }
        void Update() => RefreshInput();
        bool HasInput()
        {
            bool tracking = HasTracking();
            bool bypass = flowTestWithoutTracker != null && flowTestWithoutTracker();
            return (tracking || bypass) && (calibrationReady == null || calibrationReady()) && restSecondsRemaining <= 0f;
        }
        bool HasTracking()
        {
            if (source == null)
                foreach (var candidate in FindObjectsOfType<AvatarMotionSource>())
                    if (candidate.Model == patient.gameObject) { source = candidate; break; }
            return source != null && source.isActiveAndEnabled && source.Model == patient.gameObject && source.HasFreshFrame;
        }
        void RefreshInput()
        {
            bool tracking = HasTracking();
            bool flowTest = !tracking && flowTestWithoutTracker != null && flowTestWithoutTracker();
            bool ready = HasInput();
            start.interactable = ready && restSecondsRemaining <= 0f;
            startLabel.text = restSecondsRemaining > 0f ? $"组间休息 · {Mathf.CeilToInt(restSecondsRemaining)} 秒" :
                tracking ? ready ? "开始游戏" : "请按提示完成姿态校准" : flowTest ? "流程测试（不会评分）" : "等待动作传感器连接";
            startLabel.enableAutoSizing = true;
            startLabel.fontSizeMin = 12;
            startLabel.fontSizeMax = 23;
        }
        public void SetResult(string message) => resultLabel.text = message;

        public void ResetResult() => resultLabel.text = "";

        public void SetGroupRest(float seconds)
        {
            restSecondsRemaining = Mathf.Max(0f, seconds);
            RefreshInput();
        }

        public void Show(bool visible)
        {
            if (gameObject.activeSelf == visible) return;
            CloseDialog();
            gameObject.SetActive(visible);
        }
        Button Bind(string name, Action click)
        {
            var button = panel.Find(name).GetComponent<Button>();
            button.onClick.AddListener(() => click());
            return button;
        }
        void OpenGuide()
        {
            OpenDialog("查看教程");
            Label(dialog, steps, 0, 30, 1080, 80, 42, Ink);
            Label(dialog, "跟随左侧教练动作。保持段结束后，跟随教练放下。\n传感器记录到达标动作时，鼓面会亮起。", 0, -75, 1080, 120, 30, Ink);
            DialogButton("跟随教练练习", -195, () => { CloseDialog(); begin(); });
        }
        void OpenGames()
        {
            OpenDialog("动作选择");
            for (int i = 0; i < Games.Length; i++)
            {
                string scene = Games[i];
                var button = DialogButton(Titles[i], 90 - i * 115, () => {
                    if (scene == gameObject.scene.name) CloseDialog();
                    else if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
                });
                button.interactable = scene == gameObject.scene.name || Application.CanStreamedLevelBeLoaded(scene);
            }
        }
        void OpenWorkout()
        {
            if (workout != null) { workout(); return; }
            OpenDialog("动作串");
            string level = difficulty == "hard" ? "进阶" : difficulty == "simple" ? "简单" : "标准";
            Label(dialog, title + "  ·  " + level, 0, 32, 980, 80, 44, Ink);
            Label(dialog, $"本组目标  {readDose()} 次", 0, -72, 980, 70, 34, Ink);
        }
        void OpenDialog(string heading)
        {
            CloseDialog();
            var shade = Box(transform, "Menu dialog", new Color(.035f,.055f,.13f,.85f)); Stretch(shade);
            dialog = Box(shade, heading, new Color(.91f,.96f,1)); Place(dialog, 0, 0, 1200, 620);
            Label(dialog, heading, 0, 224, 800, 80, 46, Ink);
            ExtraButton(dialog, "返回", new Color(.2f,.3f,.44f), 475, 224, 145, 65, CloseDialog, 26);
        }
        void CloseDialog()
        {
            if (dialog == null) return;
            var shade = dialog.parent.gameObject; shade.SetActive(false); Destroy(shade); dialog = null;
        }
        Button DialogButton(string value, float y, Action click) => ExtraButton(dialog,value,
            new Color(.12f,.52f,.73f),0,y,500,82,click,32);
        static RectTransform Box(Transform parent,string name,Color color)
        {
            var image = new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent,false); image.color=color; return image.rectTransform;
        }
        static void Stretch(RectTransform r) { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero; }
        static void Place(RectTransform r,float x,float y,float w,float h)
        {
            r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;
            r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(w,h);
        }
        static void Corner(Button button, bool right)
        {
            var r=(RectTransform)button.transform; r.anchorMin=r.anchorMax=r.pivot=new Vector2(right?1:0,1);
            r.anchoredPosition=new Vector2(right?-30:24,-24);
        }
        TMP_Text Label(Transform parent,string value,float x,float y,float w,float h,float size,Color color)
        {
            var text=new GameObject(value,typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent,false); Place(text.rectTransform,x,y,w,h); text.font=font; text.text=value;
            text.fontSize=size; text.color=color; text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false; return text;
        }
        Button ExtraButton(Transform parent,string value,Color color,float x,float y,float w,float h,Action click,float size)
        {
            var r=Box(parent,value,color); Place(r,x,y,w,h);
            var button=r.gameObject.AddComponent<Button>(); button.targetGraphic=r.GetComponent<Image>();
            button.onClick.AddListener(()=>click()); Label(r,value,0,0,w,h,size,Color.white); return button;
        }
    }
}
