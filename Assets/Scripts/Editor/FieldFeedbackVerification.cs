using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FieldFeedbackVerification
{
    const string Flag="FieldFeedbackVerification",Output="output/field-fixes-20261128/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Scenes={"Crunch_RingBridge_Hold30","LyingCat_RingBridge_Hold30","BirdDog_RingBridge_Hold30","DeadBug_RingBridge_Hold30","HipThrust_RingBridge_Hold30","CrunchPractice_TaikoNight"};
    static GameWorkoutScene entry;static HipThrustTaikoController game;static HipThrustRingBridgeView view;static Animator patient,coach;static AvatarMotionSource input;
    static int index,stage,checks;static double began,at;static HumanPose neutral;static HumanPoseHandler from,to;
    static FieldFeedbackVerification(){if(SessionState.GetBool(Flag,false)){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
    public static void Run()
    {
        Directory.CreateDirectory(Output);Directory.CreateDirectory("output/prescribed-hold");Directory.CreateDirectory("output/final-score-v2");File.WriteAllText(Output+"verification.txt","Field feedback regression: real callbacks + UI, synthetic patient frames.\n");
        EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[0]+".unity");SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Log(string message){File.AppendAllText(Output+"verification.txt",message+"\n");Debug.Log(message);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-began<300,"Field test timeout");
            var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null||current.gameObject.scene.name!=Scenes[index])return;
            entry=current;game=entry.taiko;view=UnityEngine.Object.FindObjectOfType<HipThrustRingBridgeView>();
            if(game!=null&&entry.GetComponent<TrainingResultView>()?.Root==null)return;
            if(entry.taikoNightAction!=null&&entry.taikoNightAction.Preparation==null)return;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<RhythmHitHapticAdapter>())r.enabled=false;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<CoachHapticFeedbackController>())r.enabled=false;
            if(stage==0)
            {
                patient=game!=null?entry.scorer.UserAnimator:entry.taikoNightAction.recorder.UserAnimator;coach=game!=null?game.CoachAnimator:entry.taikoNightAction.recorder.CoachAnimator;
                input=UnityEngine.Object.FindObjectsOfType<AvatarMotionSource>().First(m=>m.Model==patient.gameObject);
                from=new HumanPoseHandler(coach.avatar,coach.transform);to=new HumanPoseHandler(patient.avatar,patient.transform);
                if(game!=null)
                {
                    entry.StopTraining();entry.scorer.StopAllCoroutines();Set(entry.scorer,"calibrated",false);Set(entry.scorer,"calibrating",false);
                    var prep=entry.scorer.GetComponent<SupineGameCalibration>();Check(prep.Prepare(coach),"Prepare failed");
                }
                from.GetHumanPose(ref neutral);neutral.muscles=(float[])neutral.muscles.Clone();
                if(index==0||index==5)
                {
                    // Deliberately different arms from the recorded hands-by-head position.
                    for(int i=0;i<neutral.muscles.Length;i++)if(HumanTrait.MuscleName[i].Contains("Arm Down-Up"))neutral.muscles[i]=-.6f;
                }
                if(index==1)
                {
                    neutral.muscles[Array.IndexOf(HumanTrait.MuscleName,"Spine Front-Back")]=-.3f;
                    neutral.muscles[Array.IndexOf(HumanTrait.MuscleName,"Chest Front-Back")]=-.15f;
                }
                ApplyNeutral();
                if(index==0||index==1||index==5)
                {
                    bool Accept()=>index==1?RelaxedPreparation.IsFourPoint(patient):RelaxedPreparation.IsSupineBentKnee(patient);
                    Check(Accept(),"Relaxed pose rejected");var original=patient.transform.rotation;
                    patient.transform.rotation=Quaternion.AngleAxis(90,Vector3.forward)*original;
                    Check(!Accept(),"Upright posture accepted as floor preparation");patient.transform.rotation=original;ApplyNeutral();
                }
                input.SetAvailable(false);
                if(game!=null)
                {
                    if(index<4){var controls=entry.scorer.GetComponent<HoldCalibrationControls>();if(controls?.StatusLabel==null)return;entry.scorer.BeginCalibration();Check(entry.scorer.IsCalibrating,"Automatic calibration coroutine did not start");}
                    else entry.scorer.BeginCalibration();
                }
                else entry.taikoNightAction.Preparation.Begin();
                at=EditorApplication.timeSinceStartup;stage=1;return;
            }
            if(stage==1)
            {
                ApplyNeutral();input.SetAvailable(false);if(EditorApplication.timeSinceStartup-at<.6)return;
                Check(game!=null?!entry.scorer.IsCalibrated:!entry.taikoNightAction.Preparation.Ready,"No-input calibration accepted");at=EditorApplication.timeSinceStartup;stage=2;return;
            }
            if(stage==2)
            {
                ApplyNeutral();input.ReportFrame(Time.unscaledTime,55);
                bool ready=game!=null?entry.scorer.IsCalibrated:entry.taikoNightAction.Preparation.Ready;
                Check(EditorApplication.timeSinceStartup-at<12,"Relaxed preparation cannot calibrate: "+Scenes[index]+" status="+(game!=null?entry.scorer.CalibrationInstruction:entry.taikoNightAction.CalibrationStatus));
                if(!ready)return;
                at=EditorApplication.timeSinceStartup;stage=8;return;
            }
            if(stage==8)
            {
                ApplyNeutral();input.ReportFrame(Time.unscaledTime,55);
                if(EditorApplication.timeSinceStartup-at<.3)return;
                Check(!UnityEngine.Object.FindObjectsOfType<Button>(true).Any(b=>b.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text.Contains("重新校准")||t.text.Contains("开始姿势校准"))),"Unwanted calibration button remains");
                if(game!=null)Capture("calibrated-menu");else CaptureDynamic();
                Log("PASS "+Scenes[index]+": no-data rejected, actual relaxed-pose calibration completed, visible automatic-calibration status.");
                if(game==null)
                {
                    var hidden=(DeadBugGamingPoseScorer)Get(entry.menu,"poseScorer");if(hidden!=null)Set(hidden,"calibrated",false);
                    var selection=Get(entry.taikoNightAction,"selection");var start=(Button)Get(selection,"start");Check(start.interactable,"Visible dynamic start remains disabled after calibration");start.onClick.Invoke();Check(entry.taikoNightAction.IsCountingDown,"Hidden legacy scorer blocked external game Start");at=EditorApplication.timeSinceStartup;stage=6;return;
                }
                Check(game.PrescriptionDose.SetDose(1,2,20,out _),"Dose failed");Check(entry.menu.TrainingStartButton.interactable,"Visible hold Start remains disabled");entry.menu.TrainingStartButton.onClick.Invoke();at=EditorApplication.timeSinceStartup;stage=3;return;
            }
            if(stage==3)
            {
                ApplyNeutral();input.ReportFrame(Time.unscaledTime,55);if(!game.IsRunning)return;
                Check(EditorApplication.timeSinceStartup-at>=4.7,"Countdown skipped");ReplayAndCheck();at=EditorApplication.timeSinceStartup;stage=4;return;
            }
            if(stage==4)
            {
                if(EditorApplication.timeSinceStartup-at<.2)return;
                Capture("hold");game.StopMode();entry.StopTraining();
                Check(view.HoldMetricText.Contains("0.0")||!view.artwork.activeSelf,"Return retained live panel");
                Log("PASS "+Scenes[index]+": target/current repetition/cumulative valid/continuous/qualified count shown; signal loss and reset covered.");Next();return;
            }
            if(stage==6)
            {
                ApplyNeutral();input.ReportFrame(Time.unscaledTime,55);
                if(!entry.taikoNightAction.IsRunning)return;
                Check(EditorApplication.timeSinceStartup-at>=4.7,"Dynamic crunch countdown skipped");entry.StopTraining();Check(entry.taikoNightAction.IsMenuVisible,"Dynamic crunch cannot return");
                Log("PASS dynamic crunch: external Start ignores hidden legacy calibration, actual countdown and return work.");Next();
            }
        }
        catch(Exception ex){Log("FAIL "+ex);Debug.LogException(ex);Finish(1);}
    }
    static void ReplayAndCheck()
    {
        var rep=game.AssessmentRepetitions()[0];var assessment=entry.GetComponent<GameTrainingAssessment>();
        // Real sampled patient poses through existing rule adapters, not injected UI metrics.
        game.StartMode();for(int f=1;f<=(rep.top+10)*60;f++)
        {
            double now=f/60d;Set(game,"elapsed",now);Call(game,"RenderTimeline");var pose=new HumanPose();from.GetHumanPose(ref pose);to.SetHumanPose(ref pose);input.ReportFrame((float)now,55);game.RuleScoring.Tick(1f/60);assessment.ObserveBridge(1f/60);
        }
        Call(view,"LateUpdate");Check(view.HoldTargetText.Contains("20")&&view.HoldTargetText.Contains("1/2"),"Missing target and repetition");Check(view.HoldMetricText.Contains("累计有效")&&view.HoldMetricText.Contains("20"),"Missing valid hold UI");Check(view.ContinuousMetricText.Contains("当前连续"),"Missing continuous UI");
        Check(assessment.FinalScore.ValidHold>8,"Correct pose did not earn hold time");
        if(index==1)
        {
            var source=JsonUtility.FromJson<CoachMotionPackage>(Resources.Load<TextAsset>(RecordedLyingCatCoachMotion.ResourcePath).text);int spine=Array.IndexOf(HumanTrait.MuscleName,"Spine Front-Back");
            var pose=new HumanPose();from.GetHumanPose(ref pose);Check(pose.muscles[spine]<-.5f,"Cat hold target is still concave");
            var cow=source.segments[0].frames.OrderBy(x=>Math.Abs(x.time-29.5)).First();var wrong=new HumanPose();cow.ApplyTo(ref wrong);
            double held=assessment.FinalScore.ValidHold;for(int i=1;i<=60;i++){Set(game,"elapsed",rep.top+10+i/60d);Call(game,"RenderTimeline");to.SetHumanPose(ref wrong);input.ReportFrame(i,55);game.RuleScoring.Tick(1f/60);assessment.ObserveBridge(1f/60);}
            Check(assessment.FinalScore.ValidHold-held<.1,"Old concave target still credited");Log("PASS cat direction: rounded-back frame accepted; previous head-up/concave frame rejected.");
        }
        input.SetAvailable(false);game.RuleScoring.Tick(.02f);Call(view,"LateUpdate");Check(view.ContinuousMetricText=="等待动作数据","Missing signal status");
        input.ReportFrame(1,55);Set(game,"elapsed",rep.top+10);Call(game,"RenderTimeline");var finalPose=new HumanPose();from.GetHumanPose(ref finalPose);to.SetHumanPose(ref finalPose);game.RuleScoring.Tick(.02f);
        game.TogglePause();var navigation=entry.GetComponent<GameTestMenu>();if(navigation!=null){navigation.enabled=false;navigation.Panel.SetActive(false);}Call(view,"LateUpdate");
    }
    static void CaptureDynamic()
    {
        string name="CrunchPractice-calibrated-menu";FinalScoringV2Verification.Capture(name);
        File.Copy("output/final-score-v2/"+name+".png",Output+name+".png",true);
    }
    static void ApplyNeutral()=>to.SetHumanPose(ref neutral);
    static void Capture(string label)
    {
        var t=typeof(PrescribedHoldVerification);var flags=BindingFlags.Static|BindingFlags.NonPublic;t.GetField("entry",flags).SetValue(null,entry);t.GetField("game",flags).SetValue(null,game);t.GetField("view",flags).SetValue(null,view);
        string name=Scenes[index]+"-"+label;t.GetMethod("Capture",flags).Invoke(null,new object[]{name,1688,950});File.Copy("output/prescribed-hold/"+name+".png",Output+name+".png",true);
    }
    static void Next(){from.Dispose();to.Dispose();from=to=null;index++;if(index==Scenes.Length){Log("PASS all field feedback cases, checks="+checks);Finish(0);}else{stage=0;SceneManager.LoadScene(Scenes[index]);}}
    static object Get(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
    static object Call(object o,string n)=>o.GetType().GetMethod(n,P).Invoke(o,null);
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;from?.Dispose();to?.Dispose();Time.timeScale=1;EditorApplication.Exit(code);}
}
