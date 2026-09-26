using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ReplayRuntimeInstaller
{
    private const string PlaybackSceneName = "PlayBack";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.Equals(scene.name, PlaybackSceneName, StringComparison.OrdinalIgnoreCase))
        {
            Install();
        }
    }

    public static void Install()
    {
        ReplaySceneBootstrap existing =
            UnityEngine.Object.FindObjectOfType<ReplaySceneBootstrap>(true);
        if (existing != null)
        {
            return;
        }

        MotionPlaybackController legacyPlayback =
            UnityEngine.Object.FindObjectOfType<MotionPlaybackController>(true);
        Animator userAnimator = legacyPlayback != null
            ? legacyPlayback.UserAnimator
            : null;
        Animator coachAnimator = legacyPlayback != null
            ? legacyPlayback.CoachAnimator
            : null;
        Camera camera = Camera.main ??
            UnityEngine.Object.FindObjectOfType<Camera>(true);
        ConfigureMobileCamera(camera, userAnimator, coachAnimator);
        if (legacyPlayback != null)
        {
            legacyPlayback.enabled = false;
        }

        foreach (CoachActionController controller in
                 UnityEngine.Object.FindObjectsOfType<CoachActionController>(true))
        {
            controller.enabled = false;
        }

        foreach (AutoGroundRootFromAvatar grounding in
                 UnityEngine.Object.FindObjectsOfType<AutoGroundRootFromAvatar>(true))
        {
            grounding.enabled = false;
        }

        var rootObject = new GameObject("ReplayRoot");
        Transform replayRoot = rootObject.transform;
        replayRoot.SetPositionAndRotation(
            CalculateAuthoredReplayOrigin(
                userAnimator != null ? userAnimator.transform : null,
                coachAnimator != null ? coachAnimator.transform : null),
            Quaternion.identity);

        GameObject menu = GameObject.Find("ActionMenu");
        if (menu != null)
        {
            foreach (Button button in menu.GetComponentsInChildren<Button>(true))
            {
                string buttonName = button.gameObject.name;
                if (string.Equals(buttonName, "PlayAction1Button", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "ReturnIdle", StringComparison.OrdinalIgnoreCase))
                {
                    button.gameObject.SetActive(false);
                }
            }
        }

        ReplayPlayer player = rootObject.AddComponent<ReplayPlayer>();
        ReplayPlacementController placement =
            rootObject.AddComponent<ReplayPlacementController>();
        ReplayUIController ui = InstallUi(menu, player);
        ConfigureMobileCanvas(menu);
        ReplaySceneBootstrap bootstrap = rootObject.AddComponent<ReplaySceneBootstrap>();

        placement.Configure(
            replayRoot,
            player,
            camera);
        placement.ConfigureContent(
            userAnimator != null ? userAnimator.transform : null,
            coachAnimator != null ? coachAnimator.transform : null,
            menu != null ? menu.transform : null);
        if (Application.isEditor)
        {
            // The editor is the authored scene preview, not a spatial
            // session. Keep the exact scene positions so entering PlayBack
            // cannot move the models or the menu outside the Game view.
            placement.UseConfiguredPoseWithoutSpatialRelocation();
        }
        bootstrap.Configure(
            player,
            placement,
            ui,
            replayRoot,
            userAnimator,
            coachAnimator);
    }

    public static Vector3 CalculateAuthoredReplayOrigin(
        Transform userRoot,
        Transform coachRoot)
    {
        if (userRoot == null && coachRoot == null)
        {
            return Vector3.zero;
        }

        Vector3 userPosition = userRoot != null
            ? userRoot.position
            : coachRoot.position;
        Vector3 coachPosition = coachRoot != null
            ? coachRoot.position
            : userRoot.position;
        Vector3 midpoint = (userPosition + coachPosition) * 0.5f;
        return new Vector3(midpoint.x, 0f, midpoint.z);
    }

    private static void ConfigureMobileCamera(
        Camera camera,
        Animator userAnimator,
        Animator coachAnimator)
    {
        if (camera == null)
        {
            return;
        }

        camera.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 5f),
            camera.transform.rotation);
        camera.fieldOfView = 51.38676f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = Color.black;

        Transform userRoot = userAnimator != null ? userAnimator.transform : null;
        Transform coachRoot = coachAnimator != null ? coachAnimator.transform : null;
        Vector3 target = userRoot != null
            ? userRoot.position
            : coachRoot != null
                ? coachRoot.position
                : camera.transform.position + camera.transform.forward * 2f;
        if (coachRoot != null && userRoot != null)
        {
            target = (userRoot.position + coachRoot.position) * 0.5f;
        }

        target += Vector3.up * 0.8f;

        Vector3 direction = target - camera.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            camera.transform.rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);
        }
    }

    private static ReplayUIController InstallUi(GameObject menu, ReplayPlayer player)
    {
        GameObject host = menu != null ? menu : new GameObject("ReplayUI");
        ReplayUIController ui = host.GetComponent<ReplayUIController>();
        if (ui == null)
        {
            ui = host.AddComponent<ReplayUIController>();
        }

        TMP_Text error = CreateLabel(host.transform, "ReplayError", new Vector2(0f, 125f), 20f);
        error.color = new Color(1f, 0.35f, 0.3f, 1f);
        error.gameObject.SetActive(false);

        // Preserve the authored page. Replay logic is attached to the existing
        // controls instead of generating a second row of debug-style buttons.
        Button play = FindButton(host.transform, "Play");
        Button pause = FindButton(host.transform, "Pause");
        PlaybackProgressSlider progress =
            host.GetComponentInChildren<PlaybackProgressSlider>(true);
        Slider slider = progress != null ? progress.GetComponent<Slider>() : null;
        progress?.Configure(player);

        ui.Configure(
            player,
            null,
            null,
            error,
            play,
            pause,
            null,
            null,
            null,
            slider,
            string.IsNullOrWhiteSpace(ReplaySessionContext.SessionId));
        return ui;
    }

    private static void ConfigureMobileCanvas(GameObject menu)
    {
        if (menu == null)
        {
            return;
        }

        Canvas canvas = menu.GetComponent<Canvas>();
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;

        CanvasScaler scaler = menu.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform root = menu.GetComponent<RectTransform>();
        if (root != null)
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }

        SetMobileRect(menu.transform, "Play", new Vector2(0f, 1f),
            new Vector2(160f, -100f), new Vector2(190f, 70f));
        SetMobileRect(menu.transform, "Pause", new Vector2(0f, 1f),
            new Vector2(390f, -100f), new Vector2(190f, 70f));
        SetMobileRect(menu.transform, "PlaybackProgressSlider", new Vector2(0f, 1f),
            new Vector2(900f, -100f), new Vector2(800f, 30f));

        SetMobileRect(menu.transform, "Practice", new Vector2(1f, 1f),
            new Vector2(-140f, -240f), new Vector2(190f, 70f));
        SetMobileRect(menu.transform, "Home", new Vector2(1f, 1f),
            new Vector2(-140f, -350f), new Vector2(190f, 70f));
        SetMobileRect(menu.transform, "Game", new Vector2(1f, 1f),
            new Vector2(-140f, -460f), new Vector2(190f, 70f));
    }

    private static void SetMobileRect(
        Transform root,
        string objectName,
        Vector2 anchor,
        Vector2 position,
        Vector2 size)
    {
        Transform target = FindChild(root, objectName);
        RectTransform rect = target != null
            ? target.GetComponent<RectTransform>()
            : null;
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static Transform FindChild(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static TMP_Text CreateLabel(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        float fontSize)
    {
        Transform existing = parent.Find(name);
        TextMeshProUGUI label;
        if (existing != null)
        {
            label = existing.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);
            label = labelObject.AddComponent<TextMeshProUGUI>();
        }

        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(620f, 44f);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = fontSize;
        label.enableWordWrapping = false;
        return label;
    }

    private static Button FindButton(Transform root, string name)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (string.Equals(button.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }
}
