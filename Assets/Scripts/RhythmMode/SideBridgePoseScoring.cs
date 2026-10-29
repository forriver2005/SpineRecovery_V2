using UnityEngine;

/// <summary>Left-support references, achievable recorded hold targets, and measured repetitions.</summary>
public sealed class SideBridgePoseScoring : MonoBehaviour
{
    public readonly SideBridgeHoldRule Rule=new SideBridgeHoldRule();
    public SideBridgePoseGeometry Geometry => references!=null?references[activeCycle]:null;
    public bool PostureReady { get; private set; }
    public bool SupportReady { get; private set; }
    public float TrackingQuality { get; private set; }
    public float[] RecordedHoldTargets { get; private set; }
    SideBridgePoseGeometry[] references;
    HipThrustTaikoController transport;
    int activeRepetition=-1,activeCycle;
    string difficulty;

    public void Begin(string selectedDifficulty)
    {
        difficulty=selectedDifficulty;Rule.Reset(difficulty);activeRepetition=-1;activeCycle=0;
        PostureReady=SupportReady=false;TrackingQuality=0;
    }

    public bool CaptureTargets(HipThrustTaikoController source)
    {
        transport=source;var animator=source.CoachAnimator;var timeline=source.PhaseTimeline;
        if(animator==null||timeline==null)return false;
        references=new SideBridgePoseGeometry[timeline.cycles.Length];RecordedHoldTargets=new float[references.Length];
        for(int i=0;i<references.Length;i++)
        {
            var cycle=timeline.cycles[i];Sample(cycle.liftStartFrame);
            if(!SideBridgePoseGeometry.TryMeasure(animator,out var rest))return false;
            Sample(HipThrustHold30Timeline.PeakFrame(timeline,i));
            if(!SideBridgePoseGeometry.TryMeasure(animator,out var top))return false;
            // A brief recorded overshoot must not make the whole stable plateau
            // look like a partial lift. Use its median height for amplitude scaling;
            // the coach still freezes at the original actual peak in the 30s mode.
            var plateau=new float[cycle.lowerStartFrame-cycle.holdStartFrame+1];
            for(int frame=cycle.holdStartFrame;frame<=cycle.lowerStartFrame;frame++)
            {
                Sample(frame);if(!SideBridgePoseGeometry.TryMeasure(animator,out var holdPose))return false;
                plateau[frame-cycle.holdStartFrame]=holdPose.lift;
            }
            System.Array.Sort(plateau);
            float stableLift=plateau[plateau.Length/2];
            var geometry=references[i]=new SideBridgePoseGeometry();
            if(!geometry.SetTargets(rest,top,rest.lift,stableLift))return false;
            // Measure the achievable total valid high-position time of this dynamic cycle.
            // This uses the same entry confirmation and quality gates as the player.
            float achievable=float.PositiveInfinity;
            foreach(int sampleRate in new[]{30,60,120})
            {
                var referenceRule=new SideBridgeHoldRule();referenceRule.Reset(difficulty);
                referenceRule.BeginRepetition(600,600,false);
                double duration=(cycle.lowerEndFrame-cycle.liftStartFrame)/(double)timeline.sourceClip.frameRate;
                for(int sample=0;sample<=System.Math.Floor(duration*sampleRate);sample++)
                {
                    Sample(cycle.liftStartFrame+sample*timeline.sourceClip.frameRate/sampleRate);
                    bool valid=SideBridgePoseGeometry.TryMeasure(animator,out var pose)&&SideBridgePoseGeometry.HasStableSupport(pose,top);
                    referenceRule.Step(valid?geometry.Progress(pose):0,valid?geometry.Quality(pose,top):0,1f/sampleRate,valid);
                }
                achievable=Mathf.Min(achievable,referenceRule.RoundValidHoldSeconds);
            }
            if(achievable<.4f)return false;
            // At most two source frames compensate for render/IMU sampling alignment.
            RecordedHoldTargets[i]=Mathf.Max(.3f,achievable-2/timeline.sourceClip.frameRate);
        }
        SelectRepetition(0);return true;
        void Sample(float frame)
        {animator.Play(source.RoutineStateName,0,frame/timeline.sourceClip.frameRate/timeline.sourceClip.length);animator.Update(0);}
    }

    public void UpdateReference()
    {
        if(transport.Hold30Timeline==null)return;
        int index=0;var reps=transport.Hold30Timeline.Repetitions;
        for(int i=1;i<reps.Length&&transport.Elapsed>=reps[i].start;i++)index=i;
        if(index!=activeRepetition)SelectRepetition(index);
    }

    void SelectRepetition(int index)
    {
        activeRepetition=index;activeCycle=transport.Hold30Timeline!=null?transport.Hold30Timeline.Repetitions[index].cycle:0;
        bool dynamic=transport.PrescriptionDose!=null&&transport.PrescriptionDose.preserveRecordedTiming;
        float goal=dynamic?RecordedHoldTargets[activeCycle]:30;
        Rule.BeginRepetition(goal,dynamic?goal:Rule.DefaultScoreSeconds,!dynamic);
    }

    public bool Evaluate(Animator animator,out float progress,out float quality)
    {
        progress=quality=TrackingQuality=0;PostureReady=SupportReady=false;
        if(Geometry==null||!SideBridgePoseGeometry.TryMeasure(animator,out var pose))return false;
        PostureReady=pose.IsLeftSupport;
        if(!PostureReady)return false;
        progress=Geometry.Progress(pose);quality=Geometry.Quality(pose,Geometry.Top);
        SupportReady=SideBridgePoseGeometry.HasStableSupport(pose,Geometry.Top);
        if(SupportReady&&SideBridgePoseGeometry.TryMeasure(transport.CoachAnimator,out var expected))
            TrackingQuality=Geometry.Quality(pose,expected);
        return SupportReady;
    }

    public void InvalidateInput(){TrackingQuality=0;PostureReady=SupportReady=false;}

    public void Step(float progress,float quality,float deltaTime,bool valid)
    {
        if(activeRepetition<0||transport==null||Geometry==null)
        {Rule.Step(0,0,deltaTime,false,0);return;}
        float eligible=-1;
        if(Rule.RequiresContinuousHold&&transport.Hold30Timeline!=null)
        {
            var rep=transport.Hold30Timeline.Repetitions[activeRepetition];
            // Raised time before/after the instructed hold cannot fill its 30-second goal.
            eligible=(float)System.Math.Max(0,System.Math.Min(transport.Elapsed,rep.lower)-System.Math.Max(transport.Elapsed-deltaTime,rep.top));
        }
        Rule.Step(progress,quality,deltaTime,valid,eligible);
    }

    public bool Matches(Animator user,Animator coach)
    {
        return Geometry!=null&&SideBridgePoseGeometry.TryMeasure(user,out var actual)&&
            SideBridgePoseGeometry.TryMeasure(coach,out var expected)&&expected.IsLeftSupport&&
            SideBridgePoseGeometry.HasStableSupport(actual,expected)&&
            Mathf.Abs(Geometry.Progress(actual)-Geometry.Progress(expected))<=.16f&&Geometry.Quality(actual,expected)>=65;
    }
}
