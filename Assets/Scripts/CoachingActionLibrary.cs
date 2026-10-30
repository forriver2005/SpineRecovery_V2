using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Plays recorded motions in the dedicated ActionLibrary scene.
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

    private const string SceneName = "ActionLibrary";
    private static string returnScene;
    private static Scene retainedPracticeScene;
    private static GameObject[] retainedPracticeRoots;
    private static bool[] retainedRootActiveStates;

    private readonly Color cardColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    private readonly Color selectedColor = new Color(0.22f, 0.22f, 0.22f, 1f);

    private Entry[] entries;
    private Image[] cards;
    [SerializeField] private TMP_Text selectedName;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button backButton;
    [SerializeField] private RectTransform listContent;
    [SerializeField] private Button rowTemplate;
    [SerializeField] private RawImage previewImage;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private GameObject coachPreview;
    private Button startPracticeButton;
    private Animator coachAnimator;
    private RenderTexture previewTexture;
    private TextAsset loadedMotion;
    private KeyframeCoachMotionBackend player;
    private int selectedIndex = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        returnScene = null;
        retainedPracticeScene = default;
        retainedPracticeRoots = null;
        retainedRootActiveStates = null;
        SceneManager.activeSceneChanged -= RememberSourceScene;
        SceneManager.activeSceneChanged += RememberSourceScene;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
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
        return RecordedAlternatingCoachMode.IsRecordedScene(sceneName) || LumbarCoachMode.ForScene(sceneName) != null ||
            sceneName == "DeadBugPractice" ||
            sceneName == "BirdDogPractice" ||
            sceneName == "maleDeadBugPractice" ||
            sceneName == "maleBirdDogPractice" ||
            sceneName == "HipThrustPractice" ||
            sceneName == "maleHipThrustPractice";
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneName)
        {
            if (retainedPracticeScene.IsValid() && retainedPracticeScene.isLoaded)
            {
                retainedPracticeRoots = retainedPracticeScene.GetRootGameObjects();
                retainedRootActiveStates = new bool[retainedPracticeRoots.Length];
                SceneManager.SetActiveScene(scene);
                for (int i = 0; i < retainedPracticeRoots.Length; i++)
                {
                    retainedRootActiveStates[i] = retainedPracticeRoots[i].activeSelf;
                    retainedPracticeRoots[i].SetActive(false);
                }
            }
        }
    }

    public static bool OpenFromPractice()
    {
        Scene practiceScene = SceneManager.GetActiveScene();
        if (!IsPracticeScene(practiceScene.name) ||
            !Application.CanStreamedLevelBeLoaded(SceneName))
        {
            return false;
        }

        if (retainedPracticeScene.IsValid() && retainedPracticeScene.isLoaded)
        {
            return true;
        }

        retainedPracticeScene = practiceScene;
        returnScene = practiceScene.name;
        if (SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive) == null)
        {
            retainedPracticeScene = default;
            return false;
        }

        return true;
    }

    private void Awake()
    {
        TextAsset catalogAsset = Resources.Load<TextAsset>("ActionLibrary/catalog");
        entries = catalogAsset != null
            ? JsonUtility.FromJson<Catalog>(catalogAsset.text)?.items
            : null;

        coachAnimator = coachPreview.GetComponentInChildren<Animator>(true);
        PreparePreview();
        BindInterface();

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

    private void PreparePreview()
    {
        previewTexture = new RenderTexture(1120, 980, 24, RenderTextureFormat.ARGB32)
        {
            name = "ActionLibraryCoachPreview"
        };
        previewCamera.targetTexture = previewTexture;
        previewImage.texture = previewTexture;
    }

    private void BindInterface()
    {
        backButton.onClick.AddListener(GoBack);
        CreateStartPracticeButton();
        int count = entries?.Length ?? 0;
        listContent.sizeDelta = new Vector2(520f, Mathf.Max(860, count * 84));
        cards = new Image[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            Button button = i < listContent.childCount
                ? listContent.GetChild(i).GetComponent<Button>()
                : Instantiate(rowTemplate, listContent);
            button.gameObject.name = entries[i].id;
            button.gameObject.SetActive(true);
            ((RectTransform)button.transform).anchoredPosition = new Vector2(0f, -i * 84f);
            button.GetComponentInChildren<TMP_Text>().text = entries[i].displayName;
            cards[i] = button.GetComponent<Image>();
            cards[i].color = cardColor;
            button.onClick.AddListener(() => Select(index));
        }
        for (int i = count; i < listContent.childCount; i++)
        {
            listContent.GetChild(i).gameObject.SetActive(false);
        }
    }

    private void CreateStartPracticeButton()
    {
        GameObject buttonObject = new GameObject(
            "StartPracticeButton", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(previewImage.transform, false);

        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-32f, 32f);
        rect.sizeDelta = new Vector2(240f, 76f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.10f, 0.38f, 0.31f, 0.98f);
        startPracticeButton = buttonObject.GetComponent<Button>();
        startPracticeButton.targetGraphic = background;
        startPracticeButton.transition = Selectable.Transition.None;
        startPracticeButton.interactable = false;
        background.color = new Color(0.28f, 0.28f, 0.28f, 0.95f);
        startPracticeButton.onClick.AddListener(StartSelectedPractice);

        TMP_Text templateText = rowTemplate.GetComponentInChildren<TMP_Text>(true);
        GameObject labelObject = new GameObject(
            "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = templateText.font;
        label.fontSharedMaterial = templateText.fontSharedMaterial;
        label.fontSize = 28f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.text = "开始练习";
        label.raycastTarget = false;
    }

    private static string ResolvePracticeScene(Entry entry)
    {
        if (entry == null) return null;

        LumbarCoachProfile lumbar = LumbarCoachMode.ForAction(entry.id) ??
            LumbarCoachMode.ForAction(entry.displayName);
        if (lumbar != null) return lumbar.sceneName;

        switch (entry.id)
        {
            case "clip_09": return "HipThrustPractice";
            case "clip_10":
            case "clip_11":
            case "clip_13": return LumbarCoachMode.ForAction("side_bridge")?.sceneName;
            case "clip_29": return "DeadBugRecordedPractice";
            case "clip_30": return "BirdDogRecordedPractice";
            default: return null;
        }
    }

    private void RefreshStartPracticeButton()
    {
        string sceneName = selectedIndex >= 0 && selectedIndex < entries.Length
            ? ResolvePracticeScene(entries[selectedIndex]) : null;
        if (!string.IsNullOrEmpty(sceneName))
        {
            sceneName = SpineFlowTrainingSession.ResolveSceneForMobileSession(sceneName);
        }

        bool available = !string.IsNullOrEmpty(sceneName) &&
            Application.CanStreamedLevelBeLoaded(sceneName);
        startPracticeButton.interactable = available;
        startPracticeButton.targetGraphic.color = available
            ? new Color(0.10f, 0.38f, 0.31f, 0.98f)
            : new Color(0.28f, 0.28f, 0.28f, 0.95f);
        if (!available) statusText.text = string.IsNullOrEmpty(sceneName)
            ? "该动作暂无对应的教练模式场景"
            : "对应的教练模式场景不可用";
    }

    private void StartSelectedPractice()
    {
        if (selectedIndex < 0 || selectedIndex >= entries.Length) return;

        string destination = ResolvePracticeScene(entries[selectedIndex]);
        if (string.IsNullOrEmpty(destination)) return;
        destination = SpineFlowTrainingSession.ResolveSceneForMobileSession(destination);
        if (!Application.CanStreamedLevelBeLoaded(destination))
        {
            statusText.text = "对应的教练模式场景不可用";
            startPracticeButton.interactable = false;
            return;
        }

        player?.Dispose();
        player = null;
        if (loadedMotion != null)
        {
            Resources.UnloadAsset(loadedMotion);
            loadedMotion = null;
        }

        returnScene = null;
        retainedPracticeScene = default;
        retainedPracticeRoots = null;
        retainedRootActiveStates = null;
        DeadBugStartMenuController.ShowPracticeStartOnNextLoad();
        SceneManager.LoadScene(destination, LoadSceneMode.Single);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= entries.Length || coachAnimator == null)
        {
            return;
        }

        player?.Dispose();
        player = null;
        startPracticeButton.interactable = false;
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
        RefreshStartPracticeButton();

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
        if (retainedPracticeScene.IsValid() && retainedPracticeScene.isLoaded &&
            retainedPracticeRoots != null)
        {
            Scene libraryScene = SceneManager.GetSceneByName(SceneName);
            if (SceneManager.SetActiveScene(retainedPracticeScene))
            {
                foreach (GameObject root in libraryScene.GetRootGameObjects())
                {
                    root.SetActive(false);
                }
                returnScene = null;
                AsyncOperation unload = SceneManager.UnloadSceneAsync(libraryScene);
                if (unload != null)
                {
                    unload.completed += RestorePracticeAfterLibraryUnload;
                }
                else
                {
                    RestorePracticeAfterLibraryUnload(null);
                }
                return;
            }
        }

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

    private static void RestorePracticeAfterLibraryUnload(AsyncOperation operation)
    {
        if (retainedPracticeRoots == null)
        {
            return;
        }

        for (int i = 0; i < retainedPracticeRoots.Length; i++)
        {
            if (retainedPracticeRoots[i] != null)
            {
                retainedPracticeRoots[i].SetActive(retainedRootActiveStates[i]);
            }
        }

        retainedPracticeScene = default;
        retainedPracticeRoots = null;
        retainedRootActiveStates = null;
    }

}
