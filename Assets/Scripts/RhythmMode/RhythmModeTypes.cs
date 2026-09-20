using System;
using UnityEngine;

public enum RhythmBodyTarget
{
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
    Waist
}

public enum RhythmJudgement
{
    None,
    Perfect,
    Great,
    Good,
    Miss
}

[Serializable]
public sealed class RhythmNoteVariantPool
{
    public RhythmBodyTarget target;
    public GameObject[] prefabs = new GameObject[0];
    public Material[] materials = new Material[0];
}

public readonly struct RhythmNoteResult
{
    public RhythmNoteResult(
        RhythmBodyTarget target,
        RhythmJudgement judgement,
        float poseScore,
        float timingError,
        Vector3 localHitPosition)
    {
        Target = target;
        Judgement = judgement;
        PoseScore = poseScore;
        TimingError = timingError;
        LocalHitPosition = localHitPosition;
    }

    public RhythmBodyTarget Target { get; }
    public RhythmJudgement Judgement { get; }
    public float PoseScore { get; }
    public float TimingError { get; }
    public Vector3 LocalHitPosition { get; }
}
