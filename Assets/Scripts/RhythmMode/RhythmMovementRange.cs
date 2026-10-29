using System.Collections.Generic;
using UnityEngine;

/// <summary>Normalizes each active arm/leg to its recorded start/end excursion.
/// Absolute angular tolerance alone can mistake half of a small movement for a full rep.</summary>
public sealed class RhythmMovementRange
{
    readonly Dictionary<string,Reference> references=new Dictionary<string,Reference>();
    Reference current;
    sealed class Reference { public Dictionary<HumanBodyBones,Vector3> rest,top; }
    public void Begin(DeadBugGamingPoseScorer scorer,string upState)
    {
        if(references.TryGetValue(upState,out current))return;
        var coach=scorer.CoachAnimator;var state=coach.GetCurrentAnimatorStateInfo(0);float speed=coach.speed;
        try
        {
            current=new Reference();coach.Play(upState,0,0);coach.Update(0);current.rest=scorer.CaptureAssessmentPose(false);
            coach.Play(upState,0,1);coach.Update(0);current.top=scorer.CaptureAssessmentPose(false);references[upState]=current;
        }
        finally{coach.Play(state.fullPathHash,0,state.normalizedTime);coach.Update(0);coach.speed=speed;}
    }
    public float FollowingQuality(DeadBugGamingPoseScorer scorer,RhythmBodyTarget a,RhythmBodyTarget b)
    {
        if(current==null)return 0;
        var user=scorer.CaptureAssessmentPose(true);var coach=scorer.CaptureAssessmentPose(false);
        return Mathf.Min(Compare(a),Compare(b));
        float Compare(RhythmBodyTarget target)
        {
            if(!Progress(user,target,out float actual)||!Progress(coach,target,out float expected))return 0;
            return 100*(1-Mathf.InverseLerp(.12f,.5f,Mathf.Abs(actual-expected)));
        }
    }
    public float EndpointQuality(DeadBugGamingPoseScorer scorer,RhythmBodyTarget target)
    {
        if(current==null||!Progress(scorer.CaptureAssessmentPose(true),target,out float value))return 0;
        return 100*Mathf.InverseLerp(.65f,.85f,value);
    }
    public Vector2 CurrentProgress(DeadBugGamingPoseScorer scorer,RhythmBodyTarget a,RhythmBodyTarget b)
    {
        var pose=scorer.CaptureAssessmentPose(true);Progress(pose,a,out float x);Progress(pose,b,out float y);return new Vector2(x,y);
    }
    bool Progress(Dictionary<HumanBodyBones,Vector3> pose,RhythmBodyTarget target,out float result)
    {
        HumanBodyBones bone=target==RhythmBodyTarget.LeftHand?HumanBodyBones.LeftUpperArm:
            target==RhythmBodyTarget.RightHand?HumanBodyBones.RightUpperArm:
            target==RhythmBodyTarget.LeftFoot?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg;
        result=0;
        if(!pose.TryGetValue(bone,out var actual)||!current.rest.TryGetValue(bone,out var rest)||!current.top.TryGetValue(bone,out var top))return false;
        Vector3 excursion=top-rest;
        if(excursion.sqrMagnitude<.005f)return false;
        result=Mathf.Clamp01(Vector3.Dot(actual-rest,excursion)/excursion.sqrMagnitude);return true;
    }
}
