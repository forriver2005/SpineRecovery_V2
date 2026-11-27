using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Adds tutorial links at runtime without changing any production scene or controller.</summary>
public sealed class TutorialPauseNavigation : MonoBehaviour
{
    public static readonly string[] Scenes = { "Tutorial_Limbs", "Tutorial_BridgeDynamic", "Tutorial_BridgeHold" };
    public static readonly string[] Titles = { "鸟狗 / 死虫教程", "臀桥 / 侧桥动态教程", "臀桥 / 侧桥保持教程" };
    public static bool IsTutorialOpen { get; private set; }
    public static bool IsLoading { get; private set; }
    public static string ReturnScene { get; private set; }
    GameWorkoutScene entry;
    GameTestMenu menu;
    Canvas fallback;
    GameObject fallbackPanel, pauseButton;
    bool installed;
    static TutorialPauseNavigation owner;
    bool IsPaused => entry != null && (entry.bridgeWave!=null?entry.bridgeWave.State==BridgeWave.BridgeWaveFollowController.RunState.Paused:
        entry.taikoNightAction!=null?TaikoNight.TaikoNightPracticeClock.IsPaused:entry.taiko!=null?entry.taiko.IsPaused:entry.coach!=null&&entry.coach.IsActionPaused);
    void TogglePause(){if(entry.bridgeWave!=null)entry.bridgeWave.TogglePause();else if(entry.taikoNightAction!=null)entry.taikoNightAction.TogglePause();else if(entry.taiko!=null)entry.taiko.TogglePause();else entry.coach?.TogglePause();}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        IsTutorialOpen = IsLoading = false; ReturnScene = null; owner = null;
        SceneManager.sceneLoaded -= Attach;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap() { SceneManager.sceneLoaded += Attach; }
    static void Attach(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(GameTestMenu.Scenes, scene.name) < 0) return;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var value in root.GetComponentsInChildren<GameWorkoutScene>(true))
                if (value.GetComponent<TutorialPauseNavigation>() == null)
                    value.gameObject.AddComponent<TutorialPauseNavigation>().entry = value;
    }
    void Update()
    {
        if (entry == null) return;
        if (!installed) Install();
        if (fallbackPanel != null) fallbackPanel.SetActive(IsPaused && !IsTutorialOpen);
        if (pauseButton != null) pauseButton.SetActive(!IsTutorialOpen && !entry.IsMenuVisible && !IsPaused);
        if (IsTutorialOpen || menu != null) return;
        if (Input.GetKeyDown(KeyCode.Escape) && !entry.IsMenuVisible)
        { TogglePause(); }
    }
    void Install()
    {
        menu = entry.GetComponent<GameTestMenu>();
        if (menu != null && menu.Panel == null) return;
        fallback = TutorialUI.Canvas("Tutorial navigation", 2000, new Vector2(1920, 1080));
        SceneManager.MoveGameObjectToScene(fallback.gameObject, gameObject.scene);
        if (entry.taiko == null && entry.taikoNightAction == null && entry.bridgeWave == null)
        {
            var button = TutorialUI.Button(fallback.transform, entry.font, "暂停", Vector2.zero, new Vector2(150, 60), () =>
            { if (menu != null) menu.Open(); else TogglePause(); });
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.anchoredPosition = new Vector2(-120, -70);
            pauseButton = button.gameObject;
        }
        if (menu != null)
        {
            var card = (RectTransform)menu.Panel.transform.Find("Card");
            card.sizeDelta = new Vector2(card.sizeDelta.x, 1040);
            foreach (RectTransform child in card) child.anchoredPosition += Vector2.up * 80;
            AddLinks(card, -365, -425, -485);
        }
        else
        {
            var panel = TutorialUI.Box(fallback.transform, "Tutorial links in pause", new Vector2(0, -330), new Vector2(1280, 260), TutorialUI.Panel);
            fallbackPanel = panel.gameObject;
            AddLinks(panel, 86, 18, -46);
            if (entry.taiko == null)
                TutorialUI.Button(panel, entry.font, "继续游戏", new Vector2(0, -96), new Vector2(280, 44), () => TogglePause(), 24);
        }
        installed = true;
    }
    void AddLinks(Transform parent, float titleY, float buttonsY, float hintY)
    {
        TutorialUI.Text(parent, entry.font, "玩法教程 · 中文语音自动演示", new Vector2(0, titleY), new Vector2(1180, 44), 27);
        for (int i = 0; i < Scenes.Length; i++)
        {
            int index = i;
            var button = TutorialUI.Button(parent, entry.font, Titles[i], new Vector2((i - 1) * 400, buttonsY), new Vector2(378, 62), () => Open(index), 25);
            button.name = "Open " + Scenes[i];
        }
        TutorialUI.Text(parent, entry.font, "观看期间保留本局进度，返回后仍为暂停状态", new Vector2(0, hintY), new Vector2(1180, 40), 23);
    }
    public void Open(int index)
    {
        if (IsLoading || IsTutorialOpen || index < 0 || index >= Scenes.Length) return;
        if (!Application.CanStreamedLevelBeLoaded(Scenes[index])) { Debug.LogError("Tutorial scene missing from build: " + Scenes[index]); return; }
        if (!IsPaused)
        { if (menu != null) menu.Open(); else TogglePause(); }
        // Keep the existing scene loaded and paused. Never cancel its workout or scoring session.
        ReturnScene = gameObject.scene.name; IsLoading = IsTutorialOpen = true; owner = this;
        // Prevent the original menu from also processing Escape while the tutorial is on top.
        if (menu != null) menu.enabled = false;
        StartCoroutine(Load(Scenes[index]));
    }
    IEnumerator Load(string name)
    {
        yield return SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
        IsLoading = false;
    }
    public static void Returned()
    {
        if (owner != null && owner.menu != null) owner.menu.enabled = true;
        owner = null; IsTutorialOpen = IsLoading = false; ReturnScene = null;
    }
    void OnDestroy() { if (fallback != null) Destroy(fallback.gameObject); }
}
