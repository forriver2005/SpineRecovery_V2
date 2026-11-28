using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Actual Start/countdown/pause/completion/return in every game, followed by a mixed JSON workout.</summary>
[InitializeOnLoad]
public static class AllGamePrescriptionFlowVerification
{
    const string Flag="AllGamePrescriptionFlowVerification",Output="output/all-game-prescriptions/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] AllKeys={"birddog","birddog_hold30","deadbug","deadbug_hold30","hipthrust","hipthrust_hold30","sidebridge","sidebridge_hold30","rightsidebridge","rightsidebridge_hold30","crunch","crunch_hold30","lying_cat","lyingcat_hold30","hipthrust_taiko","hipthrust_curve"};
    static string[] Keys => SessionState.GetBool("FieldRegression",false)
        ? new[]{"crunch","crunch_hold30","lyingcat_hold30","birddog_hold30","deadbug_hold30","hipthrust_hold30","hipthrust_curve"}:AllKeys;
    static GameWorkoutScene entry;static Animator patient,coach;static AvatarMotionSource input;
    static HumanPoseHandler from,to;
    static int index,stage,checks,lastAction=-1,lastSet=-1,completedSets;
    static bool bridgeReady,paused,completionSeen;static double began,sceneBegan,started,pausedAt,frozen,restBegan;
    static GameWorkoutAction action;
    static string runtimeException;
    static AllGamePrescriptionFlowVerification(){if(SessionState.GetBool(Flag,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=ObserveLog;}}
    [MenuItem("Tools/Training/Verify All Prescriptions and Workouts")]
    public static void Run(){SessionState.SetBool("FieldRegression",false);StartRun();}
    public static void RunFieldRegression(){SessionState.SetBool("FieldRegression",true);StartRun();}
    static void StartRun()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"verification.txt",$"{Keys.Length} training scenes: public JSON doses and real UI/executor. Synthetic calibrated patient; no hardware/network doctor delivery claim. Playback speed=4, countdown=5 game seconds, workout rests=30 real seconds.\n");
        index=stage=checks=completedSets=0;entry=null;began=sceneBegan=EditorApplication.timeSinceStartup;
        EditorSceneManager.OpenScene("Assets/Scenes/"+GameWorkoutPlan.SceneFor(Keys[0])+".unity");SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void ObserveLog(string message,string stack,LogType type){if(type==LogType.Exception&&runtimeException==null)runtimeException=message+"\n"+stack;}
    static void Check(bool ok,string msg){checks++;if(!ok)throw new Exception(msg);}
    static void Log(string msg){File.AppendAllText(Output+"verification.txt",msg+"\n");Debug.Log(msg);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(runtimeException==null,"Runtime exception: "+runtimeException);
            Check(EditorApplication.timeSinceStartup-began<1400,"All-scene flow timeout stage="+stage);
            var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null)return;
            if(entry!=current)
            {
                DisposeHandlers();entry=current;sceneBegan=EditorApplication.timeSinceStartup;bridgeReady=false;input=null;
                foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
                foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
                foreach(var h in UnityEngine.Object.FindObjectsOfType<RhythmHitHapticAdapter>())h.enabled=false;
                foreach(var h in UnityEngine.Object.FindObjectsOfType<CoachHapticFeedbackController>())h.enabled=false;
            }
            if(EditorApplication.timeSinceStartup-sceneBegan<.3)return;
            if(entry.taikoNightAction!=null&&entry.taikoNightAction.ResultPanel==null)return;
            if(entry.taiko!=null&&entry.GetComponent<TrainingResultView>()?.Root==null)return;
            if(!SetupInput())return;
            Feed();
            var preparation=entry.taikoNightAction!=null?entry.taikoNightAction.Preparation:null;
            if(preparation!=null&&!preparation.Ready&&entry.IsMenuVisible)
            {
                if(!preparation.Running)preparation.Begin();
                return;
            }
            if(stage>=10){Workout();return;}
            Check(entry.gameObject.scene.name==GameWorkoutPlan.SceneFor(Keys[index]),"Wrong standalone scene");
            if(stage==0)
            {
                Time.timeScale=1;
                input.SetAvailable(false);entry.menu.StartTraining();Check(entry.IsMenuVisible&&!Running(),"Disconnected Start accepted: "+Keys[index]);Feed();
                string difficulty=index%3==0?"simple":index%3==1?"standard":"hard";entry.PrepareMenu(difficulty);Feed();
                int reps=(Keys[index]=="birddog"||Keys[index]=="deadbug")?5:2;
                string json="{\"actions\":[{\"action\":\""+Keys[index]+"\",\"difficulty\":\""+difficulty+"\",\"sets\":2,\"reps\":"+reps+(GameWorkoutPlan.IsTimedHold(Keys[index])?",\"holdSeconds\":3":"")+"}]}";
                Check(GameWorkoutSession.Instance.SetPlanJson(json,out string error),error);action=GameWorkoutSession.Instance.Plan.actions[0];Check(entry.PrepareDose(action,out error),error);
                entry.menu.StartTraining();Check(!entry.IsMenuVisible,"Real Start failed: "+Keys[index]);started=EditorApplication.timeSinceStartup;paused=completionSeen=false;stage=1;return;
            }
            if(stage==1)
            {
                Check(EditorApplication.timeSinceStartup-started<20,"Countdown stalled: "+Keys[index]);
                if(!Running())return;
                // The legacy segmented controller reports playing at the end of the same shared countdown.
                Check(EditorApplication.timeSinceStartup-started>=4.7,"Countdown skipped");
                CheckDose();Check(Music()!=null&&Music().clip!=null&&Music().isPlaying&&Music().loop,"Music missing: "+Keys[index]);Pause();frozen=Clock();pausedAt=EditorApplication.timeSinceStartup;stage=2;return;
            }
            if(stage==2)
            {
                if(EditorApplication.timeSinceStartup-pausedAt<.2)return;
                Check(Math.Abs(Clock()-frozen)<.025,"Pause advanced gameplay: "+Keys[index]);Check(!Music().isPlaying,"Pause left music playing");Pause();Time.timeScale=4;stage=3;return;
            }
            if(stage==3)
            {
                Check(EditorApplication.timeSinceStartup-started<110,"Game never naturally completed: "+Keys[index]);
                if(!ResultVisible())return;
                Time.timeScale=1;Check(!Running(),"Result shown while running");Check(!Music().isPlaying,"Music continued on result: "+Keys[index]);CheckCompletedDose();
                Log("PASS standalone "+Keys[index]+$": JSON sets=2, per-run reps={action.reps}, hold={action.holdSeconds}; sensor gate, actual 5s countdown, pause, natural completion, shared result.");
                ResultPanel().ReturnButton.onClick.Invoke();Check(entry.IsMenuVisible&&!ResultVisible(),"Result return failed");
                Feed();entry.menu.StartTraining();Check(!entry.IsMenuVisible,"Replay Start failed");entry.StopTraining();started=EditorApplication.timeSinceStartup;Time.timeScale=8;stage=4;return;
            }
            if(stage==4&&EditorApplication.timeSinceStartup-started>1)
            {
                Check(entry.IsMenuVisible&&!Running(),"Cancelled countdown restarted: "+Keys[index]);Time.timeScale=1;
                index++;if(index==Keys.Length){stage=10;SceneManager.LoadScene(GameWorkoutPlan.SceneFor(Keys[0]));}else{stage=0;SceneManager.LoadScene(GameWorkoutPlan.SceneFor(Keys[index]));}
            }
        }
        catch(Exception ex){Log("FAIL "+ex);Debug.LogException(ex);Finish(1);}
    }
    static bool SetupInput()
    {
        if(entry.bridgeWave!=null)
        {
            if(!bridgeReady)
            {
                var ui=entry.bridgeWave;ui.visualRoot.SetActive(true);if(ui.game.transform.Find("TrackBandInner")==null)return false;
                BridgeInvoke("TestCalibration");bridgeReady=true;
            }
            input=entry.bridgeWave.input.motion;return true;
        }
        if(input!=null)return true;
        patient=entry.taikoNightAction!=null?entry.taikoNightAction.recorder.UserAnimator:entry.scorer.UserAnimator;
        coach=entry.taikoNightAction!=null?entry.taikoNightAction.recorder.CoachAnimator:entry.taiko!=null?entry.taiko.CoachAnimator:entry.scorer.CoachAnimator;
        input=UnityEngine.Object.FindObjectsOfType<AvatarMotionSource>().FirstOrDefault(m=>m.Model==patient.gameObject)??UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();Check(input!=null,"Missing input source");input.Configure(patient);
        if(entry.scorer!=null){entry.scorer.StopAllCoroutines();Set(entry.scorer,"calibrated",true);Set(entry.scorer,"calibrating",false);}
        from=new HumanPoseHandler(coach.avatar,coach.transform);to=new HumanPoseHandler(patient.avatar,patient.transform);
        return true;
    }
    static void Feed()
    {
        if(entry.bridgeWave!=null)
        {
            var ui=entry.bridgeWave;float level=ui.State==BridgeWave.BridgeWaveFollowController.RunState.Playing?ui.game.Level(ui.Session.Elapsed+.005f):0;
            BridgeInvoke("Feed",level);return;
        }
        var pose=new HumanPose();from.GetHumanPose(ref pose);to.SetHumanPose(ref pose);input.ReportFrame(Time.unscaledTime,55);
        if(entry.scorer!=null)Set(entry.scorer,"calibrated",true);
    }
    static void BridgeInvoke(string name,params object[] args)
    {
        var t=typeof(BridgeWave.EditorTools.BridgeWavePlayVerification);var flags=BindingFlags.Static|BindingFlags.NonPublic;t.GetField("ui",flags).SetValue(null,entry.bridgeWave);t.GetMethod(name,flags).Invoke(null,args);
    }
    static AudioSource Music()=>entry.GetComponent<GameTrainingMusic>()!=null?entry.GetComponent<GameTrainingMusic>().Source:UnityEngine.Object.FindObjectOfType<RhythmSongClock>().GetComponent<AudioSource>();
    static bool Running()=>entry.bridgeWave!=null?entry.bridgeWave.State==BridgeWave.BridgeWaveFollowController.RunState.Playing||entry.bridgeWave.State==BridgeWave.BridgeWaveFollowController.RunState.Paused:entry.taikoNightAction!=null?entry.taikoNightAction.IsRunning:entry.taiko!=null?entry.taiko.IsRunning:entry.coach.IsPlayingAction;
    static double Clock()=>entry.bridgeWave!=null?entry.bridgeWave.Session.Elapsed:entry.taikoNightAction!=null?(float)Get(entry.taikoNightAction,"clock"):entry.taiko!=null?entry.taiko.Elapsed:entry.coach.GetComponent<Animator>()!=null?entry.coach.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime:entry.scorer.CoachAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
    static void Pause(){if(entry.bridgeWave!=null)entry.bridgeWave.TogglePause();else if(entry.taikoNightAction!=null)entry.taikoNightAction.TogglePause();else if(entry.taiko!=null)entry.taiko.TogglePause();else entry.coach.TogglePause();}
    static TrainingResultPanel ResultPanel()=>entry.bridgeWave!=null?entry.bridgeWave.ResultPanel:entry.taikoNightAction!=null?entry.taikoNightAction.ResultPanel:entry.GetComponent<TrainingResultView>().Root.GetComponent<TrainingResultPanel>();
    static bool ResultVisible()=>ResultPanel()!=null&&ResultPanel().gameObject.activeInHierarchy;
    static void CheckDose()
    {
        if(entry.bridgeWave!=null)Check(entry.bridgeWave.Session.Repetitions==action.reps,"Curve reps lost");
        else if(entry.taikoNightAction!=null)Check(entry.taikoNightAction.RepetitionCount==action.reps,"Taiko reps lost");
        else if(entry.taiko!=null)
        {
            Check(entry.taiko.AssessmentRepetitions().Length==action.reps,"Ring reps lost");
            if(GameWorkoutPlan.IsTimedHold(action.action))Check(Math.Abs(entry.taiko.PrescribedHoldSeconds-action.holdSeconds)<.001,"Hold duration lost");
        }
    }
    static void CheckCompletedDose()
    {
        if(entry.taiko!=null)Check(entry.taiko.CompletedDemonstrations==action.reps||entry.taiko.Hold30Timeline==null,"Ring actual count wrong");
        else if(entry.taikoNightAction!=null)Check(entry.taikoNightAction.CompletedRepetitions==action.reps,"Taiko completed wrong count");
        else if(entry.bridgeWave==null)Check(entry.coach.AssessmentDurations.Length==action.reps,"Limb prescribed reps rounded to loop count");
        if(entry.bridgeWave==null&&entry.taikoNightAction==null)
            Check(entry.GetComponent<GameTrainingAssessment>().FinalScore.Kind==(GameWorkoutPlan.IsTimedHold(action.action)?FinalTrainingScore.Mode.TimedHold:FinalTrainingScore.Mode.Repetition),"Wrong score category");
    }
    static void Workout()
    {
        var session=GameWorkoutSession.Instance;
        if(stage==10)
        {
            var actions=Keys.Select((key,i)=>new GameWorkoutAction{action=key,difficulty=i%3==0?"simple":i%3==1?"standard":"hard",sets=i==1?2:1,reps=1,holdSeconds=GameWorkoutPlan.IsTimedHold(key)?2+i%3:0}).ToArray();
            string json=JsonUtility.ToJson(new GameWorkoutPlan{actions=actions},true);File.WriteAllText(Output+"mixed-workout.json",json);
            Check(session.SetPlanJson(json,out string error),error);Check(session.StartPlan(out error),error);Time.timeScale=4;stage=11;lastAction=lastSet=-1;completionSeen=false;restBegan=0;return;
        }
        if(session.IsComplete)
        {
            Check(completedSets==Keys.Length+1,"Mixed workout completed wrong number of sets: "+completedSets);Time.timeScale=1;Log($"PASS mixed JSON workout: {Keys.Length} game scenes, {Keys.Length+1} sets, per-action difficulty/reps/holdSeconds, real 30s rests and automatic transitions.");
            ResultPanel().ReturnButton.onClick.Invoke();Check(entry.IsMenuVisible,"Final workout return failed");Log("PASS ALL GAME FLOWS checks="+checks);Finish(0);return;
        }
        Check(session.IsRunning,"Workout stopped unexpectedly: "+session.Status);
        if(session.RestSecondsRemaining>0)
        {
            if(!completionSeen)
            {
                completionSeen=true;completedSets++;CheckCompletedDose();restBegan=EditorApplication.timeSinceStartup;
                Check(session.RestSecondsRemaining==30||session.RestSecondsRemaining==29,"Unexpected rest duration");Check(!session.AllowsStart(entry),"Manual restart allowed during rest");
                int a=session.ActionIndex,s=session.CompletedSets;session.OnSetCompleted(entry);Check(a==session.ActionIndex&&s==session.CompletedSets,"Duplicate completion advanced plan");
                Log($"PASS workout set {completedSets}/{Keys.Length+1} {action.action}: completion once, next action={a}, rest=30s.");
            }
            return;
        }
        if(Running()&&(session.ActionIndex!=lastAction||session.CompletedSets!=lastSet))
        {
            if(restBegan>0)Check(EditorApplication.timeSinceStartup-restBegan>=29.8,"30-second rest ended early");
            lastAction=session.ActionIndex;lastSet=session.CompletedSets;action=session.Plan.actions[lastAction];completionSeen=false;CheckDose();
            // Count the final naturally completed set when the executor raises Finished.
            if(lastAction==Keys.Length-1)entry.bridgeWave.Completed+=()=>{completedSets++;};
        }
    }
    static object Get(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
    static void DisposeHandlers(){from?.Dispose();to?.Dispose();from=to=null;}
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;DisposeHandlers();Time.timeScale=1;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
