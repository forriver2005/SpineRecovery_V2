using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// CoachingChoose keeps its original preview model and camera; this replaces its old
// three-action training selector with an offline library of recorded motions.
public sealed class CoachingActionLibrary : MonoBehaviour
{
    [Serializable]
    private sealed class Catalog
    {
        public Entry[] items;
    }

    [Serializable]
    private sealed class Entry
    {
        public string id;
        public string displayName;
        public string motionPath;
        public string source;
        public float viewAngle;
    }

    private const string SceneName = "CoachingChoose";
    private static string returnScene;

    private readonly Color background = Color.black;
    private readonly Color panelColor = Color.black;
    private readonly Color cardColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    private readonly Color selectedColor = new Color(0.22f, 0.22f, 0.22f, 1f);

    private Entry[] entries;
    private Image[] cards;
    private TMP_Text selectedName;
    private TMP_Text statusText;
    private TMP_FontAsset font;
    private Camera previewCamera;
    private Animator coachAnimator;
    private RenderTexture previewTexture;
    private TextAsset loadedMotion;
    private KeyframeCoachMotionBackend player;
    private int selectedIndex = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        returnScene = null;
        SceneManager.activeSceneChanged -= RememberSourceScene;
        SceneManager.activeSceneChanged += RememberSourceScene;
        SceneManager.sceneLoaded -= Install;
        SceneManager.sceneLoaded += Install;
    }

    private static void RememberSourceScene(Scene from, Scene to)
    {
        if (to.name == SceneName && from.IsValid() && IsPracticeScene(from.name))
        {
            returnScene = from.name;
        }
    }

    private static bool IsPracticeScene(string sceneName)
    {
        return sceneName == "DeadBugPractice" ||
            sceneName == "BirdDogPractice" ||
            sceneName == "maleDeadBugPractice" ||
            sceneName == "maleBirdDogPractice";
    }

    private static void Install(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneName && FindObjectOfType<CoachingActionLibrary>() == null)
        {
            new GameObject(nameof(CoachingActionLibrary)).AddComponent<CoachingActionLibrary>();
        }
    }

    private void Awake()
    {
        TextAsset catalogAsset = Resources.Load<TextAsset>("ActionLibrary/catalog");
        entries = catalogAsset != null
            ? JsonUtility.FromJson<Catalog>(catalogAsset.text)?.items
            : null;

        GameObject menu = GameObject.Find("ActionMenu");
        GameObject preview = GameObject.Find("DeadBUgPreview");
        if (menu == null || preview == null)
        {
            Debug.LogError("Action library could not find the CoachingChoose menu or coach preview.");
            return;
        }

        font = FindChineseFont();
        coachAnimator = preview.GetComponentInChildren<Animator>(true);
        DisableOldSelector(menu);
        PreparePreview();
        GameObject libraryCanvas = new GameObject(
            "ActionLibraryCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = libraryCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = libraryCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        BuildInterface(libraryCanvas.transform);

        if (entries == null || entries.Length == 0)
        {
            statusText.text = "动作库暂无录制动作";
        }
        else if (previewCamera == null)
        {
            statusText.text = "教练预览相机不可用";
        }
        else if (coachAnimator == null || coachAnimator.avatar == null || !coachAnimator.avatar.isHuman)
        {
            statusText.text = "教练模型无法播放录制动作";
            Debug.LogError("Action library coach preview needs a Humanoid avatar.");
        }
        else
        {
            Select(0);
        }
    }

    private void Update()
    {
        if (player == null || !player.IsReady)
        {
            return;
        }

        if (player.HasReachedSegmentEnd())
        {
            player.PlaySegment(0);
        }
        else
        {
            player.Tick(Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        player?.Dispose();
        if (loadedMotion != null) Resources.UnloadAsset(loadedMotion);
        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }
    }

    private static TMP_FontAsset FindChineseFont()
    {
        foreach (TMP_Text label in FindObjectsOfType<TMP_Text>(true))
        {
            if (label.font != null && label.font.name.Contains("SourceHanSans"))
            {
                return label.font;
            }
        }
        return TMP_Settings.defaultFontAsset;
    }

    private static void DisableOldSelector(GameObject menu)
    {
        CoachingActionSelectionController selector = menu.GetComponent<CoachingActionSelectionController>();
        if (selector != null) selector.enabled = false;
        CoachActionController oldCoach = menu.GetComponent<CoachActionController>();
        if (oldCoach != null) oldCoach.enabled = false;
        MobileCoachControls oldControls = menu.GetComponent<MobileCoachControls>();
        if (oldControls != null) oldControls.enabled = false;

        foreach (Transform child in menu.transform)
        {
            child.gameObject.SetActive(false);
        }
    }

    private void PreparePreview()
    {
        foreach (Camera camera in FindObjectsOfType<Camera>(true))
        {
            if (camera.name == "DeadBugPreviewCamera")
            {
                previewCamera = camera;
                previewCamera.gameObject.SetActive(true);
                previewCamera.enabled = true;
            }
            else if (camera.name == "BirdDogPreviewCamera" || camera.name == "HipThrustPreviewCamera")
            {
                camera.enabled = false;
            }
            else if (camera.name == "Main Camera")
            {
                camera.gameObject.SetActive(true);
                camera.enabled = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
            }
        }

        GameObject birdDog = GameObject.Find("BirdDogPreview");
        if (birdDog != null) birdDog.SetActive(false);
        GameObject hipThrust = GameObject.Find("HipThrustPreview");
        if (hipThrust != null) hipThrust.SetActive(false);

        if (previewCamera != null)
        {
            previewTexture = new RenderTexture(1120, 980, 24, RenderTextureFormat.ARGB32)
            {
                name = "ActionLibraryCoachPreview"
            };
            previewCamera.targetTexture = previewTexture;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.black;
        }
    }

    private void BuildInterface(Transform canvas)
    {
        RectTransform root = Rect(canvas, "ActionLibraryUI", 0, 0, 1920, 1080);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        Panel(root, "Background", 0, 0, 1920, 1080, background);
        Panel(root, "ListPanel", 48, 75, 560, 900, panelColor);
        Panel(root, "PreviewPanel", 640, 75, 1232, 900, panelColor);

        Button back = ButtonAt(root, "返回", 48, 990, 170, 62);
        back.onClick.AddListener(GoBack);
        Label(root, "动作库", 260, 987, 650, 68, 48, TextAlignmentOptions.Left);
        selectedName = Label(root, "", 680, 895, 1120, 58, 40, TextAlignmentOptions.Center);
        statusText = Label(root, "", 680, 127, 1120, 50, 27, TextAlignmentOptions.Center);

        if (previewTexture != null)
        {
            RectTransform imageRect = Rect(root, "教练动作", 750, 75, 1100, 962);
            RawImage image = imageRect.gameObject.AddComponent<RawImage>();
            image.texture = previewTexture;
            image.color = Color.white;
            image.raycastTarget = false;
            selectedName.transform.SetAsLastSibling();
            statusText.transform.SetAsLastSibling();
        }
        else
        {
            statusText.text = "教练预览相机不可用";
        }

        RectTransform viewport = Rect(root, "动作列表", 68, 95, 520, 860);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
        viewport.gameObject.AddComponent<RectMask2D>();
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;
        scroll.viewport = viewport;

        int count = entries?.Length ?? 0;
        RectTransform content = Rect(viewport, "内容", 0, 0, 520, Mathf.Max(860, count * 84));
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        scroll.content = content;

        cards = new Image[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            RectTransform row = Rect(content, entries[i].id, 0, -i * 84, 510, 76);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            cards[i] = row.gameObject.AddComponent<Image>();
            cards[i].color = cardColor;
            Button button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Select(index));
            Label(row, entries[i].displayName, 20, 8, 470, 60, 32, TextAlignmentOptions.Left);
        }
    }

    private void Select(int index)
    {
        if (index < 0 || index >= entries.Length || coachAnimator == null)
        {
            return;
        }

        player?.Dispose();
        player = null;
        if (loadedMotion != null)
        {
            Resources.UnloadAsset(loadedMotion);
            loadedMotion = null;
        }

        selectedIndex = index;
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].color = i == selectedIndex ? selectedColor : cardColor;
        }
        selectedName.text = entries[index].displayName;
        statusText.text = "";

        loadedMotion = Resources.Load<TextAsset>(entries[index].motionPath);
        if (loadedMotion == null)
        {
            statusText.text = "此动作的录制文件未找到";
            return;
        }

        CoachMotionPackage motion;
        try
        {
            motion = JsonUtility.FromJson<CoachMotionPackage>(loadedMotion.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Action library cannot read {entries[index].source}: {exception}");
            statusText.text = "此动作无法读取";
            return;
        }

        string error = "motion is empty";
        if (motion == null || !motion.IsValid(out error))
        {
            Debug.LogError($"Action library motion is invalid: {entries[index].source}: {error}");
            statusText.text = "此动作数据不完整";
            return;
        }

        player = new KeyframeCoachMotionBackend(coachAnimator, motion);
        if (!player.IsReady)
        {
            statusText.text = "教练模型无法播放此动作";
            return;
        }
        player.PlaySegment(0);
        FrameCoachFromCurrentPose();
    }

    private void FrameCoachFromCurrentPose()
    {
        Transform hips = coachAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = coachAnimator.GetBoneTransform(HumanBodyBones.Head);
        Transform leftShoulder = coachAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder) ??
            coachAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = coachAnimator.GetBoneTransform(HumanBodyBones.RightShoulder) ??
            coachAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (hips == null || head == null || leftShoulder == null || rightShoulder == null)
        {
            return;
        }

        Vector3 bodyUp = (head.position - hips.position).normalized;
        Vector3 bodyRight = (rightShoulder.position - leftShoulder.position).normalized;
        Vector3 front = Vector3.Cross(bodyRight, bodyUp).normalized;
        if (front.sqrMagnitude < 0.5f || bodyUp.sqrMagnitude < 0.5f)
        {
            return;
        }

        // Keep world up on screen so recorded lying poses stay horizontal.
        // Prone poses need a side view instead of a view through the floor.
        bool prone = front.y < -0.6f;
        float angle = prone && Mathf.Approximately(entries[selectedIndex].viewAngle, 0f)
            ? 90f : entries[selectedIndex].viewAngle;
        float radians = angle * Mathf.Deg2Rad;
        Vector3 viewDirection = (front * Mathf.Cos(radians) +
            bodyRight * Mathf.Sin(radians) + Vector3.up * (prone ? 0.45f : 0.12f)).normalized;
        Vector3 screenUp = Vector3.ProjectOnPlane(Vector3.up, viewDirection).normalized;
        if (screenUp.sqrMagnitude < 0.5f)
        {
            screenUp = Vector3.ProjectOnPlane(Vector3.forward, viewDirection).normalized;
        }

        Quaternion rotation = Quaternion.LookRotation(-viewDirection, screenUp);
        Transform[] bones =
        {
            hips, head,
            coachAnimator.GetBoneTransform(HumanBodyBones.LeftFoot),
            coachAnimator.GetBoneTransform(HumanBodyBones.RightFoot),
            coachAnimator.GetBoneTransform(HumanBodyBones.LeftHand),
            coachAnimator.GetBoneTransform(HumanBodyBones.RightHand)
        };
        Bounds bounds = new Bounds(hips.position, Vector3.zero);
        foreach (Transform bone in bones)
        {
            if (bone != null) bounds.Encapsulate(bone.position);
        }

        Vector3 target = bounds.center;
        Vector3 screenRight = rotation * Vector3.right;
        screenUp = rotation * Vector3.up;
        float tangent = Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = (float)previewTexture.width / previewTexture.height;
        float distance = 1.6f;
        foreach (Transform bone in bones)
        {
            if (bone == null) continue;
            Vector3 offset = bone.position - target;
            float depth = Vector3.Dot(offset, viewDirection);
            distance = Mathf.Max(distance,
                depth + Mathf.Abs(Vector3.Dot(offset, screenUp)) / tangent,
                depth + Mathf.Abs(Vector3.Dot(offset, screenRight)) / (tangent * aspect));
        }

        previewCamera.transform.SetPositionAndRotation(
            target + viewDirection * (distance * 1.08f), rotation);
    }

    private static void GoBack()
    {
        string destination = IsPracticeScene(returnScene)
            ? returnScene : SpineFlowTrainingSession.CurrentCoachScene;
        if (!IsPracticeScene(destination)) destination = "DeadBugPractice";
        if (!Application.CanStreamedLevelBeLoaded(destination))
        {
            Debug.LogError($"Action library return scene is unavailable: {destination}");
            return;
        }

        returnScene = null;
        DeadBugStartMenuController.ShowPracticeControlsOnNextLoad();
        SceneManager.LoadScene(destination);
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static Image Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        Image panel = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        panel.color = color;
        panel.raycastTarget = false;
        return panel;
    }

    private TMP_Text Label(Transform parent, string value, float x, float y, float width, float height,
        int size, TextAlignmentOptions alignment)
    {
        TMP_Text label = Rect(parent, value, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }

    private Button ButtonAt(Transform parent, string value, float x, float y, float width, float height)
    {
        RectTransform rect = Rect(parent, value, x, y, width, height);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = selectedColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        Label(rect, value, 0, 0, width, height, 32, TextAlignmentOptions.Center);
        return button;
    }
}
