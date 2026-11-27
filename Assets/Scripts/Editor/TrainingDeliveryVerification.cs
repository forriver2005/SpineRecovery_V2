using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class TrainingDeliveryVerification
{
    public static void Run()
    {
        string report="Delivery integrity: actual build paths and all serialized components.\n";int count=0;
        foreach(var build in EditorBuildSettings.scenes.Where(s=>s.enabled))
        {
            if(!File.Exists(build.path))throw new Exception("Missing build scene: "+build.path);
            var scene=EditorSceneManager.OpenScene(build.path);
            foreach(var root in scene.GetRootGameObjects())foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)>0)throw new Exception("Missing script: "+build.path+" / "+transform.name);
            var entry=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameWorkoutScene>(true)).FirstOrDefault();
            if(GameTestMenu.Scenes.Contains(scene.name))
            {
                if(entry==null)throw new Exception("No workout entry "+scene.name);
                if(entry.taiko!=null&&entry.taiko.RuleScoring?.recordedAction!=null)
                {
                    if(entry.taiko.PrescriptionDose==null||entry.scorer==null||entry.menu==null)throw new Exception("Hold reference missing");
                    entry.taiko.PhaseTimeline.Validate();
                }
            }
            report+="PASS "+build.path+"\n";count++;
        }
        if(count!=19)throw new Exception("Unexpected number of built scenes "+count);
        Directory.CreateDirectory("output/all-game-prescriptions");File.WriteAllText("output/all-game-prescriptions/delivery-integrity.txt",report+"PASS 19 build scenes, no missing scripts.\n");
    }
}
