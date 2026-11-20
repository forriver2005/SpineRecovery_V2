using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadBugDelivery.Editor
{
    public static class DeadBugDeliveryRuleValidation
    {
        static readonly MethodInfo Sample = typeof(DeadBugHoldScorer).GetMethod("SamplePose",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo SampleInput = typeof(DeadBugHoldScorer).GetMethod("Sample",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly List<string> report = new List<string>();
        static DeadBugHoldScorer scorer;
        static Animator avatar;
        static DeadBugPose rest,right,left;
        static int rounds;
        static float result;

        [MenuItem("Tools/Dead Bug Delivery/Validate Imported Rules")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before running rule validation.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before running rule validation.");
            var setup = EditorSceneManager.GetSceneManagerSetup(); GameObject test = null;
            try
            {
                report.Clear();
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/"+DeadBugDeliveryGame.SceneName+".unity");
                var game = UnityEngine.Object.FindObjectOfType<DeadBugDeliveryGame>(); avatar = game.userAnimator;
                test = new GameObject("Bird dog rule validation (temporary)"); scorer = test.AddComponent<DeadBugHoldScorer>();
                scorer.RoundCompleted += score => { rounds++; result = score; };
                rest = new DeadBugPose { LeftArm = RestArm(),RightArm = RestArm(),LeftLeg = RestLeg(),RightLeg = RestLeg() };
                right = rest; right.RightArm = right.LeftLeg = Extended(); left = rest; left.LeftArm = left.RightLeg = Extended();
                foreach (HipThrustDifficultyMode mode in Enum.GetValues(typeof(HipThrustDifficultyMode)))
                {
                    Fresh(mode); Feed(right,1); Check(!scorer.IsArmed && scorer.BaseScore == 0,"B without A cannot start: "+mode);
                    Fresh(mode); Enter(right); Near(scorer.CurrentLiveComparisonScore,scorer.EntryBaseScore,.01f,"B grants base only: "+mode);
                    float target = scorer.TargetHoldSeconds; Feed(right,target/2+.12f);
                    Near(scorer.CurrentLiveComparisonScore,scorer.EntryBaseScore+scorer.MaximumHoldBonus*.5f,.12f,"Linear independent hold bonus: "+mode);
                    Feed(right,target+1); Near(scorer.CurrentLiveComparisonScore,100,.01f,"Hold caps at 100: "+mode);
                    Feed(rest,.6f); Check(rounds == 1 && scorer.HasCompletedRound,"A settles exactly one round: "+mode); Near(result,100,.01f,"Best score settlement: "+mode);
                    Enter(right); Feed(rest,.6f); Check(rounds == 2,"Same-side repetition allowed: "+mode);
                }
                Fresh(); Enter(left); Check(scorer.LockedSide == DeadBugSide.LeftArmRightLeg,"Mirrored diagonal recognized");
                Fresh(); var floor = rest; floor.LeftArm = floor.RightArm = floor.LeftLeg = floor.RightLeg = Extended();
                Feed(floor,1); Check(!scorer.IsArmed && scorer.BaseScore == 0,"Arms and feet on floor are not tabletop A");
                Fresh(); var bent = rest; bent.LeftArm.Bend = 130; Feed(bent,1);
                Check(!scorer.IsArmed,"Bent elbow cannot confirm A");
                Fresh(); var droppedThigh = rest; droppedThigh.LeftLeg.ProximalElevation = 50; Feed(droppedThigh,1);
                Check(!scorer.IsArmed,"Dropped thigh cannot confirm A even with correct endpoint height");
                Fresh(); var verticalShin = rest; verticalShin.RightLeg.DistalElevation = 50; Feed(verticalShin,1);
                Check(!scorer.IsArmed,"Vertical shin cannot confirm A");
                Fresh(); Enter(right); Feed(right,.1f);
                Near(scorer.ValidHoldSeconds,0,.0001f,"First 100 milliseconds of B do not earn hold time");
                Feed(right,.12f); Check(scorer.ValidHoldSeconds > 0,"Continuous quiet window starts hold after 120 milliseconds");
                Fresh(); var low = right; low.RightArm.Elevation = -25; Enter(low);
                Near(scorer.CurrentDeduction,.54f,.001f,"25-degree low arm deducts 0.54");
                Feed(low,5.2f); Near(scorer.HoldBonus,20,.01f,"Deduction does not multiply hold bonus");
                Near(scorer.CurrentLiveComparisonScore,99.46f,.01f,"Low arm full hold = 99.46");
                Feed(right,.2f); Feed(rest,.6f); Near(result,100,.01f,"Correcting form improves round best");

                Fresh(); var max = right; max.RightArm.HorizontalError = max.LeftLeg.HorizontalError = 60;
                max.RightArm.Elevation = max.LeftLeg.Elevation = 45; Enter(max);
                Near(scorer.CurrentDeduction,5,.001f,"Four penalties total cap is 5");
                foreach (string invalid in new[] { "single arm","single leg","same side","four limbs" })
                {
                    Fresh(); var p = rest;
                    if (invalid != "single leg") p.RightArm = Extended();
                    if (invalid == "single leg") p.LeftLeg = Extended();
                    if (invalid == "same side") p.RightLeg = Extended();
                    if (invalid == "four limbs") p.LeftArm = p.LeftLeg = p.RightLeg = Extended();
                    Feed(rest,.3f); Feed(p,3);
                    Check(rounds == 0 && scorer.BaseScore == 0 && scorer.Phase == HipThrustPhase.WaitingForRaise,"Reject and time out "+invalid);
                }
                Fresh(); Enter(right); Feed(right,1); float held = scorer.ValidHoldSeconds;
                Feed(left,.8f); Check(scorer.LockedSide == DeadBugSide.RightArmLeftLeg && rounds == 0,"Direct side swap cannot start/settle round");
                Near(scorer.ValidHoldSeconds,held,.001f,"Opposite side cannot inherit time");
                for (int i = 0; i < 50 && scorer.Phase != HipThrustPhase.Holding; i++) Feed(right,.02f);
                Check(scorer.Phase == HipThrustPhase.Holding,"Same side can resume before A");
                Near(scorer.ValidHoldSeconds,held,.001f,"Resumed hold preserves accumulated time");
                Feed(rest,.6f); Check(rounds == 1,"Resume does not create extra round");

                Fresh(); Enter(right); float stableTime = scorer.ValidHoldSeconds;
                for (int i = 0; i < 100; i++)
                {
                    var unstable = right; unstable.RightArm.Height = i%2 == 0 ? -.12f : .12f;
                    Feed(unstable,.02f);
                }
                Check(scorer.ValidHoldSeconds-stableTime <= .021f,"Unstable limb movement does not accumulate hold time");
                Fresh(); Feed(rest,.3f);
                for (int i = 0; i < 570; i++)
                {
                    var partial = right; partial.RightArm.Bend = 90;
                    partial.RightArm.Height = partial.LeftLeg.Height = .35f+.1f*Mathf.Sin(i*.15f);
                    Feed(partial,.02f);
                }
                Check(scorer.Phase == HipThrustPhase.Raising,"Moving incomplete transition can last beyond stall timeout");
                for (int i = 570; i < 660; i++)
                {
                    var partial = right; partial.RightArm.Bend = 90;
                    partial.RightArm.Height = partial.LeftLeg.Height = .35f+.1f*Mathf.Sin(i*.15f);
                    Feed(partial,.02f);
                }
                Check(scorer.Phase == HipThrustPhase.WaitingForRaise && !scorer.IsArmed && rounds == 0,"12-second transition limit aborts without settlement");

                Fresh(); Enter(right); Feed(right,.5f);
                var tracking = avatar.gameObject.AddComponent<AvatarMotionSource>(); tracking.Configure(avatar); tracking.SetAvailable(false);
                typeof(DeadBugHoldScorer).GetField("tracker",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(scorer,tracking);
                for (int i = 0; i < 9; i++) SampleInput.Invoke(scorer,new object[] { .1f });
                Check(!scorer.TrackingAvailable && !scorer.IsArmed && scorer.BaseScore == 0 && rounds == 0,"Tracking loss aborts without settlement");
                UnityEngine.Object.DestroyImmediate(tracking);

                ValidateRecording(game);
                string path = Path.GetFullPath("output/deadbug-delivery-20261115/rules-validation.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path,report);
                Debug.Log("DEADBUG_RULES_PASS "+report.FindAll(line => line.StartsWith("PASS ")).Count+" assertions");
            }
            finally
            {
                if (test != null) UnityEngine.Object.DestroyImmediate(test);
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
        static void ValidateRecording(DeadBugDeliveryGame game)
        {
            var motion = game.coachAnimator.gameObject.AddComponent<RecordedDeadBugCoachMotion>();
            try
            {
                motion.Configure(game.coachAnimator,game.coachCamera.transform); motion.CreateUserReferences(avatar,out var a,out var r,out var l);
                Check(motion.IsReady && motion.SegmentCount == 6,"Recorded coach and six mirrored segments ready");
                report.Add("REFERENCE A "+JsonUtility.ToJson(a)); report.Add("REFERENCE right B "+JsonUtility.ToJson(r)); report.Add("REFERENCE left B "+JsonUtility.ToJson(l));
                foreach (var p in new[] { r,l })
                {
                    rounds = 0; scorer.CancelSession(); scorer.Configure(avatar,a,r,l); scorer.SetDifficultyMode(HipThrustDifficultyMode.Medium);
                    Check(scorer.BeginContinuousSession(),"Recorded references calibrate"); Feed(a,.4f); Check(scorer.IsArmed,"Recorded A arms scorer");
                    Feed(p,.7f); Check(scorer.Phase == HipThrustPhase.Holding,"Recorded B passes recognition");
                    Feed(a,.7f); Check(rounds == 1,"Recorded A-B-A settles");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(motion); }
        }
        static DeadBugLimb RestArm() => new DeadBugLimb { Height = .95f,Reach = .05f,Bend = 175,Elevation = 87,ProximalElevation = 0,DistalElevation = 90 };
        static DeadBugLimb RestLeg() => new DeadBugLimb { Height = .55f,Reach = .45f,Bend = 90,Elevation = 50,ProximalElevation = 0,DistalElevation = 0 };
        static DeadBugLimb Extended() => new DeadBugLimb { Height = 0,Reach = .95f,Bend = 175 };
        static void Fresh(HipThrustDifficultyMode mode = HipThrustDifficultyMode.Medium)
        { rounds = 0; result = 0; scorer.CancelSession(); scorer.Configure(avatar,rest,right,left); scorer.SetDifficultyMode(mode); if (!scorer.BeginContinuousSession()) throw new Exception("Test calibration failed"); }
        static void Enter(DeadBugPose p)
        {
            Feed(rest,.3f); for (int i = 0; i < 100 && scorer.Phase != HipThrustPhase.Holding; i++) Feed(p,.02f);
            if (scorer.Phase != HipThrustPhase.Holding) throw new Exception("Expected B: "+scorer.Guidance);
        }
        static void Feed(DeadBugPose p,float seconds) { for (int i = 0; i < Mathf.RoundToInt(seconds/.02f); i++) Sample.Invoke(scorer,new object[] { p,.02f }); }
        static void Near(float value,float expected,float tolerance,string message) => Check(Mathf.Abs(value-expected)<=tolerance,message+$" ({value:0.000}/{expected:0.000})");
        static void Check(bool condition,string message) { if (!condition) throw new Exception("DEADBUG_VALIDATION_FAILED "+message); report.Add("PASS "+message); }
    }
}
