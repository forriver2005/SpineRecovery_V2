using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GazeDwellClicker : MonoBehaviour
{
    [Header("Gaze")]
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private float dwellSeconds = 1f;
    [SerializeField] private float maxGazeDistance = 20f;

    [Header("Menu UI")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private GraphicRaycaster graphicRaycaster;
    [SerializeField] private bool scanAllActiveCanvases = true;
    [SerializeField] private Image progressRing;
    [SerializeField] private float buttonRefreshInterval = 1f;
    [SerializeField, Range(0f, 1f)] private float buttonAlphaHitThreshold = 0.1f;

    private Button currentButton;
    private Button clickedButton;
    private float dwellTimer;
    private float buttonRefreshTimer;
    private Button[] menuButtons;

    private void Awake()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        if (menuCanvas == null && graphicRaycaster != null)
        {
            menuCanvas = graphicRaycaster.GetComponent<Canvas>();
        }

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
            progressRing.raycastTarget = false;
            progressRing.gameObject.SetActive(false);
        }

        RefreshButtons();
    }

    private void Update()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        if (!scanAllActiveCanvases && menuCanvas == null && graphicRaycaster != null)
        {
            menuCanvas = graphicRaycaster.GetComponent<Canvas>();
        }

        bool missingCanvas = !scanAllActiveCanvases && menuCanvas == null;
        if (gazeCamera == null || missingCanvas || progressRing == null)
        {
            ResetProgress();
            return;
        }

        buttonRefreshTimer += Time.unscaledDeltaTime;
        if (buttonRefreshTimer >= buttonRefreshInterval)
        {
            RefreshButtons();
        }

        Button hitButton = GetButtonInGaze();

        if (hitButton == null || !hitButton.interactable)
        {
            currentButton = null;
            clickedButton = null;
            ResetProgress();
            return;
        }

        if (clickedButton == hitButton)
        {
            progressRing.gameObject.SetActive(false);
            return;
        }

        if (hitButton != currentButton)
        {
            currentButton = hitButton;
            dwellTimer = 0f;
            progressRing.fillAmount = 0f;
            progressRing.gameObject.SetActive(true);
        }

        dwellTimer += Time.deltaTime;
        progressRing.fillAmount = Mathf.Clamp01(dwellTimer / dwellSeconds);

        if (dwellTimer >= dwellSeconds)
        {
            clickedButton = currentButton;
            currentButton.onClick.Invoke();
            ResetProgress();
        }
    }

    private Button GetButtonInGaze()
    {
        if (menuButtons == null || menuButtons.Length == 0)
        {
            RefreshButtons();
        }

        Ray gazeRay = new Ray(gazeCamera.transform.position, gazeCamera.transform.forward);

        Button closestButton = null;
        float closestDistance = maxGazeDistance;

        foreach (Button button in menuButtons)
        {
            if (button == null || !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            if (buttonRect == null)
            {
                continue;
            }

            Plane buttonPlane = new Plane(buttonRect.forward, buttonRect.position);
            if (!buttonPlane.Raycast(gazeRay, out float distance) || distance > closestDistance)
            {
                continue;
            }

            Vector3 hitPoint = gazeRay.GetPoint(distance);
            Vector3 localPoint = buttonRect.InverseTransformPoint(hitPoint);
            if (buttonRect.rect.Contains(new Vector2(localPoint.x, localPoint.y)) &&
                IsVisibleButtonPixel(button, buttonRect, localPoint))
            {
                closestButton = button;
                closestDistance = distance;
            }
        }

        return closestButton;
    }

    private bool IsVisibleButtonPixel(Button button, RectTransform buttonRect, Vector3 localPoint)
    {
        if (buttonAlphaHitThreshold <= 0f)
        {
            return true;
        }

        Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
        if (image == null || image.sprite == null || image.sprite.texture == null)
        {
            return true;
        }

        Rect rect = buttonRect.rect;
        Rect spriteRect = image.sprite.rect;
        Vector2 normalizedPoint = new Vector2(
            Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y));

        int pixelX = Mathf.Clamp(
            Mathf.FloorToInt(spriteRect.x + normalizedPoint.x * spriteRect.width),
            Mathf.FloorToInt(spriteRect.x),
            Mathf.FloorToInt(spriteRect.xMax) - 1);
        int pixelY = Mathf.Clamp(
            Mathf.FloorToInt(spriteRect.y + normalizedPoint.y * spriteRect.height),
            Mathf.FloorToInt(spriteRect.y),
            Mathf.FloorToInt(spriteRect.yMax) - 1);

        try
        {
            return image.sprite.texture.GetPixel(pixelX, pixelY).a >= buttonAlphaHitThreshold;
        }
        catch (UnityException)
        {
            return true;
        }
    }

    private void ResetProgress()
    {
        dwellTimer = 0f;

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
            progressRing.gameObject.SetActive(false);
        }
    }

    private void RefreshButtons()
    {
        buttonRefreshTimer = 0f;
        if (!scanAllActiveCanvases)
        {
            menuButtons = menuCanvas != null
                ? menuCanvas.GetComponentsInChildren<Button>(true)
                : null;
            return;
        }

        List<Button> activeButtons = new List<Button>();
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled)
            {
                continue;
            }

            Button[] canvasButtons = canvas.GetComponentsInChildren<Button>(true);
            foreach (Button button in canvasButtons)
            {
                if (button != null && button.gameObject.activeInHierarchy)
                {
                    activeButtons.Add(button);
                }
            }
        }

        menuButtons = activeButtons.ToArray();
    }
}
