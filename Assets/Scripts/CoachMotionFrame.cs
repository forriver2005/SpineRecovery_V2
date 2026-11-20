using System;
using UnityEngine;

/// <summary>
/// 一帧 Humanoid 姿势，直接对应 UnityEngine.HumanPose。
/// muscle space 与具体骨架无关，因此同一份数据可驱动男女不同 avatar。
/// </summary>
[Serializable]
public class CoachMotionFrame
{
    /// <summary>
    /// HumanPose.muscles 的固定长度。
    ///
    /// Unity 会在反序列化 JSON 动作包时先构造这个可序列化类型。直接在
    /// 字段初始化器或序列化阶段读取 HumanTrait.MuscleCount 会触发
    /// "get_MuscleCount is not allowed during serialization"，导致新场景的
    /// 录制教练初始化中断。Unity Humanoid 的 muscle 数量是稳定的 95，运行时
    /// 仍由 ApplyTo/FromHumanPose 对数组长度进行校验。
    /// </summary>
    public const int MuscleCount = 95;

    /// <summary>相对该段起点的时间（秒）</summary>
    public float time;

    /// <summary>HumanPose.bodyPosition</summary>
    public Vector3 bodyPosition;

    /// <summary>HumanPose.bodyRotation</summary>
    public Quaternion bodyRotation;

    /// <summary>HumanPose.muscles</summary>
    public float[] muscles = new float[MuscleCount];

    public static CoachMotionFrame FromHumanPose(float time, HumanPose pose)
    {
        var frame = new CoachMotionFrame
        {
            time = time,
            bodyPosition = pose.bodyPosition,
            bodyRotation = pose.bodyRotation,
            muscles = new float[MuscleCount]
        };

        int count = Mathf.Min(MuscleCount, pose.muscles?.Length ?? 0);
        for (int i = 0; i < count; i++)
        {
            frame.muscles[i] = pose.muscles[i];
        }

        return frame;
    }

    public void ApplyTo(ref HumanPose pose)
    {
        pose.bodyPosition = bodyPosition;
        pose.bodyRotation = bodyRotation;

        if (pose.muscles == null || pose.muscles.Length != MuscleCount)
        {
            pose.muscles = new float[MuscleCount];
        }

        Array.Copy(muscles, pose.muscles, MuscleCount);
    }
}
