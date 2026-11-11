using System;
using System.Collections.Generic;
using System.Linq;

namespace SpineRecovery.App
{
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
