using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CoachingActionSelectionController : MonoBehaviour
{
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private ActionPreviewCard[] actionCards;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Image progressRing;
    [SerializeField] private float dwellSeconds = 1f;
    [SerializeField] private float maxGazeDistance = 20f;
    [SerializeField] private Vector2 progressRingOffset = new Vector2(40f, 0f);

    private ActionPreviewCard hoveredCard;
    private ActionPreviewCard selectedCard;
    private float dwellTimer;

    private void Awake()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = false;
            confirmButton.onClick.AddListener(ConfirmSelection);
        }

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
            progressRing.raycastTarget = false;
            progressRing.gameObject.SetActive(false);
        }

        ClearSelection();
    }

    private void OnDestroy()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(ConfirmSelection);
        }
    }

    private void Update()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        ActionPreviewCard hitCard = GetCardInGaze();
        if (hitCard == null)
        {
            hoveredCard = null;
            ResetDwell();
            return;
        }

        if (hitCard != hoveredCard)
        {
            hoveredCard = hitCard;
            dwellTimer = 0f;
            ShowProgressRing(hitCard);
        }

        if (selectedCard == hitCard)
        {
            ResetDwell();
            return;
        }

        dwellTimer += Time.deltaTime;

        if (progressRing != null)
        {
            progressRing.fillAmount = Mathf.Clamp01(dwellTimer / Mathf.Max(dwellSeconds, 0.01f));
        }

        if (dwellTimer >= dwellSeconds)
        {
            SelectCard(hitCard);
            ResetDwell();
        }
    }

    public void ConfirmSelection()
    {
        if (selectedCard == null || string.IsNullOrEmpty(selectedCard.TargetSceneName))
        {
            return;
        }

        SceneManager.LoadScene(
            SpineFlowTrainingSession.ResolveSceneForMobileSession(selectedCard.TargetSceneName));
    }

    public void ClearSelection()
    {
        selectedCard = null;

        if (actionCards != null)
        {
            foreach (ActionPreviewCard card in actionCards)
            {
                if (card != null)
                {
                    card.SetSelected(false);
                }
            }
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = false;
        }
    }

    private void SelectCard(ActionPreviewCard card)
    {
        if (card == null)
        {
            return;
        }

        if (actionCards != null)
        {
            foreach (ActionPreviewCard actionCard in actionCards)
            {
                if (actionCard != null)
                {
                    actionCard.SetSelected(actionCard == card);
                }
            }
        }

        selectedCard = card;
        selectedCard.RestartSelectedPreview();

        if (confirmButton != null)
        {
            confirmButton.interactable = true;
        }
    }

    private ActionPreviewCard GetCardInGaze()
    {
        if (gazeCamera == null || actionCards == null || actionCards.Length == 0)
        {
            return null;
        }

        Ray gazeRay = new Ray(gazeCamera.transform.position, gazeCamera.transform.forward);
        ActionPreviewCard closestCard = null;
        float closestDistance = maxGazeDistance;

        foreach (ActionPreviewCard card in actionCards)
        {
            RectTransform cardRect = card != null ? card.HitArea : null;
            if (cardRect == null || !card.gameObject.activeInHierarchy)
            {
                continue;
            }

            Plane cardPlane = new Plane(cardRect.forward, cardRect.position);
            if (!cardPlane.Raycast(gazeRay, out float distance) || distance > closestDistance)
            {
                continue;
            }

            Vector3 hitPoint = gazeRay.GetPoint(distance);
            Vector3 localPoint = cardRect.InverseTransformPoint(hitPoint);
            if (!cardRect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
            {
                continue;
            }

            closestCard = card;
            closestDistance = distance;
        }

        return closestCard;
    }

    private void ShowProgressRing(ActionPreviewCard card)
    {
        if (progressRing == null || card == null || card.HitArea == null)
        {
            return;
        }

        RectTransform ringRect = progressRing.GetComponent<RectTransform>();
        if (ringRect == null)
        {
            return;
        }

        RectTransform cardRect = card.HitArea;
        Vector3 worldOffset =
            cardRect.right * progressRingOffset.x +
            cardRect.up * progressRingOffset.y;

        ringRect.position = cardRect.position + worldOffset;
        ringRect.rotation = cardRect.rotation;
        ringRect.SetAsLastSibling();

        progressRing.fillAmount = 0f;
        progressRing.gameObject.SetActive(true);
    }

    private void ResetDwell()
    {
        dwellTimer = 0f;

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
            progressRing.gameObject.SetActive(false);
        }
    }
}
