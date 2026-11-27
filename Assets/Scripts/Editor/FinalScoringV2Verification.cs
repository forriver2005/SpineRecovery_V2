using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FinalScoringV2Verification
{
    const string Flag="FinalScoringV2Verification",Output="output/final-score-v2/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] SharedScenes={"BirdDog_RingSkinC_Recorded","DeadBug_RingSkinC_Recorded","HipThrust_RingBridge_NewScoring","HipThrust_RingBridge_Hold30","SideBridge_RingBridge_Dynamic","SideBridge_RingBridge_Hold30","HipThrust_TaikoNight_Dynamic"};
    static string[] Scenes => SessionState.GetBool("FinalV2Right",false)
        ? new[]{"SideBridgeRight_RingBridge_Dynamic","SideBridgeRight_RingBridge_Hold30"}:SessionState.GetBool("FinalV2Rhythm",false)?new[]{"BirdDog_RingSkinC_Recorded","DeadBug_RingSkinC_Recorded"}:SharedScenes;
    static string Report => Output+(SessionState.GetBool("FinalV2Right",false)?"right-scenes.txt":SessionState.GetBool("FinalV2Rhythm",false)?"rhythm-probes.txt":"verification.txt");
    static int index,stage,checks;static double start;
    static GameWorkoutScene entry;static GameTrainingAssessment assessment;static Animator scenePatient;
    static FinalScoringV2Verification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
    [MenuItem("Tools/Training/Verify Final Scoring V2")]
    public static void Run(){SessionState.SetBool("FinalV2Right",false);SessionState.SetBool("FinalV2Rhythm",false);StartRun();}
    public static void RunRhythmProbes(){SessionState.SetBool("FinalV2Adversaries",false);SessionState.SetBool("FinalV2Right",false);SessionState.SetBool("FinalV2Rhythm",true);StartRun();}
    public static void RunAdversaries(){SessionState.SetBool("FinalV2Adversaries",true);SessionState.SetBool("FinalV2Right",false);SessionState.SetBool("FinalV2Rhythm",true);StartRun();}
    public static void RunRight()
    {
        SessionState.SetBool("FinalV2Right",true);
        foreach(string name in Scenes)
        {
            string path="Assets/Scenes/"+name+".unity";
            if(!File.Exists(path))throw new InvalidOperationException("Right-side scene is not installed: "+path);
            if(!EditorBuildSettings.scenes.Any(s=>s.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
        }
        StartRun();
    }
    static void StartRun()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Report,"Final score v2: settlement-only model and scene replay; no physical device validation.\n");
        Pure();index=stage=0;start=EditorApplication.timeSinceStartup;
        EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[0]+".unity");SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    public static void PureOnly(){Directory.CreateDirectory(Output);File.WriteAllText(Output+"unit.txt","");Pure();}
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static void Log(string text){File.AppendAllText(Report,text+"\n");Debug.Log(text);}
    static void Pure()
    {
        foreach(int fps in new[]{30,60,120})
        {
            var s=new FinalTrainingScore();s.Reset(FinalTrainingScore.Mode.Repetition,new[]{new FinalTrainingScore.Plan(4,2,1,1)});
            s.ObserveMovement(0,Vector2.zero,true,false,false);s.ObserveMovement(0,Vector2.one,false,true,false);s.ObserveMovement(0,Vector2.zero,true,false,true);
            for(int f=0;f<4*fps;f++)s.ObserveCoverage(0,1d/fps,true);
            for(int f=0;f<2*fps;f++)s.ObservePart(0,FinalTrainingScore.Part.Target,1d/fps,true,95);
            for(int f=0;f<fps;f++){s.ObservePart(0,FinalTrainingScore.Part.Lift,1d/fps,true,0);s.ObservePart(0,FinalTrainingScore.Part.Lower,1d/fps,true,0);}
            var e=new FinalScoreWindow(.25);e.Add(.25,95);s.SetEndpoint(0,false,e,.25);s.Finish();
            Check(s.CanPublish&&Math.Abs(s.ExactScore-90.25)<.01&&s.Complete==1,"Zero process double-penalized endpoint: "+s.ExactScore);
            Log($"UNIT {fps} fps: target95/process0/return95 -> {s.ExactScore:0.00}, complete={s.Complete}.");
            foreach(double good in new[]{30d,27d,5d})
            {
                s.Reset(FinalTrainingScore.Mode.TimedHold,new[]{new FinalTrainingScore.Plan(34,30,2,2)});
                for(int f=0;f<30*fps;f++){s.ObserveCoverage(0,1d/fps,true);s.ObserveHold(0,1d/fps,true,f<good*fps);}
                s.Finish();Check(s.CanPublish&&Math.Abs(s.ExactScore-100*good/30)<.01,"Hold ratio mismatch");
            }
            s.Reset(FinalTrainingScore.Mode.TimedHold,new[]{new FinalTrainingScore.Plan(30,30,0,0)});
            for(int f=0;f<30*fps;f++){bool valid=!(f>=15*fps&&f<15.3*fps);s.ObserveCoverage(0,1d/fps,valid);s.ObserveHold(0,1d/fps,valid,valid);}
            s.Finish();Check(s.Score==99&&s.Longest<15.01&&s.ContinuousCompleted==0,"Short gap erased cumulative credit or fabricated continuity");
            s.Reset(FinalTrainingScore.Mode.TimedHold,new[]{new FinalTrainingScore.Plan(30,30,0,0)});
            s.ObserveCoverage(0,5,true);s.ObserveHold(0,5,true,true);s.ObserveCoverage(0,25,false);s.Finish();Check(!s.CanPublish,"Low coverage published confident score");
        }
        var counter=new FinalTrainingScore();counter.Reset(FinalTrainingScore.Mode.TimedHold,new[]{new FinalTrainingScore.Plan(30,30,0,0)});
        counter.ObserveCoverage(0,10,true);counter.ObserveHoldCounter(0,10,true,true,10);
        counter.ObserveCoverage(0,5,true);counter.ObserveHoldCounter(0,5,true,true,5);
        Check(counter.ValidHold==15&&counter.Longest==10,"Source counter reset erased time or joined separate holds");
        counter.BreakContinuity();counter.ObserveCoverage(0,1,true);counter.ObserveHoldCounter(0,1,true,true,6);
        Check(counter.ValidHold==16&&counter.Longest==10,"Pause erased credit or fabricated continuous hold");
        var missingTarget=new FinalTrainingScore();missingTarget.Reset(FinalTrainingScore.Mode.Repetition,new[]{new FinalTrainingScore.Plan(100,1,49,50)});
        missingTarget.ObserveCoverage(0,99,true);missingTarget.ObserveCoverage(0,1,false);var known=new FinalScoreWindow(.25);known.Add(.25,100);missingTarget.SetEndpoint(0,false,known,.25);missingTarget.Finish();
        Check(!missingTarget.CanPublish,"Overall coverage concealed missing critical target evidence");
        var empty=new FinalTrainingScore();empty.Reset(FinalTrainingScore.Mode.Repetition,new[]{new FinalTrainingScore.Plan(4,2,1,1),new FinalTrainingScore.Plan(4,2,1,1)});empty.Finish();Check(!empty.CanPublish&&empty.Score==0,"Empty data awarded points");
        var filter=new FinalScoreWindow(.1);filter.Add(.07,95);filter.Add(.03,0);Check(filter.Median==95,"Short spike not resisted");filter.Add(.1,0);Check(filter.Median==0,"Sustained error hidden by median");
        Log("PASS pure: 80/5/15, frame rate invariance, 27/30=90, 5/30=16.7, cumulative/continuous separation, no input, coverage and bounded filtering.");
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-start<420,"Scene replay timeout");
            if(stage==0)
            {
                entry=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(entry==null||entry.gameObject.scene.name!=Scenes[index])return;
                scenePatient=entry.scorer.UserAnimator;
                assessment=entry.GetComponent<GameTrainingAssessment>();if(assessment==null||entry.GetComponent<TrainingResultView>()?.Root==null)return;
                foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
                foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
                entry.menu.SelectSimpleDifficulty();((GameObject)Get(entry.menu,"menuRoot")).SetActive(false);
                Set(entry.scorer,"calibrated",true);entry.scorer.StopAllCoroutines();
                if(entry.taiko==null){Call(entry.coach,"StartSegmentedDeadBug");stage=1;}
                else{if(entry.taiko.PrescriptionDose!=null)entry.taiko.PrescriptionDose.SetDose(1,3,out _);stage=2;}
                return;
            }
            if(stage==1){if(!assessment.IsCollecting)return;entry.coach.StopAllCoroutines();stage=2;}
            if(stage==2)
            {
                foreach(int fps in new[]{30,60})
                {
                    if(entry.taiko!=null)Bridge(fps,false);else Rhythm(fps,false);
                    var s=assessment.FinalScore;Log($"SCENE {Scenes[index]} {fps}fps {s.Kind}: score={s.ExactScore:0.00}, target={s.TargetQuality:0.0}, process={s.ProcessQuality:0.0}, return={s.ReturnQuality:0.0}, coverage={s.Coverage:0.000}, hold={s.ValidHold:0.000}/{s.TargetHold:0.000}, complete={s.Complete}/{s.Planned}");
                    Check(s.CanPublish,"Full replay has insufficient coverage: "+Scenes[index]);
                    if(SessionState.GetBool("FinalV2Right",false))
                    {
                        Log($"SOURCE {Scenes[index]}: legacy score={assessment.Summary.Score}, accepted hold={assessment.Summary.CreditedSeconds:0.000}/{assessment.Summary.TargetSeconds:0.000}, qualified={assessment.Summary.Qualified}/{assessment.Summary.Planned}");
                        if(s.Kind==FinalTrainingScore.Mode.TimedHold)
                            Check(Math.Abs(s.ValidHold-assessment.Summary.CreditedSeconds)<.1,"Right-side settlement lost source-confirmed hold evidence");
                        else Check(s.Score>=80,"Right-side target evidence missing in final score");
                    }
                    else Check(s.Score>=85,"Perfect playback loses excessive settlement credit: "+Scenes[index]+"="+s.Score);
                    bool hold=Scenes[index].Contains("Hold30");Check((s.Kind==FinalTrainingScore.Mode.TimedHold)==hold,"Wrong task classification");
                }
                if(entry.taiko!=null&&assessment.FinalScore.Kind==FinalTrainingScore.Mode.TimedHold)
                {
                    Bridge(30,false,true);var interrupted=assessment.FinalScore;
                    Check(interrupted.ContinuousCompleted==0,"Hold gap retained continuous completion");
                    if(!SessionState.GetBool("FinalV2Right",false))Check(interrupted.Score>=98&&interrupted.ValidHold>interrupted.TargetHold*.98,"Hold gap erased cumulative time");
                    else Check(interrupted.ValidHold>20,"Right-side cumulative evidence was erased by gap");
                    Log($"GAP {Scenes[index]}: score={interrupted.Score}, valid={interrupted.ValidHold:0.00}, longest={interrupted.Longest:0.00}, continuous={interrupted.ContinuousCompleted}");
                    Bridge(60,false);
                }
                else
                {
                    float fullScore=assessment.FinalScore.ExactScore;
                    if(entry.taiko!=null)Bridge(30,false,false,true);else Rhythm(30,false,true);
                    Check(assessment.FinalScore.ExactScore>=fullScore-5.1f&&assessment.FinalScore.Complete>0,"Process zeros erased valid target/return credit");
                    Log($"ZERO PROCESS {Scenes[index]}: score={assessment.FinalScore.Score}, complete={assessment.FinalScore.Complete}");
                    if(entry.taiko!=null)Bridge(60,false);else Rhythm(60,false);
                }
                if(SessionState.GetBool("FinalV2Rhythm",false))
                {
                    bool all=SessionState.GetBool("FinalV2Adversaries",false);
                    string[] names=all?new[]{"follow","range_90","range_80","delay_350ms","delay_500ms","jitter_3deg","jitter_5deg","gap_1frame_mid","gap_1frame_end","gap_100ms_mid","spike_100ms_mid","gap_300ms_mid","rest","half_range","wrong_orientation","disconnect","heartbeat_only","wrong_arm","wrong_leg_direction","wrong_knee"}:new[]{"follow","delay_350ms","delay_500ms","jitter_3deg","jitter_5deg","spike_100ms_mid"};int probe=0;
                    Action<FinalTrainingScore> observe=s=>
                    {
                        string name=names[Math.Min(probe,names.Length-1)];
                        Log($"PROBE {Scenes[index]} {name}: score={s.ExactScore:0.00} target={s.TargetQuality:0.0} process={s.ProcessQuality:0.0} return={s.ReturnQuality:0.0} coverage={s.Coverage:0.000} publish={s.CanPublish}");
                        if(name=="rest"||name=="heartbeat_only")Check(s.Score==0,"Stationary/no-data probe earned points");
                        if(name=="half_range"||name.StartsWith("wrong_"))Check(s.Score<60&&s.Complete==0,"Wrong/partial movement earned high final credit");
                        if(name=="gap_1frame_end")Check(s.CanPublish&&s.Score>=95,"Single missing endpoint frame erased confirmed endpoint credit");
                        if(name.StartsWith("delay_"))Check(s.Score>=90,"Existing bounded response delay penalized final endpoints");
                        if(name=="heartbeat_only"||name=="disconnect")Check(!s.CanPublish,"Missing evidence published confident total");
                        probe++;
                    };
                    assessment.FinalScore.Settled+=observe;
                    var actual=scenePatient;
                    SessionState.SetBool("ToleranceFast",!all);TrainingToleranceScenarios.Begin();
                    TrainingToleranceScenarios.Rhythm(entry,actual);
                    assessment.FinalScore.Settled-=observe;SessionState.SetBool("ToleranceFast",false);
                }
                var view=entry.GetComponent<TrainingResultView>();var end=UnityEngine.Object.FindObjectsOfType<Canvas>(true).Single(c=>c.name=="EndMenuCanvas");
                end.gameObject.SetActive(true);Call(view,"LateUpdate");view.Refresh();Check(view.ScoreLabel.text==assessment.FinalScore.Score.ToString(),"Result view not wired to v2");Capture(Scenes[index]);
                if(entry.taiko!=null)Bridge(30,true);else Rhythm(30,true);
                Check(!assessment.FinalScore.CanPublish&&assessment.FinalScore.Score==0,"No-data replay fabricated result");
                view.Refresh();Check(view.ScoreLabel.text=="数据不足","Result hides missing-data status");
                if(entry.taiko==null)
                {
                    Set(assessment,"<IsFinished>k__BackingField",false);Call(view,"LateUpdate");
                    Rhythm(30,false);Call(view,"LateUpdate");
                    Check(view.ScoreLabel.text==assessment.FinalScore.Score.ToString(),"Visible report did not refresh after deferred settlement");
                }
                index++;stage=0;if(index==Scenes.Length){Log("PASS all shared game scenes. Checks="+checks);Finish(0);}else SceneManager.LoadScene(Scenes[index]);
            }
        }
        catch(Exception e){Log("FAIL "+e);Debug.LogException(e);Finish(1);}
    }
    static void Bridge(int fps,bool missing,bool gap=false,bool zeroProcess=false)
    {
        var game=entry.taiko;Set(entry.scorer,"userAnimator",game.CoachAnimator);Set(entry.scorer,"calibrated",true);
        var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();game.StartMode();
        for(int sample=1;sample<=Math.Ceiling(game.Duration*fps);sample++)
        {
            double now=Math.Min(game.Duration,sample/(double)fps);Set(game,"elapsed",now);Call(game,"RenderTimeline");
            var plan=game.AssessmentRepetitions();int rep=0;for(int j=1;j<plan.Length&&now>=plan[j].start;j++)rep=j;
            double middle=(plan[rep].top+plan[rep].lower)*.5;
            bool absent=missing||(gap&&now>=middle&&now<middle+.3);
            motion.ReportFrame((float)now,55);if(absent)motion.SetAvailable(false);
            game.RuleScoring.Tick(1f/fps);
            if(zeroProcess&&(now<plan[rep].top||now>plan[rep].lower))
                Set(game.RuleScoring,"<TrackingQuality>k__BackingField",0f);
            assessment.ObserveBridge(1f/fps);
            if(SessionState.GetBool("FinalV2Right",false)&&fps==30&&!missing&&!gap&&!zeroProcess&&Math.Abs(now-plan[rep].end)<.04)
            {var side=game.RuleScoring.sideBridge;Log($"RIGHT END rep={rep} p={game.RuleScoring.Progress:0.000} q={game.RuleScoring.Quality:0.0} low={side.Rule.LowProgress:0.00} phase={side.Rule.Phase} lowConfirmed={side.Rule.LowConfirmed} finished={side.Rule.RoundFinished}");}
        }
        assessment.Finish();game.StopMode();
    }
    static void Rhythm(int fps,bool missing,bool zeroProcess=false)
    {
        var coach=entry.coach;var source=entry.scorer;Set(source,"userAnimator",source.CoachAnimator);Set(source,"calibrated",true);
        var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();var up=(string[])Get(coach,"upSegmentStates");var down=(string[])Get(coach,"downSegmentStates");
        var profile=Call(coach,"GetSelectedDifficultyProfile");float a=(float)profile.GetType().GetField("upSpeed").GetValue(profile),b=(float)profile.GetType().GetField("downSpeed").GetValue(profile);
        Call(assessment,"BeginRhythm");int count=coach.AssessmentDurations.Length;
        for(int rep=0;rep<count;rep++)for(int direction=0;direction<2;direction++)
        {
            string state=direction==0?up[rep%up.Length]:down[rep%down.Length];float speed=direction==0?a:b;
            Call(assessment,"BeginSegment",new CoachActionController.SegmentPlaybackInfo(state,speed));double duration=coach.SegmentSeconds(state,speed);
            for(int frame=1;frame<=Math.Ceiling(duration*fps);frame++)
            {
                double t=Math.Min(duration,frame/(double)fps);source.CoachAnimator.Play(state,0,(float)(t/duration));source.CoachAnimator.Update(0);
                motion.ReportFrame((float)t,55);if(missing)motion.SetAvailable(false);Call(assessment,"Update");
                var engine=(AnatomicalMotionScoringEngine)Get(source,"scoringEngine");engine?.ResetSession();Call(source,"ScoreCurrentPose");
                if(zeroProcess)
                {
                    var scores=source.LastLimbScores;scores.leftArm.rawScore=scores.rightArm.rawScore=scores.leftLeg.rawScore=scores.rightLeg.rawScore=0;
                    Set(source,"lastLimbScores",scores);
                }
                Call(assessment,"SampleRhythm",Math.Min(1d/fps,duration-(frame-1)/(double)fps));
            }
            Call(assessment,"EndSegment",new CoachActionController.ScoringCheckpointInfo(state,0,1,rep,direction==0,0));
        }
        assessment.Finish();
    }
    public static void Capture(string name)
    {
        var canvases=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.isActiveAndEnabled&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var host=new GameObject("Final score capture");var camera=host.AddComponent<Camera>();camera.transform.position=new Vector3(1000,1000,-1000);camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var rt=RenderTexture.GetTemporary(1688,950,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
            foreach(var c in canvases)if(c.GetComponent<TrainingResultPanel>()!=null)
                typeof(UnityEngine.UI.CanvasScaler).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.GetComponent<UnityEngine.UI.CanvasScaler>(),null);
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(host);RenderTexture.ReleaseTemporary(rt);}
    }
    static object Get(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
    static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,P).Invoke(o,args);
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
