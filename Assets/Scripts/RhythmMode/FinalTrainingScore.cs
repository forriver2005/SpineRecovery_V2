using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Settlement only. Consumes existing action scores/validity, never controls a scorer, HIT or a coach.</summary>
public sealed class FinalTrainingScore
{
    public const string Version = "phase-80-5-15_hold-ratio_v2";
    public const float MinimumCoverage = .95f;
    public enum Mode { Repetition, TimedHold }
    public enum Part { Target, Lift, Lower, Return }
    public sealed class Plan
    {
        public double duration, target, lift, lower, endpoint = .25;
        public Plan(double duration, double target, double lift, double lower, double endpoint = .25)
        { this.duration=duration;this.target=target;this.lift=lift;this.lower=lower;this.endpoint=endpoint; }
    }
    public sealed class Attempt
    {
        public Plan plan;
        public double observed, tracked, hold, continuous, longest, previousHoldCounter;
        public bool counterInitialized, startedLow, targetReached, returned;
        public Vector2 minimum=Vector2.one*float.PositiveInfinity,maximum=Vector2.one*float.NegativeInfinity;
        internal readonly Bucket[] parts={new Bucket(),new Bucket(),new Bucket(),new Bucket()};
        public float Target => parts[0].Value;
        public float Process => .5f*(parts[1].Value+parts[2].Value);
        public float Return => parts[3].Value;
        public bool HasMovement => startedLow && Mathf.Min(maximum.x-minimum.x,maximum.y-minimum.y)>=.15f;
        public float Score => HasMovement ? .8f*Target+.05f*Process+.15f*Return : 0;
        public bool Complete => HasMovement&&targetReached&&returned;
        public bool ContinuousMet => longest+1e-3>=plan.target;
    }
    internal sealed class Bucket
    {
        public double integral, measured, tracked, expected;
        public readonly FinalScoreWindow filter=new FinalScoreWindow(.10);
        public float Value => expected>0 ? Mathf.Clamp((float)(integral/expected),0,100) : 0;
        public void Sample(double seconds,bool tracked,float quality)
        {
            double dt=Math.Min(seconds,Math.Max(0,expected-measured));
            measured+=dt;
            if(!tracked){filter.Clear();return;}
            this.tracked+=dt;filter.Add(dt,quality);
            integral+=dt*filter.Median;
        }
        public void Assign(double expectedSeconds,double observedSeconds,double trackedSeconds,double qualityIntegral)
        { expected=expectedSeconds;measured=Math.Min(expected,observedSeconds);tracked=Math.Min(expected,trackedSeconds);integral=Math.Min(100*expected,Math.Max(0,qualityIntegral)); }
    }
    public Mode Kind { get; private set; }
    public Attempt[] Attempts { get; private set; }=Array.Empty<Attempt>();
    public bool Finished { get; private set; }
    public event Action<FinalTrainingScore> Settled;
    public int Planned => Attempts.Length;
    public int Complete { get {int n=0;foreach(var a in Attempts)if(a.Complete)n++;return n;} }
    public int ContinuousCompleted { get {int n=0;foreach(var a in Attempts)if(a.ContinuousMet)n++;return n;} }
    public double Expected => Sum(a=>Kind==Mode.TimedHold?a.plan.target:a.plan.duration);
    public double Tracked => Sum(a=>a.tracked);
    public double ValidHold => Sum(a=>Math.Min(a.hold,a.plan.target));
    public double TargetHold => Sum(a=>a.plan.target);
    public double Longest => Max(a=>a.longest);
    public float Coverage => Expected>0?Mathf.Clamp01((float)(Tracked/Expected)):0;
    public bool CanPublish => Finished&&Planned>0&&Coverage+1e-5>=MinimumCoverage&&KeyWindowsObserved;
    bool KeyWindowsObserved
    {
        get
        {
            if(Kind==Mode.TimedHold)return true;
            foreach(var a in Attempts)
                foreach(int i in new[]{0,3})
                    if(a.parts[i].expected<=0||a.parts[i].tracked+1e-5<a.parts[i].expected*.8)return false;
            return true;
        }
    }
    public float TargetQuality => Mean(a=>a.HasMovement?a.Target:0);
    public float ProcessQuality => Mean(a=>a.HasMovement?a.Process:0);
    public float ReturnQuality => Mean(a=>a.HasMovement?a.Return:0);
    public float ExactScore => Kind==Mode.TimedHold ? TargetHold>0?Mathf.Clamp((float)(100*ValidHold/TargetHold),0,100):0 : Mean(a=>a.Score);
    public int Score => Mathf.RoundToInt(ExactScore);
    public float UpperBound => Kind==Mode.TimedHold&&TargetHold>0?Mathf.Clamp((float)(100*(ValidHold+Math.Max(0,Expected-Tracked))/TargetHold),0,100):100;
    public void Reset(Mode mode,Plan[] plans)
    {
        Kind=mode;Finished=false;Attempts=new Attempt[plans.Length];
        for(int i=0;i<plans.Length;i++)
        {
            var p=plans[i];if(!Finite(p.duration)||p.duration<=0||p.target<0||p.lift<0||p.lower<0)throw new ArgumentException("Invalid settlement plan");
            var a=Attempts[i]=new Attempt{plan=p};
            a.parts[0].expected=p.target>0?p.target:p.endpoint;
            a.parts[1].expected=p.lift;a.parts[2].expected=p.lower;a.parts[3].expected=p.endpoint;
        }
    }
    public void SetProcessDuration(int index,bool up,double seconds)
    {
        if(index<0||index>=Planned||seconds<=0)return;
        var a=Attempts[index];a.parts[up?1:2].expected=seconds;
        if(up)a.plan.lift=seconds;else a.plan.lower=seconds;
    }
    public void ObserveCoverage(int index,double seconds,bool tracked)
    {
        if(Finished||index<0||index>=Planned||!Finite(seconds)||seconds<=0)return;
        var a=Attempts[index];double limit=Kind==Mode.TimedHold?a.plan.target:a.plan.duration;
        double dt=Math.Min(seconds,Math.Max(0,limit-a.observed));a.observed+=dt;if(tracked)a.tracked+=dt;
    }
    public void ObservePart(int index,Part part,double seconds,bool tracked,float quality)
    {
        if(Finished||index<0||index>=Planned||!Finite(seconds)||seconds<=0)return;
        bool valid=tracked&&!float.IsNaN(quality)&&!float.IsInfinity(quality);
        Attempts[index].parts[(int)part].Sample(seconds,valid,Mathf.Clamp(quality,0,100));
    }
    public void SetEndpoint(int index,bool top,FinalScoreWindow window,double expected)
    {
        if(Finished||index<0||index>=Planned)return;
        Attempts[index].parts[top?0:3].Assign(expected,window.Duration,window.Tracked,window.Integral);
    }
    public void SetEndpointEvidence(int index,bool top,double expected,double observed,double tracked,double integral)
    {
        if(Finished||index<0||index>=Planned)return;
        Attempts[index].parts[top?0:3].Assign(expected,observed,tracked,integral);
    }
    public void ObserveMovement(int index,Vector2 progress,bool atRest,bool atTarget,bool returning)
    {
        if(Finished||index<0||index>=Planned||!Finite(progress.x)||!Finite(progress.y))return;
        var a=Attempts[index];a.minimum=Vector2.Min(a.minimum,progress);a.maximum=Vector2.Max(a.maximum,progress);
        if(!returning&&atRest)a.startedLow=true;
        if(a.startedLow&&atTarget)a.targetReached=true;
        if(returning&&a.targetReached&&atRest)a.returned=true;
    }
    /// <summary>Integrates positive increments of the existing scorer's accepted hold timer.
    /// Resets can break continuity but never erase previously confirmed time in this settlement.</summary>
    public void ObserveHoldCounter(int index,double seconds,bool tracked,bool qualified,double counter)
    {
        if(Finished||index<0||index>=Planned||seconds<=0||!Finite(seconds))return;
        var a=Attempts[index];counter=Finite(counter)?Math.Max(0,counter):0;
        bool reset=a.counterInitialized&&counter+1e-6<a.previousHoldCounter;
        if(reset)a.continuous=0;
        double delta=Math.Max(0,counter-(a.counterInitialized&&!reset?a.previousHoldCounter:0));
        a.counterInitialized=true;a.previousHoldCounter=counter;
        if(!tracked||!qualified){a.continuous=0;return;}
        // A scorer may confirm a short entry window in one tick; credit only time already measured.
        delta=Math.Min(delta,Math.Max(0,Math.Min(a.tracked,a.plan.target)-a.hold));
        a.hold+=delta;a.continuous+=delta;a.longest=Math.Max(a.longest,a.continuous);
    }
    // Direct accepted seconds are used only by collectors that already expose per-sample acceptance.
    public void ObserveHold(int index,double seconds,bool tracked,bool qualified)
    {
        if(Finished||index<0||index>=Planned||seconds<=0||!Finite(seconds))return;
        var a=Attempts[index];
        if(!tracked||!qualified){a.continuous=0;return;}
        double dt=Math.Min(seconds,Math.Max(0,a.plan.target-a.hold));a.hold+=dt;a.continuous+=dt;a.longest=Math.Max(a.longest,a.continuous);
    }
    public void BreakContinuity(){foreach(var a in Attempts){a.continuous=0;foreach(var b in a.parts)b.filter.Clear();}}
    public void Finish(){if(Finished)return;Finished=true;Settled?.Invoke(this);}
    double Sum(Func<Attempt,double> value){double n=0;foreach(var a in Attempts)n+=value(a);return n;}
    double Max(Func<Attempt,double> value){double n=0;foreach(var a in Attempts)n=Math.Max(n,value(a));return n;}
    float Mean(Func<Attempt,float> value){float n=0;foreach(var a in Attempts)n+=value(a);return Planned>0?n/Planned:0;}
    static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
}

/// <summary>Time-bounded settlement evidence. Unknown intervals remain zero credit, never replayed samples.</summary>
public sealed class FinalScoreWindow
{
    struct Sample{public double seconds;public float quality;public bool known;}
    readonly List<Sample> samples=new List<Sample>();
    readonly double limit;
    public double Duration {get;private set;}
    public double Integral {get;private set;}
    public double Tracked {get;private set;}
    public FinalScoreWindow(double seconds){limit=Math.Max(.001,seconds);}
    public void Clear(){samples.Clear();Duration=Integral=Tracked=0;}
    public void Add(double seconds,float quality,bool known=true)
    {
        if(seconds<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))return;
        quality=float.IsNaN(quality)||float.IsInfinity(quality)?0:Mathf.Clamp(quality,0,100);
        seconds=Math.Min(seconds,limit);samples.Add(new Sample{seconds=seconds,quality=quality,known=known});Duration+=seconds;Integral+=seconds*quality;if(known)Tracked+=seconds;
        while(Duration>limit+1e-9&&samples.Count>0)
        {
            var s=samples[0];double take=Math.Min(s.seconds,Duration-limit);Duration-=take;Integral-=take*s.quality;if(s.known)Tracked-=take;s.seconds-=take;
            if(s.seconds<1e-9)samples.RemoveAt(0);else samples[0]=s;
        }
    }
    public float Median
    {
        get
        {
            if(samples.Count==0)return 0;
            var sorted=samples.ToArray();Array.Sort(sorted,(a,b)=>a.quality.CompareTo(b.quality));double t=0;
            foreach(var s in sorted){t+=s.seconds;if(t>=Duration*.5-1e-9)return s.quality;}return 0;
        }
    }
}
