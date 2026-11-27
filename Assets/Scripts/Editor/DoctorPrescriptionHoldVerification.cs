using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Feeds D01 JSON at the public consumer boundary; does not emulate a doctor UI or network transport.</summary>
[InitializeOnLoad]
public static class DoctorPrescriptionHoldVerification
{
    const string Flag="DoctorPrescriptionHoldVerification.Active",Output="output/doctor-prescription-hold/";
    const BindingFlags P=BindingFlags.NonPublic|BindingFlags.Instance;
    const long ItemId=2026112701;
    static readonly string[] Scenes={"HipThrust_RingBridge_Hold30","SideBridge_RingBridge_Hold30","SideBridgeRight_RingBridge_Hold30"};
    static readonly string[] Prefix={"hip","left","right"},Cases={"20s","45s","default","zero"};
    static readonly float[] Goals={20,45,30,30};
    static GameWorkoutScene entry;
    static HipThrustTaikoController game;
    static HipThrustRingBridgeView view;
    static int sceneIndex,stage,liveCase,checks,lastPhase;
    static double started,caseStarted,holdEntered,holdExited,lastLiveTime,pauseAt,frozen;
    static bool tenObserved,pausedOnce,phaseEndObserved;
    static Quaternion[] peak;
    static DoctorPrescriptionHoldVerification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
    [MenuItem("Tools/Ring Bridge/Simulate Doctor Hold Prescriptions")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output+"verification.txt","D01 JSON -> SetPrescriptionJson -> real game timeline / UI. No doctor frontend, server or Android Intent delivery simulated.\n");
        File.WriteAllText(Output+"countdown.csv","mode,scene,prescription,holdElapsedSeconds,expectedRemaining,displayedText,ringProgress,targetSeconds\n");
        try
        {
            var mobileType=typeof(SpineFlowTrainingSession).GetNestedType("PlannedAction",BindingFlags.NonPublic);
            Log("AUDIT legacy mobile PlannedAction.holdSeconds exists="+(mobileType.GetField("holdSeconds",BindingFlags.Public|BindingFlags.Instance)!=null)+"; direct D01 consumer is the test boundary.");
            foreach(string name in Scenes)
            {
                string path="Assets/Scenes/"+name+".unity";
                if(!EditorBuildSettings.scenes.Any(s=>s.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
            }
            sceneIndex=stage=liveCase=checks=0;started=EditorApplication.timeSinceStartup;
            EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[0]+".unity");SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        catch(Exception ex){Fail(ex);}
    }
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Near(double actual,double expected,string message,double tolerance=.001)=>Check(Math.Abs(actual-expected)<tolerance,$"{message}: {actual} != {expected}");
    static void Log(string text){File.AppendAllText(Output+"verification.txt",text+"\n");Debug.Log(text);}
    static string Json(int scene,int scenario)=>File.ReadAllText(Output+"fixtures/"+Prefix[scene]+"-"+Cases[scenario]+".json");
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-started<360,"Doctor simulation timed out at stage "+stage);
            var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null||current.gameObject.scene.name!=Scenes[sceneIndex])return;
            entry=current;game=entry.taiko;view=UnityEngine.Object.FindObjectOfType<HipThrustRingBridgeView>();
            if(view==null||entry.GetComponent<TrainingResultView>()?.Root==null)return;
            SupplySyntheticInput();
            if(stage==0)
            {
                DeterministicCases();sceneIndex++;
                if(sceneIndex<Scenes.Length)SceneManager.LoadScene(Scenes[sceneIndex]);
                else{sceneIndex=0;stage=1;SceneManager.LoadScene(Scenes[0]);}
                return;
            }
            if(stage==1){BeginLiveCase();return;}
            Check(EditorApplication.timeSinceStartup-caseStarted<100,"Live case stalled");
            if(stage==2)
            {
                if(!game.IsRunning)return;
                Near(game.Hold30Timeline.PrescribedHoldSeconds,Goals[liveCase],"Actual Start lost D01 duration");
                Check(game.BeatCount==1,"Actual Start lost exact reps");lastPhase=-1;lastLiveTime=0;holdEntered=holdExited=0;tenObserved=pausedOnce=phaseEndObserved=false;stage=3;
            }
            if(stage==4)
            {
                Near(game.Elapsed,frozen,"Pause moved the real clock");Render();CheckVisible("live-paused",Math.Max(0,frozen-game.AssessmentRepetitions()[0].top),Goals[liveCase]);
                if(EditorApplication.timeSinceStartup-pauseAt<.5)return;
                game.TogglePause();stage=3;Log("PASS real-time pause: prescribed countdown and arc frozen, then resumed.");return;
            }
            if(stage!=3)return;
            if(!game.IsRunning)
            {
                Check(phaseEndObserved&&tenObserved,"Live hold skipped observation points");
                Check(game.CompletedDemonstrations==1,"Live repetition did not complete");
                Log(FormattableString.Invariant($"PASS LIVE {Cases[liveCase]}: automatic Start/countdown, hold observed {holdExited-holdEntered:0.000}s (target {Goals[liveCase]:0.0}s), no seek or time scaling, normal completion."));
                entry.StopTraining();liveCase++;
                if(liveCase==3){Log("PASS all doctor-prescription simulation checks: "+checks);Finish(0);}else stage=1;
                return;
            }
            Render();var rep=game.AssessmentRepetitions()[0];int phase=view.CurrentSample.phase;
            Check(game.Elapsed>=lastLiveTime,"Live clock ran backwards");lastLiveTime=game.Elapsed;
            if(phase==1)
            {
                double elapsed=game.Elapsed-rep.top;CheckVisible("live",elapsed,Goals[liveCase],false);
                if(lastPhase!=1){holdEntered=game.Elapsed;peak=Bones(game.CoachAnimator);Log("LIVE "+Cases[liveCase]+" entered hold: "+TimingText);}
                CompareBones(peak,Bones(game.CoachAnimator));
                if(!tenObserved&&elapsed>=10)
                {
                    tenObserved=true;CheckVisible("live-at10",elapsed,Goals[liveCase]);Capture("live-"+Cases[liveCase]+"-at10");
                    Log(FormattableString.Invariant($"LIVE {Cases[liveCase]} at {elapsed:0.000}s: {TimingText}; ring={view.CurrentSample.progress:0.0000}."));
                    if(liveCase==0&&!pausedOnce){pausedOnce=true;game.TogglePause();frozen=game.Elapsed;pauseAt=EditorApplication.timeSinceStartup;stage=4;}
                }
            }
            if(phase==2&&lastPhase==1)
            {
                holdExited=game.Elapsed;phaseEndObserved=true;
                Near(holdExited-holdEntered,Goals[liveCase],"Natural hold duration differs from prescription",.15);
                Check(game.Elapsed>=rep.lower,"Lowered before prescribed end");
            }
            lastPhase=phase;
        }
        catch(Exception ex){Fail(ex);}
    }
    static void SupplySyntheticInput()
    {
        foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
        foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
        foreach(var h in UnityEngine.Object.FindObjectsOfType<RhythmHitHapticAdapter>())h.enabled=false;
        foreach(var h in UnityEngine.Object.FindObjectsOfType<CoachHapticFeedbackController>())h.enabled=false;
        entry.scorer.StopAllCoroutines();Set(entry.scorer,"userAnimator",game.CoachAnimator);Set(entry.scorer,"calibrated",true);
        var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();motion.Configure(game.CoachAnimator);motion.ReportFrame(Time.unscaledTime,55);
    }
    static void DeterministicCases()
    {
        for(int scenario=0;scenario<Cases.Length;scenario++)
        {
            entry.StopTraining();entry.menu.SelectSimpleDifficulty();var dose=game.PrescriptionDose;
            Check(dose.SetPrescriptionJson(Json(sceneIndex,scenario),ItemId,out string error),error);
            float goal=Goals[scenario];Near(dose.HoldSeconds,goal,"D01 JSON not applied");Check(dose.Total==1&&!dose.IsPreview,"JSON treated as preview or wrong dose");
            Check(dose.Label.Contains($"{goal:0.##} 秒"),"Menu duration does not match doctor JSON");
            if(scenario==0)Capture(Prefix[sceneIndex]+"-D01-20s-menu");
            ((GameObject)Get(entry.menu,"menuRoot")).SetActive(false);((GameObject)Get(entry.menu,"gamingCanvasRoot")).SetActive(true);game.StartMode();
            var rep=game.AssessmentRepetitions()[0];Near(rep.lower-rep.top,goal,"D01 time not propagated to timeline");
            Near(entry.GetComponent<GameTrainingAssessment>().FinalScore.TargetHold,goal,"Final-score denominator differs from D01");
            Check(!dose.SetPrescriptionJson(Json(sceneIndex,(scenario+1)%Cases.Length),ItemId,out _)&&dose.HoldSeconds==goal,"Running JSON changed prescription");
            Set(game,"elapsed",rep.top);Render();peak=Bones(game.CoachAnimator);
            foreach(double elapsed in new[]{0d,5d,10d,goal/2d,goal-.1d})
            {
                Set(game,"elapsed",rep.top+elapsed);Render();CompareBones(peak,Bones(game.CoachAnimator));CompareBones(peak,Bones(game.PreviewAnimator));
                CheckVisible("sampled-"+Cases[scenario],elapsed,goal);
                if(Math.Abs(elapsed-10)<.001&&scenario<3)Capture(Prefix[sceneIndex]+"-D01-"+Cases[scenario]+"-at10");
            }
            Set(game,"elapsed",rep.lower);Render();Check(view.CurrentSample.phase==2,"D01 end does not switch ring to lower");
            Log($"PASS JSON {Prefix[sceneIndex]} {Cases[scenario]}: goal={goal}s; countdown and actual ring checked at 0/5/10/half/end; target={goal}s; peak frozen until lowering.");
        }
        entry.StopTraining();
    }
    static void BeginLiveCase()
    {
        entry.StopTraining();entry.menu.SelectSimpleDifficulty();var dose=game.PrescriptionDose;
        Check(dose.SetPrescriptionJson(Json(0,liveCase),ItemId,out string error),error);
        SupplySyntheticInput();Check(entry.CanStartTraining,"Synthetic input did not satisfy actual start gate");
        entry.menu.StartTraining();Check(!entry.menu.IsTrainingMenuVisible,"Actual Start left menu visible");
        Check(!dose.SetPrescriptionJson(Json(0,(liveCase+1)%3),ItemId,out _),"Countdown allowed replacement JSON");
        caseStarted=EditorApplication.timeSinceStartup;stage=2;Log("LIVE starting doctor JSON "+Cases[liveCase]+" through real menu countdown.");
    }
    static string TimingText=>((TMP_Text)Get(view,"coachTiming")).text;
    static void CheckVisible(string mode,double elapsed,float goal,bool record=true)
    {
        Check(view.CurrentSample.phase==1,"Expected holding phase");
        double remaining=goal-elapsed;string expected="剩余 "+remaining.ToString("F1",CultureInfo.InvariantCulture)+" 秒";
        Check(TimingText==expected,"Visible countdown differs: "+TimingText+" expected "+expected);
        Near(view.CurrentSample.progress,elapsed/goal,"Ring duration does not match prescription");
        var artwork=UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true);var arc=(SpriteRenderer)Get(artwork,"holdProgress");
        var properties=new MaterialPropertyBlock();arc.GetPropertyBlock(properties);Near(properties.GetFloat("_Progress"),elapsed/goal,"Actual visible ring arc differs");
        if(record)File.AppendAllText(Output+"countdown.csv",FormattableString.Invariant($"{mode},{Scenes[sceneIndex]},{game.PrescriptionDose.HoldSeconds},{elapsed:0.000},{remaining:0.000},{TimingText},{view.CurrentSample.progress:0.000000},{entry.GetComponent<GameTrainingAssessment>().FinalScore.TargetHold:0.000}\n"));
    }
    static void Render(){Call(game,"RenderTimeline");Call(view,"LateUpdate");UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true).LateUpdate();}
    // Reuse the already verified capture routine; production time remains untouched.
    static void Capture(string name)
    {
        var type=typeof(PrescribedHoldVerification);var flags=BindingFlags.NonPublic|BindingFlags.Static;
        type.GetField("entry",flags).SetValue(null,entry);type.GetField("game",flags).SetValue(null,game);type.GetField("view",flags).SetValue(null,view);
        string capture="doctor-"+name;
        type.GetMethod("Capture",flags).Invoke(null,new object[]{capture,1688,950});
        File.Copy("output/prescribed-hold/"+capture+".png",Output+name+".png",true);
    }
    static Quaternion[] Bones(Animator animator)=>Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>animator.GetBoneTransform((HumanBodyBones)i)).Select(t=>t!=null?t.localRotation:Quaternion.identity).ToArray();
    static void CompareBones(Quaternion[] a,Quaternion[] b){for(int i=0;i<a.Length;i++)Check(Quaternion.Angle(a[i],b[i])<.08f,"Frozen pose moved: "+(HumanBodyBones)i);}
    static object Get(object o,string name)=>o.GetType().GetField(name,P).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,P).SetValue(o,value);
    static object Call(object o,string name)=>o.GetType().GetMethod(name,P).Invoke(o,null);
    static void Fail(Exception ex){Log("FAIL "+ex);Debug.LogException(ex);Finish(1);}
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
