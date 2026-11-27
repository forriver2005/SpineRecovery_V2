using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RecordedHoldGamesVerification
{
    const string Flag="RecordedHoldGamesVerification",Output="output/four-hold-games/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static int index,stage,checks;static double began,pauseAt,frozen;
    static GameWorkoutScene entry;static HipThrustTaikoController game;static HipThrustRingBridgeView view;
    static Animator patient;static AvatarMotionSource input;static bool calibrationChecked;static double calibrationStarted;
    static RecordedHoldGamesVerification(){if(SessionState.GetBool(Flag,false)){index=SessionState.GetInt("HoldStartIndex",0);began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
    [MenuItem("Tools/Ring Bridge/Verify Four Recorded Hold Games")]
    public static void Run(){SessionState.SetInt("HoldStartIndex",0);StartRun();}
    static void StartRun()
    {
        Directory.CreateDirectory(Output);Directory.CreateDirectory("output/prescribed-hold");File.WriteAllText(Output+"verification.txt","Four new ring hold scenes. Recorded coach, synthetic patient input, no wearable validation.\n");
        stage=checks=0;calibrationChecked=false;index=SessionState.GetInt("HoldStartIndex",0);began=EditorApplication.timeSinceStartup;EditorSceneManager.OpenScene(RecordedHoldGamesBuilder.Scene(index));SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    public static void RunDeadBug(){SessionState.SetInt("HoldStartIndex",1);StartRun();}
    public static void RunTorso(){SessionState.SetInt("HoldStartIndex",2);StartRun();}
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Near(double a,double b,string message,double epsilon=.02)=>Check(Math.Abs(a-b)<epsilon,message+$" {a} != {b}");
    static void Log(string value){File.AppendAllText(Output+"verification.txt",value+"\n");Debug.Log(value);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-began<480,"New hold verification timeout");
            var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null||current.gameObject.scene.name!=Path.GetFileNameWithoutExtension(RecordedHoldGamesBuilder.Scene(index)))return;
            entry=current;game=entry.taiko;view=UnityEngine.Object.FindObjectOfType<HipThrustRingBridgeView>();if(view==null||entry.GetComponent<TrainingResultView>()?.Root==null)return;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
            foreach(var h in UnityEngine.Object.FindObjectsOfType<RhythmHitHapticAdapter>())h.enabled=false;
            foreach(var h in UnityEngine.Object.FindObjectsOfType<CoachHapticFeedbackController>())h.enabled=false;
            if(stage==0&&!calibrationChecked)
            {
                patient=entry.scorer.UserAnimator;input=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();input.Configure(patient);
                entry.StopTraining();entry.scorer.StopAllCoroutines();Set(entry.scorer,"calibrated",false);Set(entry.scorer,"calibrating",false);
                var c=entry.scorer.GetComponent<SupineGameCalibration>();Check(c!=null&&c.Prepare(game.CoachAnimator),"Calibration reference missing");
                using(var from=new HumanPoseHandler(game.CoachAnimator.avatar,game.CoachAnimator.transform))using(var to=new HumanPoseHandler(patient.avatar,patient.transform))
                {var pose=new HumanPose();from.GetHumanPose(ref pose);to.SetHumanPose(ref pose);}
                input.ReportFrame(Time.unscaledTime,55);if(!c.IsPoseReady(patient))
                {
                    var hips=patient.GetBoneTransform(HumanBodyBones.Hips);var chest=patient.GetBoneTransform(HumanBodyBones.Chest);var knee=patient.GetBoneTransform(HumanBodyBones.LeftLowerLeg);var thigh=patient.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    Log($"CALIBRATION DIAG {RecordedHoldGamesBuilder.Names[index]} hips={hips.position} chest={chest.position} knee={knee.position} torsoDot={Mathf.Abs(Vector3.Dot((chest.position-hips.position).normalized,Vector3.up))} kneeHeight={knee.position.y-hips.position.y} leg={Vector3.Distance(thigh.position,knee.position)}");
                }
                Check(c.IsPoseReady(patient),"Recorded preparation does not satisfy calibration posture");
                entry.scorer.BeginCalibration();calibrationStarted=EditorApplication.timeSinceStartup;stage=5;return;
            }
            if(stage==5)
            {
                using(var from=new HumanPoseHandler(game.CoachAnimator.avatar,game.CoachAnimator.transform))using(var to=new HumanPoseHandler(patient.avatar,patient.transform))
                {var pose=new HumanPose();from.GetHumanPose(ref pose);to.SetHumanPose(ref pose);}
                input.ReportFrame(Time.unscaledTime,55);Check(EditorApplication.timeSinceStartup-calibrationStarted<15,"Actual calibration did not finish");
                if(!entry.scorer.IsCalibrated)return;
                Log("PASS "+RecordedHoldGamesBuilder.Names[index]+": actual posture calibration coroutine using fresh synthetic bone frames.");calibrationChecked=true;stage=0;return;
            }
            if(stage==0)
            {
                patient=entry.scorer.UserAnimator;entry.scorer.StopAllCoroutines();Set(entry.scorer,"calibrated",false);input=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();input.Configure(patient);input.SetAvailable(false);
                entry.menu.StartTraining();Check(!game.IsRunning&&entry.menu.IsTrainingMenuVisible,"No-input Start bypassed calibration");entry.scorer.StopAllCoroutines();
                Set(entry.scorer,"calibrated",true);Set(entry.scorer,"calibrating",false);entry.menu.SelectSimpleDifficulty();
                foreach(float duration in new[]{20f,45f,0f})
                {
                    entry.StopTraining();string json="{\"prescriptionContractVersion\":2,\"items\":[{\"itemId\":22,\"actionId\":\""+RecordedHoldGamesBuilder.PrescriptionIds[index]+"\",\"sets\":1,\"reps\":2,\"holdSeconds\":"+duration+"}]}";
                    Check(game.PrescriptionDose.SetPrescriptionJson(json,22,out string error),error);float goal=duration==0?30:duration;
                    Check(game.PrescriptionDose.HoldSeconds==goal,"D01 hold not applied");
                    Replay("follow",30);var score=entry.GetComponent<GameTrainingAssessment>().FinalScore;
                    Log($"{RecordedHoldGamesBuilder.Names[index]} {goal}s: score={score.ExactScore:0.00}, hold={score.ValidHold:0.000}/{score.TargetHold:0.000}, coverage={score.Coverage:0.000}, continuous={score.ContinuousCompleted}/2");
                    Check(score.Kind==FinalTrainingScore.Mode.TimedHold&&score.CanPublish,"Wrong score mode or data coverage");Near(score.TargetHold,2*goal,"Wrong hold denominator");Check(score.Score>=90,"Correct recording not accepted");
                }
                entry.StopTraining();entry.menu.SelectStandardDifficulty();Check(entry.PrepareDose(new GameWorkoutAction{action=entry.actionId,sets=2,reps=2,holdSeconds=5},out _),"Workout dose failed");Check(game.PrescriptionDose.Total==2,"Executor multiplied sets twice");
                foreach(string scenario in new[]{"rest","half","missing","gap","wrong"})
                {
                    Replay(scenario,60);var score=entry.GetComponent<GameTrainingAssessment>().FinalScore;Log($"{RecordedHoldGamesBuilder.Names[index]} {scenario}: score={score.Score}, hold={score.ValidHold:0.00}/{score.TargetHold:0.00}, publish={score.CanPublish}, continuous={score.ContinuousCompleted}");
                    if(scenario=="missing")Check(!score.CanPublish&&score.ValidHold==0,"Missing input earned hold score");
                    else if(scenario=="rest"||scenario=="wrong")Check(score.Score<10,"Wrong/rest posture scored as hold");
                    else if(scenario=="half")Check(score.Score<75,"Half amplitude counted as a full hold");
                    else Check(score.ValidHold>2&&score.ContinuousCompleted==0,"Gap erased previous hold or fabricated continuity");
                }
                entry.StopTraining();Check(game.PrescriptionDose.SetDose(1,2,20,out _),"UI dose failed");Capture("menu");
                stage=6;pauseAt=EditorApplication.timeSinceStartup;return;
            }
            if(stage==6)
            {
                // Let Unity finish the long deterministic replay frame before measuring wall-clock countdown.
                if(EditorApplication.timeSinceStartup-pauseAt<.5)return;
                Set(entry.scorer,"calibrated",true);input.ReportFrame(0,55);entry.menu.StartTraining();Check(!entry.menu.IsTrainingMenuVisible,"Real Start failed");
                Check(!game.PrescriptionDose.SetDose(1,1,45,out _),"Countdown allowed duration edit");stage=1;pauseAt=EditorApplication.timeSinceStartup;return;
            }
            if(stage==1)
            {
                input.ReportFrame(Time.unscaledTime,55);
                if(!game.IsRunning)return;
                Check(EditorApplication.timeSinceStartup-pauseAt>=4.8,"5-second countdown skipped");
                var rep=game.AssessmentRepetitions()[0];Seek(rep.top+10);Near(view.CurrentSample.progress,.5,"Half hold ring mismatch");
                game.TogglePause();frozen=game.Elapsed;pauseAt=EditorApplication.timeSinceStartup;stage=2;return;
            }
            if(stage==2&&EditorApplication.timeSinceStartup-pauseAt>.3)
            {
                Near(game.Elapsed,frozen,"Pause moved clock");
                foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if(bone==HumanBodyBones.LastBone)continue;
                    var a=game.CoachAnimator.GetBoneTransform(bone);var b=game.PreviewAnimator.GetBoneTransform(bone);
                    if(a!=null&&b!=null)Check(Quaternion.Angle(a.localRotation,b.localRotation)<.2f,"Coach/preview mismatch "+bone);
                }
                Capture("pause-menu");
                var navigation=entry.GetComponent<GameTestMenu>();navigation.Panel.SetActive(false);
                Capture("hold");game.TogglePause();navigation.Close();
                Replay("follow",30);var result=entry.GetComponent<TrainingResultView>();result.Refresh();
                ((GameObject)Get(entry.menu,"menuRoot")).SetActive(false);((GameObject)Get(entry.menu,"endMenuRoot")).SetActive(true);Call(result,"LateUpdate");
                Check(result.Root.activeInHierarchy,"No shared result panel");Capture("result");result.Root.GetComponent<TrainingResultPanel>().ReturnButton.onClick.Invoke();Check(entry.menu.IsTrainingMenuVisible&&!result.Root.activeSelf,"Result return failed");
                entry.StopTraining();Set(entry.scorer,"calibrated",true);input.ReportFrame(0,55);entry.menu.StartTraining();entry.StopTraining();pauseAt=EditorApplication.timeSinceStartup;stage=3;return;
            }
            if(stage==3&&EditorApplication.timeSinceStartup-pauseAt>5.3)
            {
                Check(!game.IsRunning&&entry.menu.IsTrainingMenuVisible,"Cancelled countdown restarted later");Log("PASS "+RecordedHoldGamesBuilder.Names[index]+": real start/countdown, pause, shared result, return, countdown cancellation.");
                index++;if(index==4){Log("PASS all new holds, checks="+checks);Finish(0);}else{stage=0;calibrationChecked=false;SceneManager.LoadScene(Path.GetFileNameWithoutExtension(RecordedHoldGamesBuilder.Scene(index)));}
            }
        }
        catch(Exception ex){Log("FAIL "+ex);Debug.LogException(ex);Finish(1);}
    }
    static void Replay(string scenario,int fps)
    {
        ((GameObject)Get(entry.menu,"menuRoot")).SetActive(false);((GameObject)Get(entry.menu,"gamingCanvasRoot")).SetActive(true);Set(entry.scorer,"calibrated",true);game.StartMode();
        var assessment=entry.GetComponent<GameTrainingAssessment>();Check(game.RuleScoring.TargetsReady,"Action references unavailable");
        var coach=game.CoachAnimator;var original=patient.transform.rotation;
        using(var from=new HumanPoseHandler(coach.avatar,coach.transform))using(var to=new HumanPoseHandler(patient.avatar,patient.transform))
        {
            Seek(game.AssessmentRepetitions()[0].start);var rest=new HumanPose();from.GetHumanPose(ref rest);rest.muscles=(float[])rest.muscles.Clone();
            for(int frame=1;frame<=Math.Ceiling(game.Duration*fps);frame++)
            {
                double now=Math.Min(game.Duration,frame/(double)fps);Seek(now);var pose=new HumanPose();from.GetHumanPose(ref pose);
                if(scenario=="rest")pose=rest;
                if(scenario=="half")
                {
                    for(int m=0;m<pose.muscles.Length;m++)pose.muscles[m]=Mathf.Lerp(rest.muscles[m],pose.muscles[m],.5f);
                    pose.bodyPosition=Vector3.Lerp(rest.bodyPosition,pose.bodyPosition,.5f);pose.bodyRotation=Quaternion.Slerp(rest.bodyRotation,pose.bodyRotation,.5f);
                }
                if(scenario=="wrong")for(int m=0;m<pose.muscles.Length;m++)
                {
                    string name=HumanTrait.MuscleName[m];
                    if(index<2&&name.StartsWith("Left")&&(name.Contains("Arm")||name.Contains("Forearm")||name.Contains("Shoulder"))){int other=Array.IndexOf(HumanTrait.MuscleName,"Right"+name.Substring(4));if(other>=0){float a=pose.muscles[m];pose.muscles[m]=pose.muscles[other];pose.muscles[other]=a;}}
                    else if(index>=2&&(name.Contains("Spine")||name.Contains("Chest")))pose.muscles[m]=rest.muscles[m];
                }
                if(scenario=="wrong"&&index>=2)pose.bodyRotation=rest.bodyRotation;
                to.SetHumanPose(ref pose);input.ReportFrame((float)now,55);
                var reps=game.AssessmentRepetitions();int ri=0;for(int j=1;j<reps.Length&&now>=reps[j].start;j++)ri=j;var rep=reps[ri];double middle=(rep.top+rep.lower)*.5;
                if(scenario=="missing"||(scenario=="gap"&&now>=middle&&now<middle+.3))input.SetAvailable(false);
                game.RuleScoring.Tick(1f/fps);assessment.ObserveBridge(1f/fps);

            }
        }
        patient.transform.rotation=original;assessment.Finish();game.StopMode();
    }
    static void Seek(double time){Set(game,"elapsed",time);Call(game,"RenderTimeline");Call(view,"LateUpdate");UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true).LateUpdate();}
    static void Capture(string label)
    {
        var t=typeof(PrescribedHoldVerification);var flags=BindingFlags.Static|BindingFlags.NonPublic;t.GetField("entry",flags).SetValue(null,entry);t.GetField("game",flags).SetValue(null,game);t.GetField("view",flags).SetValue(null,view);
        string name=RecordedHoldGamesBuilder.Names[index]+"-"+label;t.GetMethod("Capture",flags).Invoke(null,new object[]{name,1688,950});File.Copy("output/prescribed-hold/"+name+".png",Output+name+".png",true);
    }
    static object Get(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
    static object Call(object o,string n)=>o.GetType().GetMethod(n,P).Invoke(o,null);
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
