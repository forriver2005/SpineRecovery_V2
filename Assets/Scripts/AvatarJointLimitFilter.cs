using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 VMC 驱动的人体骨骼保持在宽松、相对于模型的安全范围内。
/// 过滤器独立于 VMC 传输，保留原始姿态供诊断，同时向 Animator 输出稳定姿态。
/// </summary>
[DisallowMultipleComponent]
public sealed class AvatarJointLimitFilter : MonoBehaviour
{
    [Serializable]
    private struct JointEnvelope
    {
        public HumanBodyBones bone;
        public float softDegrees;
        public float hardDegrees;
    }

    [Header("关节活动范围过滤")]
    [Tooltip("是否启用关节活动范围过滤。")]
    [SerializeField] private bool enabledFilter = true;
    [Tooltip("允许的最大角速度，单位为度/秒。")]
    [SerializeField, Min(30f)] private float maximumAngularSpeed = 900f;
    [Tooltip("单帧角度突变超过该值时，保持上一帧有效姿态。")]
    [SerializeField, Min(30f)] private float hardJumpDegrees = 100f;
    [Tooltip("姿态回到硬限制以内多少度后，解除异常锁存。")]
    [SerializeField, Min(0.01f)] private float recoveryHysteresisDegrees = 8f;
    [Tooltip("软限制的跟随响应速度。")]
    [SerializeField, Min(0.01f)] private float softResponse = 14f;
    [Tooltip("各主要关节的软限制和硬限制，单位为度。")]
    [SerializeField] private JointEnvelope[] envelopes =
    {
        // Quaternion.Angle 返回最短弧（0 到 180 度），因此肩部硬限制略低于
        // 完整翻转，用于拦截手臂翻折。
        new JointEnvelope { bone = HumanBodyBones.LeftUpperArm, softDegrees = 170f, hardDegrees = 179f },
        new JointEnvelope { bone = HumanBodyBones.RightUpperArm, softDegrees = 170f, hardDegrees = 179f },
        new JointEnvelope { bone = HumanBodyBones.LeftLowerArm, softDegrees = 150f, hardDegrees = 160f },
        new JointEnvelope { bone = HumanBodyBones.RightLowerArm, softDegrees = 150f, hardDegrees = 160f },
        new JointEnvelope { bone = HumanBodyBones.LeftUpperLeg, softDegrees = 135f, hardDegrees = 145f },
        new JointEnvelope { bone = HumanBodyBones.RightUpperLeg, softDegrees = 135f, hardDegrees = 145f },
        new JointEnvelope { bone = HumanBodyBones.LeftLowerLeg, softDegrees = 150f, hardDegrees = 160f },
        new JointEnvelope { bone = HumanBodyBones.RightLowerLeg, softDegrees = 150f, hardDegrees = 160f }
    };

    private readonly Dictionary<HumanBodyBones, Quaternion> neutralRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private readonly Dictionary<HumanBodyBones, Quaternion> previousRawRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private readonly Dictionary<HumanBodyBones, Quaternion> previousConstrainedRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private readonly Dictionary<HumanBodyBones, PoseConstraintFlags> flags =
        new Dictionary<HumanBodyBones, PoseConstraintFlags>();
    private readonly Dictionary<HumanBodyBones, JointEnvelope> envelopeByBone =
        new Dictionary<HumanBodyBones, JointEnvelope>();
    private readonly Dictionary<HumanBodyBones, Vector3> neutralDirections =
        new Dictionary<HumanBodyBones, Vector3>();
    private readonly HashSet<HumanBodyBones> hardLatchedBones =
        new HashSet<HumanBodyBones>();
    private readonly HashSet<HumanBodyBones> sampledBones =
        new HashSet<HumanBodyBones>();

    private Animator animator;
    private bool anyConstraintActive;
    private bool hardInvalid;

    [Flags]
    public enum PoseConstraintFlags
    {
        None = 0,
        Constrained = 1,
        InvalidRotation = 2,
        SuddenJump = 4,
        HardOutOfRange = 8,
        HingeOutOfRange = 16
    }

    public bool AnyConstraintActive => anyConstraintActive;
    public bool HasHardInvalidPose => hardInvalid;

    public void Configure(Animator targetAnimator)
    {
        if (animator == targetAnimator && neutralRotations.Count > 0)
        {
            return;
        }

        animator = targetAnimator;
        neutralRotations.Clear();
        previousRawRotations.Clear();
        previousConstrainedRotations.Clear();
        flags.Clear();
        envelopeByBone.Clear();
        neutralDirections.Clear();
        hardLatchedBones.Clear();
        sampledBones.Clear();
        anyConstraintActive = false;
        hardInvalid = false;

        foreach (JointEnvelope envelope in envelopes)
        {
            envelopeByBone[envelope.bone] = envelope;
            Transform bone = animator != null ? animator.GetBoneTransform(envelope.bone) : null;
            if (bone == null)
            {
                continue;
            }

            neutralRotations[envelope.bone] = Normalize(bone.localRotation);
            HumanBodyBones distalBone = GetDistalBone(envelope.bone);
            Transform distal = distalBone != HumanBodyBones.LastBone
                ? animator.GetBoneTransform(distalBone)
                : null;
            if (distal != null)
            {
                Vector3 direction = distal.position - bone.position;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    // A joint envelope is relative to its parent, not gravity.
                    // World-space baselines misclassify lying down/turning as
                    // extra hip excursion even when the joint pose is unchanged.
                    neutralDirections[envelope.bone] = bone.parent != null
                        ? bone.parent.InverseTransformDirection(direction.normalized)
                        : direction.normalized;
                }
            }
            previousRawRotations[envelope.bone] = bone.localRotation;
            previousConstrainedRotations[envelope.bone] = bone.localRotation;
            flags[envelope.bone] = PoseConstraintFlags.None;
        }
    }

    public Quaternion Filter(HumanBodyBones bone, Quaternion rawRotation)
    {
        if (!enabledFilter || !envelopeByBone.TryGetValue(bone, out JointEnvelope envelope))
        {
            return rawRotation;
        }

        if (!IsFinite(rawRotation) || QuaternionMagnitudeSquared(rawRotation) < 0.000001f)
        {
            hardLatchedBones.Add(bone);
            hardInvalid = true;
            anyConstraintActive = true;
            flags[bone] = PoseConstraintFlags.InvalidRotation | PoseConstraintFlags.HardOutOfRange;
            return previousConstrainedRotations[bone];
        }

        rawRotation = Normalize(rawRotation);
        PoseConstraintFlags frameFlags = PoseConstraintFlags.None;
        float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 1f / 120f);
        bool hasPreviousSample = sampledBones.Contains(bone);
        float rawStep = hasPreviousSample
            ? Quaternion.Angle(previousRawRotations[bone], rawRotation)
            : 0f;
        float maxStep = maximumAngularSpeed * deltaTime;
        if (rawStep > hardJumpDegrees || rawStep > maxStep * 2f)
        {
            hardLatchedBones.Add(bone);
            frameFlags |= PoseConstraintFlags.SuddenJump;
            anyConstraintActive = true;
            hardInvalid = true;
            flags[bone] = frameFlags;
            return previousConstrainedRotations[bone];
        }

        Quaternion relative = Quaternion.Inverse(neutralRotations[bone]) * rawRotation;
        float excursion = Quaternion.Angle(Quaternion.identity, relative);
        float hardLimit = Mathf.Max(envelope.softDegrees, envelope.hardDegrees);
        if (excursion <= hardLimit - recoveryHysteresisDegrees)
        {
            hardLatchedBones.Remove(bone);
        }
        float allowed = excursion;
        if (excursion > envelope.softDegrees)
        {
            float target = Mathf.Min(excursion, hardLimit);
            float normalized = Mathf.InverseLerp(envelope.softDegrees, hardLimit, target);
            float response = Mathf.SmoothStep(0f, 1f, normalized);
            allowed = Mathf.Lerp(excursion, target, response);
            if (excursion > hardLimit)
            {
                hardLatchedBones.Add(bone);
                frameFlags |= PoseConstraintFlags.HardOutOfRange;
                hardInvalid = true;
            }
            else
            {
                if (excursion <= hardLimit - recoveryHysteresisDegrees)
                {
                    hardLatchedBones.Remove(bone);
                }
                frameFlags |= PoseConstraintFlags.Constrained;
            }

            anyConstraintActive = true;
            relative = Quaternion.RotateTowards(Quaternion.identity, relative, allowed);
        }

        Quaternion constrained = Normalize(neutralRotations[bone] * relative);
        if ((frameFlags & PoseConstraintFlags.HardOutOfRange) != 0)
        {
            constrained = previousConstrainedRotations[bone];
        }
        else if ((frameFlags & PoseConstraintFlags.Constrained) != 0)
        {
            float blend = 1f - Mathf.Exp(-softResponse * deltaTime);
            constrained = Quaternion.Slerp(previousConstrainedRotations[bone], constrained, blend);
        }

        previousRawRotations[bone] = rawRotation;
        sampledBones.Add(bone);
        previousConstrainedRotations[bone] = constrained;
        flags[bone] = frameFlags;
        hardInvalid = hardLatchedBones.Count > 0;
        return constrained;
    }

    /// <summary>在所有接收骨骼应用完成后执行几何关节限制。</summary>
    public void CompleteFrame()
    {
        if (!enabledFilter || animator == null)
        {
            return;
        }

        ConstrainHinge(
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftHand,
            150f,
            160f);
        ConstrainHinge(
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightHand,
            150f,
            160f);
        ConstrainHinge(
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftFoot,
            150f,
            160f);
        ConstrainHinge(
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightFoot,
            150f,
            160f);
        ConstrainSwing(HumanBodyBones.LeftUpperArm, 170f, 179f);
        ConstrainSwing(HumanBodyBones.RightUpperArm, 170f, 179f);
        ConstrainSwing(HumanBodyBones.LeftUpperLeg, 135f, 145f);
        ConstrainSwing(HumanBodyBones.RightUpperLeg, 135f, 145f);
    }

    public PoseConstraintFlags GetFlags(HumanBodyBones bone)
    {
        return flags.TryGetValue(bone, out PoseConstraintFlags value)
            ? value
            : PoseConstraintFlags.None;
    }

    public void BeginFrame()
    {
        anyConstraintActive = false;
        hardInvalid = hardLatchedBones.Count > 0;
        foreach (HumanBodyBones bone in envelopeByBone.Keys)
        {
            flags[bone] = PoseConstraintFlags.None;
        }
    }

    private void ConstrainHinge(
        HumanBodyBones jointBone,
        HumanBodyBones proximalBone,
        HumanBodyBones distalBone,
        float softLimit,
        float hardLimit)
    {
        Transform joint = animator.GetBoneTransform(jointBone);
        Transform proximal = animator.GetBoneTransform(proximalBone);
        Transform distal = animator.GetBoneTransform(distalBone);
        if (joint == null || proximal == null || distal == null)
        {
            return;
        }

        Vector3 towardProximal = proximal.position - joint.position;
        Vector3 towardDistal = distal.position - joint.position;
        if (towardProximal.sqrMagnitude < 0.000001f || towardDistal.sqrMagnitude < 0.000001f)
        {
            return;
        }

        float bend = 180f - Vector3.Angle(towardProximal, towardDistal);
        if (bend <= softLimit)
        {
            if (bend <= hardLimit - recoveryHysteresisDegrees)
            {
                hardLatchedBones.Remove(jointBone);
                hardInvalid = hardLatchedBones.Count > 0;
            }
            return;
        }

        Vector3 axis = Vector3.Cross(towardProximal, towardDistal);
        if (axis.sqrMagnitude < 0.000001f)
        {
            return;
        }

        float targetBend = Mathf.Min(bend, hardLimit);
        float correction = bend - targetBend;
        Vector3 normalizedAxis = axis.normalized;
        float positiveAngle = Vector3.Angle(
            towardProximal,
            Quaternion.AngleAxis(correction, normalizedAxis) * towardDistal);
        float negativeAngle = Vector3.Angle(
            towardProximal,
            Quaternion.AngleAxis(-correction, normalizedAxis) * towardDistal);
        float signedCorrection = positiveAngle >= negativeAngle ? correction : -correction;
        if (bend > hardLimit)
        {
            hardLatchedBones.Add(jointBone);
            signedCorrection = positiveAngle >= negativeAngle ? bend - hardLimit : -(bend - hardLimit);
            hardInvalid = true;
        }
        else if (bend <= hardLimit - recoveryHysteresisDegrees)
        {
            hardLatchedBones.Remove(jointBone);
        }

        joint.rotation = Quaternion.AngleAxis(signedCorrection, normalizedAxis) * joint.rotation;
        anyConstraintActive = true;
        flags[jointBone] = GetFlags(jointBone) |
            (bend > hardLimit ? PoseConstraintFlags.HingeOutOfRange | PoseConstraintFlags.HardOutOfRange : PoseConstraintFlags.Constrained);
        hardInvalid = hardLatchedBones.Count > 0;
    }

    private void ConstrainSwing(
        HumanBodyBones boneId,
        float softLimit,
        float hardLimit)
    {
        if (!neutralDirections.TryGetValue(boneId, out Vector3 neutralDirection))
        {
            return;
        }

        Transform bone = animator.GetBoneTransform(boneId);
        Transform distal = animator.GetBoneTransform(GetDistalBone(boneId));
        if (bone == null || distal == null)
        {
            return;
        }

        if (bone.parent != null)
            neutralDirection = bone.parent.TransformDirection(neutralDirection);

        Vector3 currentDirection = distal.position - bone.position;
        if (currentDirection.sqrMagnitude < 0.000001f)
        {
            return;
        }

        currentDirection.Normalize();
        float excursion = Vector3.Angle(neutralDirection, currentDirection);
        if (excursion <= softLimit)
        {
            if (excursion <= hardLimit - recoveryHysteresisDegrees)
            {
                hardLatchedBones.Remove(boneId);
                hardInvalid = hardLatchedBones.Count > 0;
            }
            return;
        }

        Vector3 axis = Vector3.Cross(currentDirection, neutralDirection);
        if (axis.sqrMagnitude < 0.000001f)
        {
            return;
        }

        float correction = excursion - Mathf.Min(excursion, hardLimit);
        Vector3 normalizedAxis = axis.normalized;
        float positiveAngle = Vector3.Angle(
            neutralDirection,
            Quaternion.AngleAxis(correction, normalizedAxis) * currentDirection);
        float negativeAngle = Vector3.Angle(
            neutralDirection,
            Quaternion.AngleAxis(-correction, normalizedAxis) * currentDirection);
        float signedCorrection = positiveAngle <= negativeAngle ? correction : -correction;
        if (excursion > hardLimit)
        {
            hardLatchedBones.Add(boneId);
            hardInvalid = true;
        }
        else if (excursion <= hardLimit - recoveryHysteresisDegrees)
        {
            hardLatchedBones.Remove(boneId);
        }

        bone.rotation = Quaternion.AngleAxis(signedCorrection, normalizedAxis) * bone.rotation;
        anyConstraintActive = true;
        flags[boneId] = GetFlags(boneId) |
            (excursion > hardLimit
                ? PoseConstraintFlags.HardOutOfRange
                : PoseConstraintFlags.Constrained);
        hardInvalid = hardLatchedBones.Count > 0;
    }

    private static HumanBodyBones GetDistalBone(HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.LeftUpperArm: return HumanBodyBones.LeftLowerArm;
            case HumanBodyBones.RightUpperArm: return HumanBodyBones.RightLowerArm;
            case HumanBodyBones.LeftUpperLeg: return HumanBodyBones.LeftLowerLeg;
            case HumanBodyBones.RightUpperLeg: return HumanBodyBones.RightLowerLeg;
            case HumanBodyBones.LeftLowerArm: return HumanBodyBones.LeftHand;
            case HumanBodyBones.RightLowerArm: return HumanBodyBones.RightHand;
            case HumanBodyBones.LeftLowerLeg: return HumanBodyBones.LeftFoot;
            case HumanBodyBones.RightLowerLeg: return HumanBodyBones.RightFoot;
            default: return HumanBodyBones.LastBone;
        }
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
        return magnitude > 0.000001f
            ? new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude)
            : Quaternion.identity;
    }

    private static bool IsFinite(Quaternion value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
            !float.IsNaN(value.w) && !float.IsInfinity(value.w);
    }

    private static float QuaternionMagnitudeSquared(Quaternion value)
    {
        return value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
    }
}
