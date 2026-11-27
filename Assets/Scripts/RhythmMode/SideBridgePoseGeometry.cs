using UnityEngine;

/// <summary>Side-bridge support geometry. Translation, body scale and horizontal heading invariant.</summary>
public sealed class SideBridgePoseGeometry
{
    public sealed class Pose
    {
        public float lift, trunkAngle, kneeAngle, elbowAngle, shoulderStack, pelvisStack, torsoVertical;
        public float shoulderOverElbow;
        public float armAngle, stackAngle;
        public Vector3 thigh, shin, shoulderAxis, pelvisAxis;
        public bool rightSupport;
        public bool IsLeftSupport => !rightSupport && shoulderStack > .35f && pelvisStack > .25f && torsoVertical < .75f;
        public bool IsRightSupport => rightSupport && shoulderStack > .35f && pelvisStack > .25f && torsoVertical < .75f;
        public bool IsSupport(bool right) => rightSupport == right && shoulderStack > .35f && pelvisStack > .25f && torsoVertical < .75f;
    }
    public bool RightSupport { get; private set; }
    public Pose Rest { get; private set; }
    public Pose Top { get; private set; }
    public float RestLift { get; private set; }
    public float TopLift { get; private set; }

    public bool SetTargets(Pose rest, Pose top, float restLift, float topLift) => SetTargets(rest,top,restLift,topLift,false);
    public bool SetTargets(Pose rest, Pose top, float restLift, float topLift, bool rightSupport)
    {
        RightSupport=rightSupport; Rest=rest; Top=top; RestLift=restLift; TopLift=topLift;
        // Recorded demonstrations can have a slightly shorter final cycle after
        // retargeting to the patient's proportions. Keep the real plateau rather
        // than rejecting that cycle outright; the scorer still normalizes against
        // this measured span and applies its usual quality/support gates.
        return rest!=null && top!=null && rest.IsSupport(rightSupport) && top.IsSupport(rightSupport) &&
            Finite(restLift) && Finite(topLift) && topLift-restLift>.008f;
    }

    public float RawProgress(Pose pose) => (pose.lift-RestLift)/(TopLift-RestLift);
    public float Progress(Pose pose) => Mathf.Clamp01(RawProgress(pose));

    public static bool HasStableSupport(Pose pose,Pose target) => HasStableSupport(pose,target,false);
    public static bool HasStableSupport(Pose pose,Pose target,bool rightSupport) => pose!=null&&target!=null&&pose.IsSupport(rightSupport)&&
        pose.shoulderAxis.sqrMagnitude>.5f&&pose.pelvisAxis.sqrMagnitude>.5f&&
        pose.stackAngle<Mathf.Max(65,target.stackAngle+30) &&
        pose.armAngle<Mathf.Max(65,target.armAngle+40) &&
        Mathf.Abs(pose.armAngle-target.armAngle)<=50 &&
        Mathf.Abs(pose.shoulderOverElbow-target.shoulderOverElbow)<=.18f &&
        Mathf.Abs(pose.trunkAngle-target.trunkAngle)<=50 &&
        Mathf.Abs(pose.kneeAngle-target.kneeAngle)<=65 &&
        Vector3.Angle(pose.pelvisAxis,target.pelvisAxis)<=45 &&
        Mathf.Abs(Vector3.Angle(pose.shoulderAxis,pose.pelvisAxis)-Vector3.Angle(target.shoulderAxis,target.pelvisAxis))<=40;

    public float Quality(Pose pose, Pose target, float tolerance=1.25f)
    {
        // Delivery SideBridgeHoldScorer: 40% trunk, 30% support, 20% mean
        // bent-knee angle, 10% shoulder alignment. Foot spacing is not scored.
        float trunk=Score(Mathf.Abs(pose.trunkAngle-target.trunkAngle),3*tolerance,20*tolerance);
        float leg=Score(Mathf.Abs(pose.kneeAngle-target.kneeAngle),6*tolerance,32*tolerance);
        float support=.6f*Score(Mathf.Abs(pose.armAngle-target.armAngle),6*tolerance,40*tolerance)+
            .4f*Score(Mathf.Abs(pose.shoulderOverElbow-target.shoulderOverElbow),.025f*tolerance,.15f*tolerance);
        float stack=Score(Mathf.Abs(pose.stackAngle-target.stackAngle),7*tolerance,35*tolerance);
        return .4f*trunk+.3f*support+.2f*leg+.1f*stack;
    }

    public static bool TryMeasure(Animator avatar, out Pose pose) => TryMeasure(avatar,false,out pose);
    public static bool TryMeasure(Animator avatar, bool rightSupport, out Pose pose)
    {
        pose=null;
        if(avatar==null || !avatar.isHuman) return false;
        var hip=avatar.GetBoneTransform(HumanBodyBones.Hips);
        var chest=avatar.GetBoneTransform(HumanBodyBones.Chest)??avatar.GetBoneTransform(HumanBodyBones.Spine);
        var ls=avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);var rs=avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var leftElbow=avatar.GetBoneTransform(HumanBodyBones.LeftLowerArm);var leftWrist=avatar.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightElbow=avatar.GetBoneTransform(HumanBodyBones.RightLowerArm);var rightWrist=avatar.GetBoneTransform(HumanBodyBones.RightHand);
        var lt=avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var rt=avatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var knee=avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var rightKnee=avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        var lf=avatar.GetBoneTransform(HumanBodyBones.LeftFoot);var rf=avatar.GetBoneTransform(HumanBodyBones.RightFoot);
        if(hip==null || chest==null || ls==null || rs==null || leftElbow==null || leftWrist==null || rightElbow==null || rightWrist==null || lt==null || rt==null || knee==null || rightKnee==null || lf==null || rf==null) return false;
        Transform supportShoulder=rightSupport?rs:ls, freeShoulder=rightSupport?ls:rs;
        Transform supportElbow=rightSupport?rightElbow:leftElbow, supportWrist=rightSupport?rightWrist:leftWrist;
        Transform supportFoot=rightSupport?rf:lf;
        Transform supportThigh=rightSupport?rt:lt, freeThigh=rightSupport?lt:rt;
        Vector3 shoulder=(ls.position+rs.position)*.5f,feet=(lf.position+rf.position)*.5f;
        float span=Vector3.Distance(shoulder,feet);
        Vector3 support=supportFoot.position-supportElbow.position;
        Vector3 along=Vector3.ProjectOnPlane(feet-shoulder,Vector3.up);
        if(span<.1f || support.sqrMagnitude<.01f || along.sqrMagnitude<.01f) return false;
        along.Normalize();Vector3 across=Vector3.Cross(Vector3.up,along);
        Vector3 Local(Vector3 v) { v.Normalize();return new Vector3(Vector3.Dot(v,along),Vector3.Dot(v,Vector3.up),Vector3.Dot(v,across)); }
        // A line through the supporting elbow and ankle remains well defined when
        // the feet overlap. The old chest/two-feet plane degenerates in this pose.
        Vector3 projected=Vector3.ProjectOnPlane(support,Vector3.up);
        float t=projected.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(hip.position-supportElbow.position,Vector3.up),projected)/projected.sqrMagnitude):.5f;
        Vector3 onSupport=Vector3.Lerp(supportElbow.position,supportFoot.position,t);
        pose=new Pose {
            lift=Vector3.Dot(hip.position-onSupport,Vector3.up)/span,
            trunkAngle=Vector3.Angle(shoulder-hip.position,feet-hip.position),
            kneeAngle=.5f*(Vector3.Angle(hip.position-knee.position,lf.position-knee.position)+Vector3.Angle(hip.position-rightKnee.position,rf.position-rightKnee.position)),
            armAngle=Vector3.Angle(supportShoulder.position-supportElbow.position,Vector3.up),
            stackAngle=Vector3.Angle(freeShoulder.position-supportShoulder.position,Vector3.up),
            elbowAngle=Vector3.Angle(supportShoulder.position-supportElbow.position,supportWrist.position-supportElbow.position),
            shoulderStack=Vector3.Dot((freeShoulder.position-supportShoulder.position).normalized,Vector3.up),
            pelvisStack=Vector3.Dot((freeThigh.position-supportThigh.position).normalized,Vector3.up),
            torsoVertical=Mathf.Abs(Vector3.Dot((chest.position-hip.position).normalized,Vector3.up)),
            shoulderOverElbow=Vector3.ProjectOnPlane(supportShoulder.position-supportElbow.position,Vector3.up).magnitude/span,
            thigh=Local(knee.position-lt.position),shin=Local(lf.position-knee.position),
            shoulderAxis=Local(freeShoulder.position-supportShoulder.position),
            pelvisAxis=Local(freeThigh.position-supportThigh.position),
            rightSupport=rightSupport
        };
        return Finite(pose.lift)&&Finite(pose.trunkAngle)&&Finite(pose.kneeAngle)&&Finite(pose.elbowAngle)&&Finite(pose.shoulderStack)&&Finite(pose.pelvisStack)&&Finite(pose.torsoVertical)&&Finite(pose.shoulderOverElbow);
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    static float Score(float error,float perfect,float zero)
    { float t=Mathf.InverseLerp(perfect,zero,error); return 100*(1-t*t*(3-2*t)); }
}
