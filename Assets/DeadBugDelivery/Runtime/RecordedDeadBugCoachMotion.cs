using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadBugDelivery
{
/// <summary>Recorded right-arm/left-leg repetition followed by its anatomical mirror.</summary>
[DefaultExecutionOrder(29000)]
public sealed class RecordedDeadBugCoachMotion : MonoBehaviour, ICoachMotionBackend
{
    public const string ResourcePath = "DeadBugDelivery/dead_bug_recorded";
    public const float RestStart = 7f, RaiseEnd = 10f, LowerStart = 10f, LowerEnd = 12.5f;
    private KeyframeCoachMotionBackend backend;
    private CoachMotionPackage fullSource;
    private Animator avatar;
    private Transform presentationParent;
    private Vector3 hipAnchor;

    public bool IsReady => backend != null && backend.IsReady;
    public int SegmentCount => 6;
    public bool RequiresRuntimeArmComposition => false;

    public void Configure(Animator animator, Transform view)
    {
        avatar = animator;
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null || avatar == null || !avatar.isHuman)
            throw new InvalidOperationException("Missing recorded dead bug JSON or Humanoid coach avatar.");
        CoachMotionPackage source = JsonUtility.FromJson<CoachMotionPackage>(asset.text);
        if (source == null)
            throw new InvalidOperationException("Recorded dead bug JSON could not be parsed.");
        if (!source.IsValid(out string validationError))
            throw new InvalidOperationException("Invalid recorded dead bug package: " + validationError);
        CoachMotionFrame[] frames = source.segments[0].frames;
        if (frames == null || frames.Length == 0 || frames[frames.Length - 1].time < LowerEnd)
            throw new InvalidOperationException("Dead-bug recording is shorter than the selected demonstration cycle.");

        fullSource = source;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        presentationParent = avatar.transform.parent;
        hipAnchor = presentationParent != null
            ? presentationParent.InverseTransformPoint(hips.position) : hips.position;

        using (var sampler = new KeyframeCoachMotionBackend(avatar, source, true))
        {
            sampler.PlaySegment(0); sampler.Tick(RestStart);
            FaceAcrossScreen(view, hips);
        }

        CoachMotionFrame[] mirror = new CoachMotionFrame[frames.Length];
        for (int i = 0; i < frames.Length; i++) mirror[i] = MirrorFrame(frames[i]);
        var cycle = new CoachMotionPackage
        {
            actionId = "dead_bug_recorded", displayName = "死虫双侧录制示范", fps = source.fps,
            sourceClipName = source.sourceClipName, setCount = 1,
            segments = new[]
            {
                Slice(frames, RestStart, RestStart, "双臂竖直、髋膝90°准备", 1f),
                Slice(frames, RestStart, RaiseEnd, "右手左腿伸展并保持", 7f),
                Slice(frames, LowerStart, LowerEnd, "右手左腿回落", 0.5f),
                Slice(mirror, RestStart, RaiseEnd, "左手右腿伸展并保持", 7f),
                Slice(mirror, LowerStart, LowerEnd, "左手右腿回落", 0.5f),
                Slice(mirror, LowerEnd, LowerEnd, "四肢回到准备位", 1f)
            }
        };
        backend = new KeyframeCoachMotionBackend(avatar, cycle, true);
        backend.ReturnIdle();
        Recenter();
    }

    public void CreateUserReferences(Animator user, out DeadBugPose rest, out DeadBugPose right, out DeadBugPose left)
    {
        Transform skeleton = CloneSkeleton(user.transform, user.transform.parent);
        skeleton.gameObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Animator reference = skeleton.gameObject.AddComponent<Animator>();
            reference.avatar = user.avatar;
            reference.Rebind(); reference.enabled = false;
            using (var sampler = new KeyframeCoachMotionBackend(reference, fullSource, true))
            {
                rest = MeasureReference(reference, sampler, 6.4f, 6.8f);
                right = MeasureReference(reference, sampler, 9.8f, 10.2f);
            }
            CoachMotionPackage mirror = JsonUtility.FromJson<CoachMotionPackage>(JsonUtility.ToJson(fullSource));
            foreach (var segment in mirror.segments)
                for (int i = 0; i < segment.frames.Length; i++) segment.frames[i] = MirrorFrame(segment.frames[i]);
            using (var sampler = new KeyframeCoachMotionBackend(reference, mirror, true))
                left = MeasureReference(reference, sampler, 9.8f, 10.2f);
        }
        finally
        {
            if (Application.isPlaying) Destroy(skeleton.gameObject);
            else DestroyImmediate(skeleton.gameObject);
        }
    }

    // Dead bug changes the diagonal pair without turning around. A world-X
    // reflection of a horizontal recording also reverses its yaw. Reflect in
    // the sagittal plane defined by the recorded torso heading instead.
    internal static CoachMotionFrame MirrorFrame(CoachMotionFrame source)
    {
        CoachMotionFrame result = DeadBugMotionMirror.MirrorFrame(source);
        Vector3 heading = Vector3.ProjectOnPlane(source.bodyRotation * Vector3.up, Vector3.up);
        if (heading.sqrMagnitude > 0.0001f)
        {
            Quaternion yaw = Quaternion.LookRotation(heading.normalized, Vector3.up);
            Quaternion local = Quaternion.Inverse(yaw) * source.bodyRotation;
            result.bodyRotation = yaw * new Quaternion(local.x, -local.y, -local.z, local.w);
            Vector3 position = Quaternion.Inverse(yaw) * source.bodyPosition;
            result.bodyPosition = yaw * new Vector3(-position.x, position.y, position.z);
        }
        return result;
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

    private static DeadBugPose MeasureReference(Animator target, KeyframeCoachMotionBackend sampler, float first, float last)
    {
        DeadBugPose average = default;
        int count = 0;
        for (float time = first; time <= last + 0.001f; time += 0.1f)
        {
            sampler.PlaySegment(0); sampler.Tick(time);
            if (!DeadBugPose.TryCapture(target, out DeadBugPose pose))
                throw new InvalidOperationException("Cannot measure recorded dead bug on this Humanoid rig.");
            Add(ref average.LeftArm, pose.LeftArm); Add(ref average.RightArm, pose.RightArm);
            Add(ref average.LeftLeg, pose.LeftLeg); Add(ref average.RightLeg, pose.RightLeg);
            average.TorsoTilt += pose.TorsoTilt; count++;
        }
        Divide(ref average.LeftArm, count); Divide(ref average.RightArm, count);
        Divide(ref average.LeftLeg, count); Divide(ref average.RightLeg, count);
        average.TorsoTilt /= count;
        return average;
    }
    private static void Add(ref DeadBugLimb a, DeadBugLimb b)
    {
        a.ProximalElevation += b.ProximalElevation; a.DistalElevation += b.DistalElevation;
        a.Height += b.Height; a.Reach += b.Reach; a.Bend += b.Bend;
        a.HorizontalError += b.HorizontalError; a.Elevation += b.Elevation;
    }
    private static void Divide(ref DeadBugLimb a, float n)
    {
        a.ProximalElevation /= n; a.DistalElevation /= n;
        a.Height /= n; a.Reach /= n; a.Bend /= n; a.HorizontalError /= n; a.Elevation /= n;
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
            sourceStateName = "recorded_dead bug",
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
    public string GetSegmentSourceName(int index) => "recorded_dead bug";

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
}
