using System;
using UnityEngine;

/// <summary>Resamples the recorded poses, freezes the actual peak for 30 seconds, and executes an exact dose.</summary>
public sealed class HipThrustHold30Timeline
{
    public const float HoldSeconds = 30;
    public readonly struct Repetition
    {
        public readonly int cycle, peakFrame;
        public readonly double start, top, lower, end;
        public Repetition(int cycle, int peak, double start, double top, double end)
        { this.cycle=cycle; peakFrame=peak; this.start=start; this.top=top; lower=top+HoldSeconds; this.end=end; }
    }
    public Repetition[] Repetitions { get; }
    public double Duration { get; }
    readonly HipThrustPhaseTimeline source;
    readonly float fps;

    public HipThrustHold30Timeline(HipThrustPhaseTimeline source, int count)
    {
        if (count < 1 || count > 2000) throw new ArgumentOutOfRangeException(nameof(count), "处方总次数必须为 1–2000。");
        source.Validate(); this.source=source; fps=source.sourceClip.frameRate;
        Repetitions=new Repetition[count];
        double start=source.cycles[0].liftStartFrame/(double)fps;
        for(int i=0;i<count;i++)
        {
            int index=i%source.cycles.Length; var cycle=source.cycles[index];
            int peak=PeakFrame(source, index);
            double top=start+(peak-cycle.liftStartFrame)/(double)fps;
            double end=top+HoldSeconds+(cycle.lowerEndFrame-peak)/(double)fps;
            Repetitions[i]=new Repetition(index,peak,start,top,end); start=end;
        }
        // A short final rest allows the final endpoint judgement to resolve. Never play subsequent source repetitions.
        Duration=start+.5;
    }

    public static int PeakFrame(HipThrustPhaseTimeline source,int index)
    {
        var cycle=source.cycles[index]; int peak=cycle.holdStartFrame;
        for(int f=cycle.liftStartFrame+1;f<cycle.lowerEndFrame;f++)
            if(source.recordedHeight.Evaluate(f)>source.recordedHeight.Evaluate(peak)) peak=f;
        return peak;
    }

    public HipThrustPhaseTimeline.Sample Evaluate(double time,out float sourceFrame,out float remaining,out int completed)
    {
        var sample=new HipThrustPhaseTimeline.Sample {phase=-1,cycle=-1,completedSeconds=Vector3.one*999};
        remaining=0; completed=0;
        sourceFrame=(float)Math.Max(0,time)*fps;
        foreach(var rep in Repetitions)
        {
            var c=source.cycles[rep.cycle];
            if(time>=rep.top) sample.completedSeconds.x=Mathf.Min(sample.completedSeconds.x,(float)(time-rep.top));
            if(time>=rep.lower) sample.completedSeconds.y=Mathf.Min(sample.completedSeconds.y,(float)(time-rep.lower));
            if(time>=rep.end) sample.completedSeconds.z=Mathf.Min(sample.completedSeconds.z,(float)(time-rep.end));
            if(time>=rep.end) { completed++; sourceFrame=c.lowerEndFrame; continue; }
            if(time<rep.start) break;
            sample.cycle=rep.cycle;
            double begin,end;
            if(time<rep.top)
            { sample.phase=0; begin=rep.start; end=rep.top; sourceFrame=c.liftStartFrame+(float)(time-begin)*fps; }
            else if(time<rep.lower)
            { sample.phase=1; begin=rep.top; end=rep.lower; sourceFrame=rep.peakFrame; }
            else
            { sample.phase=2; begin=rep.lower; end=rep.end; sourceFrame=rep.peakFrame+(float)(time-begin)*fps; }
            sample.progress=Mathf.Clamp01((float)((time-begin)/(end-begin)));
            remaining=(float)(end-time);
            sample.holdSeconds=sample.phase==0?0:sample.phase==1?(float)(time-rep.top):HoldSeconds;
            sample.poseHeight=sample.phase==1?1:Mathf.InverseLerp(c.lowHeight,source.recordedHeight.Evaluate(rep.peakFrame),source.recordedHeight.Evaluate(sourceFrame));
            break;
        }
        return sample;
    }
}
