using System;
using UnityEngine;

/// <summary>
/// 一帧 Humanoid 姿势，直接对应 UnityEngine.HumanPose。
/// muscle space 与具体骨架无关，因此同一份数据可驱动男女不同 avatar。
/// </summary>
[Serializable]
public class CoachMotionFrame
{
    /// <summary>HumanPose.muscles 的固定长度（HumanTrait.MuscleCount）</summary>
    public static int MuscleCount => HumanTrait.MuscleCount;

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

    /// <summary>
    /// Smoothly corrects a copied motion segment so it starts and ends at the
    /// supplied poses. This lets separately trimmed up/down recordings share
    /// the exact held key pose without inserting a visible boundary jump.
    /// </summary>
    public static void AlignSegmentEndpoints(
        CoachMotionFrame[] frames,
        CoachMotionFrame startPose,
        CoachMotionFrame endPose)
    {
        if (frames == null || frames.Length == 0 ||
            startPose?.muscles == null || endPose?.muscles == null)
        {
            return;
        }

        CoachMotionFrame originalStart = frames[0];
        CoachMotionFrame originalEnd = frames[frames.Length - 1];
        Vector3 startPositionCorrection =
            startPose.bodyPosition - originalStart.bodyPosition;
        Vector3 endPositionCorrection =
            endPose.bodyPosition - originalEnd.bodyPosition;
        Quaternion startRotationCorrection =
            startPose.bodyRotation * Quaternion.Inverse(originalStart.bodyRotation);
        Quaternion endRotationCorrection =
            endPose.bodyRotation * Quaternion.Inverse(originalEnd.bodyRotation);
        float firstTime = originalStart.time;
        float duration = Mathf.Max(0f, originalEnd.time - firstTime);

        float[] startMuscleCorrection = new float[MuscleCount];
        float[] endMuscleCorrection = new float[MuscleCount];
        for (int muscle = 0; muscle < MuscleCount; muscle++)
        {
            startMuscleCorrection[muscle] =
                startPose.muscles[muscle] - originalStart.muscles[muscle];
            endMuscleCorrection[muscle] =
                endPose.muscles[muscle] - originalEnd.muscles[muscle];
        }

        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            CoachMotionFrame frame = frames[frameIndex];
            float progress = duration > Mathf.Epsilon
                ? Mathf.Clamp01((frame.time - firstTime) / duration)
                : 0f;
            frame.bodyPosition += Vector3.Lerp(
                startPositionCorrection,
                endPositionCorrection,
                progress);
            frame.bodyRotation = Quaternion.Slerp(
                startRotationCorrection,
                endRotationCorrection,
                progress) * frame.bodyRotation;

            for (int muscle = 0; muscle < MuscleCount; muscle++)
            {
                frame.muscles[muscle] = Mathf.Clamp(
                    frame.muscles[muscle] + Mathf.Lerp(
                        startMuscleCorrection[muscle],
                        endMuscleCorrection[muscle],
                        progress),
                    -1f,
                    1f);
            }
        }

        CopyPose(startPose, frames[0]);
        CopyPose(endPose, frames[frames.Length - 1]);
    }

    private static void CopyPose(CoachMotionFrame source, CoachMotionFrame destination)
    {
        destination.bodyPosition = source.bodyPosition;
        destination.bodyRotation = source.bodyRotation;
        Array.Copy(source.muscles, destination.muscles, MuscleCount);
    }
}
