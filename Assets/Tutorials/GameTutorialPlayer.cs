using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A deterministic, narrated demonstration. Never reads tracking or writes workout results.</summary>
[DefaultExecutionOrder(100)]
public sealed class GameTutorialPlayer : MonoBehaviour
{
    public TutorialLesson lesson;
    public TMP_FontAsset font;
    public GameObject artwork;
    public Camera artworkCamera, coachCamera;
    public Animator coachAnimator;
    public Material displayMaterial, spriteMaterial;
    public Sprite[] noteBodies;
    public RingBridge.RingBridgeChart bridgeChart;
    public RecordedDrumArtwork drumArtwork;
    public HipThrustPhaseTimeline bridgeTimeline;
    public string limbUpState = "birddog_1_up", limbDownState = "birddog_1_down";
    public string bridgeState = "hipthrust_routine";
    public int StepIndex { get; private set; }
    public float StepElapsed { get; private set; }
    public bool Paused { get; private set; }
    public bool Completed { get; private set; }
    public AudioSource Narrator { get; private set; }
    public Canvas Display { get; private set; }
    public string Feedback => feedback != null ? feedback.text : "";
    RenderTexture artTexture, coachTexture;
    TMP_Text title, subtitle, chapter, phase, detail, feedback, pauseText, coachHeading;
    RectTransform progress;
    Button previous, next;
    SpriteRenderer[] highlights = new SpriteRenderer[4], notes = new SpriteRenderer[2];
    bool leaving;
    GameObject localEventSystem;
    static readonly Color Pink = new Color(1, .76f, .82f), Yellow = new Color(1, .92f, .66f), Mint = new Color(.71f, .94f, .9f);

    void Start()
    {
        if (EventSystem.current == null)
        {
            localEventSystem = new GameObject("Tutorial EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(localEventSystem, gameObject.scene);
        }
        Narrator = gameObject.AddComponent<AudioSource>();
        Narrator.playOnAwake = false; Narrator.spatialBlend = 0; Narrator.ignoreListenerPause = true;
        if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        coachAnimator.enabled = true; coachAnimator.speed = 0; coachAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        artTexture = new RenderTexture(1920, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 4, name = "Tutorial artwork" };
        coachTexture = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, name = "Tutorial coach" };
        artTexture.Create(); coachTexture.Create();
        artworkCamera.targetTexture = artTexture; artworkCamera.aspect = 1920f / 900;
        coachCamera.targetTexture = coachTexture; coachCamera.aspect = 16f / 9;
        artworkCamera.enabled = coachCamera.enabled = true;
        if (bridgeChart != null) bridgeChart.autoPlay = false;
        if (lesson.kind == TutorialKind.Limbs)
        {
            InstallLimbLabels();
            var background = artwork.transform.Find("Background");
            var sky = background.Find("Sky").GetComponent<SpriteRenderer>();
            float cover = Mathf.Max(artworkCamera.orthographicSize * 2 / sky.bounds.size.y,
                artworkCamera.orthographicSize * 2 * artworkCamera.aspect / sky.bounds.size.x);
            background.localScale *= cover;
        }
        BuildUI();
        GoToStep(0);
    }
    void BuildUI()
    {
        Display = TutorialUI.Canvas("Automatic tutorial", 4000, new Vector2(1920, 1080));
        SceneManager.MoveGameObjectToScene(Display.gameObject, gameObject.scene);
        var background = TutorialUI.Box(Display.transform, "Input shield", Vector2.zero, Vector2.zero, new Color(.025f, .025f, .08f));
        background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one; background.offsetMin = background.offsetMax = Vector2.zero;
        title = TutorialUI.Text(Display.transform, font, lesson.title, new Vector2(-220, 478), new Vector2(1280, 62), 38);
        title.alignment = TextAlignmentOptions.Left;
        TutorialUI.Button(Display.transform, font, "返回游戏", new Vector2(755, 478), new Vector2(250, 66), ReturnToGame);
        TutorialUI.Text(Display.transform, font, "自动演示  /  无需追踪器  /  不计入成绩", new Vector2(-300, 408), new Vector2(1180, 40), 25).color = new Color(.7f, .77f, .94f);
        var art = RawImage(Display.transform, "Copied game artwork", artTexture, new Vector2(-275, 80), new Vector2(1260, 590));
        art.material = displayMaterial;
        var card = TutorialUI.Box(Display.transform, "Coach card", new Vector2(650, 95), new Vector2(460, 610), TutorialUI.Panel);
        coachHeading = TutorialUI.Text(card, font, "教练自动示范", new Vector2(0, 254), new Vector2(420, 50), 31);
        RawImage(card, "Recorded coach", coachTexture, new Vector2(0, 80), new Vector2(432, 243));
        phase = TutorialUI.Text(card, font, "准备", new Vector2(0, -93), new Vector2(420, 54), 36);
        detail = TutorialUI.Text(card, font, "", new Vector2(0, -202), new Vector2(410, 128), 26);
        feedback = TutorialUI.Text(Display.transform, font, "", new Vector2(-290, -220), new Vector2(1160, 65), 37);
        var captions = TutorialUI.Box(Display.transform, "Chinese captions", new Vector2(0, -350), new Vector2(1760, 184), TutorialUI.Panel);
        chapter = TutorialUI.Text(captions, font, "", new Vector2(0, 60), new Vector2(1660, 40), 27); chapter.color = Yellow;
        subtitle = TutorialUI.Text(captions, font, "", new Vector2(0, -20), new Vector2(1660, 108), 28);
        var track = TutorialUI.Box(captions, "Chapter progress", new Vector2(0, -90), new Vector2(1760, 5), new Color(.2f, .2f, .34f));
        progress = TutorialUI.Box(track, "Fill", Vector2.zero, Vector2.zero, Pink);
        progress.anchorMin = Vector2.zero; progress.anchorMax = Vector2.one; progress.offsetMin = progress.offsetMax = Vector2.zero;
        previous = TutorialUI.Button(Display.transform, font, "上一节", new Vector2(-620, -493), new Vector2(250, 66), () => GoToStep(StepIndex - 1));
        TutorialUI.Button(Display.transform, font, "重播本节", new Vector2(-310, -493), new Vector2(250, 66), () => GoToStep(StepIndex));
        pauseText = TutorialUI.Button(Display.transform, font, "暂停讲解", new Vector2(0, -493), new Vector2(250, 66), TogglePause).GetComponentInChildren<TMP_Text>();
        next = TutorialUI.Button(Display.transform, font, "下一节", new Vector2(310, -493), new Vector2(250, 66), () => GoToStep(StepIndex + 1));
        TutorialUI.Button(Display.transform, font, "从头观看", new Vector2(620, -493), new Vector2(250, 66), () => GoToStep(0));
    }
    static RawImage RawImage(Transform parent, string name, Texture texture, Vector2 pos, Vector2 size)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false); image.texture = texture; image.raycastTarget = false;
        image.rectTransform.anchoredPosition = pos; image.rectTransform.sizeDelta = size; return image;
    }
    public void GoToStep(int index)
    {
        if (lesson == null || lesson.steps.Length == 0 || leaving) return;
        StepIndex = Mathf.Clamp(index, 0, lesson.steps.Length - 1); StepElapsed = 0; Completed = Paused = false;
        var step = lesson.steps[StepIndex];
        Narrator.Stop(); Narrator.clip = step.voice;
        if (step.voice != null) Narrator.Play();
        chapter.text = $"{StepIndex + 1:00} / {lesson.steps.Length:00}   {step.title}";
        subtitle.text = step.narration; pauseText.text = "暂停讲解";
        previous.interactable = StepIndex > 0; next.interactable = StepIndex < lesson.steps.Length - 1;
        RenderDemonstration();
    }
    public void TogglePause()
    {
        if (Completed || leaving) return;
        Paused = !Paused; pauseText.text = Paused ? "继续讲解" : "暂停讲解";
        if (Paused) Narrator.Pause(); else Narrator.UnPause();
    }
    void Update()
    {
        if (Display == null || leaving) return;
        if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
        if (!Paused && !Completed)
        {
            StepElapsed += Time.unscaledDeltaTime;
            if (StepElapsed >= lesson.steps[StepIndex].Duration)
            {
                if (StepIndex + 1 < lesson.steps.Length) GoToStep(StepIndex + 1);
                else { Completed = true; Narrator.Stop(); pauseText.text = "讲解完成"; }
            }
        }
        progress.anchorMax = new Vector2(Mathf.Clamp01(StepElapsed / lesson.steps[StepIndex].Duration), 1);
        RenderDemonstration();
    }
    void RenderDemonstration()
    {
        var demo = lesson.steps[StepIndex].demo;
        coachHeading.text = demo == TutorialDemo.Miss ? "未达标动作示例" : demo == TutorialDemo.Interrupted ? "连续保持演示" : "教练自动示范";
        if (lesson.kind == TutorialKind.Limbs) RenderLimbs(); else RenderBridge();
    }
    void RenderLimbs()
    {
        var demo = lesson.steps[StepIndex].demo;
        float clock = Mathf.Repeat(StepElapsed, 7);
        bool miss = demo == TutorialDemo.Miss;
        bool notesVisible = demo == TutorialDemo.Approach || demo == TutorialDemo.Hit || miss;
        float extension = miss ? 0 : clock < 3 ? clock / 3 : clock < 4 ? 1 : 1 - (clock - 4) / 3;
        coachAnimator.Play(clock < 4 || miss ? limbUpState : limbDownState, 0, miss ? 0 : clock < 4 ? extension : 1 - extension);
        coachAnimator.Update(0);
        phase.text = miss ? "未完成伸展 · 示例" : clock < 4 ? "伸展" : "收回";
        phase.color = clock < 4 ? Pink : new Color(.8f, .74f, 1);
        detail.text = notesVisible ? "音符接近圆环时\n完成对应肢体的动作" : "录制动画自动播放\n当前不读取用户动作";
        int selected = demo >= TutorialDemo.LeftArm && demo <= TutorialDemo.RightLeg ? (int)demo - (int)TutorialDemo.LeftArm : -1;
        for (int i = 0; i < highlights.Length; i++)
        {
            if (highlights[i] == null) continue;
            float flash = i == selected ? .48f + .3f * Mathf.Sin(StepElapsed * 3) :
                demo == TutorialDemo.Hit && (i == 0 || i == 3) && clock > 3 && clock < 4 ? .8f * (4 - clock) : 0;
            var color = highlights[i].color; color.a = flash; highlights[i].color = color;
        }
        for (int i = 0; i < notes.Length; i++)
        {
            if (notes[i] == null) continue;
            notes[i].enabled = notesVisible && clock < 3.8f;
            float angle = (i == 0 ? 135 : 315) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(3.25f, 1.43f, Mathf.Clamp01(clock / 3));
            notes[i].transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -.01f);
            var color = notes[i].color; color.a = clock < 3 ? Mathf.Clamp01(clock * 3) : Mathf.Clamp01((3.8f - clock) / .8f); notes[i].color = color;
        }
        feedback.text = selected >= 0 ? new[] { "左上 · 左臂", "右上 · 右臂", "左下 · 左腿", "右下 · 右腿" }[selected] :
            notesVisible && clock >= 3 ? miss ? "MISS · 对应肢体未到位" : "HIT · 对应肢体到位" : notesVisible ? "音符正在接近判定区域…" : "四个区域，对应四个肢体";
        feedback.color = miss ? new Color(1, .55f, .6f) : Mint;
    }
    void RenderBridge()
    {
        var demo = lesson.steps[StepIndex].demo;
        var cycle = bridgeTimeline.cycles[0];
        float t = Mathf.Repeat(StepElapsed, 8), u;
        int p;
        if (demo == TutorialDemo.Hold30)
        {
            t = Mathf.Min(StepElapsed, 34);
            p = t < 2 ? 0 : t < 32 ? 1 : 2;
            u = p == 0 ? t / 2 : p == 1 ? (t - 2) / 30 : (t - 32) / 2;
        }
        else if (demo == TutorialDemo.Lift) { p = 0; u = Mathf.Clamp01(StepElapsed / 4); }
        else if (demo == TutorialDemo.Hold || demo == TutorialDemo.Interrupted) { p = 1; u = Mathf.Clamp01(StepElapsed / lesson.steps[StepIndex].Duration); }
        else if (demo == TutorialDemo.Lower || (demo == TutorialDemo.Hit && lesson.kind == TutorialKind.HoldBridge))
        { p = 2; u = Mathf.Clamp01(StepElapsed / 4); }
        else { p = t < 2 ? 0 : t < 5 ? 1 : 2; u = p == 0 ? t / 2 : p == 1 ? (t - 2) / 3 : (t - 5) / 3; }
        bool missed = demo == TutorialDemo.Miss;
        bool interrupted = demo == TutorialDemo.Interrupted && StepElapsed >= 4 && StepElapsed < 7;
        float frame = p == 0 ? Mathf.Lerp(cycle.liftStartFrame, cycle.holdStartFrame, u) : p == 1 ?
            HipThrustHold30Timeline.PeakFrame(bridgeTimeline, 0) : Mathf.Lerp(cycle.lowerStartFrame, cycle.lowerEndFrame, u);
        if (missed) frame = Mathf.Lerp(cycle.liftStartFrame, cycle.holdStartFrame, .12f);
        else if (interrupted) frame = Mathf.Lerp(cycle.liftStartFrame, cycle.holdStartFrame, .25f);
        coachAnimator.Play(bridgeState, 0, frame / bridgeTimeline.sourceClip.frameRate / bridgeTimeline.sourceClip.length); coachAnimator.Update(0);
        var sample = new HipThrustPhaseTimeline.Sample { phase = p, cycle = 0, progress = u,
            poseHeight = p == 0 ? u : p == 1 ? 1 : 1 - u, holdSeconds = p == 1 ? StepElapsed : 0, completedSeconds = Vector3.one * 999 };
        bridgeChart.RenderRecorded(sample);
        if (drumArtwork != null) drumArtwork.Render(sample);
        phase.text = new[] { "抬起", "保持", "放下" }[p]; phase.color = new[] { Pink, Yellow, new Color(.8f, .74f, 1) }[p];
        feedback.color = Mint;
        detail.text = "圆环：教练示范阶段\n模型：自动演示动作\n数值：教程示意";
        feedback.text = "抬起 → 保持 → 放下";
        if (demo == TutorialDemo.Hold30)
        {
            float held = Mathf.Clamp(StepElapsed - 2, 0, 30);
            feedback.text = $"示例有效保持   {held:0.0} / 30.0 秒";
            detail.text = $"未加速 · 完整 30 秒示范\n示范剩余 {Mathf.Max(0, 30 - held):0.0} 秒\n动作达标才计有效时间";
        }
        else if (demo == TutorialDemo.Interrupted)
        {
            float held = StepElapsed < 4 ? StepElapsed : StepElapsed < 7 ? 0 : StepElapsed - 7;
            feedback.text = $"示例连续有效时间   {held:0.0} 秒";
            detail.text = interrupted ? "持续偏离要求\n连续保持清零\n等待重新达标" : StepElapsed < 4 ? "达标中 · 正在计时" : "恢复达标\n重新累计连续时间";
            if (interrupted) { phase.text = "保持中断 · 示例"; feedback.color = new Color(1, .55f, .6f); }
        }
        else if (missed)
        { feedback.text = "MISS · 未达标示意 · 有效时间 0 秒"; feedback.color = new Color(1, .55f, .6f); detail.text = "教练阶段继续推进\n示例姿势未达到要求\n等待不等于完成"; }
        else if (demo == TutorialDemo.Hit)
        { feedback.text = "HIT · 达标示意"; detail.text = lesson.kind == TutorialKind.HoldBridge ? "已完成有效保持\n跟随放下，回到起始位" : $"示例本轮最高分 {(p == 0 ? 0 : Mathf.RoundToInt(Mathf.Lerp(80, 100, p == 1 ? u : 1)))} / 100\n正式游戏以实际评分为准"; }
        else if (demo == TutorialDemo.Hold) feedback.text = "保持阶段：动作质量 + 有效时间 → 实际评分";
    }
    void InstallLimbLabels()
    {
        string[] zones = { "melody", "harmony", "bass", "drums" }, labels = { "左臂", "右臂", "左腿", "右腿" };
        Rect[] glyphs = { new Rect(358, 346, 53, 58), new Rect(792, 345, 42, 60), new Rect(351, 789, 72, 36), new Rect(786, 766, 54, 55) };
        Color[] colors = { Pink, Yellow, Mint, Color.white };
        float scale = 0;
        for (int i = 0; i < 4; i++)
        {
            var zone = artwork.transform.Find("Ring/Zone_" + zones[i]);
            var source = zone.Find("Base").GetComponent<SpriteRenderer>(); var sprite = source.sprite; var glyph = glyphs[i];
            Vector2 Uv(float x, float y) => new Vector2((sprite.textureRect.x + x / 1200 * sprite.textureRect.width) / sprite.texture.width,
                (sprite.textureRect.y + (1 - y / 1200) * sprite.textureRect.height) / sprite.texture.height);
            var min = Uv(glyph.xMin - 4, glyph.yMax + 4); var max = Uv(glyph.xMax + 4, glyph.yMin - 4); var fill = Uv(glyph.xMin - 6, glyph.yMin - 6);
            var centerUV = Uv(600, 600); var edge = Uv(940, 260); var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block);
            block.SetVector("_SymbolRect", new Vector4(min.x, min.y, max.x, max.y)); block.SetVector("_SymbolFillUV", new Vector4(fill.x, fill.y, 0, 0));
            block.SetVector("_SymbolCircle", new Vector4(centerUV.x, centerUV.y, edge.x - centerUV.x, edge.y - centerUV.y)); source.SetPropertyBlock(block);
            var text = new GameObject("Tutorial limb " + labels[i]).AddComponent<TextMeshPro>(); text.transform.SetParent(source.transform, false);
            text.gameObject.layer = source.gameObject.layer; text.font = font; text.text = labels[i]; text.fontSize = 10; text.color = colors[i];
            text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false; text.rectTransform.sizeDelta = Vector2.one * 10; text.ForceMeshUpdate();
            if (i == 0) scale = 1.3f * (glyph.width / 1200 * sprite.rect.width / sprite.pixelsPerUnit) / text.textBounds.size.x;
            Vector3 center = (Vector2.Scale(new Vector2(glyph.center.x / 1200, 1 - glyph.center.y / 1200), sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
            text.transform.localScale = Vector3.one * scale; text.transform.localPosition = center - text.textBounds.center * scale;
            var renderer = text.GetComponent<MeshRenderer>(); renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = source.sortingOrder + 1;
            highlights[i] = zone.Find("HitFlash").GetComponent<SpriteRenderer>();
        }
        for (int i = 0; i < 2; i++)
        {
            var note = new GameObject("Tutorial automatic note").AddComponent<SpriteRenderer>(); note.transform.SetParent(artwork.transform, false);
            note.gameObject.layer = artwork.transform.Find("Ring/Zone_melody/Base").gameObject.layer;
            note.sprite = noteBodies[i == 0 ? 0 : 3]; note.sharedMaterial = spriteMaterial; note.sortingOrder = 80;
            note.transform.localScale = Vector3.one * .65f; notes[i] = note;
        }
    }
    public void ReturnToGame()
    {
        if (leaving) return; leaving = true; Narrator.Stop();
        if (!string.IsNullOrEmpty(TutorialPauseNavigation.ReturnScene) && SceneManager.GetSceneByName(TutorialPauseNavigation.ReturnScene).isLoaded)
            StartCoroutine(UnloadTutorial());
        else
        {
            TutorialPauseNavigation.Returned(); Time.timeScale = 1; AudioListener.pause = false;
            SceneManager.LoadSceneAsync(lesson.defaultGame);
        }
    }
    IEnumerator UnloadTutorial()
    {
        TutorialPauseNavigation.Returned();
        yield return SceneManager.UnloadSceneAsync(gameObject.scene);
    }
    void OnApplicationPause(bool paused) { if (paused && !Paused && !Completed && Narrator != null) TogglePause(); }
    void OnDestroy()
    {
        if (Display != null) Destroy(Display.gameObject);
        if (localEventSystem != null) Destroy(localEventSystem);
        if (artworkCamera != null) artworkCamera.targetTexture = null;
        if (coachCamera != null) coachCamera.targetTexture = null;
        if (artTexture != null) { artTexture.Release(); Destroy(artTexture); }
        if (coachTexture != null) { coachTexture.Release(); Destroy(coachTexture); }
    }
}
