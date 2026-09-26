using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Physical locations of the wearable trackers. This is intentionally
/// separate from BodyPart, which is the coarser visual-guidance vocabulary.
/// </summary>
public enum TrackerWearLocation
{
    LeftUpperArm,
    LeftForearm,
    RightUpperArm,
    RightForearm,
    LeftThigh,
    LeftLowerLeg,
    RightThigh,
    RightLowerLeg,
    Chest,
    Abdomen
}

[Serializable]
public sealed class TrackerMotorEndpoint
{
    public TrackerWearLocation location;
    [Tooltip("设备 IPv4 地址，例如 192.168.31.137。")]
    public string address;
    [Min(1)] public int durationMs = 100;
}

[Serializable]
public sealed class SlimeVrHapticTrackerSnapshot
{
    public string hardwareId;
    public int trackerNum;
    public string name;
    public string ip;
    public string bodyPosition;
    public string status;
    public bool connected;
}

[Serializable]
public sealed class SlimeVrHapticTrackerResponse
{
    public int apiVersion;
    public SlimeVrHapticTrackerSnapshot[] trackers;
}

/// <summary>
/// Sends non-blocking timed pulses to tracker motor HTTP endpoints. The
/// controller deliberately owns cadence and cancellation so PoseScorer only
/// supplies confirmed guidance state.
/// </summary>
public sealed class CoachHapticFeedbackController : MonoBehaviour
{
    public static CoachHapticFeedbackController Instance { get; private set; }
    private bool applicationPaused;
    [Header("SlimeVR 自动识别")]
    [SerializeField] private string discoveryUrl = string.Empty;
    [SerializeField, Min(0.5f)] private float discoveryIntervalSeconds = 2f;

    [Header("演示设置")]
    [SerializeField] private bool enabledForDemo = true;
    [SerializeField, Min(1)] private int pulseDurationMs = 100;
    [SerializeField, Min(0.1f)] private float repeatIntervalSeconds = 2f;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 1;
    [SerializeField] private TrackerMotorEndpoint[] endpoints =
    {
        new TrackerMotorEndpoint { location = TrackerWearLocation.LeftUpperArm, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.LeftForearm, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.RightUpperArm, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.RightForearm, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.LeftThigh, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.LeftLowerLeg, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.RightThigh, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.RightLowerLeg, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.Chest, address = "" },
        new TrackerMotorEndpoint { location = TrackerWearLocation.Abdomen, address = "" }
    };

    private readonly Dictionary<TrackerWearLocation, string> endpointAddresses =
        new Dictionary<TrackerWearLocation, string>();
    private readonly Dictionary<TrackerWearLocation, string> fallbackEndpointAddresses =
        new Dictionary<TrackerWearLocation, string>();
    private readonly HashSet<TrackerWearLocation> automaticallyDiscoveredLocations =
        new HashSet<TrackerWearLocation>();
    private readonly Dictionary<TrackerWearLocation, int> endpointDurations =
        new Dictionary<TrackerWearLocation, int>();
    private readonly Dictionary<TrackerWearLocation, float> lastPulseAt =
        new Dictionary<TrackerWearLocation, float>();
    private readonly Dictionary<TrackerWearLocation, UnityWebRequest> inFlightRequests =
        new Dictionary<TrackerWearLocation, UnityWebRequest>();
    private readonly HashSet<TrackerWearLocation> activeLocations =
        new HashSet<TrackerWearLocation>();
    private string lastLoggedDiscoveryStatus;

    private bool phaseWasEnabled;
    private Coroutine discoveryCoroutine;

    public string DiscoveryStatus { get; private set; } = "等待连接修改版 SlimeVR";

    private static readonly TrackerWearLocation[] RightArmMapping =
    {
        TrackerWearLocation.RightUpperArm,
        TrackerWearLocation.RightForearm
    };

    private static readonly TrackerWearLocation[] LeftArmMapping =
    {
        TrackerWearLocation.LeftUpperArm,
        TrackerWearLocation.LeftForearm
    };

    private static readonly TrackerWearLocation[] RightLegMapping =
    {
        TrackerWearLocation.RightThigh,
        TrackerWearLocation.RightLowerLeg
    };

    private static readonly TrackerWearLocation[] LeftLegMapping =
    {
        TrackerWearLocation.LeftThigh,
        TrackerWearLocation.LeftLowerLeg
    };

    private static readonly TrackerWearLocation[] TorsoMapping =
    {
        TrackerWearLocation.Chest,
        TrackerWearLocation.Abdomen
    };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }
        Instance = this;
        RebuildEndpointMap();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && discoveryCoroutine == null)
        {
            discoveryCoroutine = StartCoroutine(PollTrackerBindings());
        }
    }

    private void OnDisable()
    {
        if (discoveryCoroutine != null) StopCoroutine(discoveryCoroutine);
        discoveryCoroutine = null;
        StopAllHaptics();
    }

    private void OnDestroy()
    {
        StopAllHaptics();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused)
        {
            StopAllHaptics();
        }
    }

    public void SetEnabled(bool enabled)
    {
        enabledForDemo = enabled;
        if (!enabled)
        {
            StopAllHaptics();
        }
    }

    public void ConfigureEndpoint(TrackerWearLocation location, string address)
    {
        if (endpointAddresses.Count == 0)
        {
            RebuildEndpointMap();
        }

        string normalizedAddress = address == null ? string.Empty : address.Trim();
        fallbackEndpointAddresses[location] = normalizedAddress;
        if (!automaticallyDiscoveredLocations.Contains(location))
        {
            endpointAddresses[location] = normalizedAddress;
        }
    }

    public void ConfigureEndpoint(
        TrackerWearLocation location,
        string address,
        int durationMs)
    {
        ConfigureEndpoint(location, address);
        endpointDurations[location] = Mathf.Clamp(durationMs, 1, 60000);
    }

    public void ConfigureDuration(TrackerWearLocation location, int durationMs)
    {
        endpointDurations[location] = Mathf.Clamp(durationMs, 1, 60000);
    }

    public string GetEndpointAddress(TrackerWearLocation location)
    {
        return endpointAddresses.TryGetValue(location, out string address)
            ? address
            : string.Empty;
    }

    public bool IsAutomaticallyDiscovered(TrackerWearLocation location)
    {
        return automaticallyDiscoveredLocations.Contains(location);
    }

    /// <summary>Triggers one pulse for a discrete gaming hit.</summary>
    public void PulseOnce(TrackerWearLocation location)
    {
        if (!enabledForDemo || !isActiveAndEnabled || applicationPaused)
        {
            return;
        }

        if (endpointAddresses.Count == 0)
        {
            RebuildEndpointMap();
        }

        PulseIfDue(location, true);
    }

    /// <summary>
    /// Advances haptic state for one guidance frame. Only confirmed body
    /// regions are accepted; invalid or stale input cancels the active state.
    /// </summary>
    public void Tick(
        bool guidancePhaseEnabled,
        bool guidanceInputIsFresh,
        ICollection<BodyPart> confirmedParts,
        bool coachIsPaused)
    {
        Tick(
            guidancePhaseEnabled,
            guidanceInputIsFresh,
            confirmedParts,
            null,
            coachIsPaused);
    }

    /// <summary>
    /// Advances the same haptic state machine while optionally accepting
    /// location-specific debug flags from the editor test tool.
    /// </summary>
    public void Tick(
        bool guidancePhaseEnabled,
        bool guidanceInputIsFresh,
        ICollection<BodyPart> confirmedParts,
        ICollection<TrackerWearLocation> debugLocations,
        bool coachIsPaused)
    {
        bool canPulse = isActiveAndEnabled && !applicationPaused && enabledForDemo &&
            guidancePhaseEnabled &&
            guidanceInputIsFresh &&
            !coachIsPaused;

        if (!canPulse)
        {
            if (phaseWasEnabled || activeLocations.Count > 0)
            {
                StopAllHaptics();
            }

            phaseWasEnabled = false;
            return;
        }

        phaseWasEnabled = true;
        HashSet<TrackerWearLocation> requestedLocations =
             BuildRequestedLocations(confirmedParts, debugLocations);
        AddConfiguredDebugLocations(requestedLocations, debugLocations);

        foreach (TrackerWearLocation location in requestedLocations)
        {
            if (activeLocations.Add(location))
            {
                PulseIfDue(location, true);
            }
            else
            {
                PulseIfDue(location, false);
            }
        }

        foreach (TrackerWearLocation location in activeLocations)
        {
            if (!requestedLocations.Contains(location) &&
                inFlightRequests.TryGetValue(location, out UnityWebRequest request))
            {
                request.Abort();
                inFlightRequests.Remove(location);
            }
        }
        activeLocations.RemoveWhere(location => !requestedLocations.Contains(location));
    }

    private void AddConfiguredDebugLocations(
        HashSet<TrackerWearLocation> requestedLocations,
        ICollection<TrackerWearLocation> debugLocations)
    {
        if (debugLocations == null)
        {
            return;
        }

        foreach (TrackerWearLocation location in debugLocations)
        {
            if (endpointAddresses.TryGetValue(location, out string address) &&
                !string.IsNullOrWhiteSpace(address))
            {
                requestedLocations.Add(location);
            }
        }
    }

    public void StopAllHaptics()
    {
        foreach (UnityWebRequest request in inFlightRequests.Values)
        {
            request.Abort();
        }

        inFlightRequests.Clear();
        activeLocations.Clear();
        lastPulseAt.Clear();
        phaseWasEnabled = false;
    }

    private void RebuildEndpointMap()
    {
        endpointAddresses.Clear();
        fallbackEndpointAddresses.Clear();
        automaticallyDiscoveredLocations.Clear();
        endpointDurations.Clear();
        if (endpoints == null)
        {
            return;
        }

        foreach (TrackerMotorEndpoint endpoint in endpoints)
        {
            if (endpoint != null)
            {
                string address = endpoint.address == null
                    ? string.Empty
                    : endpoint.address.Trim();
                endpointAddresses[endpoint.location] = address;
                fallbackEndpointAddresses[endpoint.location] = address;
                endpointDurations[endpoint.location] =
                    Mathf.Clamp(endpoint.durationMs, 1, 60000);
            }
        }
    }

    private IEnumerator PollTrackerBindings()
    {
        while (enabled)
        {
            yield return RefreshTrackerBindings();
            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.5f, discoveryIntervalSeconds));
        }
    }

    private IEnumerator RefreshTrackerBindings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string responseJson = null;
        try
        {
            responseJson = ReadAndroidTrackerBindings();
        }
        catch (Exception exception)
        {
            ClearAutomaticallyDiscoveredEndpoints();
            DiscoveryStatus = "未连接手机端内嵌 SlimeVR";
            Debug.LogWarning($"[Haptics] 无法读取手机端自动绑定数据：{exception.Message}", this);
            yield break;
        }

        ParseAndApplyTrackerBindings(responseJson, "手机端内嵌 SlimeVR");
        yield break;
#else
        if (string.IsNullOrWhiteSpace(discoveryUrl))
        {
            ClearAutomaticallyDiscoveredEndpoints();
            DiscoveryStatus = "等待外部 SlimeVR 自动绑定配置";
            yield break;
        }

        UnityWebRequest request = UnityWebRequest.Get(discoveryUrl);
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            ClearAutomaticallyDiscoveredEndpoints();
            DiscoveryStatus = "未连接修改版 SlimeVR（请先启动可执行程序）";
            request.Dispose();
            yield break;
        }

        string responseJson = request.downloadHandler.text;
        request.Dispose();
        ParseAndApplyTrackerBindings(responseJson, "修改版 SlimeVR");
#endif
    }

    private void ParseAndApplyTrackerBindings(string responseJson, string sourceName)
    {
        SlimeVrHapticTrackerResponse response = null;
        try
        {
            response = JsonUtility.FromJson<SlimeVrHapticTrackerResponse>(
                responseJson);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Haptics] 无法解析 SlimeVR 自动绑定数据：{exception.Message}", this);
        }

        if (response == null || response.apiVersion != 1 || response.trackers == null)
        {
            ClearAutomaticallyDiscoveredEndpoints();
            DiscoveryStatus = $"{sourceName}自动绑定数据格式不正确";
            return;
        }

        ApplyDiscoveredTrackers(response.trackers);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string ReadAndroidTrackerBindings()
    {
        using (AndroidJavaClass unityPlayer =
            new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject activity =
            unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
        using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
        using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>(
            "parse",
            "content://com.metaspine.mobile.trackerbindings"))
        using (AndroidJavaObject extras = new AndroidJavaObject("android.os.Bundle"))
        using (AndroidJavaObject result = resolver.Call<AndroidJavaObject>(
            "call",
            uri,
            "getTrackers",
            string.Empty,
            extras))
        {
            if (result == null)
            {
                throw new InvalidOperationException("手机端未返回 Tracker 绑定数据");
            }

            return result.Call<string>("getString", "tracker_bindings_json");
        }
    }
#endif

    private void ApplyDiscoveredTrackers(SlimeVrHapticTrackerSnapshot[] trackers)
    {
        var discovered = new Dictionary<TrackerWearLocation, string>();
        int unassignedCount = 0;

        foreach (SlimeVrHapticTrackerSnapshot tracker in trackers)
        {
            if (tracker == null || !tracker.connected || string.IsNullOrWhiteSpace(tracker.ip))
            {
                continue;
            }

            if (!TryMapBodyPosition(tracker.bodyPosition, out TrackerWearLocation location))
            {
                if (string.IsNullOrWhiteSpace(tracker.bodyPosition))
                {
                    unassignedCount++;
                }
                continue;
            }

            if (!discovered.ContainsKey(location))
            {
                discovered.Add(location, tracker.ip.Trim());
            }
        }

        automaticallyDiscoveredLocations.Clear();
        endpointAddresses.Clear();
        foreach (KeyValuePair<TrackerWearLocation, string> fallback in fallbackEndpointAddresses)
        {
            endpointAddresses[fallback.Key] = fallback.Value;
        }
        foreach (KeyValuePair<TrackerWearLocation, string> binding in discovered)
        {
            endpointAddresses[binding.Key] = binding.Value;
            automaticallyDiscoveredLocations.Add(binding.Key);
        }

        DiscoveryStatus = $"已自动识别 {discovered.Count}/10 个已分配设备";
        if (unassignedCount > 0)
        {
            DiscoveryStatus += $"，另有 {unassignedCount} 个设备未分配部位";
        }
        if (DiscoveryStatus != lastLoggedDiscoveryStatus)
        {
            Debug.Log($"[Haptics] {DiscoveryStatus}", this);
            lastLoggedDiscoveryStatus = DiscoveryStatus;
        }
    }

    private void ClearAutomaticallyDiscoveredEndpoints()
    {
        automaticallyDiscoveredLocations.Clear();
        endpointAddresses.Clear();
        foreach (KeyValuePair<TrackerWearLocation, string> fallback in fallbackEndpointAddresses)
        {
            endpointAddresses[fallback.Key] = fallback.Value;
        }
    }

    private static bool TryMapBodyPosition(
        string bodyPosition,
        out TrackerWearLocation location)
    {
        switch (bodyPosition)
        {
            case "body:upper_chest":
            case "body:chest":
                location = TrackerWearLocation.Chest;
                return true;
            case "body:waist":
            case "body:hip":
                location = TrackerWearLocation.Abdomen;
                return true;
            case "body:left_upper_arm":
                location = TrackerWearLocation.LeftUpperArm;
                return true;
            case "body:right_upper_arm":
                location = TrackerWearLocation.RightUpperArm;
                return true;
            case "body:left_lower_arm":
                location = TrackerWearLocation.LeftForearm;
                return true;
            case "body:right_lower_arm":
                location = TrackerWearLocation.RightForearm;
                return true;
            case "body:left_upper_leg":
                location = TrackerWearLocation.LeftThigh;
                return true;
            case "body:right_upper_leg":
                location = TrackerWearLocation.RightThigh;
                return true;
            case "body:left_lower_leg":
                location = TrackerWearLocation.LeftLowerLeg;
                return true;
            case "body:right_lower_leg":
                location = TrackerWearLocation.RightLowerLeg;
                return true;
            default:
                location = default;
                return false;
        }
    }

    private HashSet<TrackerWearLocation> BuildRequestedLocations(
        ICollection<BodyPart> confirmedParts,
        ICollection<TrackerWearLocation> debugLocations)
    {
        var requested = new HashSet<TrackerWearLocation>();
        var overriddenParts = new HashSet<BodyPart>();
        if (debugLocations != null)
        {
            foreach (TrackerWearLocation location in debugLocations)
            {
                BodyPart part = GetBodyPart(location);
                if (part != BodyPart.None)
                {
                    overriddenParts.Add(part);
                }
            }
        }

        if (confirmedParts == null)
        {
            return requested;
        }

        foreach (BodyPart part in confirmedParts)
        {
            if (overriddenParts.Contains(part))
            {
                continue;
            }

            TrackerWearLocation[] mapped = GetMapping(part);
            foreach (TrackerWearLocation location in mapped)
            {
                if (endpointAddresses.TryGetValue(location, out string address) &&
                    !string.IsNullOrWhiteSpace(address))
                {
                    requested.Add(location);
                }
            }
        }

        return requested;
    }

    private static BodyPart GetBodyPart(TrackerWearLocation location)
    {
        switch (location)
        {
            case TrackerWearLocation.LeftUpperArm:
            case TrackerWearLocation.LeftForearm:
                return BodyPart.LeftArm;
            case TrackerWearLocation.RightUpperArm:
            case TrackerWearLocation.RightForearm:
                return BodyPart.RightArm;
            case TrackerWearLocation.LeftThigh:
            case TrackerWearLocation.LeftLowerLeg:
                return BodyPart.LeftLeg;
            case TrackerWearLocation.RightThigh:
            case TrackerWearLocation.RightLowerLeg:
                return BodyPart.RightLeg;
            case TrackerWearLocation.Chest:
            case TrackerWearLocation.Abdomen:
                return BodyPart.Torso;
            default:
                return BodyPart.None;
        }
    }

    private static TrackerWearLocation[] GetMapping(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm:
                return LeftArmMapping;
            case BodyPart.RightArm:
                return RightArmMapping;
            case BodyPart.LeftLeg:
                return LeftLegMapping;
            case BodyPart.RightLeg:
                return RightLegMapping;
            case BodyPart.Torso:
                return TorsoMapping;
            default:
                return Array.Empty<TrackerWearLocation>();
        }
    }

    private void PulseIfDue(TrackerWearLocation location, bool enteringError)
    {
        if (!endpointAddresses.TryGetValue(location, out string address) ||
            string.IsNullOrWhiteSpace(address) ||
            inFlightRequests.ContainsKey(location))
        {
            return;
        }

        float now = Time.unscaledTime;
        bool hasPreviousPulse = lastPulseAt.TryGetValue(location, out float previousPulseAt);
        if (!enteringError &&
            hasPreviousPulse &&
            now - previousPulseAt < repeatIntervalSeconds)
        {
            return;
        }

        lastPulseAt[location] = now;
        int durationMs = endpointDurations.TryGetValue(location, out int configuredDuration)
            ? configuredDuration
            : pulseDurationMs;
        StartCoroutine(SendPulse(location, address, durationMs));
    }

    private IEnumerator SendPulse(TrackerWearLocation location, string address, int durationMs)
    {
        string normalizedAddress = address.Trim().TrimEnd('/');
        if (normalizedAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAddress = normalizedAddress.Substring("http://".Length);
        }
        else if (normalizedAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAddress = normalizedAddress.Substring("https://".Length);
        }

        string uri = string.Format(
            "http://{0}/motor?duration_ms={1}",
            normalizedAddress,
            Mathf.Clamp(durationMs, 1, 60000));
        UnityWebRequest request = UnityWebRequest.Get(uri);
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        inFlightRequests[location] = request;

        yield return request.SendWebRequest();

        bool isCurrentRequest = inFlightRequests.TryGetValue(
            location,
            out UnityWebRequest trackedRequest) && trackedRequest == request;
        if (isCurrentRequest)
        {
            inFlightRequests.Remove(location);
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                $"[Haptics] Tracker {location} request failed: {request.error}",
                this);
        }

        request.Dispose();
    }
}
