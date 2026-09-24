using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the EVMC4U receiver only for the playable game scenes.
/// The receiver drives the existing user Animator and reports fresh VMC
/// packets through the project's neutral AvatarMotionSource boundary.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class VmcGameModeBootstrap : MonoBehaviour
{
    private const int VmcInputPort = 39539;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= InstallForScene;
        SceneManager.sceneLoaded += InstallForScene;
    }

    private static void InstallForScene(Scene scene, LoadSceneMode mode)
    {
        if (FindObjectOfType<RhythmModeController>(true) == null ||
            FindUserAnimator() == null ||
            FindObjectOfType<VmcGameModeBootstrap>(true) != null)
        {
            return;
        }

        GameObject root = new GameObject("VMC Game Mode Receiver");
        root.AddComponent<VmcGameModeBootstrap>();
        EnsureHitHaptics(root);
    }

    private static void EnsureHitHaptics(GameObject sharedRoot)
    {
        if (FindObjectOfType<CoachHapticFeedbackController>(true) == null)
        {
            sharedRoot.AddComponent<CoachHapticFeedbackController>();
        }

        RhythmModeController rhythmController =
            FindObjectOfType<RhythmModeController>(true);
        if (rhythmController != null &&
            FindObjectOfType<RhythmHitHapticAdapter>(true) == null)
        {
            rhythmController.gameObject.AddComponent<RhythmHitHapticAdapter>();
        }
    }

    private void Awake()
    {
        Animator userAnimator = FindUserAnimator();
        if (userAnimator == null)
        {
            enabled = false;
            return;
        }

        EVMC4U.ExternalReceiver receiver =
            gameObject.AddComponent<EVMC4U.ExternalReceiver>();
        uOSC.uOscServer server = gameObject.AddComponent<uOSC.uOscServer>();
        AvatarMotionSource source = gameObject.AddComponent<AvatarMotionSource>();
        VmcGameModeSourceBridge bridge =
            gameObject.AddComponent<VmcGameModeSourceBridge>();

        server.port = VmcInputPort;
        server.autoStart = true;
        receiver.Model = userAnimator.gameObject;
        receiver.RootPositionSynchronize = false;
        receiver.RootRotationSynchronize = false;
        receiver.RootScaleOffsetSynchronize = false;
        // VMC positions use the sender's skeleton scale. Applying them to a
        // different VRM stretches the avatar, so this receiver is rotation-only.
        receiver.BonePositionSynchronize = false;
        receiver.BonePositionFilterEnable = false;
        receiver.BoneRotationFilterEnable = false;
        receiver.CutBonesEnable = true;
        receiver.CutBoneHips = false;
        receiver.CutBoneSpine = false;
        receiver.CutBoneChest = false;
        receiver.CutBoneUpperChest = false;
        receiver.CutBoneLeftUpperLeg = false;
        receiver.CutBoneLeftLowerLeg = false;
        receiver.CutBoneLeftFoot = false;
        receiver.CutBoneLeftToes = false;
        receiver.CutBoneRightUpperLeg = false;
        receiver.CutBoneRightLowerLeg = false;
        receiver.CutBoneRightFoot = false;
        receiver.CutBoneRightToes = false;
        receiver.CutBoneNeck = true;
        receiver.CutBoneHead = true;
        receiver.CutBoneLeftEye = true;
        receiver.CutBoneRightEye = true;
        receiver.CutBoneJaw = true;
        source.Configure(userAnimator);
        bridge.Configure(receiver, source);
    }

    private static Animator FindUserAnimator()
    {
        PoseScorer scorer = FindObjectOfType<PoseScorer>(true);
        if (scorer != null)
        {
            FieldInfo field = typeof(PoseScorer).GetField(
                "userAnimator",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Animator animator = field?.GetValue(scorer) as Animator;
            if (animator != null)
            {
                return animator;
            }
        }

        DeadBugGamingPoseScorer deadBugScorer =
            FindObjectOfType<DeadBugGamingPoseScorer>(true);
        return deadBugScorer != null ? deadBugScorer.UserAnimator : null;
    }
}

/// <summary>Feeds the existing scoring freshness gate from EVMC4U packets.</summary>
public sealed class VmcGameModeSourceBridge : MonoBehaviour
{
    private EVMC4U.ExternalReceiver receiver;
    private AvatarMotionSource source;

    public void Configure(
        EVMC4U.ExternalReceiver externalReceiver,
        AvatarMotionSource motionSource)
    {
        receiver = externalReceiver;
        source = motionSource;
    }

    private void Update()
    {
        if (receiver == null || source == null)
        {
            return;
        }

        source.SetAvailable(receiver.GetAvailable() > 0);
        if (receiver.LastBonePacketCounterInFrame > 0)
        {
            source.ReportFrame(
                receiver.GetRemoteTime(),
                receiver.LastBonePacketCounterInFrame);
        }
    }
}
