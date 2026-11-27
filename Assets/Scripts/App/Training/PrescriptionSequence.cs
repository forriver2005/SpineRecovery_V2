using System;
using System.Linq;

namespace SpineRecovery.App
{
    // One prescription item owns its coaches, game sets and final questionnaire.
    public static class PrescriptionSequence
    {
        public static bool IsValid(TrainingPlan plan)
        {
            if (plan?.prescriptionOrderId <= 0 || plan?.actions == null ||
                plan.actions.Length < 2 || plan.actions.Length > 420 || plan.actions.Any(a => a == null)) return false;
            var groups = plan.actions.GroupBy(a => a.prescriptionItemId).ToArray();
            if (groups.Length > 10 || !plan.actions.SequenceEqual(groups.SelectMany(g => g))) return false;
            for (int position = 0; position < groups.Length; position++)
            {
                var steps = groups[position].ToArray();
                if (groups[position].Key <= 0 || steps.Any(a => a.prescriptionPosition != position ||
                    a.level != steps[0].level || (a.level != "simple" && a.level != "standard" && a.level != "hard"))) return false;
                var first = TrainingActionCatalog.Find(steps[0].actionId);
                if (first == null) return false;
                bool bilateral = first.Key == "side_bridge" || first.Key == "side_bridge_hold30";
                int coaches = bilateral ? 2 : 1;
                if (bilateral ? steps.Length < 4 || steps.Length > 42 || steps.Length % 2 != 0 : steps.Length != 2) return false;
                for (int i = 0; i < steps.Length; i++)
                {
                    var action = steps[i];
                    var entry = TrainingActionCatalog.Find(action.actionId);
                    string expectedKey = bilateral && i % 2 == 1
                        ? (first.Key.EndsWith("hold30") ? "side_bridge_right_hold30" : "side_bridge_right") : first.Key;
                    bool coach = i < coaches;
                    if (entry == null || entry.Key != expectedKey || action.arTaskType != (coach ? "coach_training" : "game_training")) return false;
                    if (coach)
                    {
                        if (TrainingActionCatalog.ForCoach(action.sceneName) != TrainingActionCatalog.ForCoach(entry.Coach) ||
                            action.sets != 1 || action.reps != (bilateral ? 2 : 4) || action.holdSeconds != 0) return false;
                    }
                    else if (action.sceneName != entry.Game || action.sets < 1 || action.sets > 20 ||
                        bilateral && action.sets != 1 || action.reps < 1 || action.reps > 100 ||
                        action.holdSeconds < 0 || action.holdSeconds > 600 ||
                        action.reps != steps[coaches].reps || action.holdSeconds != steps[coaches].holdSeconds) return false;
                }
            }
            return true;
        }
    }
}
