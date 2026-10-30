using System.Collections.Generic;
using UnityEngine;

/// <summary>Normalizes each active arm/leg to its recorded start/end excursion.
/// Absolute angular tolerance alone can mistake half of a small movement for a full rep.</summary>
public sealed class RhythmMovementRange
{
    readonly Dictionary<string,Reference> references=new Dictionary<string,Reference>();
    Reference current;
    readonly List<ProgressSample> history=new List<ProgressSample>();
    double elapsed;
    struct ProgressSample { public double time;public Vector2 value; }
    sealed class Reference { public Dictionary<HumanBodyBones,Vector3> rest,top;public float front; }
    public void Begin(DeadBugGamingPoseScorer scorer,string upState)
    {
        history.Clear();elapsed=0;
        if(references.TryGetValue(upState,out current))return;
        var coach=scorer.CoachAnimator;var state=coach.GetCurrentAnimatorStateInfo(0);float speed=coach.speed;
        try
        {
            current=new Reference();coach.Play(upState,0,0);coach.Update(0);current.rest=scorer.CaptureAssessmentPose(false);
            DeadBugGamingPoseScorer.FloorOrientation(coach,out current.front,out _);
            coach.Play(upState,0,1);coach.Update(0);current.top=scorer.CaptureAssessmentPose(false);references[upState]=current;
        }
        finally{coach.Play(state.fullPathHash,0,state.normalizedTime);coach.Update(0);coach.speed=speed;}
    }
    public float FollowingQuality(DeadBugGamingPoseScorer scorer,RhythmBodyTarget a,RhythmBodyTarget b,double seconds=0,float delayHint=0)
    {
        if(current==null)return 0;
        var user=scorer.CaptureAssessmentPose(true);var coach=scorer.CaptureAssessmentPose(false);
        elapsed+=System.Math.Max(0,seconds);
        if(!Progress(user,a,out float actualA)||!Progress(user,b,out float actualB)||
            !Progress(coach,a,out float expectedA)||!Progress(coach,b,out float expectedB))return 0;
        history.Add(new ProgressSample{time=elapsed,value=new Vector2(expectedA,expectedB)});
        while(history.Count>1&&history[0].time<elapsed-TrainingScoringTolerance.MaximumResponseDelay)history.RemoveAt(0);
        float best=0,bestObjective=float.NegativeInfinity;
        foreach(var sample in history)
        {
            // Both contralateral limbs must match the SAME recent coach instant.
            // The angular engine's estimate breaks ties; no future pose is accepted.
            float error=Mathf.Max(Mathf.Abs(actualA-sample.value.x),Mathf.Abs(actualB-sample.value.y));
            float quality=100*(1-Mathf.InverseLerp(.12f,.5f,error));
            float objective=quality-5*Mathf.Abs((float)(elapsed-sample.time)-delayHint);
            if(objective>bestObjective){bestObjective=objective;best=quality;}
        }
        return best;
    }
    public void Skip(double seconds){elapsed+=System.Math.Max(0,seconds);}
    public RhythmMovementRange CaptureReference()=>new RhythmMovementRange{current=current};
    public bool AtEndpoint(DeadBugGamingPoseScorer scorer,RhythmBodyTarget a,RhythmBodyTarget b,bool up,float minimum)
    {
        if(current==null||!DeadBugGamingPoseScorer.FloorOrientation(scorer.UserAnimator,out float front,out float tilt)||
            Mathf.Abs(tilt)>=.7f||Mathf.Abs(front)<=.35f||front*current.front<=0)return false;
        var pose=scorer.CaptureAssessmentPose(true);
        if(!Progress(pose,a,out float x)||!Progress(pose,b,out float y))return false;
        return up?Mathf.Min(x,y)>=minimum:Mathf.Max(x,y)<=1-minimum;
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
