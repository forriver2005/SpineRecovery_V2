using UnityEngine;
using System.Collections.Generic;

/// <summary>Left-support references, achievable recorded hold targets, and measured repetitions.</summary>
public sealed class SideBridgePoseScoring : MonoBehaviour
{
    public readonly SideBridgeHoldRule Rule=new SideBridgeHoldRule();
    public SideBridgePoseGeometry Geometry => references!=null?references[activeCycle]:null;
    public bool PostureReady { get; private set; }
    public bool SupportReady { get; private set; }
    public bool Stable { get; private set; }
    public bool HighAmplitudeReady { get; private set; }
    public float TrackingQuality { get; private set; }
    public float[] RecordedHoldTargets { get; private set; }
    SideBridgePoseGeometry[] references;
    SideBridgePoseGeometry[] coachReferences;
    HipThrustTaikoController transport;
    int activeRepetition=-1,activeCycle;
    string difficulty;
    readonly SideBridgeMotionSignal signal=new SideBridgeMotionSignal();
    float bias,biasSum,biasSeconds;
    sealed class SampledPose {public double time;public float progress;public SideBridgePoseGeometry.Pose pose;}
    readonly List<SampledPose> history=new List<SampledPose>();

    public void Begin(string selectedDifficulty)
    {
        difficulty=selectedDifficulty;Rule.Reset(difficulty);activeRepetition=-1;activeCycle=0;
        PostureReady=SupportReady=false;TrackingQuality=0;
        signal.Reset();bias=biasSum=biasSeconds=0;history.Clear();
    }

    public bool CaptureTargets(HipThrustTaikoController source)
    {
        transport=source;var animator=source.CoachAnimator;var timeline=source.PhaseTimeline;
        if(animator==null||timeline==null)return false;
        using(var patient=new PatientPoseReference(animator,source.RuleScoring!=null&&source.RuleScoring.calibration!=null?source.RuleScoring.calibration.UserAnimator:null))
        {
        Animator measured=patient.Animator;
        references=new SideBridgePoseGeometry[timeline.cycles.Length];RecordedHoldTargets=new float[references.Length];
        coachReferences=new SideBridgePoseGeometry[references.Length];
        for(int i=0;i<references.Length;i++)
        {
            var cycle=timeline.cycles[i];Sample(cycle.liftStartFrame);
            if(!SideBridgePoseGeometry.TryMeasure(measured,out var rest)||!SideBridgePoseGeometry.TryMeasure(animator,out var coachRest))return false;
            Sample(HipThrustHold30Timeline.PeakFrame(timeline,i));
            if(!SideBridgePoseGeometry.TryMeasure(measured,out var top)||!SideBridgePoseGeometry.TryMeasure(animator,out var coachTop))return false;
            // A brief recorded overshoot must not make the whole stable plateau
            // look like a partial lift. Use its median height for amplitude scaling;
            // the coach still freezes at the original actual peak in the 30s mode.
            var plateau=new float[cycle.lowerStartFrame-cycle.holdStartFrame+1];
            var coachPlateau=new float[plateau.Length];
            for(int frame=cycle.holdStartFrame;frame<=cycle.lowerStartFrame;frame++)
            {
                Sample(frame);if(!SideBridgePoseGeometry.TryMeasure(measured,out var holdPose)||!SideBridgePoseGeometry.TryMeasure(animator,out var coachPose))return false;
                plateau[frame-cycle.holdStartFrame]=holdPose.lift;
                coachPlateau[frame-cycle.holdStartFrame]=coachPose.lift;
            }
            System.Array.Sort(plateau);
            System.Array.Sort(coachPlateau);
            float stableLift=plateau[plateau.Length/2];
            var geometry=references[i]=new SideBridgePoseGeometry();
            if(!geometry.SetTargets(rest,top,rest.lift,stableLift))return false;
            coachReferences[i]=new SideBridgePoseGeometry();
            if(!coachReferences[i].SetTargets(coachRest,coachTop,coachRest.lift,coachPlateau[coachPlateau.Length/2]))return false;
            // Measure the achievable total valid high-position time of this dynamic cycle.
            // This uses the same entry confirmation and quality gates as the player.
            float achievable=float.PositiveInfinity;
            foreach(int sampleRate in new[]{30,60,120})
            {
                var referenceRule=new SideBridgeHoldRule();referenceRule.Reset(difficulty);
                var referenceSignal=new SideBridgeMotionSignal();
                referenceRule.BeginRepetition(600,600,false);
                double duration=(cycle.lowerEndFrame-cycle.liftStartFrame)/(double)timeline.sourceClip.frameRate;
                for(int sample=0;sample<=System.Math.Floor(duration*sampleRate);sample++)
                {
                    Sample(cycle.liftStartFrame+sample*timeline.sourceClip.frameRate/sampleRate);
                    bool valid=SideBridgePoseGeometry.TryMeasure(measured,out var pose)&&SideBridgePoseGeometry.HasStableSupport(pose,top);
                    if(valid)referenceSignal.Sample(geometry.RawProgress(pose),geometry.TopLift-geometry.RestLift,1f/sampleRate);
                    referenceRule.Step(referenceSignal.Progress,valid?geometry.Quality(pose,top,referenceRule.QualityTolerance):0,1f/sampleRate,valid,-1,valid,Mathf.Abs(referenceSignal.LiftSpeed)<=referenceRule.StableSpeed);
                }
                achievable=Mathf.Min(achievable,referenceRule.RoundValidHoldSeconds);
            }
            if(achievable<.4f)return false;
            // At most two source frames compensate for render/IMU sampling alignment.
            RecordedHoldTargets[i]=Mathf.Max(.3f,achievable-2/timeline.sourceClip.frameRate);
        }
        SelectRepetition(0);return true;
        void Sample(float frame)
        {animator.Play(source.RoutineStateName,0,frame/timeline.sourceClip.frameRate/timeline.sourceClip.length);animator.Update(0);patient.Apply();}
        }
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
        Rule.BeginRepetition(goal,goal,!dynamic);
        signal.Reset();history.Clear();bias=biasSum=biasSeconds=0;
    }

    public bool Evaluate(Animator animator,out float progress,out float quality,float deltaTime=.0333333f)
    {
        progress=quality=TrackingQuality=0;PostureReady=SupportReady=Stable=HighAmplitudeReady=false;
        if(Geometry==null||!SideBridgePoseGeometry.TryMeasure(animator,out var pose))return false;
        PostureReady=pose.IsLeftSupport;
        if(!PostureReady){signal.Reset();return true;}
        // Filter before clamping: clipping peaks first turns symmetric sensor
        // noise into a downward bias and unfairly fails a correct high hold.
        float rawProgress=Geometry.RawProgress(pose);
        SideBridgePoseGeometry.TryMeasure(transport.CoachAnimator,out var expected);
        float expectedProgress=expected!=null?coachReferences[activeCycle].Progress(expected):0;
        // Learn only a small zero-height offset while both demonstrator and user
        // are at rest. Never learn amplitude from a patient's incomplete top.
        // Fixed strap/baseline errors cannot turn a half lift into a full one.
        if(biasSeconds<.2f&&expected!=null&&expectedProgress<.10f&&
            rawProgress>=-.60f&&rawProgress<=.65f&&SideBridgePoseGeometry.HasStableSupport(pose,Geometry.Rest)&&
            Geometry.Quality(pose,Geometry.Rest,Rule.QualityTolerance)>=70)
        {
            biasSum+=(rawProgress-expectedProgress)*deltaTime;biasSeconds+=deltaTime;
            // This is a zero offset, never a scale gain. The source's A/B lift
            // span is small, so modest knee strap errors can shift it by 50%.
            // A raised start (>65%) cannot be learned as the floor.
            bias=Mathf.Clamp(biasSum/biasSeconds,-.50f,.50f);
        }
        rawProgress-=bias;
        // A short filter removes normalized-height amplification of sensor jitter.
        // A genuine low return bypasses smoothing; it can never extend a hold.
        signal.Sample(rawProgress,Geometry.TopLift-Geometry.RestLift,deltaTime);
        progress=signal.Progress;quality=Geometry.Quality(pose,Geometry.Top,Rule.QualityTolerance);
        HighAmplitudeReady=expected!=null&&(expectedProgress<.4f||progress>=expectedProgress*(difficulty=="simple"?.75f:difficulty=="hard"?.78f:.76f));
        Stable=Mathf.Abs(signal.LiftSpeed)<=Rule.StableSpeed;
        SupportReady=SideBridgePoseGeometry.HasStableSupport(pose,Geometry.Top);
        if(expected!=null)
        {
            history.Add(new SampledPose{time=transport.Elapsed,progress=expectedProgress,pose=expected});
            while(history.Count>1&&(history[0].time<transport.Elapsed-TrainingScoringTolerance.MaximumResponseDelay||history.Count>160))history.RemoveAt(0);
            if(SupportReady)foreach(var sample in history)
            {
                float difference=Mathf.Abs(progress-sample.progress);
                float following=Mathf.Min(Geometry.Quality(pose,sample.pose,Rule.QualityTolerance),100*(1-Mathf.InverseLerp(.18f,.55f,difference)));
                TrackingQuality=Mathf.Max(TrackingQuality,following);
            }
        }
        if(!SupportReady)quality=0;
        return true;
    }

    public void InvalidateInput(){TrackingQuality=0;PostureReady=SupportReady=Stable=false;signal.Reset();}

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
        Rule.Step(progress,HighAmplitudeReady?quality:0,deltaTime,valid,eligible,SupportReady,Stable,PostureReady);
    }

    public bool Matches(Animator user,Animator coach)
    {
        return Geometry!=null&&SideBridgePoseGeometry.TryMeasure(user,out var actual)&&
            SideBridgePoseGeometry.TryMeasure(coach,out var expected)&&expected.IsLeftSupport&&
            SideBridgePoseGeometry.HasStableSupport(actual,expected)&&
            HighAmplitudeReady&&TrackingQuality>=GameDifficultyPolicy.FollowingThreshold(difficulty)&&
            // During a high demonstration a tiny lift must not HIT merely
            // because its other joints have a good weighted quality score.
            (coachReferences[activeCycle].Progress(expected)<.65f||Mathf.Clamp01(Geometry.RawProgress(actual)-bias)>=Rule.SustainProgress);
    }
}
