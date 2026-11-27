using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AllGameVmcVerification
{
    const string Flag="AllGameVmcVerification",Report="output/all-game-prescriptions/vmc-navigation.txt";
    static int index,stage,checks;static double at,began;static string failure;
    static GameWorkoutScene entry;static Animator patient;static AvatarMotionSource motion;static UdpClient udp;
    static readonly HumanBodyBones[] Bones={HumanBodyBones.Hips,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
    static Quaternion[] expected;static Vector3[] positions;
    static AllGameVmcVerification(){if(SessionState.GetBool(Flag,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Observe;began=EditorApplication.timeSinceStartup;}}
    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,"All 16 game navigation and localhost UDP bone telemetry. No wearable/doctor-network claim. Test receiver port 39549.\n");
        index=stage=checks=0;began=EditorApplication.timeSinceStartup;EditorSceneManager.OpenScene("Assets/Scenes/"+GameTestMenu.Scenes[0]+".unity");SessionState.SetBool(Flag,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Observe(string message,string stack,LogType type){if((type==LogType.Exception||message.Contains("SocketException"))&&failure==null)failure=message+"\n"+stack;}
    static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            Check(failure==null,"Runtime or socket error: "+failure);Check(EditorApplication.timeSinceStartup-began<240,"VMC test timed out");
            if(stage==0)
            {
                entry=UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();if(entry==null||entry.gameObject.scene.name!=GameTestMenu.Scenes[index]||entry.GetComponent<GameTestMenu>().Panel==null)return;
                if(entry.taikoNightAction!=null&&entry.taikoNightAction.ResultPanel==null)return;
                patient=entry.bridgeWave!=null?entry.bridgeWave.input.patient:entry.taikoNightAction!=null?entry.taikoNightAction.recorder.UserAnimator:entry.scorer.UserAnimator;
                patient.gameObject.SetActive(true);patient.enabled=false;
                var receivers=UnityEngine.Object.FindObjectsOfType<EVMC4U.ExternalReceiver>(true).Where(r=>r.Model==patient.gameObject).ToArray();Check(receivers.Length==1,"Duplicate patient VMC receiver: "+entry.gameObject.scene.name+" count="+receivers.Length);
                var server=receivers[0].GetComponent<uOSC.uOscServer>();Check(server!=null&&server.port==39539,"Missing production port");server.port=39549;
                motion=UnityEngine.Object.FindObjectsOfType<AvatarMotionSource>().Single(m=>m.Model==patient.gameObject);
                expected=new Quaternion[Bones.Length];positions=new Vector3[Bones.Length];for(int i=0;i<Bones.Length;i++){expected[i]=Quaternion.Euler(12+i*3,-9+i*2,-16+i*4);positions[i]=patient.GetBoneTransform(Bones[i]).localPosition;}
                udp=new UdpClient();var type=typeof(GameTestBuildVerification);var flags=BindingFlags.NonPublic|BindingFlags.Static;type.GetField("udp",flags).SetValue(null,udp);type.GetField("expected",flags).SetValue(null,expected);
                at=EditorApplication.timeSinceStartup;stage=1;return;
            }
            if(stage==1)
            {
                typeof(GameTestBuildVerification).GetMethod("SendPose",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{39549});if(EditorApplication.timeSinceStartup-at<1)return;
                Check(motion.HasFreshFrame,"Network input did not become fresh");
                for(int i=0;i<Bones.Length;i++){var bone=patient.GetBoneTransform(Bones[i]);Check(Quaternion.Angle(bone.localRotation,expected[i])<.2f,"Wrong received bone "+Bones[i]);Check(Vector3.Distance(bone.localPosition,positions[i])<.0001f,"Receiver changed bone lengths");}
                udp.Dispose();udp=null;at=EditorApplication.timeSinceStartup;stage=2;return;
            }
            if(stage==2&&EditorApplication.timeSinceStartup-at>.8)
            {
                Check(!motion.HasFreshFrame,"Stopped bone stream remained fresh");Check(entry.IsMenuVisible,"VMC stream auto-started training");
                File.AppendAllText(Report,"PASS "+GameTestMenu.Scenes[index]+": one receiver, real UDP reaches all tested bones, length preserved, signal expires, menu navigation.\n");
                index++;if(index==GameTestMenu.Scenes.Length){File.AppendAllText(Report,"PASS all VMC/navigation cases, checks="+checks+"\n");Finish(0);return;}
                entry.GetComponent<GameTestMenu>().Open();entry.GetComponent<GameTestMenu>().GameButtons[index].onClick.Invoke();stage=0;
            }
        }
        catch(Exception ex){File.AppendAllText(Report,"FAIL "+ex+"\n");Debug.LogException(ex);Finish(1);}
    }
    static void Finish(int code){udp?.Dispose();SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;}
}
