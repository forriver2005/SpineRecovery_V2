using UnityEngine;

public class PlaceMenuInFrontOfCamera : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private float distance = 1.5f;
    [SerializeField] private float horizontalOffset = 0.45f;
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private float delaySeconds = 0.1f;

    [Header("View Following")]
    [Tooltip("Keep this object in front of the user's view after the initial placement.")]
    [SerializeField] private bool followView;
    [SerializeField] [Min(0f)] private float followSpeed = 8f;
    [Tooltip("Follow head pitch but use world up so the UI does not roll with the headset.")]
    [SerializeField] private bool lockPitchAndRoll = true;

    private bool placed;
    private float timer;

    private void OnEnable()
    {
        placed = false;
        timer = 0f;
    }

    private void LateUpdate()
    {
        if (!placed)
        {
            timer += Time.unscaledDeltaTime;
            if (timer < delaySeconds)
            {
                return;
            }
        }

        if (!TryResolveHead())
        {
            return;
        }

        if (!placed)
        {
            Place();
            placed = true;
            return;
        }

        if (followView)
        {
            FollowCurrentView();
        }
    }

    public void Place()
    {
        if (!TryResolveHead())
        {
            return;
        }

        GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation);

        transform.position = targetPosition;

        if (faceCamera)
        {
            transform.rotation = targetRotation;
        }
    }

    public void RecenterNow()
    {
        timer = delaySeconds;
        Place();
        placed = head != null;
    }

    private void FollowCurrentView()
    {
        GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation);

        float blend = followSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);

        transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
        if (faceCamera)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
        }
    }

    private void GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation)
    {
        Vector3 viewForward = head.forward;
        Vector3 viewRight = head.right;
        Vector3 viewUp = head.up;

        targetPosition =
            head.position +
            viewForward * distance +
            viewRight * horizontalOffset +
            viewUp * verticalOffset;

        // A world-space Canvas reads normally when its forward axis points in
        // the same direction as the viewer's gaze (away from the viewer).
        Vector3 lookDirection = targetPosition - head.position;
        Vector3 rotationUp = lockPitchAndRoll ? Vector3.up : head.up;
        if (Vector3.Cross(lookDirection, rotationUp).sqrMagnitude < 0.001f)
        {
            rotationUp = head.up;
        }

        targetRotation = lookDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDirection.normalized, rotationUp)
            : transform.rotation;
    }

    private bool TryResolveHead()
    {
        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        return head != null;
    }
}
