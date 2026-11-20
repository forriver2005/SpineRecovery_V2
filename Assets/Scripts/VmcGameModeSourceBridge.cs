using UnityEngine;

/// <summary>Feeds the existing scoring freshness gate from EVMC4U packets.</summary>
public sealed class VmcGameModeSourceBridge : MonoBehaviour
{
    [SerializeField] private EVMC4U.ExternalReceiver receiver;
    [SerializeField] private AvatarMotionSource motionSource;

    public void Configure(
        EVMC4U.ExternalReceiver externalReceiver,
        AvatarMotionSource motionSource)
    {
        receiver = externalReceiver;
        this.motionSource = motionSource;
    }

    private void Update()
    {
        if (receiver == null || motionSource == null)
        {
            return;
        }

        bool available = receiver.isActiveAndEnabled && !receiver.Freeze && receiver.GetAvailable() > 0;
        motionSource.SetAvailable(available);
        if (!available) return;
        if (receiver.LastBonePacketCounterInFrame > 0)
        {
            motionSource.ReportFrame(
                receiver.GetRemoteTime(),
                receiver.LastBonePacketCounterInFrame);
        }
    }
}
