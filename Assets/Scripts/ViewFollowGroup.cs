using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public class ViewFollowGroup : MonoBehaviour
{
    public const float DefaultReadyOverheadPitchDegrees = 24f;
    public const float DefaultOverheadTransitionSeconds = 0.45f;

    [SerializeField] private Transform head;
    [SerializeField] private float distance = 2.5f;
    [SerializeField] private float verticalOffset = -0.2f;
    [SerializeField] private float followSpeed = 8f;
    [SerializeField] private bool lockPitchAndRoll = true;
    [Tooltip("When disabled, the group follows the view position but keeps its existing world rotation.")]
    [SerializeField] private bool rotateWithView = true;
    [Tooltip("Keep the group fixed in camera-local space so it stays front-facing at any head angle without rotating in the viewport.")]
    [SerializeField] private bool followInCameraLocalSpace;

    [Header("Practice Lying Presentation")]
    [Tooltip("Additional top-down viewing angle used only after the user's " +
        "stable lying pose and display-direction lock have both succeeded.")]
    [SerializeField, Range(0f, 30f)] private float readyOverheadPitchDegrees =
        DefaultReadyOverheadPitchDegrees;
    [Tooltip("Time used to blend into and out of the lying overhead view.")]
    [SerializeField, Min(0f)] private float overheadTransitionSeconds =
        DefaultOverheadTransitionSeconds;
    [SerializeField] private PracticeSessionController practiceSession;

    private float currentOverheadPitchDegrees;
    private Transform coachAlignmentTarget;
    private Vector3 coachAuthoredLocalPosition;
    private bool hasCoachAuthoredLocalPosition;
    private float activeCoachVerticalAlignmentOffset;

    private bool fixedRotationInitialized;
    public float CurrentOverheadPitchDegrees => currentOverheadPitchDegrees;

    private void Update()
    {
        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (head == null)
        {
            return;
        }

        if (IsPracticeScene())
        {
            ResolvePracticeSession();
            float targetOverheadPitch =
                practiceSession != null &&
                practiceSession.IsLyingOverheadPresentationReady
                    ? Mathf.Max(0f, readyOverheadPitchDegrees)
                    : 0f;
            currentOverheadPitchDegrees = StepOverheadPitch(
                currentOverheadPitchDegrees,
                targetOverheadPitch,
                readyOverheadPitchDegrees,
                overheadTransitionSeconds,
                Time.unscaledDeltaTime);

            Quaternion headRotation = lockPitchAndRoll
                ? GetYawOnlyRotation(head)
                : head.rotation;
            Vector3 unpitchedPosition = CalculatePracticePresentationPosition(
                head.localToWorldMatrix,
                verticalOffset,
                distance);
            Quaternion presentationRotation =
                CalculatePracticePresentationRotation(
                    headRotation,
                    currentOverheadPitchDegrees);
            Transform presentationAnchor = practiceSession != null
                ? practiceSession.LyingPresentationAnchor
                : null;
            if (targetOverheadPitch > Mathf.Epsilon)
            {
                activeCoachVerticalAlignmentOffset = practiceSession != null
                    ? practiceSession.LyingCoachVerticalAlignmentOffset
                    : 0f;
            }
            float readyBlend = readyOverheadPitchDegrees > Mathf.Epsilon
                ? Mathf.Clamp01(
                    currentOverheadPitchDegrees /
                    readyOverheadPitchDegrees)
                : 0f;
            ApplyCoachLyingAlignment(
                presentationAnchor,
                activeCoachVerticalAlignmentOffset,
                readyBlend);
            if (readyBlend <= Mathf.Epsilon)
            {
                activeCoachVerticalAlignmentOffset = 0f;
            }
            Vector3 anchorLocalPosition =
                presentationAnchor != null &&
                presentationAnchor.IsChildOf(transform)
                    ? transform.InverseTransformPoint(
                        presentationAnchor.position)
                    : Vector3.zero;

            transform.SetPositionAndRotation(
                CalculateAnchorCompensatedPosition(
                    unpitchedPosition,
                    headRotation,
                    presentationRotation,
                    anchorLocalPosition),
                presentationRotation);
            return;
        }

        if (followInCameraLocalSpace)
        {
            transform.position = head.TransformPoint(new Vector3(0f, verticalOffset, distance));
            transform.rotation = head.rotation;
            return;
        }

        RestoreCoachAuthoredLocalPosition();
        activeCoachVerticalAlignmentOffset = 0f;
        Vector3 targetPosition =
            head.position +
            head.forward * distance +
            Vector3.up * verticalOffset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            Time.deltaTime * followSpeed
        );

        if (!rotateWithView)
        {
            if (!fixedRotationInitialized)
            {
                transform.rotation = GetFacingRotation(transform.position, head);
                fixedRotationInitialized = true;
            }
            return;
        }

        Vector3 forward = transform.position - head.position;

        if (lockPitchAndRoll)
        {
            forward.y = 0f;
        }

        if (forward.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }

    private void OnDisable()
    {
        RestoreCoachAuthoredLocalPosition();
        activeCoachVerticalAlignmentOffset = 0f;
    }

    private static bool IsPracticeScene()
    {
        return SceneManager.GetActiveScene().name.IndexOf(
            "Practice",
            System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ResolvePracticeSession()
    {
        if (practiceSession != null &&
            practiceSession.gameObject.scene == gameObject.scene)
        {
            return;
        }

        practiceSession = null;
        foreach (PracticeSessionController candidate in
            FindObjectsOfType<PracticeSessionController>(true))
        {
            if (candidate.gameObject.scene == gameObject.scene)
            {
                practiceSession = candidate;
                break;
            }
        }
    }

    public static Quaternion CalculatePracticePresentationRotation(
        Quaternion headRotation,
        float overheadPitchDegrees)
    {
        // Negative local X turns the avatars' up axis toward the camera. This
        // is equivalent to looking down at their shared presentation space,
        // without changing the tracked XR camera or either avatar independently.
        return headRotation * Quaternion.Euler(
            -Mathf.Max(0f, overheadPitchDegrees),
            0f,
            0f);
    }

    public static Vector3 CalculatePracticePresentationPosition(
        Matrix4x4 headLocalToWorldMatrix,
        float presentationVerticalOffset,
        float presentationDistance)
    {
        return headLocalToWorldMatrix.MultiplyPoint3x4(
            new Vector3(
                0f,
                presentationVerticalOffset,
                presentationDistance));
    }

    public static Vector3 CalculateAnchorCompensatedPosition(
        Vector3 unpitchedRootPosition,
        Quaternion unpitchedRootRotation,
        Quaternion presentationRootRotation,
        Vector3 anchorLocalPosition)
    {
        // Pitch the shared model group around the coach's authored root point.
        // This preserves the accepted lying height while retaining the models'
        // fixed local spacing and shared rotation.
        Vector3 stableAnchorPosition =
            unpitchedRootPosition +
            unpitchedRootRotation * anchorLocalPosition;
        return stableAnchorPosition -
            presentationRootRotation * anchorLocalPosition;
    }

    public static Vector3 CalculateCoachLyingAlignedLocalPosition(
        Vector3 authoredLocalPosition,
        float verticalAlignmentOffset,
        float readyBlend)
    {
        return authoredLocalPosition +
            Vector3.up * verticalAlignmentOffset * Mathf.Clamp01(readyBlend);
    }

    public static float StepOverheadPitch(
        float currentPitchDegrees,
        float targetPitchDegrees,
        float configuredPitchDegrees,
        float transitionSeconds,
        float deltaTime)
    {
        float safeTarget = Mathf.Max(0f, targetPitchDegrees);
        float safeCurrent = Mathf.Max(0f, currentPitchDegrees);
        float safeTransition = Mathf.Max(0f, transitionSeconds);
        if (safeTransition <= Mathf.Epsilon)
        {
            return safeTarget;
        }

        float degreesPerSecond =
            Mathf.Max(Mathf.Max(0f, configuredPitchDegrees), safeTarget) /
            safeTransition;
        return Mathf.MoveTowards(
            safeCurrent,
            safeTarget,
            degreesPerSecond * Mathf.Max(0f, deltaTime));
    }

    private static Quaternion GetYawOnlyRotation(Transform headTransform)
    {
        Vector3 forward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.ProjectOnPlane(headTransform.up, Vector3.up);
        }

        return forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;
    }

    private void ApplyCoachLyingAlignment(
        Transform coach,
        float verticalAlignmentOffset,
        float readyBlend)
    {
        if (readyBlend <= Mathf.Epsilon)
        {
            RestoreCoachAuthoredLocalPosition();
            return;
        }

        if (coach == null)
        {
            RestoreCoachAuthoredLocalPosition();
            return;
        }

        if (coachAlignmentTarget != coach)
        {
            RestoreCoachAuthoredLocalPosition();
            coachAlignmentTarget = coach;
            coachAuthoredLocalPosition = coach.localPosition;
            hasCoachAuthoredLocalPosition = true;
        }

        coach.localPosition = CalculateCoachLyingAlignedLocalPosition(
            coachAuthoredLocalPosition,
            verticalAlignmentOffset,
            readyBlend);
    }

    private void RestoreCoachAuthoredLocalPosition()
    {
        if (coachAlignmentTarget != null && hasCoachAuthoredLocalPosition)
        {
            coachAlignmentTarget.localPosition = coachAuthoredLocalPosition;
        }

        coachAlignmentTarget = null;
        coachAuthoredLocalPosition = Vector3.zero;
        hasCoachAuthoredLocalPosition = false;
    }

    private Quaternion GetFacingRotation(Vector3 groupPosition, Transform headTransform)
    {
        Vector3 forward = groupPosition - headTransform.position;
        if (lockPitchAndRoll)
        {
            forward.y = 0f;
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            return transform.rotation;
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
