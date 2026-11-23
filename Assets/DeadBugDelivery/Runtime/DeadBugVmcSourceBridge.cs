using UnityEngine;

namespace DeadBugDelivery
{
    /// <summary>Transfer per-render-frame telemetry after EVMC4U has processed
    /// this frame, before the delivery constraint and scorer read it.</summary>
    [DefaultExecutionOrder(29500)]
    public sealed class DeadBugVmcSourceBridge : MonoBehaviour
    {
        EVMC4U.ExternalReceiver receiver;
        AvatarMotionSource source;

        public void Configure(EVMC4U.ExternalReceiver externalReceiver,AvatarMotionSource motionSource)
        { receiver = externalReceiver; source = motionSource; }

        void LateUpdate()
        {
            if (source == null) return;
            bool available = receiver != null && receiver.isActiveAndEnabled && !receiver.Freeze && receiver.GetAvailable() > 0;
            // Overwrite with zero on a missing packet frame. The host project's
            // generic source caches counts and cannot implement this contract.
            source.SetReceiverFrame(receiver != null ? receiver.GetRemoteTime() : 0,
                available ? receiver.LastPacketframeCounterInFrame : 0,
                available ? receiver.LastBonePacketCounterInFrame : 0,available);
        }
        void OnDisable() { if (source != null) source.SetReceiverFrame(0,0,0,false); }
    }
}
