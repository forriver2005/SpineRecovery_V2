using System;
using UnityEngine;

namespace BridgeWave
{
    public sealed partial class BridgeWaveFollowController
    {
        public FinalTrainingScore FinalScore {get;}=new FinalTrainingScore();
        FinalScoreWindow[] finalReturns;
        int validScoreSamples;
        void ResetFinalScore()
        {
            validScoreSamples = 0;
            var plans=new FinalTrainingScore.Plan[game.totalCount];finalReturns=new FinalScoreWindow[plans.Length];
            for(int i=0;i<plans.Length;i++)
            {
                var c=game.standardTrack.cycles[i%game.standardTrack.cycles.Length];double w=Math.Min(.25,c.lowerSeconds*.25);
                plans[i]=new FinalTrainingScore.Plan(c.Duration,c.holdSeconds,c.liftSeconds,c.lowerSeconds,w);finalReturns[i]=new FinalScoreWindow(w);
            }
            FinalScore.Reset(FinalTrainingScore.Mode.Repetition,plans);
        }
        void ObserveFinalScore(double before,double now,float frameSeconds)
        {
            bool valid=input.Valid&&!IsFlowTest&&frameSeconds<=.1001f;
            if (valid && now > before) validScoreSamples++;
            bool onTrack=valid&&Session.IsInsideCurve((float)now,input.Height,true);
            for(int i=0;i<FinalScore.Planned;i++)
            {
                var c=game.standardTrack.cycles[i%game.standardTrack.cycles.Length];double start=game.standardTrack.CycleStart(i),top=start+c.liftSeconds,lower=top+c.holdSeconds,end=lower+c.lowerSeconds;
                double Part(double a,double b)=>Math.Max(0,Math.Min(now,b)-Math.Max(before,a));
                double dt=Part(start,end);if(dt<=0)continue;
                FinalScore.ObserveCoverage(i,dt,valid);
                bool low=input.Height<=.2f;
                if(valid)FinalScore.ObserveMovement(i,new Vector2(input.Height,input.Height),low,onTrack&&now>=top&&now<=lower,now>=lower);
                float existingTrackQuality=onTrack?100:0;
                FinalScore.ObservePart(i,FinalTrainingScore.Part.Lift,Part(start,top),valid,existingTrackQuality);
                FinalScore.ObservePart(i,FinalTrainingScore.Part.Lower,Part(lower,end),valid,existingTrackQuality);
                FinalScore.ObservePart(i,FinalTrainingScore.Part.Target,Part(top,lower),valid,existingTrackQuality);
                double w=FinalScore.Attempts[i].plan.endpoint;
                finalReturns[i].Add(Part(end-w,end),valid&&low&&onTrack?100:0,valid);
                if(now>=end)FinalScore.SetEndpoint(i,false,finalReturns[i],w);
            }
        }
        void FinishFinalScore()
        {
            // Track duration is stored as float. Flush all measured return windows
            // even when its rounded end differs from the sum of phase seconds.
            for(int i=0;i<FinalScore.Planned;i++)
                FinalScore.SetEndpoint(i,false,finalReturns[i],FinalScore.Attempts[i].plan.endpoint);
            FinalScore.Finish();
        }
    }
}
