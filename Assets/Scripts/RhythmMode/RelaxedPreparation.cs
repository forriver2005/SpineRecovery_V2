using UnityEngine;

/// <summary>Broad preparation posture only; exercise acceptance remains in the action scorers.</summary>
public static class RelaxedPreparation
{
    public static bool IsSupineBentKnee(Animator avatar)
    {
        if(!CrunchPose.TryCapture(avatar,out var pose)||!DeadBugGamingPoseScorer.FloorOrientation(avatar,out _,out float tilt))return false;
        // Arms may rest by the sides, across the chest or near the head.
        return Mathf.Abs(tilt)<.8f&&pose.KneeAngle>=35&&pose.KneeAngle<=155&&pose.ShoulderTilt<.28f;
    }
    public static bool IsFourPoint(Animator avatar)
    {
        // Hands and knees support the torso; spine curvature and head angle are not calibration targets.
        return LyingCatPose.TryCapture(avatar,out var pose)&&pose.Supported&&pose.KneeAngle>=30&&pose.KneeAngle<=155;
    }
}
