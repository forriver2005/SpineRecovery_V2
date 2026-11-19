using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BetweenActionCalibrationPanel : MonoBehaviour
{
    const string AssetsPath = "BetweenActionCalibration/";
    GameObject panel;
    RawImage picture;
    TMP_Text prompt, timer, stepLabel;
    Button retry;
    AudioSource voice;
    TMP_FontAsset font;
    Action advance;
    AndroidJavaObject attempt;
    int phase, lastCount;
    float nextPoll, deadline;
    // Match the integration app's light surface, blue ink and cyan actions.
    static readonly Color Page = new Color(.961f, .973f, 1f);
    static readonly Color Ink = new Color(.10f, .12f, .18f);
    static readonly Color Accent = new Color(.290f, .525f, .851f);
    static readonly Color Primary = new Color(.22f, .74f, .97f);
    Sprite rounded;
    Texture2D roundedTexture;

    public void Initialize(TMP_FontAsset labelFont, Action onComplete)
    {
        font = labelFont;
        advance = onComplete;
        panel = new GameObject("动作间校准", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = Page;
        CreateRoundedSprite();
        var title = Text(panel.transform, "动作间校准", 435, 36);
        title.text = "动作间校准";
        title.color = Accent;
        var card = Element<Image>(panel.transform, "校准卡片", 0, 20, 1120, 680);
        card.sprite = rounded;
        card.type = Image.Type.Sliced;
        card.color = Color.white;
        card.raycastTarget = false;
        stepLabel = Text(panel.transform, "校准步骤", 310, 25);
        stepLabel.color = Accent;
        prompt = Text(panel.transform, "姿势提示", 235, 32);
        timer = Text(panel.transform, "倒计时", -235, 30);
        timer.color = Accent;
        picture = Element<RawImage>(panel.transform, "姿势示意", 0, 0, 350, 350);
        picture.raycastTarget = false;
        voice = panel.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        AddButton("返回结算", -210, false).onClick.AddListener(Close);
        retry = AddButton("重新校准", 210, true);
        retry.onClick.AddListener(Open);
        panel.SetActive(false);
    }

    void CreateRoundedSprite()
    {
        const int size = 64;
        const float radius = 30f;
        roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        roundedTexture.wrapMode = TextureWrapMode.Clamp;
        roundedTexture.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x + .5f - size / 2f) - (size / 2f - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(y + .5f - size / 2f) - (size / 2f - radius), 0f);
            pixels[y * size + x] = new Color(1f, 1f, 1f,
                Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy)));
        }
        roundedTexture.SetPixels(pixels);
        roundedTexture.Apply(false, true);
        rounded = Sprite.Create(roundedTexture, new Rect(0, 0, size, size),
            new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }

    static T Element<T>(Transform parent, string name, float x, float y, float width, float height) where T : Component
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = new Vector2(x,y);
        rect.sizeDelta = new Vector2(width,height);
        return rect.gameObject.AddComponent<T>();
    }

    TMP_Text Text(Transform parent, string name, float y, float size)
    {
        var label = Element<TextMeshProUGUI>(parent, name, 0, y, parent == panel.transform ? 1550 : 340, 100);
        label.font = font;
        label.fontSize = size;
        label.color = Ink;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    Button AddButton(string caption, float x, bool primary)
    {
        var image = Element<Image>(panel.transform, caption, x, -380, 360, 70);
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.color = primary ? Primary : new Color(.906f, .941f, .988f);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var label = Text(image.transform, caption, 0, 28);
        label.text = caption;
        label.color = primary ? Color.white : Accent;
        return button;
    }

    public void Open()
    {
        if (attempt != null) return;
        panel.SetActive(true);
        retry.gameObject.SetActive(false);
        try
        {
            attempt = new AndroidJavaObject("com.metaspine.training.BetweenActionCalibration");
            ShowPhase(1);
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void ShowPhase(int step)
    {
        phase = step;
        bool standing = step == 1;
        stepLabel.text = standing ? "第 1 步 / 共 2 步 · 站立校准" : "第 2 步 / 共 2 步 · 滑雪姿势校准";
        timer.color = Accent;
        prompt.text = standing ? "请站直，面向前方，双臂自然下垂" : "请做滑雪姿势，屈膝、上身前倾、手臂弯曲";
        timer.text = "按图就位后保持不动";
        lastCount = 0;
        var texture = Resources.Load<Texture2D>(AssetsPath + (standing ? "Standing" : "Ski"));
        picture.texture = texture;
        picture.enabled = texture != null;
        if (texture != null)
        {
            float scale = Mathf.Min(800f / texture.width, 350f / texture.height);
            picture.rectTransform.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
        }
        Speak(standing ? "Stand" : "Bend");
        deadline = nextPoll + 90;
    }

    void Speak(string clip)
    {
        voice.clip = Resources.Load<AudioClip>(AssetsPath + clip);
        voice.Play();
        nextPoll = Time.realtimeSinceStartup + (voice.clip != null ? voice.clip.length : 1);
    }

    void Update()
    {
        if (attempt == null || Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + .12f;
        if (phase == 3)
        {
            Close();
            advance();
            return;
        }
        try
        {
            int step = attempt.Call<int>("phase");
            if (step < 0 || Time.realtimeSinceStartup > deadline)
            {
                Fail(step < 0 ? "请检查追踪器连接和佩戴状态" : "未能确认姿势，请检查佩戴并重试");
                return;
            }
            if (step == 3)
            {
                phase = step;
                stepLabel.text = "2 / 2 · 已完成";
                prompt.text = "校准完成";
                timer.text = "即将进入下一项训练";
                Speak("Done");
                return;
            }
            if (phase != step) { ShowPhase(step); return; }
            int count = attempt.Call<int>("remaining");
            if (count != lastCount)
            {
                timer.text = count > 0 ? "保持不动　" + count : "按图就位后保持不动";
                if (count > 0) voice.PlayOneShot(Resources.Load<AudioClip>(AssetsPath + "Tick"));
                lastCount = count;
            }
            attempt.Call("poll");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void Fail(string detail)
    {
        Release();
        voice.Stop();
        prompt.text = "校准未完成";
        stepLabel.text = "请调整后重试";
        timer.color = new Color(.69f, .10f, .10f);
        timer.text = detail.StartsWith("请") || detail.StartsWith("未能") ? detail : "请检查 SlimeVR 连接并重试";
        picture.enabled = false;
        retry.gameObject.SetActive(true);
        Debug.LogWarning("动作间校准：" + detail);
    }

    void Release()
    {
        if (attempt == null) return;
        try { attempt.Call("close"); }
        finally { attempt.Dispose(); attempt = null; }
    }

    void Close()
    {
        Release();
        if (panel != null) { voice.Stop(); panel.SetActive(false); }
    }

    void OnApplicationPause(bool paused) { if (paused && attempt != null) Close(); }
    void OnDisable() { Close(); }

    void OnDestroy()
    {
        Release();
        if (rounded != null) Destroy(rounded);
        if (roundedTexture != null) Destroy(roundedTexture);
    }
}
