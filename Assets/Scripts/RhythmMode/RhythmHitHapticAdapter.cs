using UnityEngine;

/// <summary>Maps hit rhythm notes to exactly one corresponding distal tracker.</summary>
public sealed class RhythmHitHapticAdapter : MonoBehaviour
{
    [SerializeField] private RhythmModeController rhythmController;
    [SerializeField] private CoachHapticFeedbackController hapticController;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (rhythmController != null)
        {
            rhythmController.NoteResolved -= HandleNoteResolved;
            rhythmController.NoteResolved += HandleNoteResolved;
        }
    }

    private void OnDisable()
    {
        if (rhythmController != null)
        {
            rhythmController.NoteResolved -= HandleNoteResolved;
        }

        hapticController?.StopAllHaptics();
    }

    private void ResolveReferences()
    {
        if (rhythmController == null)
        {
            rhythmController = FindObjectOfType<RhythmModeController>(true);
        }

        if (hapticController == null)
        {
            hapticController = FindObjectOfType<CoachHapticFeedbackController>(true);
        }

        if (hapticController == null)
        {
            hapticController = gameObject.AddComponent<CoachHapticFeedbackController>();
        }
    }

    private void HandleNoteResolved(RhythmNoteResult result)
    {
        if (!result.IsHit || hapticController == null)
        {
            return;
        }

        switch (result.Target)
        {
            case RhythmBodyTarget.RightHand:
                hapticController.PulseOnce(TrackerWearLocation.RightForearm);
                break;
            case RhythmBodyTarget.LeftHand:
                hapticController.PulseOnce(TrackerWearLocation.LeftForearm);
                break;
            case RhythmBodyTarget.RightFoot:
                hapticController.PulseOnce(TrackerWearLocation.RightLowerLeg);
                break;
            case RhythmBodyTarget.LeftFoot:
                hapticController.PulseOnce(TrackerWearLocation.LeftLowerLeg);
                break;
            case RhythmBodyTarget.Waist:
                hapticController.PulseOnce(TrackerWearLocation.Abdomen);
                break;
        }
    }
}
