using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class HipThrustCoachMode : MonoBehaviour
{
    private const string MotionResource = "ActionLibrary/Motions/clip_26";
    private const string FemalePracticeScene = "HipThrustPractice";
    private const string MalePracticeScene = "maleHipThrustPractice";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != FemalePracticeScene && scene.name != MalePracticeScene)
        {
            return;
        }

        CoachMotionPackage package = SpineFlowTrainingSession.CachedMotionPackage;
        if (package == null || !IsHipThrust(package.actionId) ||
            package.packageId == "clip_26_coach")
        {
            TextAsset source = Resources.Load<TextAsset>(MotionResource);
            if (source == null) return;
            CoachMotionPackage recorded = JsonUtility.FromJson<CoachMotionPackage>(source.text);
            package = BuildPracticePackage(recorded);
        }

        if (package == null)
        {
            Debug.LogError("Hip thrust coach motion is unavailable.");
            return;
        }

        if (!package.IsValid(out string error))
        {
            Debug.LogError($"Hip thrust coach motion is invalid: {error}");
            return;
        }

        SpineFlowTrainingSession.CacheMotionPackage(package);
        SegmentedCoachController coach = FindCoach(scene);
        if (coach != null)
        {
            coach.SetMotionBackend(package);
            // This recording begins supine. Show that preparation pose from
            // the start menu onward, before either avatar is introduced.
            coach.ReturnIdle();
        }
    }

    private static bool IsHipThrust(string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return false;
        if (LumbarCoachMode.ForAction(actionId) != null) return false;
        string normalized = actionId.ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
        return normalized.Contains("hipthrust") || normalized.Contains("glutebridge") ||
            normalized.Contains("bridge") ||
            normalized.Contains("clip26") || normalized.Contains("臀桥");
    }

    private static SegmentedCoachController FindCoach(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            SegmentedCoachController controller =
                root.GetComponentInChildren<SegmentedCoachController>(true);
            if (controller != null) return controller;
        }

        return null;
    }

    private static CoachMotionPackage BuildPracticePackage(CoachMotionPackage recorded)
    {
        if (recorded == null || recorded.segments == null || recorded.segments.Length == 0 ||
            recorded.segments[0].frames == null || recorded.segments[0].frames.Length < 3)
        {
            return null;
        }

        // First complete repetition in 4_臀桥_2, checked against its synced
        // tracker trajectory. Do not pick a maximum over unrelated body motion:
        // the last seconds contain a change of posture, not the top of a bridge.
        CoachMotionFrame[] frames = recorded.segments[0].frames;
        CoachMotionFrame[] up = Slice(frames, 22f, 27f);
        // Trim the long recorded plateau and continue with the real lowering clip.
        CoachMotionFrame[] down = Slice(frames, 36.5f, 40f);
        // The held top pose and the next repetition's preparation pose must be
        // identical on both sides of the segment boundary. Preserve the
        // recorded lowering trajectory while distributing its small endpoint
        // drift across the full segment.
        CoachMotionFrame.AlignSegmentEndpoints(
            down,
            up[up.Length - 1],
            up[0]);
        CoachMotionFrame startFrame = Copy(up[0]);
        CoachMotionFrame endFrame = Copy(down[down.Length - 1]);
        return new CoachMotionPackage
        {
            actionId = "hip_thrust",
            packageId = "clip_26_teaching",
            packageVersion = "2",
            displayName = "臀桥",
            sourceClipName = "clip_26",
            fps = 30f,
            setCount = 1,
            segments = new[]
            {
                Segment("仰卧屈膝准备", new[] { startFrame }, false),
                Segment("舒适范围抬臀", up, true),
                Segment("缓慢回落并休息", down, false),
                Segment("仰卧放松结束", new[] { endFrame }, false)
            }
        };
    }

    private static CoachMotionFrame Copy(CoachMotionFrame source)
    {
        return new CoachMotionFrame
        {
            time = 0f,
            bodyPosition = source.bodyPosition,
            bodyRotation = source.bodyRotation,
            muscles = (float[])source.muscles.Clone()
        };
    }

    private static CoachMotionSegment Segment(
        string label,
        CoachMotionFrame[] frames,
        bool score)
    {
        return new CoachMotionSegment
        {
            label = label,
            sourceStateName = label,
            repeatCount = 1,
            scoreLeniency = 1.5f,
            scoringDuration = 2f,
            applyFormalActionScoreMapping = score,
            poseGuidanceRule = new SegmentPoseGuidanceRule
            {
                mode = PoseGuidanceProfileMode.Explicit,
                activeRegions = score ? PoseGuidanceRegionMask.Torso : PoseGuidanceRegionMask.None,
                stabilizerRegions = PoseGuidanceRegionMask.LeftLeg | PoseGuidanceRegionMask.RightLeg
            },
            frames = frames
        };
    }

    private static CoachMotionFrame[] Slice(CoachMotionFrame[] source, float from, float to)
    {
        var frames = new System.Collections.Generic.List<CoachMotionFrame>();
        foreach (CoachMotionFrame sourceFrame in source)
        {
            if (sourceFrame.time < from || sourceFrame.time > to) continue;
            CoachMotionFrame frame = Copy(sourceFrame);
            frame.time = sourceFrame.time - from;
            frames.Add(frame);
        }
        float offset = frames[0].time;
        foreach (CoachMotionFrame frame in frames) frame.time -= offset;
        return frames.ToArray();
    }
}
