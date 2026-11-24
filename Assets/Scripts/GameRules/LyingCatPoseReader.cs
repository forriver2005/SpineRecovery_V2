using UnityEngine;

/// <summary>Read-only Humanoid muscle measurement; never writes a tracked pose.</summary>
public sealed class LyingCatPoseReader : MonoBehaviour
{
    private HumanPoseHandler handler;
    private Avatar avatar;
    private HumanPose pose;
    public bool Read(Animator animator, out HumanPose result)
    {
        if (handler == null || avatar != animator.avatar)
        {
            handler?.Dispose();
            avatar = animator.avatar;
            handler = new HumanPoseHandler(avatar, animator.transform);
        }
        handler.GetHumanPose(ref pose);
        result = pose;
        return result.muscles != null && result.muscles.Length == HumanTrait.MuscleCount;
    }
    private void OnDestroy() { handler?.Dispose(); handler = null; }
}
