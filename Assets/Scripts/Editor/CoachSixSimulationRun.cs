using System;
using System.IO;
using System.Reflection;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicitly requested local simulation acceptance. Not included in player builds.
[InitializeOnLoad]
public static class CoachSixSimulationRun
{
    static readonly string[] Scenes={"DeadBugPractice","BirdDogPractice","HipThrustPractice","CatCamelPractice","SideBridgePractice","CurlUpPractice"};
    static readonly string[] Actions={"deadbug","birddog","hipthrust","catcamel","sidebridge","curlup"};
    static readonly float[] Times={10f,23f,27,14.5f,17,10.5f};
    static readonly float[] PreparationTimes={6.5f,20f,22,10,14,7};
    static readonly float[] ReturnTimes={12.5f,26.5f,40,20,26,15.5f};
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static double nextSample,readyAt,lessonCaptureAfter;
    static bool started;
    static string lastControl;
    static bool replayRequested;
    static string delayedCaptureName;
    static double delayedCaptureAt;
    static readonly System.Collections.Generic.HashSet<string> capturedFrames =
        new System.Collections.Generic.HashSet<string>();
    static string CaptureDirectory => Path.GetFullPath(".codex/coach-flow-screenshots");
    static CoachSixSimulationRun(){EditorApplication.update+=Tick;}
    static object Field(object obj,string name){if(obj==null)return null;for(var t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags);if(f!=null)return f.GetValue(obj);}return null;}
    static string Collection(object value){var items=new System.Collections.Generic.List<string>();var list=value as IEnumerable;if(list!=null)foreach(var x in list)items.Add(x.ToString());return string.Join(";",items);}
    static void Tick()
    {
        if(EditorApplication.isCompiling || !File.Exists(".codex/run-six-simulation"))return;
        if (delayedCaptureName != null &&
            EditorApplication.timeSinceStartup >= delayedCaptureAt)
        {
            string captureName = delayedCaptureName;
            delayedCaptureName = null;
            CaptureFrame(captureName);
        }
        if(File.Exists(".codex/resume-six-index")){SessionState.SetInt("CoachSix.index",int.Parse(File.ReadAllText(".codex/resume-six-index")));File.Delete(".codex/resume-six-index");}
        int index=SessionState.GetInt("CoachSix.index",0);
        bool original=File.Exists(".codex/sim-original-courses");
        Scenes[0]="DeadBugPractice";
        Scenes[1]="BirdDogPractice";
        if(index>=Scenes.Length){File.Delete(".codex/run-six-simulation");File.AppendAllText("Tools/CoachSimulator/six-run.log","ALL_SCENES_FINISHED\n");SessionState.EraseInt("CoachSix.index");return;}
        if(!EditorApplication.isPlaying)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);if(!scene.isDirty)continue;
                Directory.CreateDirectory(".codex/scene-backups");
                if(!EditorSceneManager.SaveScene(scene,".codex/scene-backups/"+scene.name+"_sim_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true))throw new Exception("Cannot preserve unsaved scene");
            }
            if(SceneManager.GetActiveScene().name!=Scenes[index])EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[index]+".unity");
            started=false;lastControl=null;readyAt=EditorApplication.timeSinceStartup+4;
            EditorApplication.isPlaying=true;return;
        }
        var session=UnityEngine.Object.FindObjectOfType<PracticeSessionController>();
        var coach=UnityEngine.Object.FindObjectOfType<SegmentedCoachController>();
        if(session==null || coach==null)return;
        if(!started)
        {
            if(EditorApplication.timeSinceStartup<readyAt)return;
            started=true;
            lessonCaptureAfter=EditorApplication.timeSinceStartup+2.0;
            if(File.Exists(".codex/sim-visual-review") && index==4)
                typeof(PracticeSessionController).GetMethod("SelectOrientation",Flags).Invoke(session,new object[]{PracticeSessionController.CoachOrientationMode.AlignUserToCoach});
            session.StartPractice();
            File.AppendAllText("Tools/CoachSimulator/six-run.log","START "+Scenes[index]+" "+DateTime.Now.ToString("o")+"\n");
        }
        int rep=coach.CurrentRepeat;
        float sampleTime=coach.CurrentSegmentKind==TrainingSegmentKind.CoreAction?Times[index]:coach.CurrentSegmentKind==TrainingSegmentKind.ReturnTransition?ReturnTimes[index]:PreparationTimes[index];
        if(rep%2==1 && index<2 && coach.CurrentSegmentKind==TrainingSegmentKind.CoreAction)sampleTime=index==0?19.4f:15.4f;
        if(rep%2==1 && index<2 && coach.CurrentSegmentKind==TrainingSegmentKind.ReturnTransition)sampleTime=index==0?21.5f:19.6f;
        // Rep 3 loses device validity; rep 4 returns valid input but motor requests fail.
        string selectedAction = index == 0 && File.Exists(".codex/sim-deadbug-all-limb")
            ? "deadbug_all_limb"
            : index == 1 && File.Exists(".codex/sim-positive-input")
                ? "birddog_positive"
                : Actions[index];
        string control="{\"action\":\""+selectedAction+"\",\"time\":"+sampleTime.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"yaw\":"+(rep==1?70:0)+",\"stream\":true,\"connected\":"+(rep==2?"false":"true")+",\"status\":\"OK\",\"motorFail\":"+(rep==3?"true":"false")+",\"stop\":false}";
        if(original && index<2) {
            var segments=Field(coach,"segments") as System.Collections.Generic.List<ActionSegment>;
            string action="original_"+segments[coach.CurrentSegmentIndex].clipStateName;
            control=control.Replace("\"action\":\""+Actions[index]+"\"","\"action\":\""+action+"\"");
        }
        if(control!=lastControl){File.WriteAllText("Tools/CoachSimulator/control.json",control);lastControl=control;}

        CaptureRepresentativeFrames(index, Scenes[index], session, coach);
        if(EditorApplication.timeSinceStartup>nextSample)
        {
            nextSample=EditorApplication.timeSinceStartup+1;
            var scorer=UnityEngine.Object.FindObjectOfType<PoseScorer>();
            var evidence=Field(scorer,"lumbarTeachingEvidence") as LumbarTeachingEvidence;
            float progress=0;bool hasProgress=evidence!=null && evidence.TryGetProgress(Field(scorer,"userAnimator") as Animator,out progress);
            string text=DateTime.Now.ToString("o")+" "+Scenes[index]+" rep="+rep+" completed="+coach.CompletedRepetitions+" phase="+coach.CurrentSegmentKind+" hold="+coach.IsWaitingForNext+" running="+Field(session,"practiceRunning")+" green="+Collection(Field(scorer,"guidancePartsToHighlight"))+" lumbarGreen="+Field(scorer,"lumbarPositiveHighlightConfirmed")+" fresh="+Field(scorer,"guidanceInputIsFresh")+" baseline="+(evidence!=null && evidence.HasBaseline)+" progress="+(hasProgress?progress.ToString("F3"):"unknown")+" valid="+Collection(Field(scorer,"guidancePartValidity"))+" clear="+Collection(Field(scorer,"guidancePartShouldClear"));
            if(File.Exists(".codex/pause-on-diagonal-green")) {
                var highlighter=Field(scorer,"modelBodyPartHighlighter") as AvatarBodyPartHighlighter;
                if(highlighter!=null && highlighter.IsPartVisible(BodyPart.RightArm) && highlighter.IsPartVisible(BodyPart.LeftLeg)) {
                    File.Delete(".codex/pause-on-diagonal-green");EditorApplication.isPaused=true;
                }
            }
            var recorder=UnityEngine.Object.FindObjectOfType<MotionRecorder>();
            if(recorder!=null && recorder.UserAnimator!=null && recorder.CoachAnimator!=null){object[] headings={recorder.UserAnimator,recorder.CoachAnimator,Vector3.up,Vector3.zero,Vector3.zero};
                var method=typeof(PracticeSessionController).GetMethod("TryGetComparableHeadings",BindingFlags.Static|BindingFlags.NonPublic);
                if((bool)method.Invoke(null,headings))text+=" displayYawError="+Vector3.SignedAngle((Vector3)headings[3],(Vector3)headings[4],Vector3.up).ToString("F2");
                text+=" mode="+Field(session,"orientationMode")+" coachRoot="+recorder.CoachAnimator.transform.eulerAngles.y.ToString("F2");}
            var receiver=UnityEngine.Object.FindObjectOfType<EVMC4U.ExternalReceiver>();
            var limit=UnityEngine.Object.FindObjectOfType<AvatarJointLimitFilter>();
            string flags="";
            if(limit!=null)foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones))){if(bone==HumanBodyBones.LastBone)continue;var f=limit.GetFlags(bone);if((int)f!=0)flags+=bone+":"+f+";";}
            File.AppendAllText("Tools/CoachSimulator/six-run.log",text+" hard="+(receiver!=null && receiver.HasHardPoseConstraint)+" constraints="+flags+"\n");
        }
        var menu=UnityEngine.Object.FindObjectOfType<DeadBugStartMenuController>(true);
        bool completionVisible = (bool?)Field(menu,"practiceCompletionChoicesVisible")==true;
        bool completionDone = coach.IsComplete && coach.CompletedRepetitions>=coach.TotalRepetitions && (bool?)Field(session,"practiceRunning")==false;
        if (File.Exists(".codex/capture-coach-flow") && (completionVisible || completionDone))
        {
            CaptureFrame((Scenes[index] == "DeadBugPractice" ? "deadbug" : Scenes[index].ToLowerInvariant()) +
                (replayRequested ? "-replay-completion-choices" : "-completion-choices"));
        }
        if (File.Exists(".codex/capture-replay") && index == 0 &&
            (completionVisible || completionDone) && !replayRequested)
        {
            replayRequested = true;
            PracticeSceneController.RestartPractice();
            started = false;
            lastControl = null;
            lessonCaptureAfter = EditorApplication.timeSinceStartup + 5.0;
            readyAt = EditorApplication.timeSinceStartup + 4.0;
            return;
        }
        if(completionVisible || completionDone)
        {
            File.AppendAllText("Tools/CoachSimulator/six-run.log","COMPLETE "+Scenes[index]+" reps="+coach.CompletedRepetitions+"\n");
            SessionState.SetInt("CoachSix.index",(File.Exists(".codex/sim-only-current") || index==1 && File.Exists(".codex/sim-recorded-only"))?6:File.Exists(".codex/sim-visual-review") && index==2?4:index+1);EditorApplication.isPlaying=false;
        }
    }

    private static void CaptureRepresentativeFrames(
        int index,
        string sceneName,
        PracticeSessionController session,
        SegmentedCoachController coach)
    {
        if (!File.Exists(".codex/capture-coach-flow") || !EditorApplication.isPlaying)
        {
            return;
        }

        bool formal = Field(session, "trainingPlaybackStarted") is bool trainingStarted && trainingStarted;
        string prefix = index == 0 ? "deadbug" : index == 1 ? "birddog" : sceneName.ToLowerInvariant();
        if (!formal && EditorApplication.timeSinceStartup >= lessonCaptureAfter)
        {
            CaptureFrame(prefix + (replayRequested
                ? "-replay-preparation-no-lesson"
                : "-lesson-coach-technique"));
            return;
        }

        if (coach.CurrentRepeat == 0 && coach.CurrentSegmentKind == TrainingSegmentKind.Preparation)
        {
            CaptureFrame(prefix + "-preparation-countdown");
        }
        else if (coach.CurrentRepeat < 2 && coach.CurrentSegmentKind == TrainingSegmentKind.CoreAction)
        {
            CaptureFrame(prefix + "-guided-" + (coach.CurrentRepeat + 1) + "-of-4");
            CaptureGreenFrameIfPresent(prefix);
        }
        else if (coach.CurrentRepeat >= 2 && coach.CurrentSegmentKind == TrainingSegmentKind.CoreAction)
        {
            CaptureFrame(prefix + "-autonomous-" + (coach.CurrentRepeat + 1) + "-of-4");
            CaptureGreenFrameIfPresent(prefix);
            if (coach.IsHoldingAtSegment && coach.CurrentHoldRemaining <= 3.2f)
            {
                CaptureFrameLater(prefix + "-hold-3-2-1");
            }
        }
        else if (coach.CurrentSegmentKind == TrainingSegmentKind.ReturnTransition)
        {
            object highlights = Field(UnityEngine.Object.FindObjectOfType<PoseScorer>(), "guidancePartsToHighlight");
            string suffix = highlights is IEnumerable && ((IEnumerable)highlights).GetEnumerator().MoveNext()
                ? "green" : "return-cleared";
            CaptureFrame(prefix + "-" + suffix);
        }

        if (coach.IsComplete || (bool?)Field(session, "coachResultFinalized") == true)
        {
            CaptureFrame(prefix + "-completion-choices");
        }
    }

    private static void CaptureGreenFrameIfPresent(string prefix)
    {
        object highlights = Field(UnityEngine.Object.FindObjectOfType<PoseScorer>(), "guidancePartsToHighlight");
        if (!(highlights is IEnumerable))
        {
            return;
        }

        IEnumerator enumerator = ((IEnumerable)highlights).GetEnumerator();
        try
        {
            int count = 0;
            if (enumerator.MoveNext())
            {
                count++;
                while (enumerator.MoveNext()) count++;
                CaptureFrame(prefix + (count >= 4
                    ? "-all-limb-success"
                    : "-green-highlight"));
            }
        }
        finally
        {
            (enumerator as System.IDisposable)?.Dispose();
        }
    }

    private static void CaptureFrame(string name)
    {
        if (!capturedFrames.Add(name))
        {
            return;
        }

        Directory.CreateDirectory(CaptureDirectory);
        string path = Path.Combine(CaptureDirectory, "unity-sim-current-" + name + ".png");
        ScreenCapture.CaptureScreenshot(path, 1);
        File.AppendAllText(
            Path.Combine(CaptureDirectory, "capture-log.txt"),
            DateTime.Now.ToString("o") + " " + path + Environment.NewLine);
    }

    private static void CaptureFrameLater(string name)
    {
        if (capturedFrames.Contains(name) || delayedCaptureName != null)
        {
            return;
        }

        delayedCaptureName = name;
        delayedCaptureAt = EditorApplication.timeSinceStartup + 0.15;
    }
}
