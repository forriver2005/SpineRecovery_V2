using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FinalSpecialGameVerification
{
    const string Flag="FinalSpecialGameVerification",Output="output/final-score-v2/";
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Scenes={"CrunchPractice_TaikoNight","LyingCatPractice_TaikoNight"};
    static int index;static double start;
    static FinalSpecialGameVerification(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
    public static void Run()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"special-games.txt","Taiko settlement v2 replay; source action scorers are unchanged.\n");
        index=0;start=EditorApplication.timeSinceStartup;EditorSceneManager.OpenScene("Assets/Scenes/"+Scenes[0]+".unity");
        SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static void Log(string t){File.AppendAllText(Output+"special-games.txt",t+"\n");Debug.Log(t);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(EditorApplication.timeSinceStartup-start<180,"Special scene timeout");
            var runtime=UnityEngine.Object.FindObjectOfType<TaikoNight.TaikoNightActionRuntime>();
            if(runtime==null||runtime.gameObject.scene.name!=Scenes[index]||Get(runtime,"selection")==null)return;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>())r.enabled=false;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<VmcGameModeSourceBridge>())r.enabled=false;
            runtime.enabled=false;
            var full=Replay(runtime,30,false,false);var half=Replay(runtime,30,true,false);var fps60=Replay(runtime,60,false,false);
            Log($"{Scenes[index]}: all repetitions={full:0.00}; one of two={half:0.00}; 60fps={fps60:0.00}");
            Check(full>20,"Recording/scorer fixture produced no useful target evidence");
            Check(half<full*.7,"Last good repetition replaced whole-session dose");
            Check(Mathf.Abs(full-fps60)<5,"Special settlement depends excessively on frame rate");
            Replay(runtime,30,false,true);Check(!runtime.FinalScore.CanPublish,"Special result fabricated data");
            Replay(runtime,30,false,false);Call(runtime,"ShowFinalResult");
            Check(runtime.ResultPanel.gameObject.activeInHierarchy&&runtime.ResultPanel.ScoreLabel.text==runtime.FinalScore.Score.ToString(),"Special game not using shared settlement panel");
            FinalScoringV2Verification.Capture(Scenes[index]);
            runtime.ResultPanel.ReturnButton.onClick.Invoke();
            Check(!runtime.ResultPanel.gameObject.activeSelf&&runtime.IsMenuVisible,"Shared return did not restore special-game menu");
            var selection=(TaikoNight.TaikoNightSelection)Get(runtime,"selection");
            Check(string.IsNullOrEmpty(((TMPro.TMP_Text)Get(selection,"resultLabel")).text),"Preparation page contains settlement text");
            FinalScoringV2Verification.Capture(Scenes[index]+"-ready");
            index++;if(index==Scenes.Length){Log("PASS both special games: per-planned-repetition aggregation and missing-data status.");Finish(0);}else SceneManager.LoadScene(Scenes[index]);
        }
        catch(Exception e){Log("FAIL "+e);Debug.LogException(e);Finish(1);}
    }
    static float Replay(TaikoNight.TaikoNightActionRuntime runtime,int fps,bool missFirst,bool missing)
    {
        var scorer=(ITaikoHoldScorer)Get(runtime,"scorer");var motion=(ICoachMotionBackend)Get(runtime,"motion");
        var seconds=(Vector3)Get(runtime,"phaseSeconds");var source=runtime.recorder.CoachAnimator;var patient=runtime.recorder.UserAnimator;
        AvatarMotionSource input=null;foreach(var m in UnityEngine.Object.FindObjectsOfType<AvatarMotionSource>())if(m.Model==patient.gameObject){input=m;break;}
        Check(input!=null,"No patient motion source");
        runtime.SetWorkoutDifficulty("standard");Check(runtime.SetRepetitionDose(2,out string error),error);Call(runtime,"BeginFinalScore");scorer.BeginContinuousSession();
        using(var from=new HumanPoseHandler(source.avatar,source.transform))using(var to=new HumanPoseHandler(patient.avatar,patient.transform))
        {
            motion.ReturnIdle();var rest=new HumanPose();from.GetHumanPose(ref rest);
            // Let the existing scorer recognize its preparation pose without adding settlement time.
            for(int n=0;n<fps;n++){to.SetHumanPose(ref rest);input.ReportFrame(n,55);Call(scorer,"Sample",1f/fps);}
            for(int rep=0;rep<2;rep++)
            {
                Set(runtime,"completedRepetitions",rep);double duration=seconds.x+seconds.y+seconds.z;
                for(int n=1;n<=Math.Ceiling(duration*fps);n++)
                {
                    float time=(float)Math.Min(duration,n/(double)fps);double lower=seconds.x+seconds.y;
                    if(time<=lower)
                    {
                        motion.PlaySegment(1);
                        double extra=HipThrustPhaseTimeline.ConsumedHoldExtension(time,seconds.x,lower,TaikoNight.TaikoNightChart.AddedHoldSeconds);
                        motion.Tick((float)(time-extra));
                    }
                    else{motion.PlaySegment(2);motion.Tick((float)(time-lower));}
                    var pose=new HumanPose();from.GetHumanPose(ref pose);
                    if(missFirst&&rep==0)to.SetHumanPose(ref rest);else to.SetHumanPose(ref pose);
                    input.ReportFrame(time,55);if(missing)input.SetAvailable(false);
                    Call(scorer,"Sample",1f/fps);Set(runtime,"clock",time);Call(runtime,"ObserveFinalScore",1f/fps);
                }
                Call(runtime,"FinishFinalRepetition");
            }
        }
        runtime.FinalScore.Finish();
        Log($"DETAIL {Scenes[index]} {fps}fps missingFirst={missFirst} missing={missing}: target={runtime.FinalScore.TargetQuality:0.0}, process={runtime.FinalScore.ProcessQuality:0.0}, return={runtime.FinalScore.ReturnQuality:0.0}, coverage={runtime.FinalScore.Coverage:0.000}, complete={runtime.FinalScore.Complete}/2");
        if(!missing)Check(runtime.FinalScore.CanPublish,"Full special replay lost data coverage");
        return runtime.FinalScore.ExactScore;
    }
    static object Get(object o,string n)=>o.GetType().GetField(n,P).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,P).SetValue(o,v);
    static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,P).Invoke(o,args);
    static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
