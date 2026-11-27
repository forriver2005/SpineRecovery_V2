using System;
using UnityEngine;

/// <summary>Action-specific acceptance for the shared ring hold transport. Existing geometry/scorers remain authoritative.</summary>
public sealed class RecordedActionHoldScoring : MonoBehaviour
{
    public enum ActionKind { BirdDog, DeadBug, Crunch, LyingCat }
    public ActionKind kind;
    public HipThrustTaikoController transport;
    public DeadBugGamingPoseScorer calibration;
    public string[] upStates;
    public RhythmBodyTarget[] firstTargets,secondTargets;
    public bool Ready { get; private set; }
    public bool Measured { get; private set; }
    public bool Accepted { get; private set; }
    public bool Matches { get; private set; }
    public float Progress { get; private set; }
    public float Quality { get; private set; }
    public double ValidSeconds { get; private set; }
    public double LongestSeconds { get; private set; }
    public float LastBest { get; private set; }
    public RhythmBodyTarget FirstTarget => firstTargets!=null&&firstTargets.Length>0?firstTargets[cycle%firstTargets.Length]:RhythmBodyTarget.Waist;
    public RhythmBodyTarget SecondTarget => secondTargets!=null&&secondTargets.Length>0?secondTargets[cycle%secondTargets.Length]:RhythmBodyTarget.Waist;
    public bool IsLimbAction => kind==ActionKind.BirdDog||kind==ActionKind.DeadBug;
    public float LiveScore => Accepted?100f*Mathf.Clamp01((float)(ValidSeconds/transport.PrescribedHoldSeconds)):0;
    public string Instruction => IsLimbAction?$"{TargetName(FirstTarget)}＋{TargetName(SecondTarget)} · 保持伸展":"保持目标姿势";
    public string[] PhaseNames => IsLimbAction?new[]{"伸展","保持","收回"}:kind==ActionKind.Crunch?new[]{"卷起","保持","回落"}:new[]{"拱背","保持","还原"};
    RhythmMovementRange range;
    CrunchHoldScorer crunch;
    LyingCatHoldScorer cat;
    LyingCatAnatomicalConstraint constraint;
    int repetition=-1,cycle;
    string difficulty;
    bool startedLow;
    double confirm,continuous;
    float previousCounter;

    void Awake()
    {
        // Install only for these four opt-in scenes; legacy calibration stays unchanged.
        if(calibration.GetComponent<RecordedHoldPreparation>()==null)calibration.gameObject.AddComponent<RecordedHoldPreparation>();
    }

    public bool Begin(string level)
    {
        difficulty=level;repetition=-1;ValidSeconds=LongestSeconds=LastBest=0;Accepted=Measured=Matches=false;
        var user=calibration.UserAnimator;
        if(user==null||!user.isHuman)return Ready=false;
        if(IsLimbAction)
        {
            range=new RhythmMovementRange();
            if(upStates==null||upStates.Length==0||firstTargets.Length!=upStates.Length||secondTargets.Length!=upStates.Length)return Ready=false;
        }
        else
        {
            var coach=transport.CoachAnimator;var state=coach.GetCurrentAnimatorStateInfo(0);float speed=coach.speed;
            try
            {
                using(var patient=new PatientPoseReference(coach,user))
                {
                    var timeline=transport.PhaseTimeline;
                    Sample(timeline.cycles[0].liftStartFrame);patient.Apply();
                    if(kind==ActionKind.Crunch)
                    {
                        if(!CrunchPose.TryCapture(user,out var rest))return Ready=false;
                        Sample(HipThrustHold30Timeline.PeakFrame(timeline,0));patient.Apply();
                        if(!CrunchPose.TryCapture(user,out var top))return Ready=false;
                        crunch=user.GetComponent<CrunchHoldScorer>()??user.gameObject.AddComponent<CrunchHoldScorer>();
                        crunch.enabled=false;crunch.Configure(user,rest,top);crunch.SetDifficultyMode(Difficulty());
                    }
                    else
                    {
                        if(!LyingCatPose.TryCapture(user,out var rest))return Ready=false;
                        Sample(HipThrustHold30Timeline.PeakFrame(timeline,0));patient.Apply();
                        if(!LyingCatPose.TryCapture(user,out var top))return Ready=false;
                        constraint=user.GetComponent<LyingCatAnatomicalConstraint>()??user.gameObject.AddComponent<LyingCatAnatomicalConstraint>();
                        constraint.enabled=false;
                        cat=user.GetComponent<LyingCatHoldScorer>()??user.gameObject.AddComponent<LyingCatHoldScorer>();
                        cat.enabled=false;cat.Configure(user,rest,top);cat.SetDifficultyMode(Difficulty());
                    }
                }
                if(constraint!=null)constraint.Configure(user);
            }
            finally{coach.Play(state.fullPathHash,0,state.normalizedTime);coach.Update(0);coach.speed=speed;}
        }
        Ready=true;SelectRepetition(0);return true;
    }
    HipThrustDifficultyMode Difficulty()=>difficulty=="simple"?HipThrustDifficultyMode.Easy:difficulty=="hard"?HipThrustDifficultyMode.Hard:HipThrustDifficultyMode.Medium;
    void Sample(int frame){var clip=transport.PhaseTimeline.sourceClip;var coach=transport.CoachAnimator;coach.Play(transport.RoutineStateName,0,frame/clip.frameRate/clip.length);coach.Update(0);}
    void SelectRepetition(int index)
    {
        if(repetition>=0)LastBest=100f*Mathf.Clamp01((float)(ValidSeconds/transport.PrescribedHoldSeconds));
        repetition=index;cycle=transport.AssessmentRepetitions()[index].cycle;
        ValidSeconds=LongestSeconds=continuous=confirm=0;previousCounter=0;startedLow=false;
        if(IsLimbAction)range.Begin(calibration,upStates[cycle%upStates.Length]);
        else if(crunch!=null)crunch.BeginContinuousSession();else cat.BeginContinuousSession();
    }
    public void Tick(float dt,bool fresh)
    {
        Accepted=Measured=Matches=false;Progress=Quality=0;
        if(!Ready)return;
        var plan=transport.AssessmentRepetitions();int index=0;
        for(int i=1;i<plan.Length&&transport.Elapsed>=plan[i].start;i++)index=i;
        if(index!=repetition)SelectRepetition(index);
        var rep=plan[index];double now=transport.Elapsed;
        double eligible=Math.Max(0,Math.Min(now,rep.lower)-Math.Max(now-dt,rep.top));
        bool usable=fresh&&dt>0&&dt<=.1001f&&calibration.IsCalibrated&&calibration.HasCurrentUserInput;
        bool accepted=false;
        if(IsLimbAction)
        {
            Measured=usable&&calibration.CaptureAssessmentPose(true).Count>=6;
            if(Measured)
            {
                var minimum=new Vector2(GameDifficultyPolicy.MinimumExtension(difficulty,FirstTarget),GameDifficultyPolicy.MinimumExtension(difficulty,SecondTarget));
                var progress=range.CurrentProgress(calibration,FirstTarget,SecondTarget);Progress=Mathf.Min(progress.x,progress.y);
                bool low=range.AtEndpoint(calibration,FirstTarget,SecondTarget,false,minimum);
                if(now<=rep.top&&low)startedLow=true;
                Quality=Mathf.Min(range.EndpointQuality(calibration,FirstTarget),range.EndpointQuality(calibration,SecondTarget));
                bool atTop=range.AtEndpoint(calibration,FirstTarget,SecondTarget,true,minimum)&&Quality>=GameDifficultyPolicy.FollowingThreshold(difficulty);
                accepted=startedLow&&atTop;
                Matches=startedLow&&(now<rep.lower?atTop:low);
            }
        }
        else
        {
            if(constraint!=null&&usable)constraint.Apply(dt);
            if(crunch!=null)crunch.Sample(dt);else cat.Sample(dt);
            var scorer=(ITaikoHoldScorer)crunch??cat;
            Measured=usable&&scorer.TrackingAvailable;
            Progress=crunch!=null?crunch.CurrentProgress:cat.CurrentProgress;Quality=scorer.CurrentLiveComparisonScore;
            float counter=scorer.ValidHoldSeconds;
            // A prescribed static hold requires the original target-entry amplitude
            // throughout the credited window, not the looser descent hysteresis.
            float targetProgress=crunch!=null?crunch.HoldEntryProgress:cat.HoldEntryProgress;
            accepted=Measured&&(constraint==null||!constraint.RejectedInputThisFrame)&&Progress>=targetProgress&&counter>previousCounter+1e-7f;
            previousCounter=counter;
            Matches=Measured&&(now<rep.lower?accepted:crunch!=null?crunch.IsRelaxedPreparationPose(calibration.UserAnimator,Vector3.up):cat.IsRelaxedPreparationPose(calibration.UserAnimator,Vector3.up));
        }
        confirm=accepted?confirm+Math.Max(0,dt):0;
        Accepted=accepted&&(!IsLimbAction||confirm>=.08);
        if(eligible>0&&Accepted){ValidSeconds+=eligible;continuous+=eligible;LongestSeconds=Math.Max(LongestSeconds,continuous);}
        else if(!Accepted)continuous=0;
    }
    public void BreakContinuity(){continuous=confirm=0;Accepted=false;}
    public void Stop(){crunch?.CancelSession();cat?.CancelSession();Accepted=false;}
    void OnDisable()=>Stop();
    static string TargetName(RhythmBodyTarget target)=>target==RhythmBodyTarget.LeftHand?"左手":target==RhythmBodyTarget.RightHand?"右手":target==RhythmBodyTarget.LeftFoot?"左腿":"右腿";
}
