using UnityEngine;

/// <summary>Opt-in scoring for the two new scenes. The legacy menu/calibration and transport remain in charge.</summary>
public sealed class HipThrustRuleScoring : MonoBehaviour
{
    public HipThrustTaikoController transport;
    public DeadBugGamingPoseScorer calibration;
    public readonly HipThrustHoldRule Rule = new HipThrustHoldRule();
    public float Progress { get; private set; }
    public float Quality { get; private set; }
    public bool TargetsReady { get; private set; }
    public bool InputFresh { get; private set; }
    public string Status => !TargetsReady ? "动作参考不可用" : !InputFresh ? "等待动作数据" :
        Rule.Phase == HipThrustHoldRule.Stage.Holding ? "保持计分" : "抬起进入保持位后计分";
    readonly HipThrustHoldGeometry geometry = new HipThrustHoldGeometry();
    AvatarMotionSource motion;
    int lastCounter;
    float lastFrameAt = float.NegativeInfinity;

    public void Begin(string difficulty)
    {
        Rule.Reset(difficulty); Progress=Quality=0; InputFresh=false;
        motion=FindObjectOfType<AvatarMotionSource>();
        lastCounter=motion!=null?motion.LastPacketframeCounterInFrame:0;
        lastFrameAt=float.NegativeInfinity;
        TargetsReady=CaptureTargets();
        if(!TargetsReady) Debug.LogError("臀桥新评分无法采样录制动作的 A/B 参考姿态。",this);
    }

    bool CaptureTargets()
    {
        var animator=transport.CoachAnimator; var timeline=transport.PhaseTimeline;
        if(animator==null || !animator.isHuman || timeline==null) return false;
        var state=animator.GetCurrentAnimatorStateInfo(0); float speed=animator.speed;
        try
        {
            Sample(timeline.cycles[0].liftStartFrame);
            if(!geometry.TryMeasurePose(animator,out geometry.restTarget)) return false;
            Sample(HipThrustHold30Timeline.PeakFrame(timeline,0));
            if(!geometry.TryMeasurePose(animator,out geometry.holdTarget)) return false;
            if(geometry.restTarget.hasBridgeLift && geometry.holdTarget.hasBridgeLift &&
               geometry.restTarget.bridgeLiftRatio>geometry.holdTarget.bridgeLiftRatio)
            { var swap=geometry.restTarget; geometry.restTarget=geometry.holdTarget; geometry.holdTarget=swap; }
            return true;
        }
        finally { animator.Play(state.fullPathHash,0,state.normalizedTime); animator.Update(0); animator.speed=speed; }
        void Sample(int frame)
        { animator.Play("hipthrust_routine",0,frame/timeline.sourceClip.frameRate/timeline.sourceClip.length); animator.Update(0); }
    }

    public void Tick(float deltaTime)
    {
        if(motion==null) motion=FindObjectOfType<AvatarMotionSource>();
        if(motion!=null && motion.LastPacketframeCounterInFrame!=lastCounter)
        { lastCounter=motion.LastPacketframeCounterInFrame; lastFrameAt=Time.unscaledTime; }
        InputFresh=motion!=null && motion.GetAvailable()!=0 && Time.unscaledTime-lastFrameAt<=.5f;
        bool valid=TargetsReady && InputFresh && calibration!=null && calibration.IsCalibrated;
        if(valid)
        {
            valid=geometry.TryMeasurePose(calibration.UserAnimator,out var pose);
            if(valid) { Progress=geometry.CalculateProgress(pose); Quality=geometry.CalculatePoseQuality(pose,geometry.holdTarget,true); }
        }
        Rule.Step(Progress,Quality,deltaTime,valid);
    }

    public string Result(int demonstrated) => $"训练完成  示范 {demonstrated} 次臀桥\n最高 {Rule.SessionBest:0} / 100　上轮 {Rule.LastRoundBest:0}\n识别往返 {Rule.CompletedRounds} 次";
}
