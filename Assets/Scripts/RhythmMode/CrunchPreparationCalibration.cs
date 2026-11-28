using UnityEngine;

/// <summary>Fresh-frame, stable relaxed-pose admission for the crunch game, independent of hidden practice UI.</summary>
public sealed class CrunchPreparationCalibration
{
    const float StableSeconds=1.5f;
    Animator patient;AvatarMotionSource source;float referenceFront;int lastPacket;
    float stable,lastObserved;Quaternion[] previous;
    static readonly HumanBodyBones[] Bones={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg};
    public bool Ready { get; private set; }
    public bool Running { get; private set; }
    public float Progress=>Mathf.Clamp01(stable/StableSeconds);
    public string Status { get; private set; }="请仰卧屈膝，放松上背，保持稳定";
    public void Configure(Animator patient,Animator coach)
    {
        this.patient=patient;DeadBugGamingPoseScorer.FloorOrientation(coach,out referenceFront,out _);Begin();
    }
    public void Begin(){Ready=false;Running=true;stable=0;previous=null;lastObserved=Time.unscaledTime;lastPacket=-1;Status="请仰卧屈膝，放松上背，保持稳定";}
    public void Tick()
    {
        if(source==null)foreach(var candidate in Object.FindObjectsOfType<AvatarMotionSource>())if(candidate.Model==patient.gameObject){source=candidate;break;}
        if(source==null||!source.isActiveAndEnabled||!source.HasFreshFrame)
        {Ready=false;Running=true;stable=0;previous=null;lastObserved=Time.unscaledTime;Status="等待动作传感器连接";return;}
        if(Ready||!Running)return;
        if(source.LastPacketframeCounterInFrame==lastPacket)return;
        lastPacket=source.LastPacketframeCounterInFrame;float dt=Mathf.Clamp(Time.unscaledTime-lastObserved,0,.1f);lastObserved=Time.unscaledTime;
        bool posture=RelaxedPreparation.IsSupineBentKnee(patient)&&DeadBugGamingPoseScorer.FloorOrientation(patient,out float front,out _)&&front*referenceFront>0;
        if(!posture){stable=0;previous=null;Status="请仰卧屈膝、放松上背；双臂可自然放置";return;}
        bool still=previous!=null;var rotations=new Quaternion[Bones.Length];
        for(int i=0;i<Bones.Length;i++){var b=patient.GetBoneTransform(Bones[i]);rotations[i]=b!=null?b.localRotation:Quaternion.identity;if(previous!=null&&Quaternion.Angle(previous[i],rotations[i])>6f)still=false;}
        previous=rotations;stable=still?stable+dt:0;
        Status=still?$"正在校准 {Progress:P0} · 保持放松":"检测到移动，请保持稳定";
        if(stable>=StableSeconds){Ready=true;Running=false;Status="校准完成，可以开始训练";}
    }
}
