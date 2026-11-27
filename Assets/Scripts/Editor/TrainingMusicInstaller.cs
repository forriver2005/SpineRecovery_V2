using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TrainingMusicInstaller
{
    [MenuItem("Tools/Training/Install Complete Game Music")]
    public static void Install()
    {
        Directory.CreateDirectory("output/all-game-prescriptions");var report="scene,clip,playback\n";
        foreach(string name in GameTestMenu.Scenes)
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity");
            var entry=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameWorkoutScene>(true)).Single();
            string song=name.StartsWith("BirdDog")?"BirdDog_SpaceArp":name.StartsWith("DeadBug")?"DeadBug_CrystalCave":name.StartsWith("SideBridge")?"SideBridge_EnchantedTiki":name.StartsWith("Crunch")?"Crunch_Synthwave4k":name.StartsWith("LyingCat")?"LyingCat_LifeWave2k":"HipThrust_HeavenlyLoop";
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/GameMusic/"+song+".ogg");if(clip==null)throw new System.Exception("Missing music "+song);
            if(entry.taikoNightAction!=null||entry.bridgeWave!=null)
            {
                var music=entry.GetComponent<GameTrainingMusic>()??entry.gameObject.AddComponent<GameTrainingMusic>();music.entry=entry;music.clip=clip;
                foreach(var clock in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<RhythmSongClock>(true)))
                {var data=new SerializedObject(clock);data.FindProperty("song").objectReferenceValue=null;data.ApplyModifiedPropertiesWithoutUndo();var source=clock.GetComponent<AudioSource>();if(source!=null)source.clip=null;}
                report+=name+","+song+",GameTrainingMusic\n";
            }
            else
            {
                var clock=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<RhythmSongClock>(true)).Single();var data=new SerializedObject(clock);data.FindProperty("song").objectReferenceValue=clip;data.ApplyModifiedPropertiesWithoutUndo();report+=name+","+song+",RhythmSongClock\n";
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();File.WriteAllText("output/all-game-prescriptions/music-map.csv",report);
    }
}
