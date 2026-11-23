using UnityEngine;

namespace BirdDogDelivery
{
    // Run after recorded hip recentering (29000) and user constraints (30000).
    // Both rigs are driven directly, so their bounds must update off screen too.
    [DefaultExecutionOrder(32100)]
    public sealed class BirdDogPreviewFraming : MonoBehaviour
    {
        public Animator avatar;
        public Camera previewCamera;
        public bool followBodyHeading;
        static readonly HumanBodyBones[] Bones = { HumanBodyBones.Head,HumanBodyBones.Hips,HumanBodyBones.Chest,
            HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,
            HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm };
        void LateUpdate()
        {
            if (avatar == null || !avatar.isHuman || previewCamera == null) return;
            if (followBodyHeading)
            {
                var hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
                var left = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm); var right = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (hips != null && left != null && right != null)
                {
                    var heading = Vector3.ProjectOnPlane((left.position+right.position)*.5f-hips.position,Vector3.up);
                    if (heading.sqrMagnitude > .01f) previewCamera.transform.rotation = Quaternion.LookRotation(Vector3.Cross(heading.normalized,Vector3.up),Vector3.up);
                }
            }
            var rotation = previewCamera.transform.rotation; var inverse = Quaternion.Inverse(rotation);
            var min = Vector3.one*float.PositiveInfinity; var max = Vector3.one*float.NegativeInfinity;
            foreach(var bone in Bones)
            {
                var target = avatar.GetBoneTransform(bone); if(target == null) continue;
                var p = inverse*target.position; min = Vector3.Min(min,p); max = Vector3.Max(max,p);
            }
            if (float.IsInfinity(min.x)) return;
            var center = (min+max)*.5f; center.z = min.z-3;
            previewCamera.transform.position = rotation*center; previewCamera.orthographic = true;
            previewCamera.nearClipPlane = .01f; previewCamera.farClipPlane = 100;
            previewCamera.orthographicSize = 1.12f * Mathf.Max((max.y-min.y+.35f)/2,(max.x-min.x+.35f)/(2*previewCamera.aspect));
        }
    }
}
