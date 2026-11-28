using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SceneLoadingOverlay : MonoBehaviour
{
    private static SceneLoadingOverlay instance;
    private AsyncOperation operation;
    private TMP_Text progressLabel;
    private Image progressFill;

    public static void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (instance != null) return;
        GameObject host = new GameObject("SceneLoadingOverlay");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<SceneLoadingOverlay>();
        instance.BuildUi();
        instance.StartCoroutine(instance.LoadRoutine(sceneName, mode));
    }

    private void BuildUi()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.02f, 0.025f, 0.04f, 0.98f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("CoachFlow/SourceHanSansSC-Heavy SDF");
        TMP_Text title = CreateText(panel.transform, font, "正在加载", 42, new Vector2(0, 70));
        title.alignment = TextAlignmentOptions.Center;
        TMP_Text detail = CreateText(panel.transform, font, "正在准备训练场景", 24, new Vector2(0, 10));
        detail.alignment = TextAlignmentOptions.Center;

        GameObject bar = new GameObject("ProgressBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(panel.transform, false);
        Image background = bar.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.16f);
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(620, 18);
        barRect.anchoredPosition = new Vector2(0, -55);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bar.transform, false);
        progressFill = fill.GetComponent<Image>();
        progressFill.color = new Color(0.2f, 0.75f, 0.65f, 1f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.sizeDelta = new Vector2(0, 0);

        progressLabel = CreateText(panel.transform, font, "0%", 22, new Vector2(0, -95));
        progressLabel.alignment = TextAlignmentOptions.Center;
    }

    private TMP_Text CreateText(Transform parent, TMP_FontAsset font, string value, float size, Vector2 position)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900, 70);
        rect.anchoredPosition = position;
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        return text;
    }

    private IEnumerator LoadRoutine(string sceneName, LoadSceneMode mode)
    {
        operation = SceneManager.LoadSceneAsync(sceneName, mode);
        if (operation == null)
        {
            Destroy(gameObject);
            instance = null;
            yield break;
        }

        operation.allowSceneActivation = false;
        while (operation.progress < 0.9f)
        {
            UpdateProgress(operation.progress / 0.9f);
            yield return null;
        }

        UpdateProgress(1f);
        yield return null;
        operation.allowSceneActivation = true;
        while (!operation.isDone) yield return null;
        Destroy(gameObject);
        instance = null;
    }

    private void UpdateProgress(float value)
    {
        value = Mathf.Clamp01(value);
        if (progressFill != null) progressFill.rectTransform.anchorMax = new Vector2(value, 1);
        if (progressLabel != null) progressLabel.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
