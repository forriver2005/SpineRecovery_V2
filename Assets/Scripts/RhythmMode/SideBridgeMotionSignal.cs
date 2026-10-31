using UnityEngine;

/// <summary>Signed, filtered height velocity distinguishes a real descent from
/// alternating tracker noise. The same filter calibrates the recorded dose.</summary>
public sealed class SideBridgeMotionSignal
{
    public float Progress { get; private set; }
    public float LiftSpeed { get; private set; }
    float filtered, previousLift;
    bool ready;

    public void Reset(){ready=false;Progress=LiftSpeed=0;}
    public void Sample(float rawProgress,float liftSpan,float seconds)
    {
        float dt=Mathf.Clamp(seconds,.001f,.1f);
        float alpha=1-Mathf.Exp(-dt/.08f);
        bool discontinuity=ready&&Mathf.Abs(rawProgress-filtered)>.4f;
        filtered=!ready||rawProgress<.3f||(filtered<.3f&&rawProgress>.65f)
            ?rawProgress:Mathf.Lerp(filtered,rawProgress,alpha);
        float lift=filtered*liftSpan;
        float velocity=ready&&!discontinuity?(lift-previousLift)/dt:0;
        // A rejected one-packet jump must not leave a fictitious velocity tail
        // after the real high pose returns. Height/quality and the hold rule
        // still exclude the bad sample and enforce the 200ms interruption cap.
        if(discontinuity)LiftSpeed=0;
        // Smooth signed speed before taking its magnitude. Averaging absolute
        // frame-to-frame speed treats harmless +/-3 degree jitter as motion.
        LiftSpeed=Mathf.Lerp(LiftSpeed,velocity,1-Mathf.Exp(-dt*8));
        previousLift=lift;ready=true;Progress=Mathf.Clamp01(filtered);
    }
}
