using UnityEngine;

/// <summary>
/// Keeps a world-space label readable while its parent model rotates or animates.
/// TextMeshPro text in this project reads from its negative-Z side, so the
/// transform's forward axis points away from the viewing camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class FaceCameraBillboard : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool keepWorldUpright = true;
    [Tooltip("Detach at runtime while continuing to follow the original parent position. " +
        "This prevents animated or non-uniformly scaled model roots from shearing the label.")]
    [SerializeField] private bool detachFromRotatingParent = true;

    private Transform positionAnchor;
    private Vector3 anchorLocalPosition;

    private void Awake()
    {
        positionAnchor = transform.parent;
        if (!detachFromRotatingParent || positionAnchor == null)
        {
            return;
        }

        RectTransform rectTransform = transform as RectTransform;
        anchorLocalPosition = rectTransform != null
            ? new Vector3(
                rectTransform.anchoredPosition.x,
                rectTransform.anchoredPosition.y,
                rectTransform.localPosition.z)
            : transform.localPosition;
        transform.SetParent(null, true);
    }

    private void LateUpdate()
    {
        if (detachFromRotatingParent && positionAnchor != null)
        {
            // Keep the label above the coach in screen space even when the
            // coach rotates into a lying pose. The old TransformPoint call
            // rotated the vertical offset with the avatar and could move the
            // label sideways or behind the camera.
            float verticalOffset =
                anchorLocalPosition.y * Mathf.Abs(positionAnchor.lossyScale.y);
            Vector3 horizontalOffset =
                positionAnchor.right * anchorLocalPosition.x;
            transform.position = positionAnchor.position +
                horizontalOffset + Vector3.up * verticalOffset;
        }

        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        Vector3 forwardAwayFromCamera = transform.position - targetCamera.transform.position;
        if (keepWorldUpright)
        {
            forwardAwayFromCamera = Vector3.ProjectOnPlane(forwardAwayFromCamera, Vector3.up);
        }

        if (forwardAwayFromCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 up = keepWorldUpright ? Vector3.up : targetCamera.transform.up;
        if (Vector3.Cross(forwardAwayFromCamera, up).sqrMagnitude < 0.0001f)
        {
            up = targetCamera.transform.up;
        }

        transform.rotation = Quaternion.LookRotation(forwardAwayFromCamera.normalized, up);
    }
}
