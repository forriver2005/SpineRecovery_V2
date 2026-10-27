using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The common practice UI stays in the gender-specific scenes. Only recorded
// motion, relevant body regions and teaching instructions vary by exercise.
public sealed class LumbarCoachProfile
{
    public string actionId;
    public string displayName;
    public string sceneName;
    public int clip;
    public float from, peak, returnFrom, to;
    public string preparation, instruction, recovery;
    public PoseGuidanceRegionMask active, support;
    public HumanBodyBones[] scoringBones, movementBones;
    public TrackerWearLocation[] targetHapticLocations;
    public HumanBodyBones[][] targetHapticMovementBones;
    public bool positiveHighlightAtTarget;
    public float[] recordedStart, recordedTarget;
}

public static class LumbarCoachMode
{
    private static readonly HumanBodyBones[] Torso =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest
    };
    private static readonly HumanBodyBones[] TrunkMotion =
    {
        HumanBodyBones.Spine, HumanBodyBones.Chest
    };
    private static readonly HumanBodyBones[] Legs =
    {
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg
    };
    private const PoseGuidanceRegionMask BothLegs =
        PoseGuidanceRegionMask.LeftLeg | PoseGuidanceRegionMask.RightLeg;
    private const PoseGuidanceRegionMask BothArms =
        PoseGuidanceRegionMask.LeftArm | PoseGuidanceRegionMask.RightArm;

    public static readonly LumbarCoachProfile[] Profiles =
    {
        new LumbarCoachProfile
        {
            actionId = "cat_camel", displayName = "卧位猫", sceneName = "CatCamelPractice", clip = 23,
            from = 10f, peak = 14.5f, returnFrom = 16f, to = 20f,
            preparation = "舒适四点支撑准备",
            instruction = "按示范缓慢活动背部，小幅跟做，正常呼吸",
            recovery = "缓慢回到舒适支撑位，放松休息",
            active = PoseGuidanceRegionMask.Torso, support = BothLegs | BothArms,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[] { TrunkMotion },
            positiveHighlightAtTarget = true,
            scoringBones = Join(Torso, Legs, new[] { HumanBodyBones.LeftUpperArm,
                HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm }),
            movementBones = TrunkMotion
        },
        new LumbarCoachProfile
        {
            actionId = "side_bridge", displayName = "侧桥", sceneName = "SideBridgePractice", clip = 27,
            from = 14f, peak = 17f, returnFrom = 23f, to = 26f,
            preparation = "侧卧支撑准备，先看清示范侧别",
            instruction = "按录制侧别短暂撑起，正常呼吸；可随时回落",
            recovery = "缓慢放下骨盆，回到侧卧支撑位休息",
            active = PoseGuidanceRegionMask.Torso, support = BothLegs | PoseGuidanceRegionMask.LeftArm,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[]
                { Join(TrunkMotion, new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg }) },
            positiveHighlightAtTarget = true,
            scoringBones = Join(Torso, Legs, new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm }),
            movementBones = Join(TrunkMotion, new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
        },
        new LumbarCoachProfile
        {
            actionId = "curl_up", displayName = "卷腹", sceneName = "CurlUpPractice", clip = 28,
            from = 7f, peak = 10.5f, returnFrom = 12.5f, to = 15.5f,
            preparation = "仰卧屈膝准备，颈部保持舒适",
            instruction = "按示范小幅卷起上背，不拉扯颈部，正常呼吸",
            recovery = "缓慢放下上背，仰卧放松休息",
            active = PoseGuidanceRegionMask.Torso, support = BothLegs,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[] { TrunkMotion },
            positiveHighlightAtTarget = true,
            scoringBones = Join(Torso, Legs), movementBones = TrunkMotion
        },
        new LumbarCoachProfile
        {
            actionId = "knee_flexion", displayName = "屈膝", sceneName = "KneeFlexionPractice", clip = 31,
            from = 19f, peak = 24f, returnFrom = 25f, to = 29f,
            preparation = "仰卧屈膝准备，先看双腿位置",
            instruction = "按示范缓慢屈膝收腿，在舒适范围活动，不强拉",
            recovery = "双腿缓慢回到准备位置，放松休息",
            active = BothLegs, support = PoseGuidanceRegionMask.Torso,
            targetHapticLocations = new[]
                { TrackerWearLocation.LeftLowerLeg, TrackerWearLocation.RightLowerLeg },
            targetHapticMovementBones = new[]
            {
                new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg },
                new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg }
            },
            scoringBones = Join(Torso, Legs), movementBones = Legs
        },
        new LumbarCoachProfile
        {
            actionId = "standing_extension", displayName = "站立位伸展", sceneName = "StandingExtensionPractice", clip = 32,
            from = 2f, peak = 9f, returnFrom = 10f, to = 12f,
            preparation = "舒适站立准备，双脚稳稳支撑",
            instruction = "按示范小幅伸展，保持呼吸，不追求后伸幅度",
            recovery = "缓慢回到自然站姿，放松休息",
            active = PoseGuidanceRegionMask.Torso, support = BothLegs,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[] { TrunkMotion },
            positiveHighlightAtTarget = true,
            scoringBones = Join(Torso, Legs), movementBones = TrunkMotion
        },
        new LumbarCoachProfile
        {
            actionId = "seated_cat_camel", displayName = "坐位", sceneName = "SeatedCatCamelPractice", clip = 24,
            from = 16f, peak = 20f, returnFrom = 24f, to = 27f,
            preparation = "舒适坐稳，按录制位置摆好双腿",
            instruction = "坐稳后按示范轻缓活动背部，小幅跟做，正常呼吸",
            recovery = "缓慢回到舒适坐姿，放松休息",
            active = PoseGuidanceRegionMask.Torso, support = PoseGuidanceRegionMask.None,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[] { TrunkMotion },
            positiveHighlightAtTarget = true,
            scoringBones = Torso, movementBones = TrunkMotion
        },
        new LumbarCoachProfile
        {
            actionId = "standing_cat_camel", displayName = "站位", sceneName = "StandingCatCamelPractice", clip = 25,
            from = 16f, peak = 19f, returnFrom = 22f, to = 26f,
            preparation = "舒适站稳，双脚支撑",
            instruction = "站稳后按示范轻缓活动骨盆与背部，不追求幅度",
            recovery = "缓慢回到舒适站姿，放松休息",
            active = PoseGuidanceRegionMask.Torso, support = PoseGuidanceRegionMask.None,
            targetHapticLocations = new[] { TrackerWearLocation.Abdomen },
            targetHapticMovementBones = new[] { TrunkMotion },
            positiveHighlightAtTarget = true,
            scoringBones = Torso, movementBones = TrunkMotion
        }
    };

    public static LumbarCoachProfile ForScene(string scene)
    {
        foreach (LumbarCoachProfile profile in Profiles)
            if (scene == profile.sceneName || scene == "male" + profile.sceneName) return profile;
        return null;
    }

    public static LumbarCoachProfile ForAction(string actionId)
    {
        string id = (actionId ?? "").ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        foreach (LumbarCoachProfile profile in Profiles)
        {
            string key = profile.actionId.Replace("_", "");
            if (id == key || id == "clip" + profile.clip || id == profile.displayName) return profile;
        }
        if (id == "catcamel" || id == "catcow") return Profiles[0];
        if (id == "sideplank" || id == "sidebridge") return Profiles[1];
        if (id == "curlup" || id == "crunch") return Profiles[2];
        if (id == "seatedcatcamel" || id == "seatedcatcow" || id == "坐位猫式") return Profiles[5];
        if (id == "standingcatcamel" || id == "standingcatcow" || id == "站位猫式") return Profiles[6];
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LumbarCoachProfile profile = ForScene(scene.name);
        if (profile == null) return;
        TextAsset source = Resources.Load<TextAsset>("ActionLibrary/Motions/clip_" + profile.clip);
        CoachMotionPackage package = source != null
            ? BuildPackage(profile, JsonUtility.FromJson<CoachMotionPackage>(source.text)) : null;
        if (package == null || !package.IsValid(out _))
        {
            Debug.LogError($"{profile.displayName}教练录制不可用。");
            return;
        }
        SpineFlowTrainingSession.CacheMotionPackage(package);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            SegmentedCoachController coach = root.GetComponentInChildren<SegmentedCoachController>(true);
            if (coach != null)
            {
                coach.SetMotionBackend(package);
                coach.ReturnIdle();
            }
            PoseScorer scorer = root.GetComponentInChildren<PoseScorer>(true);
            if (scorer != null) scorer.ConfigureLumbarTeaching(profile);
        }
    }

    public static CoachMotionPackage BuildPackage(LumbarCoachProfile profile, CoachMotionPackage recorded)
    {
        if (recorded?.segments == null || recorded.segments.Length == 0) return null;
        CoachMotionFrame[] up = Slice(recorded.segments[0].frames, profile.from, profile.peak);
        CoachMotionFrame[] down = Slice(recorded.segments[0].frames, profile.returnFrom, profile.to);
        if (up.Length < 2 || down.Length < 2) return null;
        CoachMotionFrame.AlignSegmentEndpoints(
            down,
            up[up.Length - 1],
            up[0]);
        profile.recordedStart = up[0].muscles;
        profile.recordedTarget = up[up.Length - 1].muscles;
        return new CoachMotionPackage
        {
            actionId = profile.actionId, displayName = profile.displayName,
            packageId = "clip_" + profile.clip + "_teaching", packageVersion = "1",
            sourceClipName = recorded.sourceClipName, fps = recorded.fps, setCount = 1,
            segments = new[]
            {
                Segment(profile, profile.preparation, new[] { Copy(up[0], 0f) }, false),
                Segment(profile, profile.instruction, up, true),
                Segment(profile, profile.recovery, down, false),
                Segment(profile, "舒适放松结束，可暂停或结束练习", new[] { Copy(down[down.Length - 1], 0f) }, false)
            }
        };
    }

    private static CoachMotionSegment Segment(LumbarCoachProfile profile, string label, CoachMotionFrame[] frames, bool core)
    {
        return new CoachMotionSegment
        {
            label = label, sourceStateName = label, repeatCount = 1,
            scoreLeniency = 1.8f, scoringDuration = 2f, applyFormalActionScoreMapping = core,
            poseGuidanceRule = new SegmentPoseGuidanceRule
            {
                mode = PoseGuidanceProfileMode.Explicit,
                activeRegions = core ? profile.active : PoseGuidanceRegionMask.None,
                stabilizerRegions = core ? profile.support : profile.support | profile.active
            },
            frames = frames
        };
    }

    private static CoachMotionFrame[] Slice(CoachMotionFrame[] source, float from, float to)
    {
        var result = new List<CoachMotionFrame>();
        float firstTime = 0f;
        foreach (CoachMotionFrame frame in source)
        {
            if (frame.time >= from && frame.time <= to)
            {
                if (result.Count == 0) firstTime = frame.time;
                result.Add(Copy(frame, frame.time - firstTime));
            }
        }
        return result.ToArray();
    }

    private static CoachMotionFrame Copy(CoachMotionFrame frame, float time) => new CoachMotionFrame
    {
        time = time, bodyPosition = frame.bodyPosition, bodyRotation = frame.bodyRotation,
        muscles = (float[])frame.muscles.Clone()
    };

    private static HumanBodyBones[] Join(params HumanBodyBones[][] lists)
    {
        var result = new List<HumanBodyBones>();
        foreach (HumanBodyBones[] list in lists) result.AddRange(list);
        return result.ToArray();
    }
}

// Relative movement on the exercise's selected joints, projected onto the
// recorded direction. This is teaching evidence, not a clinical ROM measure.
public sealed class LumbarTeachingEvidence : IDisposable
{
    private readonly LumbarCoachProfile profile;
    private readonly List<int> muscleIndices = new List<int>();
    private readonly Dictionary<TrackerWearLocation, List<int>> hapticMuscleIndices =
        new Dictionary<TrackerWearLocation, List<int>>();
    private HumanPoseHandler handler;
    private HumanPose pose;
    private float[] baseline;
    public bool HasBaseline => baseline != null;

    public LumbarTeachingEvidence(LumbarCoachProfile profile)
    {
        this.profile = profile;
        foreach (HumanBodyBones bone in profile.movementBones)
            for (int axis = 0; axis < 3; axis++)
            {
                int index = HumanTrait.MuscleFromBone((int)bone, axis);
                if (index >= 0) muscleIndices.Add(index);
            }

        if (profile.targetHapticLocations == null) return;
        for (int locationIndex = 0;
             locationIndex < profile.targetHapticLocations.Length;
             locationIndex++)
        {
            HumanBodyBones[] targetBones =
                profile.targetHapticMovementBones != null &&
                locationIndex < profile.targetHapticMovementBones.Length
                    ? profile.targetHapticMovementBones[locationIndex]
                    : profile.movementBones;
            var indices = new List<int>();
            foreach (HumanBodyBones bone in targetBones)
                for (int axis = 0; axis < 3; axis++)
                {
                    int index = HumanTrait.MuscleFromBone((int)bone, axis);
                    if (index >= 0) indices.Add(index);
                }
            hapticMuscleIndices[profile.targetHapticLocations[locationIndex]] =
                indices;
        }
    }

    public bool Sample(Animator user, Animator coach, out float progress)
    {
        progress = 0f;
        if (!HipThrustPoseGuidance.TryCapture(user, out HipThrustPoseGeometry current) ||
            !HipThrustPoseGuidance.TryCapture(coach, out HipThrustPoseGeometry target) ||
            Mathf.Abs(current.torsoTilt - target.torsoTilt) > 35f ||
            Mathf.Abs(current.pelvisTilt - target.pelvisTilt) > 35f ||
            Mathf.Abs(current.hipFlexion - target.hipFlexion) > 60f) return false;
        if (!TryReadPose(user)) return false;
        if (HasBaseline) progress = ProjectProgress(pose.muscles, baseline,
            profile.recordedStart, profile.recordedTarget, muscleIndices);
        return true;
    }

    public bool TryGetProgress(Animator user, out float progress)
    {
        return TryGetProgress(user, muscleIndices, out progress);
    }

    public bool TryGetProgress(
        Animator user,
        TrackerWearLocation location,
        out float progress)
    {
        progress = 0f;
        return hapticMuscleIndices.TryGetValue(
                location,
                out List<int> indices) &&
            TryGetProgress(user, indices, out progress);
    }

    private bool TryGetProgress(
        Animator user,
        IReadOnlyList<int> indices,
        out float progress)
    {
        progress = 0f;
        if (!HasBaseline || profile.recordedStart == null ||
            profile.recordedTarget == null || !TryReadPose(user)) return false;
        progress = ProjectProgress(pose.muscles, baseline,
            profile.recordedStart, profile.recordedTarget, indices);
        return !float.IsNaN(progress) && !float.IsInfinity(progress);
    }

    private bool TryReadPose(Animator user)
    {
        if (user == null || !user.isHuman || user.avatar == null) return false;
        if (handler == null)
        {
            handler = new HumanPoseHandler(user.avatar, user.transform);
            pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
        }
        handler.GetHumanPose(ref pose);
        return pose.muscles != null && pose.muscles.Length >= HumanTrait.MuscleCount;
    }

    public void CaptureBaseline() => baseline = (float[])pose.muscles.Clone();

    public static float ProjectProgress(float[] current, float[] rest, float[] start,
        float[] target, IReadOnlyList<int> indices)
    {
        float dot = 0f, length = 0f, movement = 0f;
        foreach (int i in indices)
        {
            float direction = target[i] - start[i];
            float delta = current[i] - rest[i];
            dot += direction * delta;
            length += direction * direction;
            movement += delta * delta;
        }
        // Reject very small/noisy motion instead of counting a static pose.
        return length > 0.0001f && movement >= 0.0025f ? dot / length : 0f;
    }

    public void Reset() => baseline = null;
    public void Dispose() => handler?.Dispose();
}
