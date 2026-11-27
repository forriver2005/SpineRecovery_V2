using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds right-support copies of the two delivered left side-bridge games.</summary>
public static class SideBridgeRightGameBuilder
{
    const string Root = "Assets/Movements/RecordedSideBridge/";
    const string Recording = Root + "SideBridgeRight.coachrecording";
    const string PhaseMap = Root + "SideBridgeRight.phases.json";
    const string ControllerPath = Root + "SideBridgeRight_Recorded.controller";
    const string TimelinePath = Root + "SideBridgeRightTimeline.asset";
    const string Dynamic = "Assets/Scenes/SideBridgeRight_RingBridge_Dynamic.unity";
    const string Hold30 = "Assets/Scenes/SideBridgeRight_RingBridge_Hold30.unity";

    [MenuItem("Tools/Ring Bridge/Create Right Side Bridge Games")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        // Rebuild is intentionally idempotent so a failed validation never leaves a
        // half-configured copied scene blocking the next editor run.
        if (File.Exists(Dynamic)) AssetDatabase.DeleteAsset(Dynamic);
        if (File.Exists(Hold30)) AssetDatabase.DeleteAsset(Hold30);
        if (!File.Exists(SideBridgeGameBuilder.Dynamic) || !File.Exists(SideBridgeGameBuilder.Hold30))
            throw new IOException("Build the left side bridge scenes first.");

        EnsureSource();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Recording, ImportAssetOptions.ForceSynchronousImport);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Recording).OfType<AnimationClip>().ToDictionary(c => c.name);
        if (clips.Count != 9 || clips.Values.Any(c => !c.humanMotion))
            throw new Exception("Right side bridge HumanPose clips missing.");

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        foreach (var clip in clips.Values)
        {
            var state = machine.AddState(clip.name);
            state.motion = clip;
            if (clip.name == "sidebridgeright_start") machine.defaultState = state;
        }
        machine.AddState("Idle").motion = clips["sidebridgeright_start"];
        var frames = JsonUtility.FromJson<CoachMotionPackage>(File.ReadAllText(Recording)).segments[0].frames;
        var wrap = RecordedCoachMotionImporter.Bake(
            "sidebridgeright_wrap", new[] { frames[1459], frames[435] }, 0, 1,
            1f / HipThrustHold30Timeline.WrapSeconds, 0, false, true);
        AssetDatabase.CreateAsset(wrap, Root + "SideBridgeRight_Wrap.anim");
        machine.AddState("sidebridgeright_wrap").motion = wrap;
        AssetDatabase.SaveAssets();

        HipThrustPhaseTimeline timeline = null;
        foreach (string path in new[] { Dynamic, Hold30 })
        {
            string source = path == Dynamic ? SideBridgeGameBuilder.Dynamic : SideBridgeGameBuilder.Hold30;
            if (!AssetDatabase.CopyAsset(source, path)) throw new IOException("Scene copy failed: " + source);
            var scene = EditorSceneManager.OpenScene(path);
            T Find<T>() where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
            var game = Find<HipThrustTaikoController>();
            var entry = Find<GameWorkoutScene>();
            var data = new SerializedObject(game);
            var coach = (Animator)data.FindProperty("coachAnimator").objectReferenceValue;
            var preview = (Animator)data.FindProperty("coachPreviewAnimator").objectReferenceValue;
            coach.runtimeAnimatorController = preview.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(coach);
            EditorUtility.SetDirty(preview);
            if (timeline == null)
            {
                timeline = BakeTimeline(coach, clips["sidebridgeright_routine"], "sidebridgeright_routine");
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }
            data.FindProperty("sourceClip").objectReferenceValue = clips["sidebridgeright_routine"];
            data.FindProperty("phaseTimeline").objectReferenceValue = timeline;
            data.FindProperty("hipThrustAnimatorController").objectReferenceValue = controller;
            data.FindProperty("routineStateName").stringValue = "sidebridgeright_routine";
            data.FindProperty("hipThrustStartStateName").stringValue = "sidebridgeright_start";
            data.FindProperty("actionDisplayName").stringValue = "右侧桥";
            data.FindProperty("restTransitionStateName").stringValue = "sidebridgeright_wrap";
            var sourceBeats = data.FindProperty("sourceBeats");
            sourceBeats.arraySize = timeline.cycles.Length;
            for (int i = 0; i < timeline.cycles.Length; i++)
            {
                var beat = sourceBeats.GetArrayElementAtIndex(i);
                beat.FindPropertyRelative("upFrame").intValue = timeline.cycles[i].holdStartFrame;
                beat.FindPropertyRelative("holdEndFrame").intValue = timeline.cycles[i].lowerStartFrame;
                beat.FindPropertyRelative("downFrame").intValue = timeline.cycles[i].lowerEndFrame;
            }
            data.ApplyModifiedPropertiesWithoutUndo();

            var side = Find<SideBridgePoseScoring>();
            side.rightSupport = true;
            EditorUtility.SetDirty(side);
            var rules = Find<HipThrustRuleScoring>();
            rules.sideBridge = side;
            EditorUtility.SetDirty(rules);

            var dose = Find<HipThrustPrescriptionDose>();
            var doseData = new SerializedObject(dose);
            doseData.FindProperty("transport").objectReferenceValue = game;
            doseData.FindProperty("menu").objectReferenceValue = entry.menu;
            doseData.FindProperty("prescriptionActionId").stringValue = "right_side_bridge";
            doseData.FindProperty("actionDisplayName").stringValue = "右侧桥";
            doseData.FindProperty("preserveRecordedTiming").boolValue = path == Dynamic;
            doseData.FindProperty("skipRecordedPlateauAfterHold").boolValue = path == Hold30;
            doseData.FindProperty("preserveSourceGaps").boolValue = true;
            doseData.FindProperty("previewPrescription").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "SideBridgeRight_PreviewPrescription.json");
            doseData.FindProperty("menuDescription").objectReferenceValue =
                scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).First(t => t.name == "Description");
            doseData.ApplyModifiedPropertiesWithoutUndo();

            entry.actionId = path == Dynamic ? "rightsidebridge" : "rightsidebridge_hold30";
            entry.hipThrustSceneOverride = "";
            entry.useRecordedCoachAnimations = true;
            SetString(entry.coach, "actionStateName", "sidebridgeright_start");
            foreach (string key in new[] { "simpleActionStateName", "standardActionStateName", "hardActionStateName" })
                SetString(entry.menu, key, "sidebridgeright_start");
            SetString(entry.countdown, "autoStartActionStateName", "sidebridgeright_start");
            var card = Find<ActionPreviewCard>();
            SetString(card, "actionName", "右侧桥");
            SetString(card, "previewStateName", "sidebridgeright_start");
            SetString(card, "segmentedStartStateName", "sidebridgeright_start");
            SetString(card, "targetSceneName", Path.GetFileNameWithoutExtension(path));
            var calibration = entry.scorer.GetComponent<SupineGameCalibration>();
            calibration.leftSideBridge = false;
            calibration.rightSideBridge = true;
            calibration.referenceClip = clips["sidebridgeright_routine"];
            calibration.referenceSeconds = timeline.cycles[0].liftStartFrame / 30f;
            var sideData = new SerializedObject(side);
            sideData.FindProperty("rightSupport").boolValue = true;
            sideData.ApplyModifiedPropertiesWithoutUndo();
            int peakFrame = HipThrustHold30Timeline.PeakFrame(timeline, 0);
            coach.Play("sidebridgeright_routine", 0, peakFrame / 30f / timeline.sourceClip.length);
            coach.Update(0f);
            if (!SideBridgePoseGeometry.TryMeasure(coach, true, out var rightPose) || !rightPose.IsRightSupport)
                throw new InvalidOperationException("Right-side coach reference is not a right-support pose.");
            side.Begin("standard");
            if (!side.CaptureTargets(game)) throw new InvalidOperationException("Right-side scoring targets could not be captured.");
            foreach (var text in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)))
                text.text = text.text.Replace("左侧桥", "右侧桥").Replace("Hip Thrust 臀桥", path == Dynamic ? "右侧桥 · 动态" : "右侧桥 · 保持30秒").Replace("臀桥", "右侧桥");
            dose.menuDescription.text = "演示处方 1 组 × 3 次 · " + (path == Dynamic ? "录制节奏" : "保持 30 秒");
            var song = new SerializedObject(Find<RhythmSongClock>());
            song.FindProperty("song").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/GameMusic/SideBridge_EnchantedTiki.ogg");
            song.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        // Preserve the project's existing build settings entries verbatim; this
        // builder only appends the two new right-side scenes.
        var builds = EditorBuildSettings.scenes.ToList();
        foreach (var path in new[] { Dynamic, Hold30 })
        {
            builds.RemoveAll(s => s.path == path);
            builds.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = builds.ToArray();
        Debug.Log("RIGHT_SIDE_BRIDGE_CREATED " + Dynamic + " and " + Hold30);
    }

    [MenuItem("Tools/Ring Bridge/Validate BirdDog Prone Calibration")]
    public static void ValidateBirdDogProneCalibration()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/BirdDog_RingSkinC_Recorded.unity");
        var scorer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DeadBugGamingPoseScorer>(true)).Single();
        var calibration = scorer.GetComponent<SupineGameCalibration>();
        if (calibration == null || !calibration.proneBirdDog) throw new InvalidOperationException("Bird-dog is not configured for prone calibration.");
        if (!calibration.Prepare(scorer.CoachAnimator))
            throw new InvalidOperationException("Bird-dog coach could not enter the quadruped calibration state.");
        if (!calibration.IsPoseReady(scorer.CoachAnimator))
            throw new InvalidOperationException("Bird-dog coach does not satisfy the quadruped calibration posture.");
        calibration.Release();
        Debug.Log("BIRDDOG_PRONE_CALIBRATION_PASS");
    }

    static void EnsureSource()
    {
        if (!File.Exists(Recording)) File.Copy(Root + "SideBridge.coachrecording", Recording);
        File.WriteAllText(PhaseMap, "{\n  \"action\": \"SideBridgeRight\",\n  \"source\": \"SideBridge.coachrecording\",\n  \"fps\": 30,\n  \"bodyYawDegrees\": 0,\n  \"mirrorArmChannels\": false,\n  \"mirrorSideChannels\": true,\n  \"routineStart\": 330,\n  \"routineEnd\": 1470,\n  \"boundaries\": [344, 734, 1190, 1459],\n  \"peaks\": [486, 984, 1259],\n  \"repeatStart\": 14,\n  \"repeatEnd\": 1129\n}\n");
        File.WriteAllText(Root + "SideBridgeRight_PreviewPrescription.json", "{\n  \"prescriptionContractVersion\": 2,\n  \"items\": [{\"itemId\": 1, \"actionId\": \"right_side_bridge\", \"sets\": 1, \"reps\": 3}]\n}\n");
    }

    static void SetString(UnityEngine.Object target, string name, string value)
    {
        var data = new SerializedObject(target);
        var property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException(target.GetType().Name + " lacks " + name);
        property.stringValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static HipThrustPhaseTimeline BakeTimeline(Animator source, AnimationClip clip, string routineStateName)
    {
        var timeline = ScriptableObject.CreateInstance<HipThrustPhaseTimeline>();
        timeline.sourceClip = clip;
        timeline.sourceStartFrame = 330;
        timeline.repeatStartFrame = 14;
        timeline.repeatEndFrame = 1129;
        var host = UnityEngine.Object.Instantiate(source.gameObject);
        host.transform.SetParent(null);
        host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        host.transform.localScale = Vector3.one;
        foreach (var script in host.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        host.SetActive(true);
        var animator = host.GetComponent<Animator>();
        animator.enabled = true;
        animator.Rebind();
        animator.Update(0);
        var keys = new Keyframe[Mathf.RoundToInt(clip.length * clip.frameRate) + 1];
        try
        {
            for (int f = 0; f < keys.Length; f++)
            {
                animator.Play(routineStateName, 0, f / (float)(keys.Length - 1));
                animator.Update(0);
                if (!SideBridgePoseGeometry.TryMeasure(animator, true, out var pose)) throw new Exception("Invalid right side bridge skeleton");
                keys[f] = new Keyframe(f, pose.lift);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
        timeline.recordedHeight = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(timeline.recordedHeight, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(timeline.recordedHeight, i, AnimationUtility.TangentMode.Linear);
        }
        int[,] raw = { { 435, 480, 695, 734 }, { 880, 950, 1085, 1190 }, { 1215, 1259, 1410, 1459 } };
        timeline.cycles = new HipThrustPhaseTimeline.Cycle[3];
        for (int i = 0; i < 3; i++)
        {
            int start = raw[i, 0] - 330, hold = raw[i, 1] - 330, lower = raw[i, 2] - 330, end = raw[i, 3] - 330;
            float high = Enumerable.Range(hold, lower - hold + 1).Max(f => keys[f].value);
            timeline.cycles[i] = new HipThrustPhaseTimeline.Cycle { liftStartFrame = start, holdStartFrame = hold, lowerStartFrame = lower, lowerEndFrame = end, lowHeight = keys[start].value, peakHeight = high };
        }
        timeline.Validate();
        return timeline;
    }
}
