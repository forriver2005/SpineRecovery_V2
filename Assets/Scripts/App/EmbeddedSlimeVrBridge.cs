using UnityEngine;

namespace SpineRecovery.App
{
    /// <summary>
    /// Thin Unity-to-Android entry point for the embedded SlimeVR runtime.
    /// The native Activity and runtime are supplied by the SlimeVR Android plugin.
    /// </summary>
    public static class EmbeddedSlimeVrBridge
    {
        [System.Serializable]
        public sealed class RuntimeState
        {
            public string state;
            public bool serverReady;
            public bool rpcReady;
            public bool skeletonFresh;
            public bool vmcSkeletonOutputFresh;
            public SlimeVrHapticTrackerSnapshot[] trackers;
        }

        public static bool TryReadState(out RuntimeState state)
        {
            state = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var managerClass = new AndroidJavaClass("dev.slimevr.android.SlimeVrRuntimeManager"))
                using (var manager = managerClass.GetStatic<AndroidJavaObject>("INSTANCE"))
                using (var snapshot = manager.Call<AndroidJavaObject>("snapshot"))
                using (var nativeState = snapshot.Call<AndroidJavaObject>("getState"))
                using (var trackers = snapshot.Call<AndroidJavaObject>("getTrackerStates"))
                {
                    state = new RuntimeState
                    {
                        state = nativeState.Call<string>("name"),
                        serverReady = snapshot.Call<bool>("getServerReady"),
                        rpcReady = snapshot.Call<bool>("getRpcReady"),
                        skeletonFresh = snapshot.Call<bool>("getSkeletonFresh"),
                        vmcSkeletonOutputFresh = snapshot.Call<bool>("getVmcSkeletonOutputFresh"),
                        trackers = new SlimeVrHapticTrackerSnapshot[trackers.Call<int>("size")]
                    };
                    for (int i = 0; i < state.trackers.Length; i++)
                    {
                        using (var tracker = trackers.Call<AndroidJavaObject>("get", i))
                            state.trackers[i] = new SlimeVrHapticTrackerSnapshot
                            {
                                hardwareId = tracker.Call<string>("getHardwareId"),
                                trackerNum = tracker.Call<int>("getTrackerNum"),
                                name = tracker.Call<string>("getName"),
                                ip = tracker.Call<string>("getIp"),
                                bodyPosition = tracker.Call<string>("getBodyPosition"),
                                status = tracker.Call<string>("getStatus"),
                                connected = tracker.Call<bool>("getConnected")
                            };
                    }
                    return true;
                }
            }
            catch (System.Exception error) { Debug.LogWarning("SlimeVR snapshot unavailable: " + error.Message); }
#endif
            return false;
        }
        private const string ActivityClass = "dev.slimevr.android.EmbeddedSlimeVrActivity";
        private const string InitialRouteExtra = "dev.slimevr.android.extra.INITIAL_ROUTE";
        private const string AllowDirectRouteExtra = "dev.slimevr.android.extra.ALLOW_DIRECT_ROUTE";

        public static bool TryOpenCalibration(int profileHeightCm, out string error)
        {
            error = null;
            Debug.Log("[SlimeVR] calibration launch requested; profileHeightCm=" + profileHeightCm);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var classApi = new AndroidJavaClass("java.lang.Class"))
                using (var activityClass = classApi.CallStatic<AndroidJavaObject>("forName", ActivityClass))
                using (var intent = new AndroidJavaObject("android.content.Intent", activity, activityClass))
                {
                    string route = "/checklist";
                    if (profileHeightCm >= 120 && profileHeightCm <= 220)
                        route += "?profileHeightCm=" + profileHeightCm;
                    intent.Call<AndroidJavaObject>("putExtra", InitialRouteExtra, route);
                    intent.Call<AndroidJavaObject>("putExtra", AllowDirectRouteExtra, true);
                    activity.Call("startActivity", intent);
                    Debug.Log("[SlimeVR] embedded calibration activity started; route=" + route);
                    return true;
                }
            }
            catch (System.Exception exception)
            {
                error = "无法打开内嵌 SlimeVR 校准：" + exception.Message;
                Debug.LogError(error + "\n" + exception.StackTrace);
                return false;
            }
#else
            error = "内嵌 SlimeVR 校准仅可在安装了 Android SlimeVR 插件的设备上使用。";
            return false;
#endif
        }

        /// <summary>
        /// Returns the embedded runtime snapshot so connection/startup failures
        /// are included in the exported Unity diagnostic log. The native
        /// manager owns the server, RPC and tracker state; Unity only samples
        /// its public snapshot and never touches the server directly.
        /// </summary>
        public static bool TryGetRuntimeDiagnostics(out string diagnostics)
        {
            diagnostics = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var managerClass = new AndroidJavaClass("dev.slimevr.android.SlimeVrRuntimeManager"))
                using (var manager = managerClass.GetStatic<AndroidJavaObject>("INSTANCE"))
                using (var snapshot = manager.Call<AndroidJavaObject>("snapshot"))
                {
                    diagnostics = snapshot.Call<string>("toString");
                    return !string.IsNullOrEmpty(diagnostics);
                }
            }
            catch (System.Exception exception)
            {
                diagnostics = "snapshot exception: " + exception.Message;
                return false;
            }
#else
            diagnostics = "embedded runtime diagnostics require Android";
            return false;
#endif
        }

        /// <summary>
        /// Probes the native WebSocket API connection list.  The runtime can be
        /// healthy while the embedded page is unable to open its local socket;
        /// this distinguishes those two cases in the exported log.
        /// </summary>
        public static bool TryGetWebSocketDiagnostics(out string diagnostics)
        {
            bool available = TryReadState(out RuntimeState snapshot);
            diagnostics = available ? "runtime=" + snapshot.state + ", rpcReady=" + snapshot.rpcReady : "runtime unavailable";
            return available;
        }
    }
}
