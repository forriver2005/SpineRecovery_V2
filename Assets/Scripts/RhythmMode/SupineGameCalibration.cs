using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Opt-in for the new game scenes; legacy scenes retain their original calibration.
public sealed class SupineGameCalibration : MonoBehaviour
{
    [Tooltip("Temporarily skip calibration to preview the game flow. Disable to restore posture calibration.")]
    public bool skipCalibrationForPreview;
    public AnimationClip referenceClip;
    public float referenceSeconds = 6.8f;
    [Tooltip("For a recorded bird-dog scene, freeze the coach at this controller state instead of using the supine reference clip.")]
    public string referenceStateName = "birddog_start";
    [Tooltip("Use the four-point bird-dog setup: both hands and both knees supporting the body.")]
    public bool proneBirdDog;
    public string poseInstructionOverride;
    public const string Instruction = "请仰卧屈膝、双脚着地，双臂放身体两侧";
    public bool leftSideBridge;
    public SideBridgePoseGeometry.SupportSide supportSide = SideBridgePoseGeometry.SupportSide.Left;
    public bool rightSideBridge;
    private bool IsSideBridge => leftSideBridge || rightSideBridge;
    private SideBridgePoseGeometry.SupportSide EffectiveSide =>
        rightSideBridge || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("SideBridgeOpposite")
            ? SideBridgePoseGeometry.SupportSide.Right : supportSide;
    private bool IsRightSupport => EffectiveSide == SideBridgePoseGeometry.SupportSide.Right;

    public string PoseName => proneBirdDog ? "趴姿" : IsSideBridge ? (IsRightSupport ? "右侧支撑" : "左侧支撑") : "仰卧";
    public string PoseInstruction => !string.IsNullOrEmpty(poseInstructionOverride)?poseInstructionOverride:proneBirdDog
        ? "请趴姿准备：双手支撑、双膝跪地，肩髋保持平稳"
        : IsSideBridge ? (IsRightSupport ? "请按教练右侧支撑，右肘与右脚支撑，肩髋叠放"
            : "请按教练左侧支撑，左肘与左脚支撑，肩髋叠放") : Instruction;
    public bool IsPoseReady(Animator avatar)
    {
        var recorded = GetComponent<RecordedHoldPreparation>();
        if (recorded != null) return recorded.Matches(avatar);
        if (proneBirdDog) return IsProne(avatar);
        if (!IsSideBridge) return IsLying(avatar);
        return SideBridgePoseGeometry.TryMeasure(avatar, EffectiveSide, out var pose) &&
            (IsRightSupport ? pose.IsRightSupport : pose.IsLeftSupport);
    }
    private PlayableGraph graph;

    public bool Prepare(Animator coach)
    {
        Release();
        if (coach == null || !coach.isHuman) return false;
        coach.enabled = true;
        coach.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var recorded=GetComponent<RecordedHoldPreparation>();
        if(recorded!=null)
        {
            coach.Rebind();coach.Update(0);coach.Play(referenceStateName,0,0);coach.Update(0);coach.speed=0;
            return recorded.Capture(coach);
        }
        if (proneBirdDog)
        {
            coach.Rebind();
            coach.Update(0f);
            string state = referenceStateName;
            if (string.IsNullOrEmpty(state))
            {
                var controller = FindObjectOfType<CoachActionController>();
                state = controller != null ? controller.StartSegmentStateName : null;
            }
            if (string.IsNullOrEmpty(state)) return false;
            coach.Play(state, 0, 0f);
            coach.Update(0f);
            coach.speed = 0f;
            return true;
        }
        if (referenceClip == null) return false;
        graph = PlayableGraph.Create("Supine calibration reference");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var clip = AnimationClipPlayable.Create(graph, referenceClip);
        clip.SetTime(referenceSeconds);
        clip.SetSpeed(0);
        AnimationPlayableOutput.Create(graph, "Reference", coach).SetSourcePlayable(clip);
        graph.Play();
        graph.Evaluate(0);
        return true;
    }

    public void SampleReference() { if (graph.IsValid()) graph.Evaluate(0); }

    public static bool IsLying(Animator avatar)
    {
        if (avatar == null || !avatar.isHuman) return false;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Transform chest = avatar.GetBoneTransform(HumanBodyBones.Chest) ?? avatar.GetBoneTransform(HumanBodyBones.Spine);
        if (hips == null || chest == null) return false;
        Vector3 torso = chest.position - hips.position;
        Transform leftKnee = avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform thigh = avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        if (leftKnee == null || rightKnee == null || thigh == null) return false;
        float legLength = Vector3.Distance(thigh.position, leftKnee.position);
        float kneeHeight = (leftKnee.position.y + rightKnee.position.y) * 0.5f - hips.position.y;
        // Bent knees above the hips distinguish the requested supine pose from four-point kneeling.
        return torso.sqrMagnitude > 0.001f && Mathf.Abs(Vector3.Dot(torso.normalized, Vector3.up)) < 0.45f
            && legLength > 0.001f && kneeHeight > legLength * 0.15f;
    }

    /// <summary>
    /// Detects the bird-dog setup without requiring a special camera angle. The
    /// torso must be close to horizontal, knees must not be lifted above the
    /// hips like a supine dead-bug setup, and hands/knees must sit close to the
    /// body's support plane.
    /// </summary>
    public static bool IsProne(Animator avatar)
    {
        if (avatar == null || !avatar.isHuman) return false;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Transform chest = avatar.GetBoneTransform(HumanBodyBones.Chest) ?? avatar.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftShoulder = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftHand = avatar.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = avatar.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftThigh = avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform leftKnee = avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        if (hips == null || chest == null || leftShoulder == null || rightShoulder == null ||
            leftHand == null || rightHand == null || leftThigh == null || leftKnee == null || rightKnee == null)
            return false;

        Vector3 torso = chest.position - hips.position;
        float legLength = Vector3.Distance(leftThigh.position, leftKnee.position);
        if (torso.sqrMagnitude < .001f || legLength < .05f) return false;
        float torsoVertical = Mathf.Abs(Vector3.Dot(torso.normalized, Vector3.up));
        float kneeHeight = ((leftKnee.position.y + rightKnee.position.y) * .5f) - hips.position.y;
        Vector3 shoulders = (leftShoulder.position + rightShoulder.position) * .5f;
        Vector3 hands = (leftHand.position + rightHand.position) * .5f;
        float handToShoulder = Mathf.Abs(hands.y - shoulders.y);
        float handToHip = Mathf.Abs(hands.y - hips.position.y);
        // The recorded coach's neutral spine has a modest chest incline rather
        // than a perfectly flat torso, so allow that realistic setup angle.
        return torsoVertical < .75f && kneeHeight < legLength * .30f &&
            handToShoulder < legLength * 1.50f && handToHip < legLength * 1.15f;
    }

    public void Release() { if (graph.IsValid()) graph.Destroy(); }
    private void OnDisable() { Release(); }
}
