using EVMC4U;
using UnityEngine;

/// <summary>
/// Bridges EVMC4U's SlimeVR/VMC receiver into V2's SDK-neutral motion source.
/// Pose application stays in ExternalReceiver and scoring stays in the existing
/// coach controllers; this component forwards availability and packet freshness.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class SlimeVrAvatarMotionAdapter : MonoBehaviour
{
    [SerializeField] private ExternalReceiver receiver;
    [SerializeField] private AvatarMotionSource motionSource;

    private GameObject configuredModel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForLoadedScene()
    {
        foreach (ExternalReceiver externalReceiver in
                 FindObjectsOfType<ExternalReceiver>(true))
        {
            SlimeVrAvatarMotionAdapter adapter =
                externalReceiver.GetComponent<SlimeVrAvatarMotionAdapter>();
            if (adapter == null)
            {
                adapter = externalReceiver.gameObject.AddComponent<SlimeVrAvatarMotionAdapter>();
            }

            AvatarMotionSource source = externalReceiver.GetComponent<AvatarMotionSource>();
            if (source == null)
            {
                source = externalReceiver.gameObject.AddComponent<AvatarMotionSource>();
            }

            // Awake has already initialized serialized adapters before the session
            // disables root-scale sync. Do not overwrite that policy here.
            if (adapter.motionSource == null) adapter.Configure(externalReceiver, source);
        }
    }

    private void Awake()
    {
        if (receiver == null)
        {
            receiver = GetComponent<ExternalReceiver>();
        }

        if (motionSource == null)
        {
            motionSource = GetComponent<AvatarMotionSource>();
        }

        InitializeMotionSource();
    }

    public void Configure(ExternalReceiver externalReceiver, AvatarMotionSource source)
    {
        receiver = externalReceiver;
        motionSource = source;
        InitializeMotionSource();
    }

    private void LateUpdate()
    {
        if (receiver == null || motionSource == null)
        {
            return;
        }

        if (configuredModel != receiver.Model)
        {
            ConfigureModel(receiver.Model);
        }

        receiver.Freeze = motionSource.Freeze;
        receiver.RootScaleOffsetSynchronize = motionSource.RootScaleOffsetSynchronize;
        receiver.SyncCalibrationModeWithScaleOffsetSynchronize =
            motionSource.SyncCalibrationModeWithScaleOffsetSynchronize;

        bool available = receiver.isActiveAndEnabled &&
            receiver.GetAvailable() > 0 && receiver.Model != null;
        motionSource.SetReceiverFrame(receiver.GetRemoteTime(),
            available ? receiver.LastPacketframeCounterInFrame : 0,
            available ? receiver.LastBonePacketCounterInFrame : 0, available);
    }

    private void OnDisable()
    {
        if (motionSource != null) motionSource.SetReceiverFrame(0f, 0, 0, false);
    }

    private void InitializeMotionSource()
    {
        if (receiver == null || motionSource == null)
        {
            return;
        }

        motionSource.Freeze = receiver.Freeze;
        motionSource.RootScaleOffsetSynchronize = receiver.RootScaleOffsetSynchronize;
        motionSource.SyncCalibrationModeWithScaleOffsetSynchronize =
            receiver.SyncCalibrationModeWithScaleOffsetSynchronize;
        ConfigureModel(receiver.Model);
        motionSource.SetAvailable(receiver.GetAvailable() > 0 && receiver.Model != null);
    }

    private void ConfigureModel(GameObject model)
    {
        configuredModel = model;
        Animator animator = model != null ? model.GetComponent<Animator>() : null;
        if (animator == null && model != null)
        {
            animator = model.GetComponentInChildren<Animator>();
        }

        motionSource.Configure(animator);
    }
}
