using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BetweenActionCalibrationPanel : MonoBehaviour
{
    const string AssetsPath = "BetweenActionCalibration/";
    GameObject panel;
    RawImage picture;
    TMP_Text prompt, timer;
    Button retry;
    AudioSource voice;
    TMP_FontAsset font;
    Action advance;
    AndroidJavaObject attempt;
    int phase, lastCount;
    float nextPoll, deadline;

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
        panel.GetComponent<Image>().color = new Color(.09f,.08f,.18f);
        prompt = Text(panel.transform, "姿势提示", 355, 46);
        timer = Text(panel.transform, "倒计时", -245, 38);
        picture = Element<RawImage>(panel.transform, "姿势示意", 0, 60, 500, 500);
        picture.raycastTarget = false;
        voice = panel.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        AddButton("返回结算", -210).onClick.AddListener(Close);
        retry = AddButton("重新校准", 210);
        retry.onClick.AddListener(Open);
        panel.SetActive(false);
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
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    Button AddButton(string caption, float x)
    {
        var image = Element<Image>(panel.transform, caption, x, -380, 360, 70);
        image.color = new Color(.4f,.31f,.67f);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Text(image.transform, caption, 0, 30).text = caption;
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
        prompt.text = standing ? "请站直，面向前方，双臂自然下垂" : "请做滑雪姿势，屈膝、上身前倾、手臂弯曲";
        timer.text = "按图就位后保持不动";
        lastCount = 0;
        var texture = Resources.Load<Texture2D>(AssetsPath + (standing ? "Standing" : "Ski"));
        picture.texture = texture;
        picture.enabled = texture != null;
        if (texture != null)
            picture.rectTransform.sizeDelta = new Vector2(500f * texture.width / texture.height, 500);
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
}
