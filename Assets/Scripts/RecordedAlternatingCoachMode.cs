using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Formal practice uses the same recording as the action library. Source assets
// stay intact; these excerpts define complete repetitions, not whole recordings.
public static class RecordedAlternatingCoachMode
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public static bool IsRecordedScene(string name) => name == "DeadBugRecordedPractice" || name == "BirdDogRecordedPractice";

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool deadBug = scene.name == "DeadBugRecordedPractice";
        bool birdDog = scene.name == "BirdDogRecordedPractice";
        if (!deadBug && !birdDog) return;
        var source = Resources.Load<TextAsset>(deadBug ? "ActionLibrary/Motions/clip_29" : "CoachMotions/BirdDogRecordedSides");
        if (source == null) return;
        var package = Build(JsonUtility.FromJson<CoachMotionPackage>(source.text), deadBug);
        foreach (var root in scene.GetRootGameObjects())
        {
            var coach = root.GetComponentInChildren<SegmentedCoachController>(true);
            if (coach == null) continue;
            coach.SetMotionBackend(package);
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                var scorer = sceneRoot.GetComponentInChildren<PoseScorer>(true);
                if (scorer != null)
                    scorer.gameObject.AddComponent<RecordedPracticePoseGuidance>().Initialize(coach.CoachAnimator, package);
            }
            coach.ReturnIdle();
            break;
        }
    }

    public static CoachMotionPackage Build(CoachMotionPackage source, bool deadBug)
    {
        var frames = source.segments[0].frames;
        var first = Slice(frames, deadBug ? 6.5f : 20f, deadBug ? 10f : 23f);
        var firstReturn = Slice(frames, deadBug ? 10f : 23f, deadBug ? 12.5f : 26.5f);
        var second = Slice(frames, deadBug ? 17.3f : 12f, deadBug ? 19.4f : 15.4f);
        var secondReturn = Slice(frames, deadBug ? 19.4f : 15.4f, deadBug ? 21.5f : 19.6f);
        // Retain recorded timing. Remove drift only at joins between excerpts.
        CoachMotionFrame.AlignSegmentEndpoints(firstReturn, first[first.Length-1], first[0]);
        CoachMotionFrame.AlignSegmentEndpoints(second, first[0], second[second.Length-1]);
        CoachMotionFrame.AlignSegmentEndpoints(secondReturn, second[second.Length-1], first[0]);
        return new CoachMotionPackage
        {
            actionId = deadBug ? "dead_bug" : "bird_dog",
            packageId = deadBug ? "clip_29_practice" : "clip_30_practice_side_mapping", packageVersion = "1",
            displayName = deadBug ? "死虫" : "鸟狗", sourceClipName = source.sourceClipName, fps = source.fps,
            segments = new[] {
                Segment(deadBug ? "仰卧屈膝，手臂举起" : "四点支撑准备", new[]{Copy(first[0],0)}, PoseGuidanceRegionMask.None),
                Segment(deadBug ? "右臂和左腿放下" : "右臂和左腿伸展", first, PoseGuidanceRegionMask.RightArm | PoseGuidanceRegionMask.LeftLeg),
                Segment(deadBug ? "手脚收回" : "收回支撑", firstReturn, PoseGuidanceRegionMask.None),
                Segment(deadBug ? "左臂和右腿放下" : "左臂和右腿伸展", second, PoseGuidanceRegionMask.LeftArm | PoseGuidanceRegionMask.RightLeg),
                Segment(deadBug ? "手脚收回" : "收回支撑", secondReturn, PoseGuidanceRegionMask.None),
                Segment("放松结束", new[]{Copy(first[0],0)}, PoseGuidanceRegionMask.None)
            }
        };
    }

    private static CoachMotionSegment Segment(string label, CoachMotionFrame[] frames, PoseGuidanceRegionMask targets)
    {
        return new CoachMotionSegment {
            label = label, sourceStateName = label, frames = frames,
            // Keep the existing formal five-second hold, not the source file's
            // full-recording scoringDuration, which is not a clinical hold.
            scoringDuration = 5f, scoreLeniency = 3f,
            poseGuidanceRule = new SegmentPoseGuidanceRule {
                mode = targets == PoseGuidanceRegionMask.None ? PoseGuidanceProfileMode.Disabled : PoseGuidanceProfileMode.Explicit,
                activeRegions = targets, stabilizerRegions = PoseGuidanceRegionMask.Torso
            }
        };
    }

    private static CoachMotionFrame Copy(CoachMotionFrame source, float time)
    {
        return new CoachMotionFrame { time=time, bodyPosition=source.bodyPosition,
            bodyRotation=source.bodyRotation, muscles=(float[])source.muscles.Clone() };
    }

    private static CoachMotionFrame[] Slice(CoachMotionFrame[] frames, float from, float to)
    {
        var result = new List<CoachMotionFrame>();
        foreach (var frame in frames)
            if (frame.time >= from && frame.time <= to) result.Add(Copy(frame,frame.time-from));
        float offset=result[0].time;
        foreach(var frame in result) frame.time-=offset;
        return result.ToArray();
    }
}
