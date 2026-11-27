using System;
using UnityEngine;

namespace TaikoNight
{
    // Reads the published per-action outputs only; the source scorers remain unchanged.
    public sealed partial class TaikoNightActionRuntime
    {
        public FinalTrainingScore FinalScore { get; }=new FinalTrainingScore();
        FinalScoreWindow[] finalReturns;
        double[] finalClock;
        void BeginFinalScore()
        {
            var plans=new FinalTrainingScore.Plan[repetitionCount];finalReturns=new FinalScoreWindow[repetitionCount];finalClock=new double[repetitionCount];
            for(int i=0;i<plans.Length;i++)
            {
                double w=Math.Min(.25,phaseSeconds.z*.25);
                plans[i]=new FinalTrainingScore.Plan(phaseSeconds.x+phaseSeconds.y+phaseSeconds.z,phaseSeconds.y,phaseSeconds.x,phaseSeconds.z,w);
                finalReturns[i]=new FinalScoreWindow(w);
            }
            FinalScore.Reset(FinalTrainingScore.Mode.Repetition,plans);
        }
        void ObserveFinalScore(float elapsedSeconds)
        {
            int i=completedRepetitions;if(i<0||i>=FinalScore.Planned||FinalScore.Finished)return;
            double end=Math.Min(clock,FinalScore.Attempts[i].plan.duration),before=finalClock[i];if(end<=before)return;
            finalClock[i]=end;double dt=end-before;
            bool tracked=HasFreshTracker()&&scorer.TrackingAvailable&&dt<=.1001&&elapsedSeconds<=.1001f;
            float progress=0,quality=0;bool validPosture=true,atRest=false,atTop=false;
            if(scorer is CrunchHoldScorer c)
            {
                progress=c.CurrentProgress;
                quality=.35f*c.CurrentCurlQuality+.25f*c.CurrentLiftQuality+.15f*c.CurrentSymmetryQuality+.10f*c.CurrentNeckQuality+.15f*c.CurrentLegQuality;
                atRest=c.IsRelaxedPreparationPose(recorder.UserAnimator,Vector3.up);
                atTop=c.Phase==HipThrustPhase.Holding&&c.CurrentLiveComparisonScore>0;
            }
            else if(scorer is LyingCatHoldScorer l)
            {
                progress=l.CurrentProgress;
                quality=.45f*l.CurrentCurlQuality+.15f*l.CurrentChestBendQuality+.15f*l.CurrentSymmetryQuality+.10f*l.CurrentNeckQuality+.15f*l.CurrentLegQuality;
                validPosture=l.SupportAvailable&&!l.UnsafeInput;
                atRest=l.Phase==HipThrustPhase.WaitingForRaise&&l.PreparationRecognized;
                atTop=l.Phase==HipThrustPhase.Holding&&l.CurrentLiveComparisonScore>0;
            }
            if(!validPosture)quality=0;
            double top=phaseSeconds.x,lower=top+phaseSeconds.y,finish=lower+phaseSeconds.z;
            double Part(double a,double b)=>Math.Max(0,Math.Min(end,b)-Math.Max(before,a));
            FinalScore.ObserveCoverage(i,dt,tracked);
            if(tracked&&validPosture)FinalScore.ObserveMovement(i,new Vector2(progress,progress),atRest,atTop,end>=lower);
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Lift,Part(0,top),tracked,scorer.CurrentLiveComparisonScore);
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Lower,Part(lower,finish),tracked,scorer.CurrentLiveComparisonScore);
            FinalScore.ObservePart(i,FinalTrainingScore.Part.Target,Part(top,lower),tracked,atTop?Mathf.Min(quality,100*Mathf.InverseLerp(.4f,.9f,progress)):0);
            double w=FinalScore.Attempts[i].plan.endpoint;
            finalReturns[i].Add(Part(finish-w,finish),tracked&&validPosture&&atRest?100:0,tracked);
            if(end>=finish-1e-5)FinalScore.SetEndpoint(i,false,finalReturns[i],w);
        }
        void FinishFinalRepetition()
        {
            int i=completedRepetitions;
            if(i>=0&&i<FinalScore.Planned)FinalScore.SetEndpoint(i,false,finalReturns[i],FinalScore.Attempts[i].plan.endpoint);
        }
        void ShowFinalResult()
        {
            selection.ResetResult();
            ResultPanel.Show(actionKind==TaikoNightActionKind.Crunch?"卷腹":"卧位猫式",FinalScore);
        }
    }
}
