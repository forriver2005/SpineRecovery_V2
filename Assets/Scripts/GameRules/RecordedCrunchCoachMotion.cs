using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Loops one clean crunch repetition selected from the supplied recording.</summary>
[DefaultExecutionOrder(29000)]
public sealed class RecordedCrunchCoachMotion : MonoBehaviour, ICoachMotionBackend
{
    public const string ResourcePath = "ActionLibrary/Motions/crunch_recorded";
    private const float RestStart = 43.0f;
    private const float RaiseAndHoldEnd = 50.0f;
    private const float LowerStart = 50.0f;
    private const float LowerEnd = 52.4f;

    private const float HoldStart = 47.2f;
    public static Vector3 PhaseSeconds => new Vector3(HoldStart - RestStart, LowerStart - HoldStart, LowerEnd - LowerStart);

    private KeyframeCoachMotionBackend backend;
    private CoachMotionPackage fullSource;
    private Animator avatar;
    private Transform presentationParent;
    private Vector3 hipAnchor;

    public bool IsReady => backend != null && backend.IsReady &&
        (Mathf.Abs(HoldPose.Lift - RestPose.Lift) > 0.025f ||
         Mathf.Abs(HoldPose.CurlAngle - RestPose.CurlAngle) > 5f);
    public int SegmentCount => 4;
    public bool RequiresRuntimeArmComposition => false;
    public CrunchPose RestPose { get; private set; }
    public CrunchPose HoldPose { get; private set; }

    public void Configure(Animator animator, Transform view)
    {
        avatar = animator;
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null || avatar == null || !avatar.isHuman)
            throw new InvalidOperationException("Missing recorded crunch JSON or Humanoid coach avatar.");
        CoachMotionPackage source = JsonUtility.FromJson<CoachMotionPackage>(asset.text);
        if (source == null)
            throw new InvalidOperationException("Recorded crunch JSON could not be parsed.");
        if (!source.IsValid(out string validationError))
            throw new InvalidOperationException("Invalid recorded crunch package: " + validationError);
        CoachMotionFrame[] frames = source.segments[0].frames;
        if (frames == null || frames.Length == 0 || frames[frames.Length - 1].time < LowerEnd)
            throw new InvalidOperationException("Crunch recording is shorter than the selected demonstration cycle.");

        fullSource = source;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        presentationParent = avatar.transform.parent;
        hipAnchor = presentationParent != null
            ? presentationParent.InverseTransformPoint(hips.position) : hips.position;

        using (var sampler = new KeyframeCoachMotionBackend(avatar, source, true))
        {
            RestPose = MeasureReference(avatar, sampler, 43.0f, 44.0f);
            HoldPose = MeasureReference(avatar, sampler, HoldStart, 49.5f);
            FaceAcrossScreen(view, hips);
        }

        var cycle = new CoachMotionPackage
        {
            actionId = "crunch_recorded",
            displayName = "卷腹录制示范",
            fps = source.fps,
            sourceClipName = source.sourceClipName,
            setCount = 1,
            segments = new[]
            {
                Slice(frames, RestStart, RestStart, "仰卧准备", 1f),
                Slice(frames, RestStart, RaiseAndHoldEnd, "卷起并保持", 0.5f),
                Slice(frames, LowerStart, LowerEnd, "控制回落", 0.8f),
                Slice(frames, LowerEnd, LowerEnd, "仰卧休息", 1f)
            }
        };
        backend = new KeyframeCoachMotionBackend(avatar, cycle, true);
        backend.ReturnIdle();
        Recenter();
    }

    public void CreateUserReferences(Animator user, out CrunchPose rest, out CrunchPose hold)
    {
        Transform skeleton = CloneSkeleton(user.transform, user.transform.parent);
        skeleton.gameObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Animator reference = skeleton.gameObject.AddComponent<Animator>();
            reference.avatar = user.avatar;
            reference.Rebind();
            reference.enabled = false;
            using (var sampler = new KeyframeCoachMotionBackend(reference, fullSource, true))
            {
                rest = MeasureReference(reference, sampler, 43.0f, 44.0f);
                hold = MeasureReference(reference, sampler, HoldStart, 49.5f);
            }
        }
        finally
        {
            if (Application.isPlaying) Destroy(skeleton.gameObject);
            else DestroyImmediate(skeleton.gameObject);
        }
    }

    private void FaceAcrossScreen(Transform view, Transform hips)
    {
        if (view == null) return;
        Transform leftShoulder = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (leftShoulder == null || rightShoulder == null) return;
        Vector3 shoulders = (leftShoulder.position + rightShoulder.position) * 0.5f;
        Vector3 heading = Vector3.ProjectOnPlane(shoulders - hips.position, Vector3.up);
        Vector3 screenRight = Vector3.ProjectOnPlane(view.right, Vector3.up);
        if (heading.sqrMagnitude > 0.0001f && screenRight.sqrMagnitude > 0.0001f)
            avatar.transform.rotation = Quaternion.AngleAxis(
                Vector3.SignedAngle(heading, screenRight, Vector3.up), Vector3.up) * avatar.transform.rotation;
    }

    private static Transform CloneSkeleton(Transform source, Transform parent)
    {
        Transform clone = new GameObject(source.name).transform;
        clone.SetParent(parent, false);
        clone.localPosition = source.localPosition;
        clone.localRotation = source.localRotation;
        clone.localScale = source.localScale;
        foreach (Transform child in source) CloneSkeleton(child, clone);
        return clone;
    }

    private static CrunchPose MeasureReference(
        Animator target, KeyframeCoachMotionBackend sampler, float first, float last)
    {
        CrunchPose average = default;
        int count = 0;
        for (float time = first; time <= last + 0.001f; time += 0.1f)
        {
            sampler.PlaySegment(0);
            sampler.Tick(time);
            if (!CrunchPose.TryCapture(target, out CrunchPose pose))
                throw new InvalidOperationException("Cannot measure recorded crunch on this Humanoid rig.");
            average.Lift += pose.Lift;
            average.CurlAngle += pose.CurlAngle;
            average.KneeAngle += pose.KneeAngle;
            average.NeckAngle += pose.NeckAngle;
            average.ShoulderTilt += pose.ShoulderTilt;
            average.ArmAngle += pose.ArmAngle;
            count++;
        }
        average.Lift /= count;
        average.CurlAngle /= count;
        average.KneeAngle /= count;
        average.NeckAngle /= count;
        average.ShoulderTilt /= count;
        average.ArmAngle /= count;
        return average;
    }

    private static CoachMotionSegment Slice(
        CoachMotionFrame[] source, float start, float end, string label, float hold)
    {
        var selected = new List<CoachMotionFrame>();
        foreach (CoachMotionFrame frame in source)
        {
            if (frame.time < start - 0.017f || frame.time > end + 0.017f) continue;
            selected.Add(new CoachMotionFrame
            {
                time = Mathf.Max(0f, frame.time - start),
                bodyPosition = frame.bodyPosition,
                bodyRotation = frame.bodyRotation,
                muscles = frame.muscles
            });
            if (Mathf.Approximately(start, end)) break;
        }
        if (selected.Count == 0) throw new InvalidOperationException("Empty recorded segment: " + label);
        selected[0].time = 0f;
        return new CoachMotionSegment
        {
            label = label,
            sourceStateName = "recorded_crunch",
            repeatCount = 1,
            scoringDuration = hold,
            frames = selected.ToArray(),
            poseGuidanceRule = new SegmentPoseGuidanceRule { mode = PoseGuidanceProfileMode.Disabled }
        };
    }

    public void PlaySegment(int index) => backend?.PlaySegment(index);
    public void Tick(float deltaTime) => backend?.Tick(deltaTime);
    public bool HasReachedSegmentEnd() => backend != null && backend.HasReachedSegmentEnd();
    public void SnapToSegmentEnd() => backend?.SnapToSegmentEnd();
    public void Freeze() => backend?.Freeze();
    public void Resume() => backend?.Resume();
    public void ReturnIdle() => backend?.ReturnIdle();
    public string GetSegmentSourceName(int index) => "recorded_crunch";

    private void LateUpdate() => Recenter();

    private void Recenter()
    {
        if (avatar == null || backend == null) return;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null)
            hips.position = presentationParent != null
                ? presentationParent.TransformPoint(hipAnchor) : hipAnchor;
    }

    private void OnDestroy()
    {
        backend?.Dispose();
        backend = null;
    }
}
