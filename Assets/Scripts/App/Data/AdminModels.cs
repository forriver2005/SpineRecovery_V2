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
        public FixedTrackerSlot slot; // Legacy files only.
        public string bodyPosition;
        public string hardwareId;
        public string ip;
        public bool online;
        public int batteryPercent = -1;
        public string lastSeenAt;

        public string DisplayName()
        {
            if (!string.IsNullOrWhiteSpace(bodyPosition)) return PositionLabel(bodyPosition);
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

        public static string PositionLabel(string position)
        {
            if (string.IsNullOrWhiteSpace(position)) return "未分配部位";
            string name = position.StartsWith("body:") ? position.Substring(5) : position;
            string side = name.StartsWith("left_") ? "左" : name.StartsWith("right_") ? "右" : "";
            string part = side.Length == 0 ? name : name.Substring(name.IndexOf('_') + 1);
            switch (part)
            {
                case "head": return "头部";
                case "neck": return "颈部";
                case "upper_chest": return "上胸";
                case "chest": return "胸部";
                case "waist": return "腰部";
                case "hip": return "髋部";
                case "upper_arm": return side + "上臂";
                case "lower_arm": return side + "前臂";
                case "hand": return side + "手";
                case "upper_leg": return side + "大腿";
                case "lower_leg": return side + "小腿";
                case "foot": return side + "脚";
                case "shoulder": return side + "肩";
                default: return name;
            }
        }

        public static bool CanBind(AdminTrackerBinding[] rows, out string message)
        {
            int count = rows.Length;
            if (count != 5)
                message = $"已有 {count} 台在线 IMU 分配了部位，需要恰好 5 台。";
            else if (rows.Any(row => !row.online || string.IsNullOrEmpty(row.bodyPosition) ||
                !System.Text.RegularExpressions.Regex.IsMatch(row.hardwareId ?? "", @"^(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$")) ||
                rows.Select(row => row.hardwareId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5)
                message = "需要五台有独立 MAC 且已分配部位的在线 IMU。";
            else if (rows.Select(row => row.bodyPosition).Distinct().Count() != 5)
                message = "有设备共用同一部位，请在 SlimeVR 中调整。";
            else
            {
                message = "五台设备均已分配部位，可以绑定。";
                return true;
            }
            return false;
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
        // Disabling a published binding stops patient-side use while retaining
        // the MAC-to-slot configuration so an administrator can re-enable it.
        public bool trackerBindingsDisabled;

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
