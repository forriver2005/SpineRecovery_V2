using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>Prescription-to-animation regression with recorded poses and synthetic telemetry.</summary>
[InitializeOnLoad]
public static class PrescribedHoldVerification
{
    const string Flag="PrescribedHoldVerification.Active", Output="output/prescribed-hold/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Scenes={"HipThrust_RingBridge_Hold30","SideBridge_RingBridge_Hold30","SideBridgeRight_RingBridge_Hold30"};
    static int sceneIndex,stage,checks,workoutCompleted;
    static double started,pauseStarted,frozen;
    static GameWorkoutScene entry;
    static HipThrustTaikoController game;
    static HipThrustRingBridgeView view;
    static Quaternion[] frozenBones;
    static PrescribedHoldVerification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}

    [MenuItem("Tools/Ring Bridge/Verify Prescribed Hold Duration")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"verification.txt","Prescribed duration: real Unity scenes and recorded poses; synthetic input, no wearable validation.\n");
        try
        {
            foreach(string name in Scenes)
            {
                string path="Assets/Scenes/"+name+".unity";
                if(!EditorBuildSettings.scenes.Any(scene=>scene.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
            }
            Pure();sceneIndex=stage=0;started=EditorApplication.timeSinceStartup;
            EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[0]+".unity");
            SessionState.SetBool(Flag,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        catch(Exception ex){Fail(ex);}
    }
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static void Near(double a,double b,string text,double tolerance=.001)=>Check(Math.Abs(a-b)<tolerance,text+$" ({a} != {b})");
    static void Log(string text){File.AppendAllText(Output+"verification.txt",text+"\n");Debug.Log(text);}
    static void Reject(Action action,string text){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,text);}
    static void Pure()
    {
        foreach(string action in new[]{"hipthrust_hold30","sidebridge_hold30","rightsidebridge_hold30"})
        {
            var missing=JsonUtility.FromJson<GameWorkoutPlan>("{\"actions\":[{\"action\":\""+action+"\",\"difficulty\":\"standard\",\"sets\":2,\"reps\":3}]}").ValidatedCopy();
            Near(missing.actions[0].holdSeconds,30,"Old plan lost 30-second default");
            foreach(float seconds in new[]{0f,1f,12.5f,20f,30f,45f,600f})
            {
                var original=new GameWorkoutAction{action=action,sets=2,reps=3,holdSeconds=seconds};
                var copy=new GameWorkoutPlan{actions=new[]{original}}.ValidatedCopy().ValidatedCopy();
                Near(copy.actions[0].holdSeconds,seconds==0?30:seconds,"Plan copy lost hold duration");
                Near(original.holdSeconds,seconds,"Validation mutated caller");
                Check(GameWorkoutPlan.SceneFor(action)!=null,"Legacy action key no longer routes");
            }
        }
        foreach(float invalid in new[]{-1f,.5f,601f,float.NaN,float.PositiveInfinity})
            Reject(()=>new GameWorkoutPlan{actions=new[]{new GameWorkoutAction{action="hipthrust_hold30",reps=1,holdSeconds=invalid}}}.ValidatedCopy(),"Invalid duration accepted");
        Reject(()=>new GameWorkoutPlan{actions=new[]{new GameWorkoutAction{action="hipthrust",holdSeconds=20}}}.ValidatedCopy(),"Dynamic action silently accepted hold override");
        var host=new GameObject("Prescription parser test");
        try
        {
            var dose=host.AddComponent<HipThrustPrescriptionDose>();
            Check(dose.SetPrescriptionJson("{\"prescriptionContractVersion\":2,\"items\":[{\"itemId\":1,\"actionId\":\"5\",\"sets\":2,\"reps\":3,\"holdSeconds\":20},{\"itemId\":2,\"actionId\":\"5\",\"sets\":1,\"reps\":2,\"holdSeconds\":45}]}",2,out _),"D01 selection failed");
            Check(dose.Total==2&&dose.HoldSeconds==45,"D01 selected wrong item/duration");
            Check(!dose.SetDose(1,1,-1,out _)&&dose.Total==2&&dose.HoldSeconds==45,"Rejected duration corrupted existing prescription");
            Check(dose.SetPrescriptionJson("{\"prescriptionContractVersion\":2,\"items\":[{\"actionId\":\"5\",\"sets\":1,\"reps\":2}]}",0,out _)&&dose.HoldSeconds==30,"Missing D01 duration reused previous prescription");
            Check(dose.SetDose(1,1,20,out _)&&dose.SetDose(1,2,out _)&&dose.HoldSeconds==30,"Legacy SetDose did not restore default");
            dose.preserveRecordedTiming=true;
            Check(!dose.SetDose(1,1,20,out _)&&dose.Total==2,"Recorded timing silently overridden");
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
        string[] tracks={"Assets/RingBridge/RecordedHipThrustTimeline.asset","Assets/Movements/RecordedSideBridge/SideBridgeTimeline.asset","Assets/Movements/RecordedSideBridge/SideBridgeRightTimeline.asset"};
        foreach(string path in tracks)
        {
            var source=AssetDatabase.LoadAssetAtPath<HipThrustPhaseTimeline>(path);Check(source!=null,"Missing track: "+path);
            bool side=path.Contains("SideBridge");var baseline=new HipThrustHold30Timeline(source,7,false,side,side);
            foreach(float seconds in new[]{1f,12.5f,20f,30f,45f,600f})
            {
                var timeline=new HipThrustHold30Timeline(source,7,false,side,side,holdSeconds:seconds);
                Near(timeline.Duration-baseline.Duration,7*(seconds-30),"Changing hold changed other phase timing");
                for(int i=0;i<7;i++)
                {
                    var rep=timeline.Repetitions[i];var old=baseline.Repetitions[i];
                    Near(rep.lower-rep.top,seconds,"Hold window not prescribed");Near(rep.top-rep.start,old.top-old.start,"Lift speed changed");Near(rep.end-rep.lower,old.end-old.lower,"Lower speed changed");
                    foreach(float fraction in new[]{0f,.25f,.5f,.999f})
                    {
                        var sample=timeline.Evaluate(rep.top+seconds*fraction,out float frame,out float remaining,out _);
                        Check(sample.phase==1,"Hold phase wrong");Near(sample.progress,fraction,"Ring phase ratio wrong");Near(frame,rep.peakFrame,"Peak pose moved");Near(remaining,seconds*(1-fraction),"Countdown mismatch");
                    }
                    Check(timeline.Evaluate(rep.lower,out _,out _,out _).phase==2,"Lowering boundary late");
                }
                timeline.Evaluate(timeline.Duration,out _,out _,out int completed);Check(completed==7,"Prescription repetition count changed");
            }
            var dynamic=new HipThrustHold30Timeline(source,3,true,false,true);
            foreach(var rep in dynamic.Repetitions)Near(rep.lower-rep.top,(source.cycles[rep.cycle].lowerStartFrame-source.cycles[rep.cycle].holdStartFrame)/(double)source.sourceClip.frameRate,"Recorded timing changed");
        }
        foreach(int fps in new[]{30,60,120})foreach(double seconds in new[]{20d,30d,45d})
        {
            var score=new FinalTrainingScore();score.Reset(FinalTrainingScore.Mode.TimedHold,new[]{new FinalTrainingScore.Plan(seconds+4,seconds,2,2)});
            for(int frame=0;frame<seconds*fps;frame++){score.ObserveCoverage(0,1d/fps,true);score.ObserveHold(0,1d/fps,true,frame<seconds*.9*fps);}
            score.Finish();Near(score.ExactScore,90,"Prescribed hold denominator wrong");
        }
        Log("PASS contract: omitted/zero=30; 1/12.5/20/30/45/600 seconds; D01 item selection; invalid updates atomic; exact counts; dynamic timing unchanged; 90% hold=90 at 30/60/120 fps.");
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-started<300,"Runtime verification timeout");
            if(stage>=3){WorkoutTick();return;}
            if(stage==0)
            {
                entry=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(entry==null||entry.gameObject.scene.name!=Scenes[sceneIndex])return;
                game=entry.taiko;view=UnityEngine.Object.FindObjectOfType<HipThrustRingBridgeView>();if(view==null||entry.GetComponent<TrainingResultView>()?.Root==null)return;
                foreach(var receiver in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())receiver.enabled=false;
                foreach(var bridge in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())bridge.enabled=false;
                entry.menu.SelectSimpleDifficulty();Set(entry.scorer,"userAnimator",game.CoachAnimator);Set(entry.scorer,"calibrated",true);entry.scorer.StopAllCoroutines();
                VerifyRuntime();
                // Leave a real running transport at its halfway point to check that normal Update honours pause.
                entry.StopTraining();Check(entry.PrepareDose(new GameWorkoutAction{action=entry.actionId,sets=1,reps=1,holdSeconds=20},out _),"Pause setup failed");
                HideMenu();game.StartMode();var rep=game.AssessmentRepetitions()[0];Seek(rep.top+10);
                game.TogglePause();frozen=game.Elapsed;frozenBones=Bones(game.CoachAnimator);pauseStarted=EditorApplication.timeSinceStartup;stage=1;
            }
            else if(stage==1&&EditorApplication.timeSinceStartup-pauseStarted>.3)
            {
                Near(game.Elapsed,frozen,"Pause advanced prescribed clock");CompareBones(frozenBones,Bones(game.CoachAnimator));Near(view.CurrentSample.progress,.5,"Pause changed ring");
                game.TogglePause();Seek(game.Duration);Call(game,"LateUpdate");
                Check(game.CompletedDemonstrations==1&&!game.IsRunning,"Completion mismatch");entry.StopTraining();
                Log("PASS "+Scenes[sceneIndex]+": 20/45/default30, menu, coach/preview freeze, ring shader progress, remaining time, targets, actual-pose scoring, no-data and pause/completion.");
                sceneIndex++;if(sceneIndex==Scenes.Length){stage=3;started=EditorApplication.timeSinceStartup;SceneManager.LoadScene(Scenes[0]);return;}
                stage=0;SceneManager.LoadScene(Scenes[sceneIndex]);
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    static void VerifyRuntime()
    {
        foreach(float seconds in new[]{20f,45f,0f})
        {
            entry.StopTraining();var action=new GameWorkoutAction{action=entry.actionId,sets=2,reps=2,holdSeconds=seconds};
            Check(entry.PrepareDose(action,out string error),error);var dose=game.PrescriptionDose;float goal=seconds==0?30:seconds;
            Check(dose.Total==2&&dose.HoldSeconds==goal,"Workout lost seconds or multiplied sets twice");
            Check(dose.Label.Contains($"{goal:0.##} 秒"),"Menu has stale hold duration");
            if(seconds==20)Capture(Scenes[sceneIndex]+"-20s-menu");
            if(game.RuleScoring.sideBridge!=null)
            {
                Set(game,"hold30Timeline",null);game.RuleScoring.Begin("simple");
                Check(game.RuleScoring.TargetsReady,"Editor/preview target capture failed before timeline exists");
                Near(game.RuleScoring.sideBridge.Rule.TargetHoldSeconds,goal,"Preview ignores prescription duration");
            }
            HideMenu();Check(!dose.SetDose(1,3,12,out _),"Countdown allowed prescription replacement");game.StartMode();
            Check(game.BeatCount==2,"Beat count differs from exact dose");Check(!dose.SetDose(1,3,12,out _),"Running duration changed");
            var reps=game.AssessmentRepetitions();Near(reps[0].lower-reps[0].top,goal,"Transport ignores prescription");
            var assessment=entry.GetComponent<GameTrainingAssessment>();Near(assessment.FinalScore.TargetHold,2*goal,"Final target still fixed at 30");
            if(game.RuleScoring.sideBridge!=null)Near(game.RuleScoring.sideBridge.Rule.TargetHoldSeconds,goal,"Side goal still fixed at 30");
            else Near((float)Get(game.RuleScoring.Rule,"maximumSeconds"),goal,"Hip goal still fixed at 30");
            Seek(reps[0].top);var peak=Bones(game.CoachAnimator);Seek(reps[0].top+goal*.5);
            CompareBones(peak,Bones(game.CoachAnimator));CompareBones(peak,Bones(game.PreviewAnimator));Near(view.CurrentSample.progress,.5,"Ring not halfway at half prescribed duration");
            var properties=new MaterialPropertyBlock();view.chart.sections[1].progress.GetPropertyBlock(properties);Near(properties.GetFloat("_Progress"),.5,"Rendered arc not half filled");
            Check(((TMP_Text)Get(view,"coachTiming")).text.Contains((goal*.5f).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)),"Visible countdown still uses 30");
            var art=UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true);
            var arc=(SpriteRenderer)Get(art,"holdProgress");arc.GetPropertyBlock(properties);Near(properties.GetFloat("_Progress"),.5,"Visible artwork arc not half filled");
            if(seconds==20||seconds==45)Capture(Scenes[sceneIndex]+$"-{goal:0}s-half");
            if(sceneIndex==0&&seconds==45)
            {
                Capture("HipThrust-45s-half-4x3",1280,960);
                Capture("HipThrust-45s-half-wide",1920,888);
                Capture("HipThrust-45s-half-16x9",1688,950);
            }
            Seek(reps[0].lower);Check(view.CurrentSample.phase==2,"Ring lowering starts at wrong time");
            Replay(30,goal,false);var score=assessment.FinalScore;
            Check(score.CanPublish,"Full recorded replay lost coverage");Near(score.TargetHold,2*goal,"Replay target mismatch");
            if(sceneIndex<2)Check(score.Score>=90,"Recorded hold unexpectedly lost score: "+score.ExactScore);
            else Near(score.ValidHold,assessment.Summary.CreditedSeconds,"Right hold lost source-accepted evidence",.1);
            Log($"REPLAY {Scenes[sceneIndex]} {goal:0.##}s: {score.ValidHold:0.000}/{score.TargetHold:0.000}s, score={score.ExactScore:0.00}, continuous={score.ContinuousCompleted}/{score.Planned}");
            if(seconds==20)
            {
                var result=entry.GetComponent<TrainingResultView>();result.Refresh();
                Replay(60,goal,true);Check(!assessment.FinalScore.CanPublish&&assessment.FinalScore.ValidHold==0,"Missing input earned hold time");
            }
        }
    }
    static void WorkoutTick()
    {
        var current=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(current==null||current.gameObject.scene.name!=Scenes[0])return;
        entry=current;game=entry.taiko;view=UnityEngine.Object.FindObjectOfType<HipThrustRingBridgeView>();
        foreach(var receiver in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())receiver.enabled=false;
        foreach(var bridge in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())bridge.enabled=false;
        entry.scorer.StopAllCoroutines();Set(entry.scorer,"userAnimator",game.CoachAnimator);Set(entry.scorer,"calibrated",true);
        var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();motion.Configure(game.CoachAnimator);motion.ReportFrame(Time.unscaledTime,55);
        var session=GameWorkoutSession.Instance;
        if(stage==3)
        {
            var plan=new GameWorkoutPlan{actions=new[]{
                new GameWorkoutAction{action="hipthrust_hold30",difficulty="simple",sets=1,reps=2,holdSeconds=20},
                new GameWorkoutAction{action="hipthrust_hold30",difficulty="standard",sets=1,reps=1,holdSeconds=45},
                new GameWorkoutAction{action="hipthrust_hold30",difficulty="hard",sets=1,reps=1}}};
            Check(session.SetPlan(plan,out string error),error);Check(session.StartPlan(out error),error);workoutCompleted=0;stage=4;return;
        }
        if(!game.IsRunning)return;
        float expected=workoutCompleted==0?20:workoutCompleted==1?45:30;int reps=workoutCompleted==0?2:1;
        Near(game.Hold30Timeline.PrescribedHoldSeconds,expected,"Live executor reused previous duration");Check(game.BeatCount==reps,"Live executor changed repetition count");
        Seek(game.Duration);Call(game,"LateUpdate");workoutCompleted++;
        Log($"WORKOUT completed {workoutCompleted}/3: {reps} x {expected}s, rest={session.RestSecondsRemaining}, next={session.ActionIndex}");
        if(workoutCompleted<3)
        {
            Check(session.IsRunning&&session.RestSecondsRemaining==30,"Hold duration altered 30-second inter-set rest");
            entry.menu.StartTraining();Check(!game.IsRunning,"Manual start bypassed group rest");
        }
        else
        {
            Check(session.IsComplete&&!session.IsRunning,"Live plan did not complete");
            Log("PASS live workout: 2 reps x 20s -> 1 rep x 45s -> omitted/default30; real automatic countdown and 30s rests; accelerated playback completion, synthetic calibration.");
            Log("PASS all prescribed-hold checks: "+checks);Finish(0);
        }
    }
    static void Replay(int fps,float goal,bool missing)
    {
        Set(entry.scorer,"calibrated",true);game.StartMode();var motion=UnityEngine.Object.FindObjectOfType<AvatarMotionSource>();var assessment=entry.GetComponent<GameTrainingAssessment>();
        for(int frame=1;frame<=Math.Ceiling(game.Duration*fps);frame++)
        {
            double now=Math.Min(game.Duration,frame/(double)fps);Set(game,"elapsed",now);Call(game,"RenderTimeline");motion.ReportFrame((float)now,55);if(missing)motion.SetAvailable(false);
            game.RuleScoring.Tick(1f/fps);assessment.ObserveBridge(1f/fps);
        }
        assessment.Finish();game.StopMode();
    }
    static void HideMenu()
    {
        ((GameObject)Get(entry.menu,"menuRoot")).SetActive(false);
        ((GameObject)Get(entry.menu,"gamingCanvasRoot")).SetActive(true);
    }
    static void Seek(double seconds){Set(game,"elapsed",seconds);Call(game,"RenderTimeline");Call(view,"LateUpdate");UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true).LateUpdate();}
    static Quaternion[] Bones(Animator animator)=>Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>animator.GetBoneTransform((HumanBodyBones)i)).Select(t=>t!=null?t.localRotation:Quaternion.identity).ToArray();
    static void CompareBones(Quaternion[] a,Quaternion[] b){for(int i=0;i<a.Length;i++)Check(Quaternion.Angle(a[i],b[i])<.08f,"Frozen/preview pose changed: "+(HumanBodyBones)i);}
    static void Capture(string name,int width=1688,int height=950)
    {
        view.SetCaptureResolution(width,height);Call(view,"LateUpdate");UnityEngine.Object.FindObjectOfType<RecordedDrumArtwork>(true).LateUpdate();var framing=UnityEngine.Object.FindObjectOfType<HipThrustPreviewFraming>();if(framing!=null){Call(framing,"LateUpdate");framing.previewCamera.Render();}view.presentationCamera.Render();
        var canvases=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.isActiveAndEnabled&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var host=new GameObject("Prescribed hold capture");var camera=host.AddComponent<Camera>();camera.transform.position=new Vector3(1000,1000,-1000);camera.orthographic=true;camera.cullingMask=~(1<<29);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var previous=RenderTexture.active;
        var centre=view.presentationCamera.transform.position+Vector3.forward*10;
        var projectedX=view.presentationCamera.WorldToScreenPoint(centre+Vector3.right)-view.presentationCamera.WorldToScreenPoint(centre);
        var projectedY=view.presentationCamera.WorldToScreenPoint(centre+Vector3.up)-view.presentationCamera.WorldToScreenPoint(centre);
        Near(projectedX.magnitude/projectedY.magnitude,1,"Capture stretches circular geometry",.001);
        Near(view.Composition.width/(double)view.Composition.height,width/(double)height,"Render/output aspect mismatch");
        var preview=(RectTransform)Get(view,"previewPanelRect");
        if(!entry.menu.IsTrainingMenuVisible)Near(preview.localScale.x,height/(view.presentationCamera.orthographicSize*200),"Coach scale uses a different resolution");
        try
        {
            camera.targetTexture=rt;foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(host);RenderTexture.ReleaseTemporary(rt);}
    }
    static object Get(object o,string name)=>o.GetType().GetField(name,P).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,P).SetValue(o,value);
    static object Call(object o,string name)=>o.GetType().GetMethod(name,P).Invoke(o,null);
    static void Fail(Exception ex){Log("FAIL "+ex);Debug.LogException(ex);Finish(1);}
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
