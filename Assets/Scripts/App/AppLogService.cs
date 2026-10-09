using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SpineRecovery.App
{
    /// <summary>
    /// Collects Unity and embedded SlimeVR diagnostics into a small rolling file.
    /// The file is kept in persistentDataPath so it survives app restarts and can
    /// be shared from the device without requiring a separate logging app.
    /// </summary>
    public sealed class AppLogService : MonoBehaviour
    {
        private const string LogFileName = "spine-recovery-runtime.log";
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly object FileLock = new object();
        private static AppLogService instance;
        private bool subscribed;
        private float nextRuntimeDiagnosticsTime;
        private string lastRuntimeDiagnostics;
        private string lastWebSocketDiagnostics;

        public static string LogPath
        {
            get { return Path.Combine(Application.persistentDataPath, LogFileName); }
        }

        public static AppLogService Ensure()
        {
            if (instance != null) return instance;
            GameObject host = new GameObject("AppLogService");
            instance = host.AddComponent<AppLogService>();
            DontDestroyOnLoad(host);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Subscribe();
            WriteLine("[AppLog] logger started; persistentDataPath=" + Application.persistentDataPath);
        }

        private void OnDestroy()
        {
            if (subscribed)
            {
                Application.logMessageReceivedThreaded -= HandleLog;
                subscribed = false;
            }
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < nextRuntimeDiagnosticsTime) return;
            nextRuntimeDiagnosticsTime = Time.unscaledTime + 2f;
            if (EmbeddedSlimeVrBridge.TryGetRuntimeDiagnostics(out string diagnostics) &&
                !string.Equals(diagnostics, lastRuntimeDiagnostics, StringComparison.Ordinal))
            {
                lastRuntimeDiagnostics = diagnostics;
                WriteLine("[SlimeVR] runtime snapshot: " + diagnostics);
            }
            if (EmbeddedSlimeVrBridge.TryGetWebSocketDiagnostics(out string websocketDiagnostics) &&
                !string.Equals(websocketDiagnostics, lastWebSocketDiagnostics, StringComparison.Ordinal))
            {
                lastWebSocketDiagnostics = websocketDiagnostics;
                WriteLine("[SlimeVR] websocket probe: " + websocketDiagnostics);
            }
#endif
        }

        private void Subscribe()
        {
            if (subscribed) return;
            Application.logMessageReceivedThreaded += HandleLog;
            subscribed = true;
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                string line = string.Format("[{0:O}] [{1}] {2}", DateTimeOffset.UtcNow, type, condition);
                if (!string.IsNullOrWhiteSpace(stackTrace) && type != LogType.Log)
                    line += "\n" + stackTrace;
                WriteLine(line);
            }
            catch
            {
                // Logging must never cause a second application failure.
            }
        }

        private static void WriteLine(string line)
        {
            lock (FileLock)
            {
                try
                {
                    string path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                    {
                        string previous = path + ".1";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Device storage may be unavailable during early startup.
                }
            }
        }

    }
}
