using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Development-only comparison. Originals and patient input are never modified.
[InitializeOnLoad]
public static class RecordedBirdDogMappingExport
{
    static RecordedBirdDogMappingExport() { EditorApplication.delayCall += ExportRequested; }
    static void ExportRequested()
    {
        const string marker = ".codex/export-bird-mapping";
        if (!File.Exists(marker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(marker);
        ExportPreview();
    }
    [MenuItem("Spine Recovery/Export Recorded BirdDog Side Mapping Preview")]
    public static void ExportPreview()
    {
        var recorder = UnityEngine.Object.FindObjectOfType<MotionRecorder>(true);
        var clone = UnityEngine.Object.Instantiate(recorder.UserAnimator.gameObject);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.transform.SetParent(null);
        clone.transform.rotation = Quaternion.identity;
        var animator = clone.GetComponent<Animator>();
        animator.enabled = false;
        var pairs = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
        try
        {
            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
            {
                var neutral = new HumanPose { bodyRotation = Quaternion.identity,
                    muscles = new float[HumanTrait.MuscleCount] };
                handler.SetHumanPose(ref neutral);
                var rest = new Quaternion[pairs.Length];
                for (int j = 0; j < pairs.Length; j++) rest[j] = animator.GetBoneTransform(pairs[j]).rotation;
                var package = JsonUtility.FromJson<CoachMotionPackage>(Resources.Load<TextAsset>("ActionLibrary/Motions/clip_30").text);
                var pose = neutral;
                var rotations = new Quaternion[pairs.Length];
                foreach (var frame in package.segments[0].frames)
                {
                    frame.ApplyTo(ref pose);
                    handler.SetHumanPose(ref pose);
                    for (int j = 0; j < pairs.Length; j++) rotations[j] = animator.GetBoneTransform(pairs[j]).rotation;
                    // Exchange side roles while preserving the recorded world
                    // rotation delta. A geometric mirror would reverse lateral
                    // motion and would not repair the observed crossed leg.
                    for (int j = 0; j < pairs.Length; j++)
                        animator.GetBoneTransform(pairs[j]).rotation = rotations[j ^ 1] * Quaternion.Inverse(rest[j ^ 1]) * rest[j];
                    handler.GetHumanPose(ref pose);
                    // Only the two leg muscle blocks are derived. Body, arms,
                    // timestamps and all original files remain unchanged.
                    for (int j = 21; j <= 36; j++) frame.muscles[j] = pose.muscles[j];
                }
                package.packageId = "clip_30_side_mapping_preview";
                package.sourceClipName = "8_v1; leg-side mapping comparison (world rotation delta, not mirror)";
                File.WriteAllText(".codex/birddog-side-remap-preview.json", JsonUtility.ToJson(package));
                Debug.Log("[BirdDogMapping] Exported comparison only; formal scene unchanged.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
}
