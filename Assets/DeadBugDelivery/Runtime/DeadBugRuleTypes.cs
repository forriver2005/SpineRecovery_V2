using System;
using UnityEngine;

namespace DeadBugDelivery
{
    // Preserve the delivery's rule vocabulary without importing its practice UI.
    public enum HipThrustPhase { WaitingForRaise, Raising, Holding, Lowering, Completed }
    public enum HipThrustDifficultyMode { Hard, Medium, Easy }

    internal static class MobileCoachControls
    {
        public static bool IsPaused;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => IsPaused = false;
    }

    internal static class DeadBugMotionMirror
    {
        public static CoachMotionFrame MirrorFrame(CoachMotionFrame source)
        {
            string[] names = HumanTrait.MuscleName;
            var result = new CoachMotionFrame
            {
                time = source.time,
                bodyPosition = new Vector3(-source.bodyPosition.x, source.bodyPosition.y, source.bodyPosition.z),
                bodyRotation = new Quaternion(source.bodyRotation.x, -source.bodyRotation.y, -source.bodyRotation.z, source.bodyRotation.w),
                muscles = new float[source.muscles.Length]
            };
            for (int i = 0; i < result.muscles.Length; i++)
            {
                string name = names[i];
                string other = name.StartsWith("Left") ? "Right" + name.Substring(4) : name.StartsWith("Right") ? "Left" + name.Substring(5) : null;
                int paired = other == null ? -1 : Array.IndexOf(names, other);
                result.muscles[i] = paired >= 0 ? source.muscles[paired] : name.Contains("Left-Right") ? -source.muscles[i] : source.muscles[i];
            }
            return result;
        }
    }
}
