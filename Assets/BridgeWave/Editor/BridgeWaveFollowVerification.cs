using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BridgeWave;

namespace BridgeWave.EditorTools
{
    public static class BridgeWaveFollowVerification
    {
        const string Output="output/bridgewave/verification.txt";
        [MenuItem("Tools/Bridge Wave/Verify Standard Follow Scene")]
        public static void Run()
        {
            var scene=EditorSceneManager.OpenScene(BridgeWaveFollowBuilder.ScenePath,OpenSceneMode.Single);
            var game=UnityEngine.Object.FindObjectOfType<BridgeWaveGame>(true);
            var input=UnityEngine.Object.FindObjectOfType<BridgeWaveHeightInput>(true);
            var ui=UnityEngine.Object.FindObjectOfType<BridgeWaveFollowController>(true);
            Check(game!=null&&game.standardTrack!=null&&input!=null&&ui!=null,"Scene components / standard track");
            Check(game.totalCount==4&&game.standardTrack.cycles.Length==4,"Four recorded standard-motion cycles");
            Check(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/BridgeWave/Sprites/bw_bg.png")!=null,"BridgeWave sprites imported as 2D sprites");
            Check(Mathf.Abs(game.tolerance-.08f)<.001f,"Visible track band matches the tightened judging tolerance");
            Check(Mathf.Approximately(BridgeWaveHeightInput.NormalizeHeight(1.5f,0,1),1f),"Height above qualified maximum must clamp to 1");
            Check(Mathf.Approximately(BridgeWaveHeightInput.NormalizeHeight(1f,0,1),1f),"Qualified maximum must map to 1");
            Check(Mathf.Approximately(BridgeWaveHeightInput.NormalizeHeight(-.5f,0,1),-.5f),"Only upper height is capped; lower input remains independent");
            Check(input.referenceHeightSpan>=.05f,"Standard height span baked on patient avatar");
            Check(typeof(BridgeWaveHeightInput).GetMethod("CaptureHigh")==null,"No patient high-pose calibration step");
            var track=game.standardTrack;
            var sb=new System.Text.StringBuilder("Standard follow scene verification\n");
            var timeline=track.source;timeline.Validate();
            for(int i=0;i<track.cycles.Length;i++)
            {
                var c=track.cycles[i];
                Check(Mathf.Abs(c.liftSeconds-(timeline.cycles[i].holdStartFrame-timeline.cycles[i].liftStartFrame)/timeline.sourceClip.frameRate)<.001f,"Recorded lift duration "+i);
                Check(Mathf.Abs(c.holdSeconds-(timeline.cycles[i].lowerStartFrame-timeline.cycles[i].holdStartFrame)/timeline.sourceClip.frameRate)<.001f,"Recorded hold duration "+i);
                Check(Mathf.Abs(c.lowerSeconds-(timeline.cycles[i].lowerEndFrame-timeline.cycles[i].lowerStartFrame)/timeline.sourceClip.frameRate)<.001f,"Recorded lowering duration "+i);
                float previous=-1;
                for(int s=0;s<=100;s++) { float y=c.lift.Evaluate(s*.01f);Check(y>=previous-.001f,"Recorded lift must rise monotonically "+i);previous=y; }
                previous=2;
                for(int s=0;s<=100;s++) { float y=c.lower.Evaluate(s*.01f);Check(y<=previous+.001f,"Recorded lowering must descend monotonically "+i);previous=y; }
                sb.AppendLine($"Cycle {i+1}: lift {c.liftSeconds:0.000}s, hold {c.holdSeconds:0.000}s, lower {c.lowerSeconds:0.000}s; recorded phase timing, smooth standard S-curve.");
            }
            foreach(var renderer in game.transform.root.GetComponentsInChildren<SpriteRenderer>(true))Check(renderer.sprite!=null,"Missing art: "+renderer.name);
            Check(UnityEngine.Object.FindObjectOfType<SimulatedPatient>()==null,"Training scene must not auto-follow");
            Check(UnityEngine.Object.FindObjectOfType<HipThrustRuleScoring>()==null,"No legacy score dependency");
            for(int i=0;i<track.cycles.Length;i++)
            {
                var c=track.cycles[i];
                Check(c.lift.length==2&&c.lower.length==2,"Two-key smooth standard profile, no mocap jitter");
                for(int k=0;k<=100;k++) {float u=k/100f,expected=u*u*(3-2*u);Check(Mathf.Abs(c.lift.Evaluate(u)-expected)<.0001f&&Mathf.Abs(c.lower.Evaluate(u)-(1-expected))<.0001f,"Smoothstep profile");}
            }
            var geometry=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);
            float duration=track.Duration(4),sampled=0;
            for(float t=0;t<duration;t+=.01f)
            {
                var expected=track.Evaluate(t,4);bool active=expected.phase!=BridgeWaveGame.Phase.Rest;
                bool match=geometry.IsInsideCurve(t,expected.level,true);
                bool highMiss=geometry.IsInsideCurve(t,expected.level+.4f,true)==false;
                bool lost=geometry.IsInsideCurve(t,expected.level,false)==false;
                Check(match,"Reference curve must be accepted");Check(highMiss&&lost,"Off-track and stale-input cases must be rejected");
                if(active)sampled+=.01f;
            }
            // Exercise both sides of the visible ribbon on lift, plateau and lowering.
            foreach(float at in new[]{2.2f,3.6f,5.7f})
            {
                float h=track.Evaluate(at,4).level;
                float dy=(track.Evaluate(at+.001f,4).level-track.Evaluate(at-.001f,4).level)*geometry.HeightPixels;
                var normal=new Vector2(-dy,geometry.SpeedPixels*.002f).normalized;
                foreach(float sign in new[]{-1f,1f})
                {
                    Vector2 inner=normal*(sign*geometry.RadiusPixels*.9f),outer=normal*(sign*geometry.RadiusPixels*1.1f);
                    Check(geometry.IsInsideCurve(at+inner.x/geometry.SpeedPixels,h+inner.y/geometry.HeightPixels,true),"Inside normal-offset corridor");
                    Check(!geometry.IsInsideCurve(at+outer.x/geometry.SpeedPixels,h+outer.y/geometry.HeightPixels,true),"Outside normal-offset corridor");
                }
            }
            float plateauTime=track.CycleStart(0)+track.cycles[0].liftSeconds+.5f;
            Check(geometry.IsInsideCurve(plateauTime,BridgeWaveHeightInput.NormalizeHeight(1.5f,0,1),true),"Above-qualified height remains inside the qualified hold plateau");
            sb.AppendLine("PASS: equal-width stroke judgement tested at 90%/110% of the normal half-width on lift, hold and lowering.");
            var overHeight=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);
            while(overHeight.Elapsed<plateauTime)overHeight.Advance(.01f,1.5f,true);
            Check(overHeight.Inside&&overHeight.Hits>0,"Full session awards HIT for above-qualified input on the hold plateau");
            float loweringTime=track.CycleStart(0)+track.cycles[0].liftSeconds+track.cycles[0].holdSeconds+track.cycles[0].lowerSeconds*.75f;
            while(overHeight.Elapsed<loweringTime)overHeight.Advance(.01f,1.5f,true);
            Check(!overHeight.Inside,"High input must still lower to follow the descending track");
            Check(!geometry.IsInsideCurve(plateauTime,1f,false),"Capped height cannot bypass stale-data protection");
            sb.AppendLine("PASS: above-qualified hip height saturates at 1.0 and earns plateau HIT; lowering still requires following the descending track; stale input still rejected.");
            var session=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);int pulses=0;
            for(float t=0;t<duration+.1f;t+=.01f) if(session.Advance(.01f,track.Evaluate(session.Elapsed,4).level,true))pulses++;
            Check(session.Finished&&session.InTrackSeconds>sampled-1.2f&&session.InTrackSeconds<=sampled+1.2f,"Correct height follows every scored track interval");
            Check(session.Hits==pulses&&pulses>30,"Continuous in-track hit effects ("+pulses+" pulses)");
            var missed=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);int missPulses=0;
            for(float t=0;t<duration+.1f;t+=.01f) { var expected=track.Evaluate(missed.Elapsed,4);if(missed.Advance(.01f,expected.level>.5f?-.2f:1f,true))missPulses++; }
            Check(missPulses==0&&missed.InTrackSeconds==0&&missed.Hits==0,"Out-of-corridor player continues without hit");
            var stale=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);
            for(float t=0;t<duration+.1f;t+=.01f)stale.Advance(.01f,track.Evaluate(stale.Elapsed,4).level,false);
            Check(stale.Hits==0&&stale.InTrackSeconds==0,"Lost VMC input cannot award hits");
            var pauseTest=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);
            pauseTest.Advance(.05f,0,true);float before=pauseTest.Elapsed;pauseTest.Pause(true);pauseTest.Advance(1,1,true);
            Check(Mathf.Approximately(pauseTest.Elapsed,before),"Pause freezes track judgement");pauseTest.Pause(false);
            pauseTest.Advance(.05f,0,true);Check(pauseTest.Elapsed>before,"Resume advances time");
            var restarted=new BridgeWaveSession(track,4,game.tolerance,game.speedPx,game.bottomY-game.topY,game.stepPx);Check(restarted.Elapsed==0&&restarted.Hits==0,"Restart starts a clean session");
            Check(ui.startButton!=null&&ui.calibrationButton!=null&&ui.pauseButton!=null&&ui.replayButton!=null,"Calibration and training controls are wired");
            Check(ui.startMenu!=null && ui.coachPreview!=null && ui.countdownText!=null,"Shared menu, countdown and recorded coach are wired");
            Check(ui.coachPreview.animator.runtimeAnimatorController!=null && ui.coachPreview.previewCamera!=null,"Existing recorded coach rig and preview camera");
            Check(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Slider>(true)).Count()==0,"No manual input slider");
            Check(typeof(BridgeWaveHeightInput).GetField("manualHeight")==null,"No runtime manual input path");
            Check(ui.startMenu.GetComponentsInChildren<RawImage>(true).Length==0,"Preparation menu has no coach window");
            Check(ui.coachPreview.trainingPanel!=null,"Coach belongs to gameplay HUD only");
            Check(!ui.skipCalibrationForFlowTest,"Shipped scene requires calibration and sensor admission");
            Check(ui.workout!=null&&ui.workout.bridgeWave==ui,"Workout entry is wired");
            int missing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check(missing==0,"No missing MonoBehaviour scripts");
            sb.AppendLine($"PASS: {pulses} continuous HIT pulse events when following; 0 when outside the band; 0 when sensor is stale; pause and reset verified.");
            Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,sb.ToString());Debug.Log(sb.ToString());
        }
        static void Check(bool ok,string message) { if(!ok)throw new Exception("Bridge Wave verification failed: "+message); }
    }
}
