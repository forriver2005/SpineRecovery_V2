using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirdDogDelivery
{
/// <summary>Applies only the constraint delta from a hidden Humanoid rig, preserving the incoming pose.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30000)]
public sealed class BirdDogAnatomicalConstraint : MonoBehaviour
{
    private sealed class BonePair
    {
        public Transform Live, Shadow;
        public HumanBodyBones Id;
        public Quaternion SafeRotation;
        public Vector3 BonePosition, BoneScale;
        public Quaternion InputRotation;
        public Quaternion InputWorld, BaselineWorld;
    }
    private readonly List<BonePair> bones = new List<BonePair>();
    private readonly List<BonePair> hierarchy = new List<BonePair>();
    private Animator target;
    private Transform shadowRoot;
    private HumanPoseHandler handler;
    private HumanPose pose;
    private float[] minimum, maximum, previous, projectedMuscles;
    private bool hasSafePose;
    public bool RejectedInputThisFrame { get; private set; }
    public bool IsReady => handler != null;
    public BirdDogPose InputPose { get; private set; }
    public bool HasInputPose { get; private set; }
    public int CorrectedMusclesThisFrame { get; private set; }

    public void Configure(Animator animator)
    {
        Dispose();
        target = animator;
        if (target == null || !target.isHuman || target.avatar == null || !target.avatar.isValid)
            throw new InvalidOperationException("Bird-dog constraints need a valid Humanoid avatar.");
        shadowRoot = Clone(target.transform, target.transform.parent);
        shadowRoot.gameObject.hideFlags = HideFlags.HideAndDontSave;
        Animator shadow = shadowRoot.gameObject.AddComponent<Animator>();
        shadow.avatar = target.avatar;
        shadow.Rebind();
        shadow.enabled = false;
        MapHierarchy(target.transform, shadowRoot);
        shadowRoot.name = "BirdDogConstraintRig";
        handler = new HumanPoseHandler(shadow.avatar, shadow.transform);
        pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
        minimum = new float[HumanTrait.MuscleCount];
        maximum = new float[HumanTrait.MuscleCount];
        previous = new float[HumanTrait.MuscleCount];
        projectedMuscles = new float[HumanTrait.MuscleCount];
        BuildRanges();
        for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
        {
            HumanBodyBones id = (HumanBodyBones)index;
            Transform live = target.GetBoneTransform(id), copy = shadow.GetBoneTransform(id);
            if (live != null && copy != null)
                bones.Add(new BonePair { Live = live, Shadow = copy, Id = id, SafeRotation = live.localRotation, BonePosition = live.localPosition, BoneScale = live.localScale });
        }
        bones.Sort((a, b) => Depth(a.Live).CompareTo(Depth(b.Live)));
        hasSafePose = false;

    }

    private void LateUpdate() { if (!MobileCoachControls.IsPaused) Apply(Time.unscaledDeltaTime); }

    internal void Apply(float deltaTime)
    {
        if (handler == null || target == null) return;
        float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);
        RejectedInputThisFrame = false;
        CorrectedMusclesThisFrame = 0;
        HasInputPose = BirdDogPose.TryCapture(target, out var input);
        InputPose = input;
        shadowRoot.localPosition = target.transform.localPosition;
        shadowRoot.localRotation = target.transform.localRotation;
        shadowRoot.localScale = target.transform.localScale;
        // Restore authored segment lengths before projection. Hips translation
        // belongs to the presentation/recording pipeline and is preserved.
        foreach (BonePair bone in bones)
        {
            if (bone.Id == HumanBodyBones.Hips) continue;
            if (!Finite(bone.Live.localPosition) || !Finite(bone.Live.localScale) ||
                (bone.Live.localPosition - bone.BonePosition).sqrMagnitude > 0.000001f ||
                (bone.Live.localScale - bone.BoneScale).sqrMagnitude > 0.000001f)
            {
                bone.Live.localPosition = bone.BonePosition;
                bone.Live.localScale = bone.BoneScale;
                RejectedInputThisFrame = true;
            }
        }
        // VRM has non-Humanoid intermediary nodes. Their current transforms
        // are part of the coordinate system used by HumanPoseHandler.
        foreach (BonePair pair in hierarchy)
        {
            pair.Shadow.localPosition = pair.Live.localPosition;
            pair.Shadow.localScale = pair.Live.localScale;
            if (!TryNormalize(pair.Live.localRotation, out Quaternion rotation))
            { rotation = pair.SafeRotation; pair.Live.localRotation = rotation; RejectedInputThisFrame = true; }
            pair.Shadow.localRotation = rotation;
        }
        foreach (BonePair bone in bones)
        {
            if (!TryNormalize(bone.Live.localRotation, out Quaternion incoming))
            {
                incoming = bone.SafeRotation;
                RejectedInputThisFrame = true;
            }
            // Hips has no muscle indices. Validate and rate-limit it explicitly.
            if (hasSafePose && bone.Id == HumanBodyBones.Hips)
            {
                float angle = Quaternion.Angle(bone.SafeRotation, incoming);
                if (angle > 65f) RejectedInputThisFrame = true;
                incoming = Quaternion.RotateTowards(bone.SafeRotation, incoming, 180f * dt);
            }
            bone.Shadow.localRotation = incoming;
            bone.InputRotation = incoming;
        }
        handler.GetHumanPose(ref pose);
        if (!TryNormalize(pose.bodyRotation, out Quaternion bodyRotation))
        {
            RejectedInputThisFrame = true;
            bodyRotation = Quaternion.identity;
        }
        pose.bodyRotation = bodyRotation;
        bool needsProjection = false;
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            float raw = pose.muscles[i];
            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                raw = hasSafePose ? previous[i] : 0f;
                RejectedInputThisFrame = true;
            }
            float projected = Mathf.Clamp(raw, minimum[i], maximum[i]);
            if (hasSafePose && Mathf.Abs(raw - previous[i]) > 0.65f) RejectedInputThisFrame = true;
            if (Mathf.Abs(projected - raw) > 0.001f) CorrectedMusclesThisFrame++;
            if (Mathf.Abs(projected - raw) > 0.18f) RejectedInputThisFrame = true;
            if (hasSafePose) projected = Mathf.MoveTowards(previous[i], projected, 4f * dt);
            needsProjection |= Mathf.Abs(projected - raw) > 0.00001f ||
                float.IsNaN(pose.muscles[i]) || float.IsInfinity(pose.muscles[i]);
            // Keep a finite uncorrected pose for the baseline reconstruction.
            pose.muscles[i] = raw;
            previous[i] = projectedMuscles[i] = projected;
        }
        // Capture the actual input before Humanoid reconstruction can discard
        // bone-axis/twist information that has no exact muscle representation.
        foreach (BonePair bone in bones) bone.InputWorld = bone.Shadow.rotation;
        BonePair pelvis = bones.Find(bone => bone.Id == HumanBodyBones.Hips);
        Quaternion baselineAlignment = Quaternion.identity;
        Quaternion correctedAlignment = Quaternion.identity;
        if (needsProjection)
        {
            // Compare two reconstructions on the SAME rig. Their difference is
            // the constraint, not the lossy input -> Humanoid round-trip error.
            handler.SetHumanPose(ref pose);
            foreach (BonePair bone in bones) bone.BaselineWorld = bone.Shadow.rotation;
            if (pelvis != null)
                baselineAlignment = pelvis.InputWorld * Quaternion.Inverse(pelvis.Shadow.rotation);
            Array.Copy(projectedMuscles, pose.muscles, projectedMuscles.Length);
            handler.SetHumanPose(ref pose);
            if (pelvis != null)
                correctedAlignment = pelvis.InputWorld * Quaternion.Inverse(pelvis.Shadow.rotation);
        }
        if (pelvis != null)
        {
            pelvis.Live.localRotation = pelvis.InputRotation;
            pelvis.SafeRotation = pelvis.Live.localRotation;
        }
        foreach (BonePair bone in bones)
        {
            if (bone.Id == HumanBodyBones.Hips) continue;
            Quaternion output = bone.InputWorld;
            if (needsProjection)
            {
                Quaternion baseline = baselineAlignment * bone.BaselineWorld;
                Quaternion constrained = correctedAlignment * bone.Shadow.rotation;
                output = constrained * Quaternion.Inverse(baseline) * bone.InputWorld;
            }
            if (!TryNormalize(output, out Quaternion corrected))
            {
                corrected = bone.Live.parent != null ? bone.Live.parent.rotation * bone.SafeRotation : bone.SafeRotation;
                RejectedInputThisFrame = true;
            }
            // Preserve the original pose plus the correction delta. Never copy
            // a fully reconstructed pose over otherwise valid VMC rotations.
            Quaternion local = bone.Live.parent != null ? Quaternion.Inverse(bone.Live.parent.rotation) * corrected : corrected;
            if (hasSafePose)
            {
                if (Quaternion.Angle(bone.SafeRotation, local) > 65f) RejectedInputThisFrame = true;
                local = Quaternion.RotateTowards(bone.SafeRotation, local, 360f * dt);
            }
            bone.Live.localRotation = local;
            bone.SafeRotation = bone.Live.localRotation;
        }
        foreach (BonePair pair in hierarchy) pair.SafeRotation = pair.Live.localRotation;
        hasSafePose = true;
    }

    private void MapHierarchy(Transform live, Transform shadow)
    {
        for (int i = 0; i < live.childCount; i++)
        {
            Transform a = live.GetChild(i), b = shadow.GetChild(i);
            hierarchy.Add(new BonePair { Live = a, Shadow = b, SafeRotation = a.localRotation });
            MapHierarchy(a, b);
        }
    }
    private static int Depth(Transform transform)
    {
        int depth = 0;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent) depth++;
        return depth;
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

    private void BuildRanges()
    {
        for (int i = 0; i < minimum.Length; i++) { minimum[i] = -1f; maximum[i] = 1f; }
        Range("Spine Front-Back", -0.55f, 0.55f);
        Range("Chest Front-Back", -0.55f, 0.55f);
        Range("UpperChest Front-Back", -0.5f, 0.5f);
        foreach (string part in new[] { "Spine", "Chest", "UpperChest" })
        {
            Range(part + " Left-Right", -0.3f, 0.3f);
            Range(part + " Twist Left-Right", -0.3f, 0.3f);
        }
        Range("Neck Nod Down-Up", -0.65f, 0.65f);
        Range("Head Nod Down-Up", -0.65f, 0.65f);
        Range("Neck Tilt Left-Right", -0.35f, 0.35f);
        Range("Head Tilt Left-Right", -0.35f, 0.35f);
        Range("Neck Turn Left-Right", -0.45f, 0.45f);
        Range("Head Turn Left-Right", -0.45f, 0.45f);
        foreach (string side in new[] { "Left", "Right" })
        {
            Range(side + " Upper Leg In-Out", -0.5f, 0.5f);
            Range(side + " Upper Leg Twist In-Out", -0.35f, 0.35f);
            Range(side + " Lower Leg Stretch", -1f, 1f);
            Range(side + " Lower Leg Twist In-Out", -0.15f, 0.15f);
            Range(side + " Arm Twist In-Out", -0.65f, 0.65f);
            Range(side + " Arm Front-Back", -0.85f, 0.85f);
            Range(side + " Forearm Stretch", -1f, 1f);
            Range(side + " Forearm Twist In-Out", -0.65f, 0.65f);
            Range(side + " Hand Down-Up", -0.8f, 0.8f);
            Range(side + " Hand In-Out", -0.45f, 0.45f);
            Range(side + " Foot Up-Down", -0.8f, 0.8f);
            Range(side + " Foot Twist In-Out", -0.35f, 0.35f);
        }
        // Preserve the clean quadruped/extension cycle and its true mirror.
        // Only expand anatomical limits within the Humanoid range [-1,1].
        TextAsset asset = Resources.Load<TextAsset>(RecordedBirdDogCoachMotion.ResourcePath);
        if (asset == null) return;
        var source = JsonUtility.FromJson<CoachMotionPackage>(asset.text);
        if (source == null || !source.IsValid(out string error)) return;
        foreach (var frame in source.segments[0].frames)
        {
            if (frame.time < RecordedBirdDogCoachMotion.RestStart || frame.time > RecordedBirdDogCoachMotion.LowerEnd) continue;
            foreach (var sample in new[] { frame, RecordedBirdDogCoachMotion.MirrorFrame(frame) })
            for (int i = 0; i < minimum.Length; i++)
            {
                float value = sample.muscles[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                minimum[i] = Mathf.Min(minimum[i], Mathf.Clamp(value - 0.08f, -1f, 1f));
                maximum[i] = Mathf.Max(maximum[i], Mathf.Clamp(value + 0.08f, -1f, 1f));
            }
        }
        // Both support limbs have the same anatomical range.
        for (int i = 0; i < minimum.Length; i++)
        {
            string name = HumanTrait.MuscleName[i];
            if (!name.StartsWith("Left ", StringComparison.Ordinal)) continue;
            int other = Array.IndexOf(HumanTrait.MuscleName, "Right " + name.Substring(5));
            if (other < 0) continue;
            minimum[i] = minimum[other] = Mathf.Min(minimum[i], minimum[other]);
            maximum[i] = maximum[other] = Mathf.Max(maximum[i], maximum[other]);
        }
    }
    private void Range(string name, float low, float high)
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            if (HumanTrait.MuscleName[i] == name) { minimum[i] = low; maximum[i] = high; return; }
    }
    internal static bool TryNormalize(Quaternion value, out Quaternion normalized)
    {
        float length = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.000001f)
        { normalized = Quaternion.identity; return false; }
        float scale = 1f / Mathf.Sqrt(length);
        normalized = new Quaternion(value.x * scale, value.y * scale, value.z * scale, value.w * scale);
        return true;
    }
    private static Transform Clone(Transform source, Transform parent)
    {
        Transform result = new GameObject(source.name).transform;
        result.SetParent(parent, false);
        result.localPosition = source.localPosition;
        result.localRotation = source.localRotation;
        result.localScale = source.localScale;
        foreach (Transform child in source) Clone(child, result);
        return result;
    }
    private void OnEnable() { hasSafePose = false; }
    private void OnDisable() { RejectedInputThisFrame = false; hasSafePose = false; }
    private void OnDestroy() => Dispose();
    private void Dispose()
    {
        handler?.Dispose(); handler = null;
        if (shadowRoot != null)
        {
            if (Application.isPlaying) Destroy(shadowRoot.gameObject);
            else DestroyImmediate(shadowRoot.gameObject);
        }
        shadowRoot = null; bones.Clear(); hierarchy.Clear(); hasSafePose = false;
    }
}
}
