using UnityEngine;

public sealed class RhythmPoseJudge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DeadBugGamingPoseScorer poseScorer;

    [Header("Pose Thresholds")]
    [Range(0f, 100f)] [SerializeField] private float successThreshold = 40f;

    [Header("Waist Evaluation")]
    [Min(1f)] [SerializeField] private float waistPerfectAngle = 10f;
    [Min(5f)] [SerializeField] private float waistZeroScoreAngle = 48f;

    private AnatomicalLimbScores limbScores;
    private float waistScore;
    private bool hasWaistTracking;

    public float SuccessThreshold => successThreshold;
    public Animator CoachAnimator => poseScorer != null ? poseScorer.CoachAnimator : null;

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        UpdateWaistScore();
    }

    public bool TryHit(RhythmBodyTarget target, out float poseScore)
    {
        poseScore = GetScore(target);
        if (!HasTracking(target) || poseScore < successThreshold)
        {
            return false;
        }
        return true;
    }

    public float GetScore(RhythmBodyTarget target)
    {
        switch (target)
        {
            case RhythmBodyTarget.LeftHand: return limbScores.leftArm.score;
            case RhythmBodyTarget.RightHand: return limbScores.rightArm.score;
            case RhythmBodyTarget.LeftFoot: return limbScores.leftLeg.score;
            case RhythmBodyTarget.RightFoot: return limbScores.rightLeg.score;
            default: return waistScore;
        }
    }

    private bool HasTracking(RhythmBodyTarget target)
    {
        switch (target)
        {
            case RhythmBodyTarget.LeftHand: return limbScores.leftArm.validSegmentCount > 0;
            case RhythmBodyTarget.RightHand: return limbScores.rightArm.validSegmentCount > 0;
            case RhythmBodyTarget.LeftFoot: return limbScores.leftLeg.validSegmentCount > 0;
            case RhythmBodyTarget.RightFoot: return limbScores.rightLeg.validSegmentCount > 0;
            default: return hasWaistTracking;
        }
    }

    private void UpdateWaistScore()
    {
        hasWaistTracking = false;
        waistScore = 0f;
        if (poseScorer == null || poseScorer.CoachAnimator == null || poseScorer.UserAnimator == null)
        {
            return;
        }

        Transform coachHips = poseScorer.CoachAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform coachChest = GetChest(poseScorer.CoachAnimator);
        Transform userHips = poseScorer.UserAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform userChest = GetChest(poseScorer.UserAnimator);
        if (coachHips == null || coachChest == null || userHips == null || userChest == null)
        {
            return;
        }

        Quaternion coachRelative = Quaternion.Inverse(coachHips.rotation) * coachChest.rotation;
        Quaternion userRelative = Quaternion.Inverse(userHips.rotation) * userChest.rotation;
        float error = Quaternion.Angle(coachRelative, userRelative);
        float normalized = Mathf.InverseLerp(waistPerfectAngle, waistZeroScoreAngle, error);
        waistScore = (1f - normalized * normalized * (3f - 2f * normalized)) * 100f;
        hasWaistTracking = true;
    }

    private static Transform GetChest(Animator animator)
    {
        return animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
            animator.GetBoneTransform(HumanBodyBones.Chest) ??
            animator.GetBoneTransform(HumanBodyBones.Spine);
    }

    private void HandleLimbScoresChanged(AnatomicalLimbScores scores)
    {
        limbScores = scores;
    }

    private void ResolveReferences()
    {
        if (poseScorer == null)
        {
            poseScorer = FindObjectOfType<DeadBugGamingPoseScorer>(true);
        }
    }

    private void Subscribe()
    {
        if (poseScorer == null)
        {
            return;
        }

        poseScorer.LimbScoresChanged -= HandleLimbScoresChanged;
        poseScorer.LimbScoresChanged += HandleLimbScoresChanged;
    }

    private void Unsubscribe()
    {
        if (poseScorer != null)
        {
            poseScorer.LimbScoresChanged -= HandleLimbScoresChanged;
        }
    }
}
