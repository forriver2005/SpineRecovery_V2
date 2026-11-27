using System.Collections.Generic;
using UnityEngine;

/// <summary>Checks the new hold action's recorded neutral posture before the shared stable calibration.</summary>
public sealed class RecordedHoldPreparation : MonoBehaviour
{
    readonly AnatomicalMotionScoringEngine reader=new AnatomicalMotionScoringEngine(new AnatomicalMotionScoringSettings());
    Dictionary<HumanBodyBones,Vector3> reference;
    float front,tilt;
    static readonly HumanBodyBones[] Required={HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg};
    public bool Capture(Animator coach)
    {
        reference=reader.CaptureRawPose(coach);
        return reference.Count>=6&&DeadBugGamingPoseScorer.FloorOrientation(coach,out front,out tilt);
    }
    public bool Matches(Animator patient)
    {
        if(reference==null||!DeadBugGamingPoseScorer.FloorOrientation(patient,out float actualFront,out float actualTilt))return false;
        if(Mathf.Abs(actualTilt-tilt)>.35f||Mathf.Abs(actualFront-front)>.45f||actualFront*front<0)return false;
        var pose=reader.CaptureRawPose(patient);
        foreach(var bone in Required)
            if(!pose.TryGetValue(bone,out var actual)||!reference.TryGetValue(bone,out var target)||Vector3.Angle(actual,target)>35f)return false;
        return true;
    }
}
