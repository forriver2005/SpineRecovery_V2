using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UI = BirdDogDelivery.BirdDogUI;

namespace BirdDogDelivery
{
    /// <summary>Scene-local adapter. The imported scorer owns recognition and scoring;
    /// the game owns presentation, session controls and an independent recorded coach.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class BirdDogDeliveryGame : MonoBehaviour
    {
        public const string SceneName = "BirdDog_RingSkinC_Delivery";
        public const string OriginalSceneName = "BirdDog_RingSkinC_Recorded";
        public TMP_FontAsset font;
        public Animator userAnimator, coachAnimator;
        public Camera artworkCamera, userCamera, coachCamera;
        public GameObject artwork;
        public Material displayMaterial;
        public BirdDogRingPresentation ring;
        public BirdDogHoldScorer Scorer { get; private set; }
        public BirdDogAnatomicalConstraint Constraint { get; private set; }
        public RecordedBirdDogCoachMotion Coach { get; private set; }
        public AvatarMotionSource Input { get; private set; }
        public EVMC4U.ExternalReceiver Receiver { get; private set; }
        public Canvas Display { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Ready { get; private set; }
        public int CompletedRounds { get; private set; }
        public float AverageScore => CompletedRounds == 0 ? 0 : totalScore / CompletedRounds;
        public float BestScore { get; private set; }
        public string InitializationError { get; private set; }

        RenderTexture artTexture, coachTexture, userTexture;
        TMP_Text score, breakdown, status, guidance, feedback, roundInfo, coachLabel, userLabel, deduction, menuTitle, menuDescription, difficultyText;
        RectTransform holdFill, modal;
        GameObject startControls, pauseControls, resultControls, eventSystem;
        int coachSegment;
        float endpointTime, totalScore, feedbackTime;
        bool committed, exiting;
        HipThrustPhase lastPhase;
        readonly float[] holds = { 1, 7, .5f, 7, .5f, 1 };
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        static readonly Color Pink = new Color32(255,130,184,255), Mint = new Color32(180,240,229,255);

        void Start()
        {
            MobileCoachControls.IsPaused = false;
            Time.timeScale = 1; AudioListener.pause = false;
            try
            {
                InitializePresentation();
                Coach = coachAnimator.gameObject.AddComponent<RecordedBirdDogCoachMotion>(); Coach.Configure(coachAnimator,coachCamera.transform);
                Coach.CreateUserReferences(userAnimator,out var rest,out var right,out var left);
                // This component snapshots raw measurements before correcting the rendered rig.
                Constraint = userAnimator.gameObject.AddComponent<BirdDogAnatomicalConstraint>(); Constraint.Configure(userAnimator);
                InstallInput();
                Scorer = gameObject.AddComponent<BirdDogHoldScorer>(); Scorer.Configure(userAnimator,rest,right,left);
                Scorer.RoundCompleted += RoundCompleted; Scorer.PhaseChanged += PhaseChanged;
                ring.Initialize(); BuildUI(); Coach.PlaySegment(0); Ready = true;
                ShowStart();
            }
            catch (Exception error)
            {
                InitializationError = error.ToString(); Debug.LogException(error);
                if (Display == null) Display = UI.Canvas("Bird dog initialization",200,new Vector2(1920,900));
                var panel = UI.Box(Display.transform,"Initialization error",Vector2.zero,new Vector2(1180,420),UI.Panel);
                UI.Text(panel,font,"鸟狗场景初始化失败\n请检查模型、录制资源和场景引用",new Vector2(0,65),new Vector2(1100,150),34);
                UI.Button(panel,font,"返回原鸟狗游戏",new Vector2(0,-100),new Vector2(420,74),ReturnToOriginal);
            }
        }

        void InstallInput()
        {
            // Create exactly one receiver. No global rhythm/bootstrap component is present in this scene.
            Input = userAnimator.gameObject.AddComponent<AvatarMotionSource>(); Input.Configure(userAnimator); Input.SetAvailable(false);
            var connection = new GameObject("Bird dog VMC input · 39539"); connection.transform.SetParent(transform,false); connection.SetActive(false);
            Receiver = connection.AddComponent<EVMC4U.ExternalReceiver>();
            var server = connection.AddComponent<uOSC.uOscServer>(); server.port = 39539; server.autoStart = true;
            Receiver.Model = userAnimator.gameObject;
            Receiver.RootPositionSynchronize = Receiver.RootRotationSynchronize = Receiver.RootScaleOffsetSynchronize = false;
            Receiver.BonePositionSynchronize = Receiver.BonePositionFilterEnable = false;
            Receiver.BoneRotationFilterEnable = false;
            // Feed the complete body to the delivery constraint; it owns the limits.
            Receiver.CutBonesEnable = false;
            connection.AddComponent<VmcGameModeSourceBridge>().Configure(Receiver,Input);
            userAnimator.enabled = false;
            connection.SetActive(true);
        }

        void InitializePresentation()
        {
            const string bodyShader = "Knife/Built-in/Transparent Energy Body";
            Material bodyTemplate = null;
            foreach (var renderer in coachAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.shader.name == bodyShader) bodyTemplate = material;
            // Isolate material changes from shared original-game assets.
            foreach (var rig in new[] { coachAnimator,userAnimator })
                foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null)
                        {
                            // The old user hologram was designed for superimposing on a
                            // teacher. A separate preview needs the visible body material.
                            var source = rig == userAnimator && materials[i].shader.name == "Knife/Hologram Shader Unlit" && bodyTemplate != null ? bodyTemplate : materials[i];
                            materials[i] = new Material(source); ownedMaterials.Add(materials[i]);
                        }
                    renderer.sharedMaterials = materials;
                    bool energyBody = false;
                    foreach (var material in materials) if (material != null && material.shader.name == bodyShader) energyBody = true;
                    if (energyBody && renderer.sharedMesh != null)
                    {
                        var original = renderer.sharedMesh; var mesh = Instantiate(original); ownedMeshes.Add(mesh);
                        var uv = new List<Vector2>(original.vertexCount);
                        foreach (var vertex in original.vertices) uv.Add(new Vector2(vertex.x-original.bounds.center.x,0));
                        mesh.SetUVs(2,uv); renderer.sharedMesh = mesh;
                        foreach (var material in materials) if (material != null && material.shader.name == bodyShader) material.SetFloat("_VertexAnchorEnabled",1);
                    }
                }
            artworkCamera.orthographicSize = artwork.transform.lossyScale.x * 2.4f;
            artTexture = Texture("Bird dog artwork",1920,900,true);
            coachTexture = Texture("Bird dog recorded coach",900,460,false);
            userTexture = Texture("Bird dog user",900,460,false);
            artworkCamera.targetTexture = artTexture; artworkCamera.aspect = 1920f/900;
            coachCamera.targetTexture = coachTexture; coachCamera.aspect = 900f/460;
            userCamera.targetTexture = userTexture; userCamera.aspect = 900f/460;
            artworkCamera.enabled = coachCamera.enabled = userCamera.enabled = true;
            var background = artwork.transform.Find("Background"); var sky = background.Find("Sky").GetComponent<SpriteRenderer>();
            float cover = Mathf.Max(artworkCamera.orthographicSize*2/sky.bounds.size.y,artworkCamera.orthographicSize*2*artworkCamera.aspect/sky.bounds.size.x);
            background.localScale *= cover;
            if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            if (EventSystem.current == null)
            {
                eventSystem = new GameObject("Bird dog input",typeof(EventSystem),typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform,false);
            }
        }
        static RenderTexture Texture(string name,int width,int height,bool linear)
        {
            var texture = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default)
                { name = name, antiAliasing = 2 }; texture.Create(); return texture;
        }
        RawImage Picture(Transform parent,string name,Texture texture,Vector2 position,Vector2 size)
        {
            var image = new GameObject(name,typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(parent,false); image.texture = texture; image.raycastTarget = false;
            image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = size; return image;
        }
        TMP_Text Text(Transform parent,string value,float x,float y,float w,float h,float size) => UI.Text(parent,font,value,new Vector2(x,y),new Vector2(w,h),size);

        void BuildUI()
        {
            Display = UI.Canvas("Bird dog delivery game",160,new Vector2(1920,900));
            Picture(Display.transform,"Ring scene",artTexture,Vector2.zero,new Vector2(1920,900)).material = displayMaterial;
            var header = UI.Box(Display.transform,"Header",new Vector2(0,389),new Vector2(1820,76),new Color(.04f,.04f,.12f,.75f));
            Text(header,"鸟狗 · 对角伸展",-590,0,540,64,34);
            status = Text(header,"准备开始",40,0,660,58,25);
            UI.Button(header,font,"暂停",new Vector2(770,0),new Vector2(180,54),() => SetPaused(true),26);
            var scorePanel = UI.Box(Display.transform,"Scoring",new Vector2(-718,18),new Vector2(382,600),UI.Panel);
            Text(scorePanel,"当前动作得分",0,249,340,45,26);
            score = Text(scorePanel,"0.00",0,166,340,104,70); score.color = Mint;
            breakdown = Text(scorePanel,"基础 0  +  保持 0  −  扣分 0",0,87,352,66,24);
            var track = UI.Box(scorePanel,"Hold track",new Vector2(0,32),new Vector2(320,10),new Color(.25f,.25f,.4f));
            holdFill = UI.Box(track,"Valid hold",Vector2.zero,new Vector2(0,10),Mint);
            holdFill.anchorMin = holdFill.anchorMax = new Vector2(0,.5f); holdFill.pivot = new Vector2(0,.5f);
            difficultyText = Text(scorePanel,"",0,-12,346,62,23);
            deduction = Text(scorePanel,"等待动作数据",0,-112,346,128,22);
            roundInfo = Text(scorePanel,"已完成 0 轮\n上一轮 —",0,-237,346,72,25);
            var coachPanel = UI.Box(Display.transform,"Recorded coach",new Vector2(706,184),new Vector2(440,296),UI.Panel);
            coachLabel = Text(coachPanel,"教练示范 · 四点支撑",0,117,410,42,25);
            Picture(coachPanel,"Recorded motion",coachTexture,new Vector2(0,-17),new Vector2(430,220));
            var userPanel = UI.Box(Display.transform,"Tracked user",new Vector2(706,-137),new Vector2(440,296),UI.Panel);
            userLabel = Text(userPanel,"我的动作 · 等待连接",0,117,410,42,25);
            Picture(userPanel,"Tracked motion",userTexture,new Vector2(0,-17),new Vector2(430,220));
            feedback = Text(Display.transform,"A → B → A",0,292,840,60,35); feedback.color = Mint;
            var footer = UI.Box(Display.transform,"Guidance",new Vector2(0,-369),new Vector2(1820,104),UI.Panel);
            guidance = Text(footer,"先四点支撑，再伸展一组对角手腿，最后收回",0,16,1760,50,29);
            Text(footer,"圆环：左上左臂 · 右上右臂 · 左下左腿 · 右下右腿　｜　教练独立循环，可按自己的节奏练习",0,-26,1760,36,20);

            var scrim = UI.Box(Display.transform,"Session menu",Vector2.zero,new Vector2(8000,8000),new Color(0,0,.06f,.72f));
            modal = UI.Box(scrim,"Card",Vector2.zero,new Vector2(1100,660),UI.Panel);
            menuTitle = Text(modal,"鸟狗 · 对角伸展",0,250,1000,80,44);
            menuDescription = Text(modal,"",0,104,1000,186,28);
            startControls = new GameObject("Start controls",typeof(RectTransform)); startControls.transform.SetParent(modal,false);
            string[] labels = { "轻松 · 3 秒", "标准 · 5 秒", "挑战 · 7 秒" };
            HipThrustDifficultyMode[] modes = { HipThrustDifficultyMode.Easy,HipThrustDifficultyMode.Medium,HipThrustDifficultyMode.Hard };
            for (int i = 0; i < 3; i++)
            {
                var mode = modes[i]; UI.Button(startControls.transform,font,labels[i],new Vector2((i-1)*320,-68),new Vector2(294,68),() => SelectDifficulty(mode),27);
            }
            UI.Button(startControls.transform,font,"开始训练",new Vector2(0,-177),new Vector2(650,78),BeginGame,31);
            UI.Button(startControls.transform,font,"返回原鸟狗游戏",new Vector2(0,-269),new Vector2(650,56),ReturnToOriginal,24);
            pauseControls = new GameObject("Pause controls",typeof(RectTransform)); pauseControls.transform.SetParent(modal,false);
            UI.Button(pauseControls.transform,font,"继续训练",new Vector2(0,-55),new Vector2(650,76),() => SetPaused(false),30);
            UI.Button(pauseControls.transform,font,"结束并查看成绩",new Vector2(0,-158),new Vector2(650,76),FinishGame,28);
            UI.Button(pauseControls.transform,font,"返回原鸟狗游戏",new Vector2(0,-261),new Vector2(650,56),ReturnToOriginal,24);
            resultControls = new GameObject("Result controls",typeof(RectTransform)); resultControls.transform.SetParent(modal,false);
            UI.Button(resultControls.transform,font,"再练一次",new Vector2(0,-85),new Vector2(650,78),ShowStart,30);
            UI.Button(resultControls.transform,font,"返回原鸟狗游戏",new Vector2(0,-198),new Vector2(650,68),ReturnToOriginal,27);
        }

        public void SelectDifficulty(HipThrustDifficultyMode mode)
        {
            if (Running) return;
            Scorer.SetDifficultyMode(mode); RefreshStartDescription();
        }
        void RefreshStartDescription()
        {
            string level = Scorer.DifficultyMode == HipThrustDifficultyMode.Easy ? "轻松" : Scorer.DifficultyMode == HipThrustDifficultyMode.Hard ? "挑战" : "标准";
            menuDescription.text = $"四点支撑 A → 对角手腿伸展 B → 收回到 A\n当前：{level}　基础 {Scorer.EntryBaseScore:0} 分 + 保持最多 {Scorer.MaximumHoldBonus:0} 分\n有效保持 {Scorer.TargetHoldSeconds:0} 秒可加满；姿态扣分合计最多 5 分\n任一侧均可开始，回到 A 后可继续同侧或换侧";
        }
        public void ShowStart()
        {
            Running = false; SetPausedInternal(false); Scorer.CancelSession();
            menuTitle.text = "鸟狗 · 对角伸展"; RefreshStartDescription(); ShowModal(startControls);
        }
        void ShowModal(GameObject controls)
        {
            modal.parent.gameObject.SetActive(true);
            startControls.SetActive(controls == startControls); pauseControls.SetActive(controls == pauseControls); resultControls.SetActive(controls == resultControls);
        }
        public void BeginGame()
        {
            if (!Ready || Running) return;
            CompletedRounds = 0; totalScore = BestScore = 0; committed = false; lastPhase = HipThrustPhase.WaitingForRaise;
            SetPausedInternal(false);
            if (!Scorer.BeginContinuousSession()) { menuDescription.text = "录制动作参考初始化失败，请返回重试"; return; }
            Running = true; modal.parent.gameObject.SetActive(false); feedbackTime = 0;
            coachSegment = 0; endpointTime = 0; Coach.PlaySegment(0);
        }
        public void SetPaused(bool paused)
        {
            if (!Ready || !Running || Paused == paused) return;
            SetPausedInternal(paused);
            if (paused)
            {
                menuTitle.text = "已暂停";
                menuDescription.text = $"已完成 {CompletedRounds} 轮　平均 {AverageScore:0.00} 分\n动作识别、保持计时与教练示范已暂停\n继续后，以恢复接收的动作数据为准";
                ShowModal(pauseControls);
            }
            else modal.parent.gameObject.SetActive(false);
        }
        void SetPausedInternal(bool paused)
        {
            Paused = MobileCoachControls.IsPaused = paused;
            if (Receiver != null) Receiver.Freeze = paused;
            if (Input != null) Input.Freeze = paused;
            if (Coach != null) { if (paused) Coach.Freeze(); else Coach.Resume(); }
        }
        public void FinishGame()
        {
            if (!Running) return;
            Running = false; Scorer.CancelSession(); SetPausedInternal(true);
            menuTitle.text = "本次训练成绩";
            menuDescription.text = CompletedRounds == 0 ? "尚未完成一个完整的 A → B → A\n本次没有已结算动作" :
                $"已完成 {CompletedRounds} 轮\n平均分 {AverageScore:0.00}　最高分 {BestScore:0.00}\n仅统计完成回到 A 的轮次";
            ShowModal(resultControls);
        }
        public void ReturnToOriginal()
        {
            if (exiting) return; exiting = true; Running = false;
            Scorer?.CancelSession(); SetPausedInternal(false); Time.timeScale = 1; AudioListener.pause = false;
            SceneManager.LoadSceneAsync(OriginalSceneName);
        }
        void RoundCompleted(float value)
        {
            committed = true; CompletedRounds++; totalScore += value; BestScore = Mathf.Max(BestScore,value);
            Notify($"完成一轮 · {value:0.00} 分",Mint,2.5f);
        }
        void PhaseChanged(HipThrustPhase phase)
        {
            if (Running && phase == HipThrustPhase.Holding) { ring.Hit(Scorer.LockedSide); Notify("HIT · 对角伸展已识别",Mint,1.6f); }
            if (Running && phase == HipThrustPhase.WaitingForRaise && lastPhase != HipThrustPhase.WaitingForRaise)
            {
                if (!committed) Notify("MISS · 未完成，请回到 A",Pink,2.5f);
                committed = false;
            }
            lastPhase = phase;
        }
        void Notify(string message,Color color,float seconds) { if (feedback == null) return; feedback.text = message; feedback.color = color; feedbackTime = seconds; }

        void Update()
        {
            if (!Ready) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && Running) SetPaused(!Paused);
            if (Paused) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime,.1f);
            Coach.Tick(dt);
            if (Coach.HasReachedSegmentEnd())
            {
                endpointTime += dt;
                if (endpointTime >= holds[coachSegment]) { endpointTime = 0; coachSegment = (coachSegment+1)%6; Coach.PlaySegment(coachSegment); }
            }
            feedbackTime = Mathf.Max(0,feedbackTime-dt);
        }
        void LateUpdate()
        {
            if (!Ready) return;
            ring.Render(Scorer,Constraint.InputPose,Paused ? 0 : Mathf.Min(Time.unscaledDeltaTime,.1f),Running);
            if (Paused) return;
            status.text = !Running ? "选择难度后开始" : !Scorer.TrackingAvailable ? "等待动作连接" : Scorer.Phase == HipThrustPhase.Holding ? "B · 保持对角伸展" :
                Scorer.Phase == HipThrustPhase.Lowering ? "收回 · 回到 A 结算" : Scorer.Phase == HipThrustPhase.Raising ? "伸展 · 对角手腿一起抬起" : Scorer.IsArmed ? "A · 已准备" : "A · 四点支撑准备";
            score.text = Scorer.CurrentLiveComparisonScore.ToString("0.00");
            breakdown.text = $"基础 {Scorer.BaseScore:0}  +  保持 {Scorer.HoldBonus:0.00}\n− 姿态扣分 {Scorer.CurrentDeduction:0.00}";
            holdFill.sizeDelta = new Vector2(320*Mathf.Clamp01(Scorer.ValidHoldSeconds/Scorer.TargetHoldSeconds),10);
            difficultyText.text = $"有效保持 {Scorer.ValidHoldSeconds:0.0} / {Scorer.TargetHoldSeconds:0} 秒";
            deduction.text = Scorer.TrackingAvailable ? Scorer.DeductionReasons : "等待动作数据\n请确认设备已连接";
            roundInfo.text = $"已完成 {CompletedRounds} 轮\n上一轮 " + (Scorer.HasCompletedRound ? $"{Scorer.LastRoundBestScore:0.00} 分" : "—");
            if (Running) guidance.text = Scorer.Guidance;
            userLabel.text = "我的动作 · " + (Scorer.TrackingAvailable ? "已连接" : "等待连接");
            string[] names = { "四点支撑", "右臂 + 左腿伸展", "右臂 + 左腿收回", "左臂 + 右腿伸展", "左臂 + 右腿收回", "四点支撑" };
            coachLabel.text = "教练示范 · " + names[coachSegment];
            if (feedbackTime <= 0) { feedback.text = Scorer.Phase == HipThrustPhase.Holding ? "保持稳定，积累有效时间" : "A → B → A"; feedback.color = Mint; }
        }
        void OnApplicationPause(bool paused) { if (paused && Running) SetPaused(true); }
        void OnDestroy()
        {
            MobileCoachControls.IsPaused = false;
            if (Scorer != null) { Scorer.RoundCompleted -= RoundCompleted; Scorer.PhaseChanged -= PhaseChanged; }
            if (Display != null) Destroy(Display.gameObject);
            if (artworkCamera != null) artworkCamera.targetTexture = null;
            if (coachCamera != null) coachCamera.targetTexture = null;
            if (userCamera != null) userCamera.targetTexture = null;
            foreach (var texture in new[] { artTexture,coachTexture,userTexture }) if (texture != null) { texture.Release(); Destroy(texture); }
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
            foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
