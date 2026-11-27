using System;
using System.Collections.Generic;
using UnityEngine;

// Only the session settlement observes these snapshots. Existing action scorers and HIT are untouched.
public sealed partial class GameTrainingAssessment
{
    public FinalTrainingScore FinalScore { get; }=new FinalTrainingScore();
    sealed class FinalEndpoint
    {
        public int repetition;public bool top,lastTracked;public double elapsed,confirmed,windowSeconds,delay,observed;
        public double confirmedAt=double.NegativeInfinity,confirmedDuration,confirmedTracked,confirmedIntegral;
        public RhythmMovementRange reference;public RhythmBodyTarget a,b;public Vector2 minimum;
        public FinalScoreWindow samples,filter;
    }
    readonly List<FinalEndpoint> finalPending=new List<FinalEndpoint>();
    FinalEndpoint finalEndpoint;
    FinalScoreWindow[] bridgeReturns;
    double[] bridgeLastTime;
    void FinalBeginBridge(bool continuous)
    {
        var plans=new FinalTrainingScore.Plan[bridgePlan.Length];bridgeReturns=new FinalScoreWindow[plans.Length];bridgeLastTime=new double[plans.Length];
        for(int i=0;i<plans.Length;i++)
        {
            var p=bridgePlan[i];double w=Math.Min(.25,(p.end-p.lower)*.25);
            plans[i]=new FinalTrainingScore.Plan(p.end-p.start,p.lower-p.top,p.top-p.start,p.end-p.lower,w);
            bridgeReturns[i]=new FinalScoreWindow(w);bridgeLastTime[i]=p.start;
        }
        FinalScore.Reset(continuous?FinalTrainingScore.Mode.TimedHold:FinalTrainingScore.Mode.Repetition,plans);
    }
    void FinalObserveBridge(double seconds)
    {
        var game=Entry.taiko;var rules=game.RuleScoring;if(rules==null||bridgeLastTime==null)return;
        double now=game.Elapsed;
        bool measured=rules.TargetsReady&&rules.InputFresh&&rules.HasMeasurement&&Entry.scorer.IsCalibrated&&seconds<=.1001;
        bool matches=measured&&(rules.sideBridge!=null?rules.MatchesSideBridgeCoach():rules.MatchesHipCoach());
        float low=rules.sideBridge!=null?rules.sideBridge.Rule.LowProgress:Entry.menu.SelectedDifficultyKey=="simple"?.10f:Entry.menu.SelectedDifficultyKey=="hard"?.18f:.15f;
        // A source-confirmed completed return remains evidence even when its displayed progress advances again.
        bool returnedByRule=rules.sideBridge!=null&&rules.sideBridge.Rule.RoundFinished;
        bool atRest=measured&&((rules.Quality>0&&rules.Progress<=low)||returnedByRule);
        for(int i=0;i<bridgePlan.Length;i++)
        {
            var p=bridgePlan[i];double end=Math.Min(now,p.end),before=bridgeLastTime[i];
            if(end<=before)continue;
            double dt=end-before;bridgeLastTime[i]=end;
            bool valid=measured&&dt<=.1001;
            double holding=Overlap(before,end,p.top,p.lower);
            if(FinalScore.Kind==FinalTrainingScore.Mode.TimedHold)
            {
                if(holding<=0)continue;
                FinalScore.ObserveCoverage(i,holding,valid);
                bool accepted=rules.recordedAction!=null?rules.recordedAction.Accepted:rules.sideBridge!=null?rules.sideBridge.Rule.HoldingCorrectly:rules.Rule.HoldingCorrectly;
                double counter=rules.recordedAction!=null?rules.recordedAction.ValidSeconds:rules.sideBridge!=null?rules.sideBridge.Rule.RoundFinished?rules.sideBridge.Rule.LastRoundHoldSeconds:rules.sideBridge.Rule.RoundValidHoldSeconds:rules.Rule.ValidHoldSeconds;
                FinalScore.ObserveHoldCounter(i,holding,valid,accepted,counter);
                continue;
            }
            FinalScore.ObserveCoverage(i,dt,valid);
            if(valid)FinalScore.ObserveMovement(i,new Vector2(rules.Progress,rules.Progress),atRest,matches&&now>=p.top&&now<=p.lower,now>=p.lower);
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Lift,Overlap(before,end,p.start,p.top),valid,rules.TrackingQuality);
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Lower,Overlap(before,end,p.lower,p.end),valid,rules.TrackingQuality);
            // Existing target/geometry gates own acceptance; process zeroes never zero a valid target phase.
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Target,holding,valid,matches?rules.Quality:0);
            double window=FinalScore.Attempts[i].plan.endpoint;
            double returning=Overlap(before,end,p.end-window,p.end);
            bridgeReturns[i].Add(returning,valid&&atRest?100:0,valid);
            if(end>=p.end-1e-8)FinalScore.SetEndpoint(i,false,bridgeReturns[i],window);
        }
    }
    static double Overlap(double a,double b,double x,double y)=>Math.Max(0,Math.Min(b,y)-Math.Max(a,x));
    void FinalBeginRhythm(double[] durations)
    {
        finalPending.Clear();finalEndpoint=null;
        var plans=new FinalTrainingScore.Plan[durations.Length];
        for(int i=0;i<plans.Length;i++)plans[i]=new FinalTrainingScore.Plan(durations[i],0,durations[i]/2,durations[i]/2,Math.Min(.25,durations[i]/8));
        FinalScore.Reset(FinalTrainingScore.Mode.Repetition,plans);
    }
    void FinalBeginSegment()
    {
        if(segmentIndex<0||segmentIndex/2>=FinalScore.Planned)return;
        int rep=segmentIndex/2;
        FinalScore.SetProcessDuration(rep,segmentUp,segmentDuration);
        double window=Math.Min(.25,segmentDuration*.25);
        finalEndpoint=new FinalEndpoint{repetition=rep,top=segmentUp,reference=hasTargets?movementRange.CaptureReference():null,
            a=first,b=second,minimum=EndpointMinimums,windowSeconds=window,samples=new FinalScoreWindow(window),filter=new FinalScoreWindow(.10)};
    }
    void FinalObserveRhythm(double seconds,bool tracked,float processQuality)
    {
        if(finalEndpoint==null||seconds<=0)return;
        int rep=segmentIndex/2;
        FinalScore.ObserveCoverage(rep,seconds,tracked);
        FinalScore.ObservePart(rep,segmentUp?FinalTrainingScore.Part.Lift:FinalTrainingScore.Part.Lower,seconds,tracked,processQuality);
        if(tracked)
        {
            var limbs=Entry.scorer.LastLimbScores;
            finalEndpoint.delay=Mathf.Clamp(.5f*(Limb(limbs,first).matchedDelay+Limb(limbs,second).matchedDelay),0,TrainingScoringTolerance.MaximumResponseDelay);
        }
        ObserveEndpoint(finalEndpoint,seconds,tracked);
    }
    void ObserveEndpoint(FinalEndpoint endpoint,double seconds,bool tracked)
    {
        endpoint.observed+=Math.Max(0,seconds);
        if(endpoint.reference==null){endpoint.samples.Add(seconds,0,false);return;}
        bool valid=tracked&&Fresh&&seconds<=.1001;endpoint.lastTracked=valid;
        bool atEnd=valid&&endpoint.reference.AtEndpoint(Entry.scorer,endpoint.a,endpoint.b,endpoint.top,endpoint.minimum);
        float quality=0;
        if(valid)
        {
            var progress=endpoint.reference.CurrentProgress(Entry.scorer,endpoint.a,endpoint.b);
            bool atRest=endpoint.reference.AtEndpoint(Entry.scorer,endpoint.a,endpoint.b,false,endpoint.minimum);
            bool atTop=endpoint.reference.AtEndpoint(Entry.scorer,endpoint.a,endpoint.b,true,endpoint.minimum);
            FinalScore.ObserveMovement(endpoint.repetition,progress,atRest,atTop,!endpoint.top);
            quality=endpoint.top?Mathf.Min(endpoint.reference.EndpointQuality(Entry.scorer,endpoint.a),endpoint.reference.EndpointQuality(Entry.scorer,endpoint.b)):atRest?100:0;
            endpoint.filter.Add(seconds,quality);quality=endpoint.filter.Median;
        }
        else endpoint.filter.Clear();
        endpoint.samples.Add(seconds,quality,valid);
        endpoint.confirmed=atEnd?endpoint.confirmed+seconds:0;
        if(endpoint.confirmed+.00001>=.08)
        {
            endpoint.confirmedAt=endpoint.observed;endpoint.confirmedDuration=endpoint.samples.Duration;
            endpoint.confirmedTracked=endpoint.samples.Tracked;endpoint.confirmedIntegral=endpoint.samples.Integral;
        }
    }
    void FinalEndSegment()
    {
        if(finalEndpoint==null)return;
        // Keep recent confirmed evidence when the cue packet is missing. Otherwise,
        // observe the existing matched response delay before sampling the top window.
        if(RecentlyConfirmed(finalEndpoint)&&(!finalEndpoint.top||finalEndpoint.delay<.05||!finalEndpoint.lastTracked))
            SettleEndpoint(finalEndpoint);
        else finalPending.Add(finalEndpoint);
        finalEndpoint=null;
    }
    void FinalProcessPending(double seconds)
    {
        for(int i=finalPending.Count-1;i>=0;i--)
        {
            var p=finalPending[i];p.elapsed+=Math.Max(0,seconds);
            ObserveEndpoint(p,seconds,Fresh&&seconds<=.1001);
            if((RecentlyConfirmed(p)&&(!p.top||p.elapsed>=p.delay))||p.elapsed>=TrainingScoringTolerance.MaximumResponseDelay)
            {SettleEndpoint(p);finalPending.RemoveAt(i);}
        }
    }
    static bool RecentlyConfirmed(FinalEndpoint p)=>p.observed-p.confirmedAt<=.3+.00001;
    void SettleEndpoint(FinalEndpoint p)
    {
        if(!p.top&&RecentlyConfirmed(p))
        {
            // Return credit describes the confirmed endpoint, not the low scores while approaching it.
            var confirmed=new FinalScoreWindow(.08);confirmed.Add(.08,100);
            FinalScore.SetEndpoint(p.repetition,false,confirmed,.08);
        }
        else if(p.top&&RecentlyConfirmed(p))
            FinalScore.SetEndpointEvidence(p.repetition,true,p.windowSeconds,p.confirmedDuration,p.confirmedTracked,p.confirmedIntegral);
        else FinalScore.SetEndpoint(p.repetition,p.top,p.samples,p.windowSeconds);
    }
    void FinalFinish()
    {
        foreach(var p in finalPending)SettleEndpoint(p);
        finalPending.Clear();FinalScore.Finish();
    }
}
