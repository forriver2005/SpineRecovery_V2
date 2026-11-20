using System;
using UnityEngine;

public enum TutorialKind { Limbs, DynamicBridge, HoldBridge }
public enum TutorialDemo { Overview, LeftArm, RightArm, LeftLeg, RightLeg, Coach, Approach, Hit, Miss, Lift, Hold, Lower, Hold30, Interrupted, Summary }

[Serializable]
public sealed class TutorialStep
{
    public string id, title;
    [TextArea(2, 5)] public string narration;
    public TutorialDemo demo;
    public float minimumSeconds = 6;
    public AudioClip voice;
    public float Duration => Mathf.Max(minimumSeconds, voice != null ? voice.length + .8f : 8);
}

[CreateAssetMenu(menuName = "Training/Automatic Tutorial")]
public sealed class TutorialLesson : ScriptableObject
{
    public TutorialKind kind;
    public string title, defaultGame;
    public TutorialStep[] steps;
}
