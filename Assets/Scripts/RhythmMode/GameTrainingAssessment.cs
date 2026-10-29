using System;
using UnityEngine;

/// <summary>Observes existing training clocks and poses; does not drive animation,
/// HIT, vibration, phase transitions or the prescription executor.</summary>
[DefaultExecutionOrder(250)]
public sealed class GameTrainingAssessment : MonoBehaviour
{
    public TrainingQualitySummary Summary { get; } = new TrainingQualitySummary();
    public bool IsCollecting { get; private set; }
    public bool IsFinished { get; private set; }
    public GameWorkoutScene Entry { get; private set; }
    HipThrustHold30Timeline.Repetition[] bridgePlan;
    TrainingBridgeAttempt[] bridgeAttempts;
    AvatarMotionSource motion;
    int lastPacket,segmentIndex=-1;
    float lastPacketTime=float.NegativeInfinity;
    double segmentDuration,segmentObserved,segmentQualified,endpointGood;
    bool upQualified;
    RhythmBodyTarget first,second;
    bool hasTargets;
    RhythmMovementRange movementRange;
    Vector2[] minimumMotion,maximumMotion;
    public float EndpointQuality(RhythmBodyTarget target)=>movementRange!=null?movementRange.EndpointQuality(Entry.scorer,target):0;
    float Threshold => Entry.menu.SelectedDifficultyKey=="simple"?60:Entry.menu.SelectedDifficultyKey=="hard"?80:70;

    public void Initialize(GameWorkoutScene entry)
    {
        Entry=entry;
        gameObject.AddComponent<TrainingResultView>().Initialize(this);
        if(entry.taiko!=null)entry.taiko.Assessment=this;
        else
        {
            entry.coach.ScoringSessionStarted+=BeginRhythm;
            entry.coach.SegmentPlaybackStarted+=BeginSegment;
            entry.coach.ScoringCheckpointStarted+=EndSegment;
            entry.coach.ScoringSessionCompleted+=Finish;
        }
    }

    void OnDestroy()
    {
        if(Entry==null)return;
        if(Entry.taiko!=null)Entry.taiko.Assessment=null;
        else if(Entry.coach!=null)
        {
            Entry.coach.ScoringSessionStarted-=BeginRhythm;
            Entry.coach.SegmentPlaybackStarted-=BeginSegment;
            Entry.coach.ScoringCheckpointStarted-=EndSegment;
            Entry.coach.ScoringSessionCompleted-=Finish;
        }
    }

    void ResetInput()
    {
        motion=FindObjectOfType<AvatarMotionSource>();
        lastPacket=motion!=null?motion.LastPacketframeCounterInFrame:0;
        lastPacketTime=float.NegativeInfinity;IsCollecting=true;IsFinished=false;
    }

    // Observe input during pauses too: an old packet must never become fresh on resume.
    void Update()
    {
        if(motion==null)motion=FindObjectOfType<AvatarMotionSource>();
        if(motion!=null && motion.LastPacketframeCounterInFrame!=lastPacket)
        {lastPacket=motion.LastPacketframeCounterInFrame;lastPacketTime=Time.unscaledTime;}
        if(IsCollecting && Entry.menu.IsTrainingMenuVisible)IsCollecting=false;
        if(IsCollecting && Entry.taiko!=null && Entry.taiko.IsPaused && bridgeAttempts!=null)
            foreach(var attempt in bridgeAttempts)attempt.BreakContinuity();
    }
    bool Fresh => Entry.scorer!=null && Entry.scorer.IsCalibrated && motion!=null &&
        motion.HasFreshFrame && Time.unscaledTime-lastPacketTime<=.5f;

    public void BeginBridge()
    {
        ResetInput();bridgePlan=Entry.taiko.AssessmentRepetitions();
        bridgeAttempts=new TrainingBridgeAttempt[bridgePlan.Length];
        var durations=new double[bridgePlan.Length];var targets=new double[bridgePlan.Length];
        var following=new double[bridgePlan.Length];
        bool continuous=Entry.taiko.Hold30Timeline!=null&&!Entry.taiko.Hold30Timeline.UsesRecordedTiming;
        var rules=Entry.taiko.RuleScoring;
        for(int i=0;i<bridgePlan.Length;i++)
        {
            var rep=bridgePlan[i];durations[i]=rep.end-rep.start;
            following[i]=(rep.top-rep.start)+(rep.end-rep.lower);
            targets[i]=rules!=null && rules.sideBridge!=null && !continuous && rules.TargetsReady
                ? rules.sideBridge.RecordedHoldTargets[rep.cycle] : rep.lower-rep.top;
            bridgeAttempts[i]=new TrainingBridgeAttempt();
        }
        Summary.Reset(durations,targets,true,continuous,Threshold,following);
        ResetMotionEvidence();
    }

    public void ObserveBridge(float deltaTime)
    {
        if(!IsCollecting||Entry.taiko.IsPaused||bridgePlan==null)return;
        var rules=Entry.taiko.RuleScoring;
        double now=Entry.taiko.Elapsed,dt=Math.Max(0,deltaTime),before=now-dt;
        bool tracked=rules!=null && rules.HasMeasurement && rules.InputFresh && Entry.scorer.IsCalibrated &&
            motion!=null && motion.LastBonePacketCounterInFrame>0;
        // A stalled rendering frame cannot manufacture seconds of patient evidence.
        // Its elapsed time remains in the denominator, but no pose is extrapolated.
        bool usable=tracked && dt<=.1001;
        float quality=rules!=null?rules.TrackingQuality:0;
        for(int i=0;i<bridgePlan.Length;i++)
        {
            var rep=bridgePlan[i];
            double overlap=Math.Max(0,Math.Min(now,rep.end)-Math.Max(before,rep.start));
            double holding=Math.Max(0,Math.Min(now,rep.lower)-Math.Max(before,rep.top));
            if(overlap>0)
            {
                Summary.Observe(i,overlap,usable,quality,usable&&quality>=Threshold,Math.Max(0,overlap-holding));
                if(usable)ObserveMotion(i,new Vector2(rules.Progress,rules.Progress));
            }
        }
        if(rules==null)return;
        int index=0;
        for(int i=1;i<bridgePlan.Length&&now>=bridgePlan[i].start;i++)index=i;
        if(bridgePlan.Length==0)return;
        if(rules.sideBridge!=null)
        {
            var rule=rules.sideBridge.Rule;
            double credit=Summary.IsContinuous?rule.BestContinuousSeconds:rule.RoundValidHoldSeconds;
            if(rule.RoundFinished)credit=rule.LastRoundHoldSeconds;
            double longest=rule.BestContinuousSeconds;
            if(Summary.IsContinuous)
            {
                var rep=bridgePlan[index];var measured=bridgeAttempts[index];
                double eligible=Math.Max(0,Math.Min(now,rep.lower)-Math.Max(before,rep.top));
                float lowPoint=Entry.menu.SelectedDifficultyKey=="simple"?.22f:Entry.menu.SelectedDifficultyKey=="hard"?.25f:.23f;
                bool high=rules.sideBridge.SupportReady && rules.Progress>=rule.EnterProgress && rules.Quality>=rule.ValidQuality;
                measured.Step(dt,eligible,usable,rules.Progress<=lowPoint,high,now>=rep.lower);
                credit=longest=measured.LongestHold;
            }
            Summary.RecordDose(index,credit,longest,rule.RoundFinished,
                credit+.0001>=Summary.Repetitions[index].targetSeconds);
            return;
        }
        var p=bridgePlan[index];var attempt=bridgeAttempts[index];
        double hold=Math.Max(0,Math.Min(now,p.lower)-Math.Max(before,p.top));
        float low=Entry.menu.SelectedDifficultyKey=="simple"?.10f:Entry.menu.SelectedDifficultyKey=="hard"?.18f:.15f;
        bool good=usable&&quality>=Threshold&&Mathf.Abs(rules.Progress-rules.CoachProgress)<=.16f;
        attempt.Step(dt,hold,usable,rules.Progress<=low,good,now>=p.lower);
        double achieved=Summary.IsContinuous?attempt.LongestHold:attempt.TotalHold;
        Summary.RecordDose(index,achieved,attempt.LongestHold,attempt.Completed,
            achieved+.0001>=Summary.Repetitions[index].targetSeconds);
    }

    void BeginRhythm()
    {
        ResetInput();segmentIndex=-1;upQualified=false;hasTargets=false;
        movementRange=new RhythmMovementRange();
        var durations=Entry.coach.AssessmentDurations;
        Summary.Reset(durations,durations,false,false,Threshold);
        ResetMotionEvidence();
    }
    void BeginSegment(CoachActionController.SegmentPlaybackInfo info)
    {
        if(!IsCollecting)return;
        segmentIndex++;segmentDuration=Entry.coach.SegmentSeconds(info.stateName,info.playbackSpeed);
        segmentObserved=segmentQualified=endpointGood=0;
        // Down states may not spawn notes. They use the same contralateral pair as their up state.
        string up=info.stateName.EndsWith("_down",StringComparison.Ordinal)?info.stateName.Substring(0,info.stateName.Length-5)+"_up":info.stateName;
        hasTargets=Entry.rhythm!=null && Entry.rhythm.TryGetSegmentTargets(up,out first,out second);
        if(hasTargets)movementRange.Begin(Entry.scorer,up);
    }
    void LateUpdate()
    {
        if(!IsCollecting||Entry.taiko!=null||Entry.coach.IsActionPaused||segmentIndex<0||Entry.coach.IsHoldingPose)return;
        SampleRhythm(Time.deltaTime);
    }
    void SampleRhythm(double seconds)
    {
        var limbs=Entry.scorer.LastLimbScores;
        var a=Limb(limbs,first);var b=Limb(limbs,second);
        bool tracked=Fresh&&hasTargets&&a.validSegmentCount>0&&b.validSegmentCount>0&&seconds<=.1001;
        // The weaker active limb owns the quality: a stationary left arm cannot
        // be hidden by a good right leg or three inactive limbs.
        float quality=Mathf.Min(a.rawScore,b.rawScore);
        if(tracked)quality=Mathf.Min(quality,movementRange.FollowingQuality(Entry.scorer,first,second));
        if(tracked)ObserveMotion(segmentIndex/2,movementRange.CurrentProgress(Entry.scorer,first,second));
        bool good=tracked&&quality>=Threshold;
        double dt=Math.Min(Math.Max(0,seconds),Math.Max(0,segmentDuration-segmentObserved));
        segmentObserved+=dt;if(good)segmentQualified+=dt;
        endpointGood=good?endpointGood+dt:0;
        Summary.Observe(segmentIndex/2,dt,tracked,quality,good);
    }
    void EndSegment(CoachActionController.ScoringCheckpointInfo info)
    {
        if(!IsCollecting||segmentIndex<0)return;
        double remaining=segmentDuration-segmentObserved;
        if(remaining>0&&remaining<=.1)SampleRhythm(remaining);
        bool success=segmentDuration>0 && segmentQualified/segmentDuration>=.8 && endpointGood>=.08;
        if(info.isUp)upQualified=success;
        else
        {
            bool qualified=upQualified&&success;
            Summary.RecordDose(segmentIndex/2,0,0,qualified,qualified);upQualified=false;
        }
    }
    static AnatomicalPoseScore Limb(AnatomicalLimbScores s,RhythmBodyTarget target)
    {
        switch(target)
        {
            case RhythmBodyTarget.LeftHand:return s.leftArm;
            case RhythmBodyTarget.RightHand:return s.rightArm;
            case RhythmBodyTarget.LeftFoot:return s.leftLeg;
            default:return s.rightLeg;
        }
    }
    void ResetMotionEvidence()
    {
        minimumMotion=new Vector2[Summary.Planned];maximumMotion=new Vector2[Summary.Planned];
        for(int i=0;i<Summary.Planned;i++){minimumMotion[i]=Vector2.one*float.PositiveInfinity;maximumMotion[i]=Vector2.one*float.NegativeInfinity;}
    }
    void ObserveMotion(int index,Vector2 value)
    {
        if(index<0||index>=Summary.Planned)return;
        minimumMotion[index]=Vector2.Min(minimumMotion[index],value);maximumMotion[index]=Vector2.Max(maximumMotion[index],value);
    }
    public void Finish()
    {
        if(minimumMotion!=null)for(int i=0;i<Summary.Planned;i++)
        {
            var range=maximumMotion[i]-minimumMotion[i];
            // Matching the coach's neutral pose while doing nothing is not training.
            if(Mathf.Max(range.x,range.y)<.15f)Summary.RejectStationary(i);
        }
        IsCollecting=false;IsFinished=true;
    }
}
