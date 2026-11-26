using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BridgeWave.EditorTools
{
    // Editor-only fixtures. Production gates remain enabled throughout the sequence.
    [InitializeOnLoad]
    public static class BridgeWaveWorkoutVerification
    {
        const string Flag="BridgeWave.WorkoutVerification";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        const BindingFlags StaticPrivate=BindingFlags.Static|BindingFlags.NonPublic;
        static GameWorkoutScene entry;
        static double began,sceneBegan,setBegan,cancelBegan;
        static int lastAction=-1,lastSet=-1,completed,phase;
        static bool calibrated,waitingProved,restProved,restCaptured,finished;
        static double restBegan;
        static string report;
        static Action completionHandler;
        static BridgeWaveWorkoutVerification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
        [MenuItem("Tools/Bridge Wave/Load Example Workout")]
        public static void LoadExample()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play mode first");
            var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();
            if(current==null)throw new InvalidOperationException("Open a game scene with a workout entry");
            string json=File.ReadAllText("Assets/BridgeWave/ExampleWorkout.json");
            if(!GameWorkoutSession.Instance.SetPlanJson(json,out string error))throw new InvalidOperationException(error);
            current.OpenWorkoutPanelAbove(1500);
        }
        [MenuItem("Tools/Bridge Wave/Verify Workout Integration")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first");
            EditorSceneManager.OpenScene(BridgeWaveFollowBuilder.ScenePath);
            began=0;phase=completed=0;lastAction=lastSet=-1;entry=null;finished=false;restProved=restCaptured=false;
            SessionState.SetBool(Flag,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Invoke(BridgeWaveFollowController ui,string method,params object[] args)
        {
            typeof(BridgeWavePlayVerification).GetField("ui",StaticPrivate).SetValue(null,ui);
            typeof(BridgeWavePlayVerification).GetMethod(method,StaticPrivate).Invoke(null,args);
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                if(began==0){began=EditorApplication.timeSinceStartup;report="Workout integration using synthetic patient poses and legacy completion events.\n";}
                Check(EditorApplication.timeSinceStartup-began<240,"Workout timeout phase "+phase);
                var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null)return;
                var session=GameWorkoutSession.Instance;
                if(current!=entry)
                {
                    entry=current;sceneBegan=EditorApplication.timeSinceStartup;calibrated=waitingProved=false;setBegan=0;
                    foreach(var receiver in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())receiver.enabled=false;
                    foreach(var bridge in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())bridge.enabled=false;
                    if(entry.bridgeWave!=null)
                    {
                        Check(!entry.bridgeWave.skipCalibrationForFlowTest,"Workout entered bypassed bridge scene");
                        entry.bridgeWave.input.motion.SetAvailable(false);
                    }
                    else
                    {
                        entry.scorer.StopAllCoroutines();
                        typeof(DeadBugGamingPoseScorer).GetField("calibrated",Private).SetValue(entry.scorer,true);
                        report+="INFO legacy scene calibration is an editor fixture; completion is injected after the real countdown.\n";
                    }
                }
                if(phase==0)
                {
                    var ui=entry.bridgeWave;Check(ui!=null,"Bridge entry missing");
                    Check(GameWorkoutPlan.SceneFor("hipthrust_curve")==ui.gameObject.scene.name&&GameWorkoutPlan.SceneFor("bridgewave")==ui.gameObject.scene.name,"Action route missing");
                    Check(GameWorkoutPlan.SceneFor("hipthrust")=="HipThrust_RingBridge_NewScoring","Existing hip route changed");
                    Check(!ui.SetRepetitionDose(101,out _)&&!ui.SetRepetitionDose(-1,out _),"Invalid direct dose accepted");
                    Check(ui.SetRepetitionDose(5,out _)&&ui.Session.Repetitions==5,"More than four repetitions not supported");
                    Check(ui.SetRepetitionDose(0,out _)&&ui.Session.Repetitions==4,"Default dose did not restore four repetitions");
                    Check(!session.SetPlanJson("{\"actions\":[{\"action\":\"hipthrust_curve\",\"sets\":1,\"reps\":101}]}",out _),"Invalid repetition dose accepted");
                    var plan=new GameWorkoutPlan {actions=new[]{
                        new GameWorkoutAction{action="hipthrust_curve",difficulty="hard",sets=2,reps=1},
                        new GameWorkoutAction{action="deadbug",difficulty="simple",sets=1},
                        new GameWorkoutAction{action="bridgewave",difficulty="standard",sets=1,reps=3}}};
                    Check(session.SetPlan(plan,out string error),error);Check(session.StartPlan(out error),error);
                    ui.startMenu.StartTraining();Check(ui.State==BridgeWaveFollowController.RunState.Ready,"Manual Start bypassed workout preparation");
                    phase=1;return;
                }
                if(phase==1)
                {
                    if(entry.bridgeWave!=null)
                    {
                        var ui=entry.bridgeWave;
                        if(!calibrated)
                        {
                            if(EditorApplication.timeSinceStartup-sceneBegan<4.2)return;
                            Check(!entry.IsPlaying&&!entry.CanStartTraining&&ui.State==BridgeWaveFollowController.RunState.Ready,"Workout skipped fresh-data/calibration admission");
                            waitingProved=true;
                            // Let geometric visuals initialize before exercising the actual calibration/controller fixture.
                            ui.visualRoot.SetActive(true);
                            if(ui.game.transform.Find("TrackBandInner")==null)return;
                            Invoke(ui,"TestCalibration");Invoke(ui,"Feed",0f);ui.RefreshHud();calibrated=true;
                            report+="PASS bridge workout waits for fresh data and stable supine calibration before automatic Start.\n";
                        }
                        if(ui.State==BridgeWaveFollowController.RunState.Ready||ui.State==BridgeWaveFollowController.RunState.Countdown)
                            Invoke(ui,"Feed",0f);
                        else if(ui.State==BridgeWaveFollowController.RunState.Playing)
                            Invoke(ui,"Feed",ui.game.Level(ui.Session.Elapsed+.005f));
                        if(entry.IsPlaying && ui.State==BridgeWaveFollowController.RunState.Playing && (lastAction!=session.ActionIndex||lastSet!=session.CompletedSets))
                        {
                            var action=session.Plan.actions[session.ActionIndex];
                            Check(ui.game.totalCount==action.reps&&ui.Session.Repetitions==action.reps,"Prescription repetitions not applied");
                            Check(ui.startMenu.SelectedDifficultyKey==action.difficulty,"Difficulty not applied");
                            Check(Mathf.Abs(ui.game.tolerance-(action.difficulty=="hard"?.06f:.08f))<.0001f,"Track width does not follow difficulty");
                            int actionIndex=session.ActionIndex,set=session.CompletedSets;
                            if(completionHandler!=null)ui.Completed-=completionHandler;
                            completionHandler=()=>
                            {
                                completed++;
                                int next=session.ActionIndex,sets=session.CompletedSets;
                                session.OnSetCompleted(entry);
                                Check(next==session.ActionIndex&&sets==session.CompletedSets,"Duplicate completion advanced twice");
                                report+=$"PASS bridge action {actionIndex}, set {set+1}: exact {action.reps} repetitions completed.\n";
                            };
                            ui.Completed+=completionHandler;
                            // Remove previous test callback when reusing the same scene for another set.
                            lastAction=actionIndex;lastSet=set;
                        }
                    }
                    else
                    {
                        var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();if(motion!=null)motion.ReportFrame(Time.unscaledTime,55);
                        if(entry.IsPlaying)
                        {
                            if(setBegan==0)setBegan=EditorApplication.timeSinceStartup;
                            if(EditorApplication.timeSinceStartup-setBegan>6&&lastAction!=session.ActionIndex)
                            {
                                Check(entry.menu.SelectedDifficultyKey=="simple","Legacy action difficulty not applied");
                                lastAction=session.ActionIndex;lastSet=session.CompletedSets;
                                entry.coach.CancelTraining();entry.scorer.CancelScoringSession();if(entry.rhythm!=null)entry.rhythm.StopRhythmMode();
                                ((Action)typeof(CoachActionController).GetField("ActionCompleted",Private).GetValue(entry.coach))?.Invoke();
                                completed++;report+="PASS transition into DeadBug, real Start/countdown, completion dispatch and return to bridge.\n";
                            }
                        }
                    }
                    if(session.RestSecondsRemaining>0&&!restProved)
                    {
                        Check(!session.AllowsStart(entry),"Manual restart allowed during group rest");
                        restProved=true;report+="PASS existing 30-second group rest prevents manual restart.\n";
                        restBegan=EditorApplication.timeSinceStartup;
                    }
                    if(restProved&&!restCaptured&&session.RestSecondsRemaining>0&&EditorApplication.timeSinceStartup-restBegan>.5)
                    {
                        Invoke(entry.bridgeWave,"Capture","workout-rest",1688,780);restCaptured=true;
                    }
                    if(session.IsComplete)
                    {
                        Check(session.ActionIndex==3&&completed==4&&restProved&&waitingProved,"Wrong action/set completion totals: "+completed);
                        Check(!entry.IsPlaying,"Entry still playing after completion");
                        report+="PASS plan completed: 2 x 1 bridge, 1 DeadBug set, 1 x 3 bridge; aliases, doses and difficulties.\n";
                        entry.TogglePanel();Invoke(entry.bridgeWave,"Capture","workout-complete",1688,780);
                        Check(session.StartPlan(out string error),error);phase=2;
                    }
                    return;
                }
                if(phase==2)
                {
                    Invoke(entry.bridgeWave,"Feed",0f);
                    if(entry.bridgeWave.State!=BridgeWaveFollowController.RunState.Countdown)return;
                    entry.bridgeWave.menuButton.onClick.Invoke();cancelBegan=EditorApplication.timeSinceStartup;phase=3;return;
                }
                if(phase==3)
                {
                    if(EditorApplication.timeSinceStartup-cancelBegan<6)return;
                    Check(!session.IsRunning&&!entry.IsPlaying&&entry.bridgeWave.State==BridgeWaveFollowController.RunState.Ready,"Cancellation leaked delayed Start");
                    report+="PASS Return to setup during countdown cancels workout and returns to preparation with no delayed restart.\n";
                    Finish(true);
                }
            }
            catch(Exception ex){report+="FAIL "+ex+"\n";Debug.LogException(ex);Finish(false);}
        }
        static void Finish(bool success)
        {
            if(finished)return;finished=true;
            Directory.CreateDirectory("output/bridgewave");File.WriteAllText("output/bridgewave/workout-verification.txt",report);
            SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;
            if(Application.isBatchMode)EditorApplication.Exit(success?0:1);else EditorApplication.isPlaying=false;
        }
    }
}
