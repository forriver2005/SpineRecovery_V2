using UnityEngine;

/// <summary>Opt-in pose policy for the two left-side bridge scenes.</summary>
public sealed class SideBridgePoseScoring : MonoBehaviour
{
    public readonly SideBridgePoseGeometry Geometry=new SideBridgePoseGeometry();
    public bool PostureReady { get; private set; }
    public bool CaptureTargets(HipThrustTaikoController transport)
    {
        var animator=transport.CoachAnimator;var timeline=transport.PhaseTimeline;
        if(animator==null || timeline==null) return false;
        float low=0,high=0;SideBridgePoseGeometry.Pose firstLow=null,firstHigh=null;
        foreach(var cycle in timeline.cycles)
        {
            Sample(cycle.liftStartFrame);
            if(!SideBridgePoseGeometry.TryMeasure(animator,out var rest)) return false;
            int index=System.Array.IndexOf(timeline.cycles,cycle);
            Sample(HipThrustHold30Timeline.PeakFrame(timeline,index));
            if(!SideBridgePoseGeometry.TryMeasure(animator,out var top)) return false;
            low+=rest.lift;high+=top.lift;
            if(firstLow==null){firstLow=rest;firstHigh=top;}
        }
        return Geometry.SetTargets(firstLow,firstHigh,low/timeline.cycles.Length,high/timeline.cycles.Length);
        void Sample(int frame)
        { animator.Play(transport.RoutineStateName,0,frame/timeline.sourceClip.frameRate/timeline.sourceClip.length);animator.Update(0); }
    }
    public bool Evaluate(Animator animator,out float progress,out float quality)
    {
        progress=quality=0;PostureReady=false;
        if(!SideBridgePoseGeometry.TryMeasure(animator,out var pose))return false;
        PostureReady=pose.IsLeftSupport;
        if(!PostureReady)return false;
        progress=Geometry.Progress(pose);quality=Geometry.Quality(pose,Geometry.Top);return true;
    }
    public bool Matches(Animator user,Animator coach)
    {
        return SideBridgePoseGeometry.TryMeasure(user,out var actual)&&actual.IsLeftSupport&&
            SideBridgePoseGeometry.TryMeasure(coach,out var expected)&&expected.IsLeftSupport&&
            Mathf.Abs(Geometry.Progress(actual)-Geometry.Progress(expected))<=.16f&&Geometry.Quality(actual,expected)>=65;
    }
}
