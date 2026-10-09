using UnityEngine;

namespace SpineRecovery.App
{
    /// <summary>
    /// Thin Unity-to-Android entry point for the embedded SlimeVR runtime.
    /// The native Activity and runtime are supplied by the SlimeVR Android plugin.
    /// </summary>
    public static class EmbeddedSlimeVrBridge
    {
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
            diagnostics = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var managerClass = new AndroidJavaClass("dev.slimevr.android.SlimeVrRuntimeManager"))
                using (var manager = managerClass.GetStatic<AndroidJavaObject>("INSTANCE"))
                using (var server = manager.Call<AndroidJavaObject>("serverForTesting"))
                using (var protocolApi = server.Get<AndroidJavaObject>("protocolAPI"))
                using (var apiServers = protocolApi.Call<AndroidJavaObject>("getApiServers"))
                {
                    int apiCount = apiServers.Call<int>("size");
                    long websocketConnections = 0;
                    string apiTypes = string.Empty;
                    for (int i = 0; i < apiCount; i++)
                    {
                        using (var api = apiServers.Call<AndroidJavaObject>("get", i))
                        {
                            string type = api.Call<AndroidJavaObject>("getClass")
                                .Call<string>("getName");
                            apiTypes += (i == 0 ? string.Empty : ",") + type;
                            if (type.Contains("WebsocketAPI"))
                            {
                                using (var connections = api.Call<AndroidJavaObject>("getApiConnections"))
                                    websocketConnections += connections.Call<long>("count");
                            }
                        }
                    }
                    diagnostics = "apiServers=" + apiCount +
                        ", websocketConnections=" + websocketConnections +
                        ", apiTypes=" + apiTypes;
                    return true;
                }
            }
            catch (System.Exception exception)
            {
                diagnostics = "websocket probe exception: " + exception.Message;
                return false;
            }
#else
            diagnostics = "websocket probe requires Android";
            return false;
#endif
        }
    }
}
