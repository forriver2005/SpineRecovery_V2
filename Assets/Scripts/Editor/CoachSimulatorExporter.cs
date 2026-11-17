using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Development-only exporter for the external VMC simulator. Never runs in a player.
[InitializeOnLoad]
public static class CoachSimulatorExporter
{
    static CoachSimulatorExporter()
    {
        EditorApplication.delayCall += RunRequested;
        EditorApplication.update += ConnectSimulation;
    }
    static int connectedInstance;
    static void ConnectSimulation()
    {
        if (!EditorApplication.isPlaying) { connectedInstance=0; return; }
        if (!File.Exists(".codex/enable-coach-simulator")) return;
        var controller=UnityEngine.Object.FindObjectOfType<CoachHapticFeedbackController>();
        if(controller==null || controller.GetInstanceID()==connectedInstance)return;
        var serialized=new SerializedObject(controller);
        serialized.FindProperty("discoveryUrl").stringValue="http://127.0.0.1:18761/trackers";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        connectedInstance=controller.GetInstanceID();
        Debug.Log("[CoachSimulator] SIMULATED tracker status/motors connected for this Play session only.");
    }
    static void RunRequested()
    {
        if(File.Exists(".codex/export-original-simulator") && !EditorApplication.isPlayingOrWillChangePlaymode) {
            File.Delete(".codex/export-original-simulator");ExportOriginalTargets();
        }
        const string request=".codex/export-coach-simulator";
        if(!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request); Export();
    }
    [MenuItem("Spine Recovery/Export Original Coach Simulator Targets")]
    public static void ExportOriginalTargets()
    {
        var recorder=UnityEngine.Object.FindObjectOfType<MotionRecorder>(true);
        var clone=UnityEngine.Object.Instantiate(recorder.UserAnimator.gameObject);
        clone.hideFlags=HideFlags.HideAndDontSave;
        try {
            var animator=clone.GetComponent<Animator>();
            foreach(string action in new[]{"deadbug","birddog"})
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Movements/"+action+".fbx")) {
                var clip=asset as AnimationClip;
                if(clip==null || !clip.name.StartsWith(action+"_"))continue;
                clip.SampleAnimation(clone,clip.length);
                var bones=new System.Collections.Generic.List<Transform>();
                var names=new System.Collections.Generic.List<string>();
                for(int i=0;i<(int)HumanBodyBones.LastBone;i++) {
                    var bone=animator.GetBoneTransform((HumanBodyBones)i);
                    if(bone!=null){bones.Add(bone);names.Add(((HumanBodyBones)i).ToString());}
                }
                using(var writer=new BinaryWriter(File.Create("Tools/CoachSimulator/data/original_"+clip.name+".vmc"))) {
                    writer.Write(1);writer.Write(bones.Count);
                    for(int i=0;i<bones.Count;i++) {
                        var p=bones[i].localPosition;var q=bones[i].localRotation;
                        writer.Write(names[i]);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);
                        writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);
                    }
                }
            }
        } finally {UnityEngine.Object.DestroyImmediate(clone);}
    }
    [MenuItem("Spine Recovery/Export Coach Simulator Motions")]
    public static void Export()
    {
        var recorder=UnityEngine.Object.FindObjectOfType<MotionRecorder>(true);
        if(recorder==null || recorder.UserAnimator==null)throw new InvalidOperationException("Open a formal Practice scene first.");
        var clone=UnityEngine.Object.Instantiate(recorder.UserAnimator.gameObject);
        clone.name="Simulator export only";clone.hideFlags=HideFlags.HideAndDontSave;
        clone.transform.SetParent(null);clone.transform.rotation=Quaternion.identity;
        var animator=clone.GetComponent<Animator>();
        string[] names={"deadbug","birddog","hipthrust","catcamel","sidebridge","curlup","birddog_positive"};
        string[] clips={"29","30","26","23","27","28","30"};
        string output="Tools/CoachSimulator/data";Directory.CreateDirectory(output);
        try
        {
            for(int action=0;action<names.Length;action++)
            {
                var package=JsonUtility.FromJson<CoachMotionPackage>(File.ReadAllText(action==1 || action==6 ? "Assets/Resources/CoachMotions/BirdDogRecordedSides.json" : "Assets/Resources/ActionLibrary/Motions/clip_"+clips[action]+".json"));
                var frames=package.segments[0].frames;
                int count=Mathf.FloorToInt(frames[frames.Length-1].time*30f)+1;
                using(var backend=new KeyframeCoachMotionBackend(animator,package))
                using(var writer=new BinaryWriter(File.Create(output+"/"+names[action]+".vmc")))
                {
                    writer.Write(count);
                    backend.PlaySegment(0);
                    var bones=new System.Collections.Generic.List<HumanBodyBones>();
                    for(int j=0;j<(int)HumanBodyBones.LastBone;j++)if(animator.GetBoneTransform((HumanBodyBones)j)!=null)bones.Add((HumanBodyBones)j);
                    for(int frame=0;frame<count;frame++)
                    {
                        if(frame>0)backend.Tick(1f/30f);
                        if(action==6) AlignActiveArmForPositiveSimulation(animator);
                        writer.Write(bones.Count);
                        foreach(var bone in bones)
                        {
                            var t=animator.GetBoneTransform(bone);var p=t.localPosition;var q=t.localRotation;
                            writer.Write(bone.ToString());writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);
                            writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);
                        }
                    }
                }
                Debug.Log("[CoachSimulator] Exported "+names[action]+" frames="+count);
            }
            File.WriteAllText(".codex/simulator-export-result.txt","Exported six recorded motions plus separately named synthetic BirdDog positive input at 30 Hz. Synthetic input is not recording acceptance.");
        }
        finally {UnityEngine.Object.DestroyImmediate(clone);}
    }

    static void AlignActiveArmForPositiveSimulation(Animator animator)
    {
        // This extra INPUT fixture represents a patient extending the active
        // arm along the torso. It never changes the coach, thresholds, validity
        // or feedback booleans. Ordinary birddog.vmc remains recorded input.
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest=animator.GetBoneTransform(HumanBodyBones.Chest);
        var thighs=new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg};
        var knees=new[]{HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg};
        var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
        var arms=new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.LeftUpperArm};
        var hands=new[]{HumanBodyBones.RightHand,HumanBodyBones.LeftHand};
        for(int i=0;i<2;i++) {
            var thigh=animator.GetBoneTransform(thighs[i]);var knee=animator.GetBoneTransform(knees[i]);var foot=animator.GetBoneTransform(feet[i]);
            if(Vector3.Angle(knee.position-thigh.position,foot.position-knee.position)>=50f)continue;
            var arm=animator.GetBoneTransform(arms[i]);var hand=animator.GetBoneTransform(hands[i]);
            arm.rotation=Quaternion.FromToRotation(hand.position-arm.position,chest.position-hips.position)*arm.rotation;
        }
    }
}
