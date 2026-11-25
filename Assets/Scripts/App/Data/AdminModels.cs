using System;
using System.Collections.Generic;
using System.Linq;

namespace SpineRecovery.App
{
    public enum FixedTrackerSlot
    {
        LeftAnkle,
        RightAnkle,
        LeftWrist,
        RightWrist,
        Waist,
    }

    [Serializable]
    public sealed class AdminTrackerBinding
    {
        public FixedTrackerSlot slot;
        public string hardwareId;
        public string ip;
        public bool online;
        public int batteryPercent = -1;
        public string lastSeenAt;

        public string DisplayName()
        {
            switch (slot)
            {
                case FixedTrackerSlot.LeftAnkle: return "左脚腕";
                case FixedTrackerSlot.RightAnkle: return "右脚腕";
                case FixedTrackerSlot.LeftWrist: return "左手腕";
                case FixedTrackerSlot.RightWrist: return "右手腕";
                case FixedTrackerSlot.Waist: return "腰";
                default: return slot.ToString();
            }
        }
    }

    public static class FixedTrackerSlotMapping
    {
        public static bool TryParse(string bodyPosition, out FixedTrackerSlot slot)
        {
            switch ((bodyPosition ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "left_ankle": case "left_foot": case "left_lower_leg": slot = FixedTrackerSlot.LeftAnkle; return true;
                case "right_ankle": case "right_foot": case "right_lower_leg": slot = FixedTrackerSlot.RightAnkle; return true;
                case "left_wrist": case "left_forearm": slot = FixedTrackerSlot.LeftWrist; return true;
                case "right_wrist": case "right_forearm": slot = FixedTrackerSlot.RightWrist; return true;
                case "waist": case "abdomen": case "body:abdomen": slot = FixedTrackerSlot.Waist; return true;
                default: slot = FixedTrackerSlot.Waist; return false;
            }
        }
    }

    public enum AdminConfigState
    {
        Draft,
        ArTestPassed,
        Published,
        Archived,
    }

    [Serializable]
    public sealed class AdminActionDefinition
    {
        public string actionId;
        public string displayName;
        public string[] actionTags = Array.Empty<string>();
        public string trajectoryId;
        public int coachSets;
        public int coachReps;
        public int coachHoldSeconds;
        public int transitionSeconds;
        public int[] gameDifficultySeconds = Array.Empty<int>();
        public int keyFrameHoldSeconds;
        public string[] keyFrameIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class AdminRecommendationRule
    {
        public string ruleId;
        public string userTag;
        public string actionTag;
        public int priority;
        public bool enabled = true;
        public string[] actionIds = Array.Empty<string>();

        public AdminActionDefinition[] ResolveActions(IEnumerable<AdminActionDefinition> library)
        {
            var actions = library?.ToArray() ?? Array.Empty<AdminActionDefinition>();
            if (actionIds == null || actionIds.Length == 0)
                return actions.Where(a => a != null && (a.actionTags ?? Array.Empty<string>()).Contains(actionTag)).Take(1).ToArray();

            var byId = actions.Where(a => a != null).ToDictionary(a => a.actionId, a => a);
            var resolved = actionIds.Select(id => byId.TryGetValue(id, out var action) ? action : null).ToArray();
            return resolved.All(a => a != null) ? resolved : Array.Empty<AdminActionDefinition>();
        }
    }

    [Serializable]
    public sealed class AdminTrainingConfiguration
    {
        public string configId;
        public string versionName;
        public AdminConfigState state;
        public string basedOnConfigId;
        public string createdAt;
        public string updatedAt;
        public string arTestedAt;
        public string verifiedFingerprint;
        public string importSourcePackage;
        public AdminActionDefinition[] actions = Array.Empty<AdminActionDefinition>();
        public string[] userTags = Array.Empty<string>();
        public string[] actionTags = Array.Empty<string>();
        public AdminRecommendationRule[] recommendationRules = Array.Empty<AdminRecommendationRule>();
        // Exactly five administrator-owned bindings. Patient flows only read this published snapshot.
        public AdminTrackerBinding[] trackerBindings = Array.Empty<AdminTrackerBinding>();
        // First-time provisioning is intentionally one-way.  Once the five
        // hardware IDs have been confirmed, normal patient accounts must not
        // be able to replace them by discovering a different tracker.
        public bool trackerBindingsLocked;

        public AdminActionDefinition[] RecommendedActions(IEnumerable<string> tags)
        {
            var tagSet = new HashSet<string>((tags ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)));
            var rule = (recommendationRules ?? Array.Empty<AdminRecommendationRule>())
                .Where(r => r != null && r.enabled && tagSet.Contains(r.userTag))
                .OrderByDescending(r => r.priority)
                .FirstOrDefault();
            return rule == null ? Array.Empty<AdminActionDefinition>() : rule.ResolveActions(actions);
        }
    }
}
