using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public sealed class MobileDisplayModeBootstrap : MonoBehaviour
{
    [SerializeField] private ScreenOrientation orientation = ScreenOrientation.LandscapeLeft;
    [SerializeField] private bool forceScreenSpaceCanvases = true;
    [SerializeField] private bool activateExternalDisplays;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindObjectOfType<MobileDisplayModeBootstrap>() != null) return;
        GameObject host = new GameObject(nameof(MobileDisplayModeBootstrap));
        DontDestroyOnLoad(host);
        host.AddComponent<MobileDisplayModeBootstrap>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ApplyPolicy();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyPolicy();
    }

    private void ApplyPolicy()
    {
        ApplyOrientation();
        if (forceScreenSpaceCanvases) ConvertCanvasesToScreenSpace();
        EnsureTouchEventSystem();
        // Keep this off for wired mirror mode. Android duplicates Display 0,
        // which guarantees the phone and glasses receive the same frame.
        if (activateExternalDisplays) ActivateExternalDisplays();
    }

    private void ApplyOrientation()
    {
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = orientation == ScreenOrientation.LandscapeLeft;
        Screen.autorotateToLandscapeRight = orientation == ScreenOrientation.LandscapeRight;
        Screen.orientation = orientation;
    }

    private static void ConvertCanvasesToScreenSpace()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        bool hasCoachControls = FindObjectOfType<MobileCoachControls>(true) != null;
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null) continue;
            // Avatar labels must remain attached to the models, not cover the
            // phone viewport as independent full-screen canvases.
            if (canvas.GetComponentInParent<Animator>() != null &&
                hasCoachControls) continue;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            if (canvas.name == "StartMenuCanvas")
            {
                foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(child.localEulerAngles.y, 180f)) < 1f)
                    {
                        child.localRotation = Quaternion.identity;
                    }
                }
            }
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = canvas.name == "ActionLibraryCanvas"
                ? CanvasScaler.ScreenMatchMode.Expand
                : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    private static void EnsureTouchEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem = go.GetComponent<EventSystem>();
        }

        // Migrated AR scenes used XRUIInputModule. When the XR packages are
        // intentionally absent on mobile, keep the EventSystem and provide a
        // touch-capable fallback instead of leaving every Button inert.
        if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }

        eventSystem.sendNavigationEvents = false;
    }

    private static void ActivateExternalDisplays()
    {
        if (Display.displays == null || Display.displays.Length <= 1) return;
        for (int i = 1; i < Display.displays.Length; i++)
        {
            if (!Display.displays[i].active) Display.displays[i].Activate();
        }
    }
}
