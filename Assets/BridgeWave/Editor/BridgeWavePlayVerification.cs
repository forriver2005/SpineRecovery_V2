using System;
using System.IO;
using System.Reflection;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BridgeWave.EditorTools
{
    [InitializeOnLoad]
    public static class BridgeWavePlayVerification
    {
        const string Flag="BridgeWave.PlayVerification";
        const string FlowFlag="BridgeWave.FlowVerification";
        static BridgeWaveFollowController ui;
        static double began,pauseBegan;
        static int phase,offHits;
        static float pauseTime, coachFrame;
        static double networkBegan;
        static int syntheticFrame;
        static string report;
        static BridgeWavePlayVerification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
        public static void BuildAndRun()
        {
            BridgeWaveFollowBuilder.Build();
            Run();
        }
        [MenuItem("Tools/Bridge Wave/Verify Play and Capture Screenshots")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
            SessionState.SetBool(FlowFlag,false);
            phase=0;offHits=0;syntheticFrame=0;
            BridgeWaveFollowVerification.Run();
            SessionState.SetBool(Flag,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        public static void BuildAndRunFlow()
        {
            BridgeWaveFollowBuilder.Build();
            RunFlow();
        }
        [MenuItem("Tools/Bridge Wave/Verify Flow Test Without Sensors")]
        public static void RunFlow()
        {
            Run();
            SessionState.SetBool(FlowFlag,true);
        }
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Tick()
        {
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            if(SessionState.GetBool(FlowFlag,false)){TickFlow();return;}
            try
            {
                if(phase==0)
                {
                    began=EditorApplication.timeSinceStartup;report="Play mode checks (synthetic input, no hardware calibration claim)\n";
                    ui=UnityEngine.Object.FindObjectOfType<BridgeWaveFollowController>();Check(ui!=null,"Controller not found");

                    // Verify strict sensor training independently of the scene's temporary flow-test switch.
                    ui.skipCalibrationForFlowTest=false;ui.RefreshHud();
                    Check(!ui.CanStart&&ui.Session.Elapsed==0,"Uncalibrated sensor must not start");
                    ui.input.motion.SetAvailable(false);ui.Begin();Check(ui.State==BridgeWaveFollowController.RunState.Ready,"Missing sensor started session");
                    Check(ui.startMenu.IsTrainingMenuVisible&&!ui.visualRoot.activeSelf,"Shared start menu must occlude gameplay");
                    Check(ui.calibrationButton!=null,"Single supine calibration control missing");
                    Capture("unified-menu",1688,780);
                    Capture("unified-menu-16x9",1280,720);
                    Capture("unified-menu-4x3",1024,768);
                    coachFrame=ui.coachPreview.SourceFrame;
                    ui.visualRoot.SetActive(true);
                    phase=98;networkBegan=EditorApplication.timeSinceStartup;return;
                }
                if(phase==98)
                {
                    SendVmcFrame();
                    if(EditorApplication.timeSinceStartup-networkBegan>.3 && ui.input.Fresh)
                    {
                        Check(!ui.input.Calibrated&&!ui.CanStart,"Packets alone bypassed calibration");
                        phase=99;networkBegan=EditorApplication.timeSinceStartup;return;
                    }
                    Check(EditorApplication.timeSinceStartup-networkBegan<3,"Real UDP/VMC receiver did not report fresh input");
                    return;
                }
                if(phase==99)
                {
                    if(EditorApplication.timeSinceStartup-networkBegan<.8)return;
                    Check(!ui.input.Fresh&&!ui.CanStart,"UDP silence retained live input");
                    report+="PASS real localhost UDP 39539 -> uOSC -> EVMC4U -> motion-source input; silence invalidates freshness.\n";
                    ui.visualRoot.SetActive(true);phase=100;return;
                }
                if(phase==100)
                {
                    if(ui.game.transform.Find("TrackBandInner")==null)return;
                    ui.visualRoot.SetActive(true);
                    foreach(var receiver in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())receiver.enabled=false;
                    foreach(var bridge in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())bridge.enabled=false;
                    TestCalibration();
                    Check(typeof(BridgeWaveHeightInput).GetField("manualHeight")==null,"Manual runtime input remains");
                    ui.startMenu.SelectSimpleDifficulty();Check(ui.startMenu.SelectedDifficultyKey=="simple","Simple difficulty");
                    ui.startMenu.SelectHardDifficulty();Check(ui.startMenu.SelectedDifficultyKey=="hard","Hard difficulty");
                    ui.startMenu.SelectStandardDifficulty();
                    Feed(0);ui.RefreshHud();networkBegan=EditorApplication.timeSinceStartup;phase=103;return;
                }
                if(phase==103)
                {
                    Feed(0);ui.RefreshHud();
                    if(EditorApplication.timeSinceStartup-networkBegan<.2)return;
                    Check(ui.CanStart&&ui.startButton.interactable,"Stable calibration did not enable Start");
                    Capture("calibrated-ready");
                    report+="PASS shared start menu, working difficulty controls, one-button supine calibration and no manual slider.\n";
                    ui.startButton.onClick.Invoke();Check(ui.State==BridgeWaveFollowController.RunState.Countdown,"Start button countdown");
                    phase=1;return;
                }
                Check(EditorApplication.timeSinceStartup-began<65,"Play verification timeout");
                if(phase==1)
                {
                    Feed(0);
                    if(ui.CountdownRemaining < 4.5f && offHits == 0)
                    {
                        Capture("unified-countdown");
                        ui.pauseButton.onClick.Invoke();pauseTime=ui.CountdownRemaining;coachFrame=ui.coachPreview.SourceFrame;
                        pauseBegan=EditorApplication.timeSinceStartup;offHits=1;phase=101;return;
                    }
                    if(ui.State==BridgeWaveFollowController.RunState.Playing){phase=2;report+="PASS actual Start button and countdown.\n";}
                }
                else if(phase==101)
                {
                    Feed(0);
                    if(EditorApplication.timeSinceStartup-pauseBegan>.3)
                    {
                        Check(Mathf.Approximately(ui.CountdownRemaining,pauseTime),"Paused countdown advanced");
                        Check(Mathf.Approximately(ui.coachPreview.SourceFrame,coachFrame),"Paused coach moved");
                        ui.pauseButton.onClick.Invoke();phase=102;pauseTime=ui.CountdownRemaining;
                        ui.input.motion.SetAvailable(false);pauseBegan=EditorApplication.timeSinceStartup;
                    }
                }
                else if(phase==102)
                {
                    ui.input.motion.SetAvailable(false);
                    if(EditorApplication.timeSinceStartup-pauseBegan>.3)
                    {
                        Check(Mathf.Abs(ui.CountdownRemaining-pauseTime)<.05f,"Countdown must wait on lost sensor");
                        Feed(0);phase=1;
                        report+="PASS five-second shared countdown; pause and sensor loss freeze preparation.\n";
                    }
                }
                else if(phase==2)
                {
                    Feed(ui.game.Level(ui.Session.Elapsed+.005f));
                    if(ui.Session.Elapsed>2.4f)
                    {
                        Check(ui.game.inTrack&&ui.Session.Hits>0,"In-track live input must emit HIT");
                        CheckCoachPhase(BridgeWaveGame.Phase.Lift);Capture("smooth-lift-hit");offHits=ui.Session.Hits;phase=3;Feed(-.18f);
                    }
                }
                else if(phase==3)
                {
                    Feed(-.18f);
                    if(ui.Session.Elapsed>3.35f)
                    {
                        Check(!ui.game.inTrack&&ui.Session.Hits<=offHits+1,"Off-track input incorrectly emitted HIT");
                        Check(ui.game.burstGold.color.a==0&&ui.game.burstLilac.color.a==0,"Hit bursts visible outside track");
                        Check(Mathf.Abs(ui.game.orb.position.y-DesignSpace.ToWorld(ui.game.orbX,ui.game.bottomY-(ui.game.bottomY-ui.game.topY)*ui.input.Height).y)<.01f,"Orb height snapped to target");
                        Capture("smooth-off-track");report+="PASS off-track orb continues forward; no new HIT; bursts hidden.\n";phase=4;
                    }
                }
                else if(phase==4)
                {
                    Feed(1);
                    if(ui.Session.Elapsed>3.85f)
                    {
                        Check(ui.game.inTrack,"Re-entering track must resume HIT");CheckCoachPhase(BridgeWaveGame.Phase.Hold);Capture("smooth-hold-hit");
                        ui.pauseButton.onClick.Invoke();pauseTime=ui.Session.Elapsed;coachFrame=ui.coachPreview.SourceFrame;pauseBegan=EditorApplication.timeSinceStartup;phase=5;
                    }
                }
                else if(phase==5)
                {
                    if(EditorApplication.timeSinceStartup-pauseBegan>.25)
                    {
                        Check(ui.Session.Elapsed==pauseTime&&!ui.game.judging,"Pause did not freeze track / judgement");
                        Check(Mathf.Approximately(ui.coachPreview.SourceFrame,coachFrame),"Pause did not freeze recorded coach");
                        Capture("paused");ui.pauseButton.onClick.Invoke();Check(ui.State==BridgeWaveFollowController.RunState.Playing,"Resume failed");phase=6;
                        report+="PASS pause freezes progression, recorded coach and feedback; lift/hold/lower coach phases match the track.\n";
                    }
                }
                else if(phase==6)
                {
                    Feed(ui.game.Level(ui.Session.Elapsed+.005f));
                    if(ui.Session.Elapsed>5.7f&&ui.Session.Elapsed<5.8f){CheckCoachPhase(BridgeWaveGame.Phase.Lower);Capture("smooth-lower-hit");}
                    if(ui.State==BridgeWaveFollowController.RunState.Finished)
                    {
                        ui.RefreshHud();Check(ui.finishPanel.activeSelf&&ui.game.counterText.text=="4","End panel / count incorrect");Capture("result");
                        report+=$"PASS four standard-motion tracks completed; {ui.Session.Hits} pulses, {ui.Session.InTrackSeconds:0.00}s in corridor.\n";
                        ui.replayButton.onClick.Invoke();Check(ui.State==BridgeWaveFollowController.RunState.Ready&&ui.Session.Hits==0&&ui.Session.Elapsed==0,"Replay didn't reset");
                        Check(ui.startMenu.IsTrainingMenuVisible&&!ui.visualRoot.activeSelf,"Replay must return to unified menu");
                        ui.input.motion.SetAvailable(false);ui.input.Read();ui.RefreshHud();
                        Check(!ui.startButton.interactable,"Replay cannot bypass disconnected sensor");
                        report+="PASS replay resets time, counters and feedback and restores the shared sensor-gated menu.\n";File.WriteAllText("output/bridgewave/play-verification.txt",report);Debug.Log(report);Finish(0);
                    }
                }
            }
            catch(Exception ex){File.WriteAllText("output/bridgewave/play-verification.txt",report+"FAIL "+ex);Debug.LogException(ex);Finish(1);}
        }
        static void TickFlow()
        {
            try
            {
                if(phase==0)
                {
                    began=EditorApplication.timeSinceStartup;report="Flow preview verification (no wearable input, no training results)\n";
                    ui=UnityEngine.Object.FindObjectOfType<BridgeWaveFollowController>();
                    Check(ui!=null&&ui.skipCalibrationForFlowTest,"Scene must enable requested temporary bypass");
                    ui.input.motion.SetAvailable(false);ui.input.Read();ui.RefreshHud();
                    Check(ui.CanStart&&ui.startButton.interactable&&!ui.input.Calibrated,"Flow start still requires calibration or sensor");
                    Check(!ui.calibrationButton.gameObject.activeInHierarchy&&!ui.calibrationStatus.gameObject.activeInHierarchy,"Calibration controls shown during bypass");
                    Check(!ui.coachPreview.previewCamera.enabled&&!ui.coachPreview.trainingPanel.activeInHierarchy,"Coach must not appear on preparation page");
                    Check(ui.startMenu.GetComponentsInChildren<RawImage>(true).Length==0,"Ready menu still contains a coach image");
                    Check(ui.startMenu.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.name=="Title"&&t.text=="臀桥"),"Menu title must be Hip Thrust only");
                    Check(!ui.startMenu.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Curve follow tutorial"),"Gameplay instruction popup must be removed");
                    ui.startMenu.SelectHardDifficulty();ui.startMenu.SelectSimpleDifficulty();ui.startMenu.SelectStandardDifficulty();
                    phase=1;networkBegan=EditorApplication.timeSinceStartup;return;
                }
                Check(EditorApplication.timeSinceStartup-began<55,"Flow preview timed out");
                ui.input.motion.SetAvailable(false);ui.input.Read();
                if(phase==1)
                {
                    if(EditorApplication.timeSinceStartup-networkBegan<.25)return;
                    Capture("flow-ready");Capture("flow-ready-16x9",1280,720);Capture("flow-ready-4x3",1024,768);
                    ui.startButton.onClick.Invoke();Check(ui.State==BridgeWaveFollowController.RunState.Countdown,"Sensor-free Start did not enter countdown");
                    Check(ui.coachPreview.previewCamera.enabled&&ui.coachPreview.trainingPanel.activeInHierarchy,"Training coach missing after Start");
                    report+="PASS preparation layout: logo/title, difficulty and Start; no coach, tutorial popup or calibration controls.\n";
                    phase=2;return;
                }
                if(phase==2)
                {
                    if(ui.CountdownRemaining>4.5f)return;
                    Capture("flow-countdown");ui.TogglePause();pauseTime=ui.CountdownRemaining;pauseBegan=EditorApplication.timeSinceStartup;phase=3;return;
                }
                if(phase==3)
                {
                    if(EditorApplication.timeSinceStartup-pauseBegan<.3)return;
                    Check(ui.CountdownRemaining==pauseTime,"Paused countdown moved");ui.TogglePause();phase=4;return;
                }
                if(phase==4)
                {
                    if(ui.State!=BridgeWaveFollowController.RunState.Playing||ui.Session.Elapsed<2)return;
                    Capture("flow-playing");ui.TogglePause();pauseTime=ui.Session.Elapsed;coachFrame=ui.coachPreview.SourceFrame;pauseBegan=EditorApplication.timeSinceStartup;phase=5;return;
                }
                if(phase==5)
                {
                    if(EditorApplication.timeSinceStartup-pauseBegan<.3)return;
                    Check(ui.Session.Elapsed==pauseTime&&ui.coachPreview.SourceFrame==coachFrame,"Paused track or coach moved");
                    Capture("flow-paused");ui.TogglePause();phase=6;return;
                }
                if(phase==6)
                {
                    Check(!ui.game.judging&&ui.Session.Hits==0,"Flow preview recorded training feedback");
                    if(ui.State!=BridgeWaveFollowController.RunState.Finished)return;
                    Check(ui.finishPanel.activeInHierarchy&&ui.game.counterText.text=="4","Flow did not finish all repetitions");
                    Capture("flow-result");ui.replayButton.onClick.Invoke();
                    Check(ui.State==BridgeWaveFollowController.RunState.Ready&&ui.CanStart&&ui.Session.Elapsed==0,"Replay failed without sensors");
                    Check(!ui.coachPreview.previewCamera.enabled&&!ui.coachPreview.trainingPanel.activeInHierarchy,"Coach visible after replay");
                    ui.skipCalibrationForFlowTest=false;ui.RefreshHud();
                    Check(!ui.CanStart&&!ui.startButton.interactable&&ui.calibrationButton.gameObject.activeInHierarchy,"Disabling bypass failed to restore admission");
                    ui.skipCalibrationForFlowTest=true;ui.RefreshHud();ui.startButton.onClick.Invoke();ui.menuButton.onClick.Invoke();
                    Check(ui.CanStart&&ui.State==BridgeWaveFollowController.RunState.Ready,"Return to setup from countdown failed");
                    report+="PASS no-device Start -> five-second countdown -> four repetitions -> result -> replay; pause/resume and return to preparation.\n";
                    report+="PASS coach only appears after Start and freezes on pause; no HIT or training results in flow preview; disabling bypass restores sensor/calibration gate.\n";
                    Directory.CreateDirectory("output/bridgewave");File.WriteAllText("output/bridgewave/flow-verification.txt",report);Debug.Log(report);Finish(0);
                }
            }
            catch(Exception ex){Directory.CreateDirectory("output/bridgewave");File.WriteAllText("output/bridgewave/flow-verification.txt",report+"FAIL "+ex);Debug.LogException(ex);Finish(1);}
        }
        static void SendVmcFrame()
        {
            using(var socket=new UdpClient())
            {
                void Text(List<byte> bytes,string value){bytes.AddRange(Encoding.UTF8.GetBytes(value));bytes.Add(0);while(bytes.Count%4!=0)bytes.Add(0);}
                void Send(string address,params object[] values)
                {
                    var bytes=new List<byte>();Text(bytes,address);
                    Text(bytes,","+string.Concat(values.Select(v=>v is string?"s":v is int?"i":"f")));
                    foreach(var value in values)
                    {
                        if(value is string text){Text(bytes,text);continue;}
                        byte[] data=value is int integer?BitConverter.GetBytes(integer):BitConverter.GetBytes((float)value);
                        if(BitConverter.IsLittleEndian)Array.Reverse(data);bytes.AddRange(data);
                    }
                    byte[] packet=bytes.ToArray();socket.Send(packet,packet.Length,"127.0.0.1",BridgeWaveHeightInput.Port);
                }
                Send("/VMC/Ext/OK",1);Send("/VMC/Ext/T",(float)EditorApplication.timeSinceStartup);
                foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if(bone==HumanBodyBones.LastBone)continue;
                    var transform=ui.input.patient.GetBoneTransform(bone);if(transform==null)continue;
                    var p=transform.localPosition;var r=transform.localRotation;
                    Send("/VMC/Ext/Bone/Pos",bone.ToString(),p.x,p.y,p.z,r.x,r.y,r.z,r.w);
                }
            }
        }
        static void TestCalibration()
        {
            var input=ui.input;var animator=input.patient;var saved=animator.runtimeAnimatorController;
            animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Movements/Recorded20261018/HipThrust_Recorded.controller");animator.Rebind();animator.Update(0);
            var timeline=ui.game.standardTrack.source;var cycle=timeline.cycles[0];
            void Pose(int frame){animator.Play("hipthrust_routine",0,frame/timeline.sourceClip.frameRate/timeline.sourceClip.length);animator.Update(0);input.motion.ReportFrame(frame,55);input.Read();}
            void ApplyToOrb()
            {
                typeof(BridgeWaveFollowController).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
                typeof(BridgeWaveGame).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui.game,null);
            }
            var step=typeof(BridgeWaveHeightInput).GetMethod("StepCalibration",BindingFlags.Instance|BindingFlags.NonPublic);
            void Step(){step.Invoke(input,new object[]{.02f});}
            input.motion.SetAvailable(false);input.BeginCalibration();Check(!input.Calibrating,"Disconnected calibration accepted");
            var hipBone=animator.GetBoneTransform(HumanBodyBones.Hips);
            var chestBone=animator.GetBoneTransform(HumanBodyBones.Chest);
            Pose(cycle.liftStartFrame);
            chestBone.position=hipBone.position+Vector3.up*.4f;
            input.motion.ReportFrame(1,55);input.Read();input.BeginCalibration();
            for(int i=0;i<80;i++){input.motion.ReportFrame(i,55);input.Read();Step();}
            Check(!input.Calibrated&&input.CalibrationProgress==0,"Non-lying posture completed calibration");
            input.CancelCalibration();
            Pose(cycle.liftStartFrame);Check(input.PoseReady,"Recorded low pose must qualify as supine rest");
            ui.calibrationButton.onClick.Invoke();Check(input.Calibrating&&!input.Calibrated,"One-button calibration did not begin");
            for(int i=0;i<30;i++){Pose(cycle.liftStartFrame);Step();}
            Check(!input.Calibrated&&input.CalibrationProgress>0,"Stable calibration completed too early");
            float before=input.CalibrationProgress;
            for(int i=0;i<80;i++)Step();
            Check(Mathf.Approximately(input.CalibrationProgress,before),"Repeated reads of one packet completed calibration");
            input.motion.SetAvailable(false);input.Read();Step();Check(input.CalibrationProgress==0,"Lost signal did not reset stability window");
            for(int i=0;i<30;i++){Pose(cycle.liftStartFrame);Step();}
            var chest=animator.GetBoneTransform(HumanBodyBones.Chest);chest.rotation=Quaternion.Euler(0,15,0)*chest.rotation;input.motion.ReportFrame(5,55);input.Read();Step();
            Check(input.CalibrationProgress==0,"Motion during calibration did not restart samples");
            for(int i=0;i<80;i++){Pose(cycle.liftStartFrame);Step();}
            Check(input.Calibrated&&!input.Calibrating,"Single resting pose did not complete calibration");
            Check(Mathf.Abs(input.highHeight-input.lowHeight-input.referenceHeightSpan)<.00001f,"Standard high target must come from baked reference excursion");
            float savedLow=input.lowHeight,savedHigh=input.highHeight;
            Pose(cycle.liftStartFrame);Check(Mathf.Abs(input.Height)<.001f,"Calibrated rest must map to zero");
            ApplyToOrb();float lowOrbY=ui.game.orb.position.y;
            Pose(cycle.holdStartFrame);ApplyToOrb();
            Check(ui.game.orb.position.y>lowOrbY+.4f,"Recorded raised hip must move orb upward after rest-only calibration");
            animator.runtimeAnimatorController=saved;animator.enabled=false;
            Feed(1);ApplyToOrb();float highOrbY=ui.game.orb.position.y;
            Feed(1.5f);ApplyToOrb();
            Check(input.AboveQualifiedHeight&&Mathf.Abs(input.Height-1)<.001f&&Mathf.Abs(ui.game.orb.position.y-highOrbY)<.001f,"150% standard lift must clamp to qualified ceiling");
            float plateau=ui.game.standardTrack.CycleStart(0)+ui.game.standardTrack.cycles[0].liftSeconds+.5f;
            Check(ui.Session.IsInsideCurve(plateau,input.Height,input.Valid),"Over-height sensor input must qualify at the hold plateau");
            input.lowHeight=savedLow;input.highHeight=savedHigh;
            Feed(0);ApplyToOrb();Check(Mathf.Abs(ui.game.orb.position.y-lowOrbY)<.001f,"Orb must descend again when hip returns below the ceiling");
            report+=$"PASS sensor-mode controller + orb rendering: low Y={lowOrbY:0.000}, high Y={highOrbY:0.000}; 150% input saturates at high Y; returning low moves orb down.\n";
            input.motion.SetAvailable(false);input.Read();Check(!input.Valid,"Stale input remains valid");animator.runtimeAnimatorController=saved;animator.enabled=false;
            report+="PASS one stable supine pose completes calibration; no high-pose step; non-lying posture, motion, repeated frames and disconnect cannot complete it.\n";
        }
        static void CheckCoachPhase(BridgeWaveGame.Phase expected)
        {
            var actual=ui.game.standardTrack.source.Evaluate(ui.coachPreview.SourceFrame);
            Check(actual.phase==(int)expected-1,"Recorded coach must match track phase: "+expected);
        }
        static void Feed(float height)
        {
            // Editor-only synthetic skeleton input; the scene contains no manual or auto-follow input path.
            var input=ui.input;var animator=input.patient;
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var right=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var otherFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var knee=animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var lp=left.position;var rp=right.position;
            float length=Vector3.Distance(left.position,knee.position)+Vector3.Distance(knee.position,foot.position);
            var p=hips.position;
            p.y=(foot.position.y+otherFoot.position.y)*.5f+length*Mathf.LerpUnclamped(input.lowHeight,input.highHeight,height);
            hips.position=p;left.position=lp;right.position=rp;
            input.motion.ReportFrame(++syntheticFrame,55);input.Read();
        }
        static void Capture(string name,int width=1688,int height=780)
        {
            var view=UnityEngine.Object.FindObjectOfType<BridgeWaveViewport>();var camera=view.view;
            view.ConfigureCanvas();typeof(BridgeWaveCoachPreview).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui.coachPreview,null);if(ui.coachPreview.previewCamera.enabled)ui.coachPreview.previewCamera.Render();
            var canvases=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.isActiveAndEnabled&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras=canvases.Select(c=>c.worldCamera).ToArray();var distances=canvases.Select(c=>c.planeDistance).ToArray();
            var previousRect=camera.rect;var previousTarget=camera.targetTexture;var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);target.antiAliasing=1;
            try
            {
                camera.rect=new Rect(0,0,1,1);camera.targetTexture=target;
                foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                Directory.CreateDirectory("output/bridgewave");File.WriteAllBytes("output/bridgewave/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=RenderMode.ScreenSpaceOverlay;canvases[i].worldCamera=cameras[i];canvases[i].planeDistance=distances[i];}
                camera.rect=previousRect;camera.targetTexture=previousTarget;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            }
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
    }
}
