using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class CorrectCatHoldRecording
{
    [MenuItem("Tools/Ring Bridge/Correct Cat Hold Recording Direction")]
    public static void Run()
    {
        const string folder="Assets/RecordedHoldGames/LyingCat/";
        var source=JsonUtility.FromJson<CoachMotionPackage>(Resources.Load<TextAsset>(RecordedLyingCatCoachMotion.ResourcePath).text);var frames=source.segments[0].frames;
        int At(float seconds)=>Enumerable.Range(0,frames.Length).OrderBy(i=>Math.Abs(frames[i].time-seconds)).First();
        int start=At(30),peak=At(34),lower=At(36),end=At(40);
        void Update(string name,AnimationClip baked)
        {
            var asset=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+name+".anim");if(asset==null)throw new Exception("Missing "+name);
            string original=asset.name;EditorUtility.CopySerialized(baked,asset);asset.name=original;UnityEngine.Object.DestroyImmediate(baked);EditorUtility.SetDirty(asset);
        }
        Update("Routine",RecordedCoachMotionImporter.Bake("hold_routine",frames,start,end,source.fps,0,false));
        Update("Start",RecordedCoachMotionImporter.Bake("hold_start",new[]{frames[start],frames[start]},0,1,30,0,false));
        Update("Wrap",RecordedCoachMotionImporter.Bake("hold_wrap",new[]{frames[end],frames[start]},0,1,2,0,false));
        var timeline=AssetDatabase.LoadAssetAtPath<HipThrustPhaseTimeline>(folder+"Timeline.asset");timeline.repeatStartFrame=0;timeline.repeatEndFrame=end-start;
        timeline.cycles=new[]{new HipThrustPhaseTimeline.Cycle{liftStartFrame=0,holdStartFrame=peak-start,lowerStartFrame=lower-start,lowerEndFrame=end-start,lowHeight=0,peakHeight=1}};
        timeline.recordedHeight=new AnimationCurve(new Keyframe(0,0),new Keyframe(peak-start,1),new Keyframe(lower-start,.999f),new Keyframe(end-start,0));
        for(int i=0;i<timeline.recordedHeight.length;i++){AnimationUtility.SetKeyLeftTangentMode(timeline.recordedHeight,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(timeline.recordedHeight,i,AnimationUtility.TangentMode.Linear);}
        timeline.Validate();EditorUtility.SetDirty(timeline);AssetDatabase.SaveAssets();Debug.Log("Corrected cat hold: source30 -> rounded back34 -> restore40; GUIDs preserved.");
    }
}
