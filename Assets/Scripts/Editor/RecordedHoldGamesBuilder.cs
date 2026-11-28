using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RecordedHoldGamesBuilder
{
    public const string Root="Assets/RecordedHoldGames/";
    public static readonly string[] Names={"BirdDog","DeadBug","Crunch","LyingCat"};
    public static readonly string[] Titles={"鸟狗式","死虫式","卷腹","卧位猫式"};
    public static readonly string[] Keys={"birddog_hold30","deadbug_hold30","crunch_hold30","lyingcat_hold30"};
    public static readonly string[] PrescriptionIds={"8","7","6","1"};
    public static string Scene(int i)=>"Assets/Scenes/"+Names[i]+"_RingBridge_Hold30.unity";

    [MenuItem("Tools/Ring Bridge/Create Four Recorded Hold Games")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
        if(!AssetDatabase.IsValidFolder(Root.TrimEnd('/')))AssetDatabase.CreateFolder("Assets","RecordedHoldGames");
        for(int i=0;i<Names.Length;i++)
        {
            if(File.Exists(Scene(i)))throw new IOException("Preserving existing scene: "+Scene(i));
            string folder=Root+Names[i]+"/";AssetDatabase.CreateFolder(Root.TrimEnd('/'),Names[i]);
            AnimationClip routine,start,wrap;string[] upStates=null;RecordedCoachSceneTools.PhaseMap map=null;
            var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+Names[i]+"_Hold.controller");
            var machine=controller.layers[0].stateMachine;
            int[] boundaries,peaks,lowerStarts;
            if(i<2)
            {
                map=JsonUtility.FromJson<RecordedCoachSceneTools.PhaseMap>(File.ReadAllText(RecordedCoachSceneTools.Root+Names[i]+".phases.json"));
                var clips=RecordedCoachSceneTools.LoadClips(Names[i]);routine=clips[Names[i].ToLowerInvariant()+"_routine"];
                boundaries=map.boundaries.Select(f=>f-map.routineStart).ToArray();peaks=map.peaks.Select(f=>f-map.routineStart).ToArray();lowerStarts=peaks.Select(f=>f+1).ToArray();
                upStates=Enumerable.Range(1,peaks.Length).Select(n=>Names[i].ToLowerInvariant()+"_"+n+"_up").ToArray();
                foreach(string state in upStates)machine.AddState(state).motion=clips[state];
                var frames=JsonUtility.FromJson<CoachMotionPackage>(File.ReadAllText(RecordedCoachSceneTools.Root+Names[i]+".coachrecording")).segments[0].frames;
                start=RecordedCoachMotionImporter.Bake("hold_start",new[]{frames[map.boundaries[0]],frames[map.boundaries[0]]},0,1,30,map.bodyYawDegrees,map.mirrorArmChannels);
                wrap=RecordedCoachMotionImporter.Bake("hold_wrap",new[]{frames[map.boundaries.Last()],frames[map.boundaries[0]]},0,1,2,map.bodyYawDegrees,map.mirrorArmChannels);
            }
            else
            {
                string resource=i==2?RecordedCrunchCoachMotion.ResourcePath:RecordedLyingCatCoachMotion.ResourcePath;
                var package=JsonUtility.FromJson<CoachMotionPackage>(Resources.Load<TextAsset>(resource).text);var frames=package.segments[0].frames;
                int Frame(float seconds)=>Enumerable.Range(0,frames.Length).OrderBy(n=>Mathf.Abs(frames[n].time-seconds)).First();
                int first=Frame(i==2?43:30),top=Frame(i==2?48.5f:34),lower=Frame(i==2?50:36),last=Frame(i==2?52.4f:40);
                routine=RecordedCoachMotionImporter.Bake("hold_routine",frames,first,last,package.fps,0,false);AssetDatabase.CreateAsset(routine,folder+"Routine.anim");
                boundaries=new[]{0,last-first};peaks=new[]{top-first};lowerStarts=new[]{lower-first};
                start=RecordedCoachMotionImporter.Bake("hold_start",new[]{frames[first],frames[first]},0,1,30,0,false);
                wrap=RecordedCoachMotionImporter.Bake("hold_wrap",new[]{frames[last],frames[first]},0,1,2,0,false);
            }
            AssetDatabase.CreateAsset(start,folder+"Start.anim");AssetDatabase.CreateAsset(wrap,folder+"Wrap.anim");
            machine.defaultState=machine.AddState("hold_start");machine.defaultState.motion=start;
            machine.AddState("hold_routine").motion=routine;machine.AddState("hold_wrap").motion=wrap;machine.AddState("Idle").motion=start;
            var timeline=ScriptableObject.CreateInstance<HipThrustPhaseTimeline>();timeline.sourceClip=routine;timeline.repeatStartFrame=boundaries[0];timeline.repeatEndFrame=boundaries.Last();
            timeline.sourceStartFrame=0;timeline.cycles=new HipThrustPhaseTimeline.Cycle[peaks.Length];timeline.recordedHeight=new AnimationCurve();
            for(int n=0;n<peaks.Length;n++)
            {
                timeline.cycles[n]=new HipThrustPhaseTimeline.Cycle{liftStartFrame=boundaries[n],holdStartFrame=peaks[n],lowerStartFrame=lowerStarts[n],lowerEndFrame=boundaries[n+1],lowHeight=0,peakHeight=1};
                timeline.recordedHeight.AddKey(boundaries[n],0);timeline.recordedHeight.AddKey(peaks[n],1);timeline.recordedHeight.AddKey(lowerStarts[n],.999f);
            }
            timeline.recordedHeight.AddKey(boundaries.Last(),0);
            for(int k=0;k<timeline.recordedHeight.length;k++){AnimationUtility.SetKeyLeftTangentMode(timeline.recordedHeight,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(timeline.recordedHeight,k,AnimationUtility.TangentMode.Linear);}
            timeline.Validate();AssetDatabase.CreateAsset(timeline,folder+"Timeline.asset");
            File.WriteAllText(folder+"PreviewPrescription.json","{\"prescriptionContractVersion\":2,\"items\":[{\"itemId\":1,\"actionId\":\""+PrescriptionIds[i]+"\",\"sets\":1,\"reps\":"+(i<2?4:3)+",\"holdSeconds\":30}]}");
            AssetDatabase.ImportAsset(folder+"PreviewPrescription.json",ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            if(!AssetDatabase.CopyAsset(HipThrustNewRulesBuilder.Hold30,Scene(i)))throw new IOException("Scene copy failed");
            var scene=EditorSceneManager.OpenScene(Scene(i));
            T Find<T>() where T:Component=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<T>(true)).Single();
            var game=Find<HipThrustTaikoController>();var entry=Find<GameWorkoutScene>();var rule=Find<HipThrustRuleScoring>();var dose=Find<HipThrustPrescriptionDose>();var data=new SerializedObject(game);
            var coach=(Animator)data.FindProperty("coachAnimator").objectReferenceValue;var preview=(Animator)data.FindProperty("coachPreviewAnimator").objectReferenceValue;
            coach.runtimeAnimatorController=preview.runtimeAnimatorController=controller;PrefabUtility.RecordPrefabInstancePropertyModifications(coach);PrefabUtility.RecordPrefabInstancePropertyModifications(preview);
            data.FindProperty("sourceClip").objectReferenceValue=routine;data.FindProperty("phaseTimeline").objectReferenceValue=timeline;data.FindProperty("hipThrustAnimatorController").objectReferenceValue=controller;
            data.FindProperty("routineStateName").stringValue="hold_routine";data.FindProperty("hipThrustStartStateName").stringValue="hold_start";data.FindProperty("restTransitionStateName").stringValue="hold_wrap";data.FindProperty("actionDisplayName").stringValue=Titles[i];
            var beats=data.FindProperty("sourceBeats");beats.arraySize=peaks.Length;
            for(int n=0;n<peaks.Length;n++){var beat=beats.GetArrayElementAtIndex(n);beat.FindPropertyRelative("upFrame").intValue=peaks[n];beat.FindPropertyRelative("holdEndFrame").intValue=lowerStarts[n];beat.FindPropertyRelative("downFrame").intValue=boundaries[n+1];}
            data.ApplyModifiedPropertiesWithoutUndo();
            var action=game.gameObject.AddComponent<RecordedActionHoldScoring>();action.kind=(RecordedActionHoldScoring.ActionKind)i;action.transport=game;action.calibration=entry.scorer;action.upStates=upStates;
            if(map!=null){action.firstTargets=map.firstTargets.Select(n=>(RhythmBodyTarget)n).ToArray();action.secondTargets=map.secondTargets.Select(n=>(RhythmBodyTarget)n).ToArray();}
            rule.recordedAction=action;dose.prescriptionActionId=PrescriptionIds[i];dose.actionDisplayName=Titles[i];dose.preserveRecordedTiming=false;dose.skipRecordedPlateauAfterHold=i>=2;dose.preserveSourceGaps=true;dose.previewPrescription=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"PreviewPrescription.json");
            entry.actionId=Keys[i];entry.hipThrustSceneOverride="";entry.useRecordedCoachAnimations=true;
            SetString(entry.coach,"actionStateName","hold_start");SetString(entry.coach,"startSegmentState","hold_start");
            foreach(string field in new[]{"simpleActionStateName","standardActionStateName","hardActionStateName"})SetString(entry.menu,field,"hold_start");
            SetString(entry.countdown,"autoStartActionStateName","hold_start");
            var card=Find<ActionPreviewCard>();SetString(card,"actionName",Titles[i]);SetString(card,"previewStateName","hold_start");SetString(card,"segmentedStartStateName","hold_start");SetString(card,"targetSceneName",Path.GetFileNameWithoutExtension(Scene(i)));
            var calibration=entry.scorer.GetComponent<SupineGameCalibration>();calibration.proneBirdDog=i==0||i==3;calibration.referenceStateName="hold_start";calibration.referenceClip=start;calibration.referenceSeconds=0;calibration.leftSideBridge=calibration.rightSideBridge=false;calibration.skipCalibrationForPreview=false;
            calibration.poseInstructionOverride=i==0?"请四点支撑，双手与双膝着地，保持起始姿势":i==1?"请仰卧，双臂抬起、髋膝弯曲，保持教练的准备姿势":i==2?"请仰卧屈膝，双脚着地、上背放松":"请双手双膝支撑，保持教练的起始背部姿势";
            foreach(var text in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)))
            {
                if(text.name=="Title"||text.text.Contains("Hip Thrust"))text.text=Titles[i]+" · 保持30秒";
                else text.text=text.text.Replace("臀桥",Titles[i]);
            }
            dose.menuDescription.text=$"演示处方 1 组 × {(i<2?4:3)} 次 · 每次保持 30 秒";
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Debug.Log("BUILT_HOLD "+Scene(i));
        }
        var settings=EditorBuildSettings.scenes.ToList();for(int i=0;i<Names.Length;i++)if(!settings.Any(s=>s.path==Scene(i)))settings.Add(new EditorBuildSettingsScene(Scene(i),true));
        EditorBuildSettings.scenes=settings.ToArray();AssetDatabase.SaveAssets();
    }
    static void SetString(UnityEngine.Object target,string name,string value){var data=new SerializedObject(target);var field=data.FindProperty(name);if(field==null)throw new Exception("Missing field "+name);field.stringValue=value;data.ApplyModifiedPropertiesWithoutUndo();}
}
