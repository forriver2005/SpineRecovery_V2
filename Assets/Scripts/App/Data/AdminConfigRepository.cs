using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpineRecovery.App
{
    public interface IAdminConfigRepository
    {
        IReadOnlyList<AdminTrainingConfiguration> ListConfigurations();
        AdminTrainingConfiguration CurrentPublished();
        AdminTrainingConfiguration Find(string configId);
        void SaveConfigurations(IEnumerable<AdminTrainingConfiguration> configurations);
        AdminTrainingConfiguration CreateDraftFromPublished();
        bool MarkArTestPassed(string configId, string fingerprint);
        bool Publish(string configId);
        bool RollbackTo(string configId);
        bool BindPublishedTrackers(IEnumerable<SlimeVrHapticTrackerSnapshot> trackers, out string message);
    }

    /// <summary>
    /// Local configuration store. Patient flows only read the single published snapshot;
    /// administrator tooling can create a draft and publish it after a separate AR proof.
    /// </summary>
    public sealed class JsonAdminConfigRepository : IAdminConfigRepository
    {
        private const string FileName = "spine-admin-configurations.json";

        [Serializable]
        private sealed class ConfigurationDocument
        {
            public AdminTrainingConfiguration[] configurations = Array.Empty<AdminTrainingConfiguration>();
        }

        private List<AdminTrainingConfiguration> configurations;

        public IReadOnlyList<AdminTrainingConfiguration> ListConfigurations()
        {
            EnsureLoaded();
            return configurations
                .OrderBy(c => c.state == AdminConfigState.Published ? 0 : c.state == AdminConfigState.ArTestPassed ? 1 : c.state == AdminConfigState.Draft ? 2 : 3)
                .ThenByDescending(c => c.updatedAt)
                .ToArray();
        }

        public AdminTrainingConfiguration CurrentPublished()
        {
            EnsureLoaded();
            return configurations.FirstOrDefault(c => c.state == AdminConfigState.Published) ?? DefaultConfiguration();
        }

        public AdminTrainingConfiguration Find(string configId)
        {
            EnsureLoaded();
            return configurations.FirstOrDefault(c => c.configId == configId);
        }

        public void SaveConfigurations(IEnumerable<AdminTrainingConfiguration> values)
        {
            var next = (values ?? Array.Empty<AdminTrainingConfiguration>()).Where(c => c != null).ToList();
            foreach (AdminTrainingConfiguration config in next) NormalizeTrackerBindings(config);
            if (next.Count(c => c.state == AdminConfigState.Published) > 1)
                throw new InvalidOperationException("Only one administrator configuration may be published.");
            configurations = next;
            JsonFileStore.Save(FileName, new ConfigurationDocument { configurations = configurations.ToArray() });
        }

        public AdminTrainingConfiguration CreateDraftFromPublished()
        {
            AdminTrainingConfiguration published = CurrentPublished();
            AdminTrainingConfiguration draft = JsonUtility.FromJson<AdminTrainingConfiguration>(JsonUtility.ToJson(published));
            string now = DateTimeOffset.UtcNow.ToString("O");
            draft.configId = "cfg_draft_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            draft.versionName = published.versionName + "-draft";
            draft.state = AdminConfigState.Draft;
            draft.basedOnConfigId = published.configId;
            draft.createdAt = now;
            draft.updatedAt = now;
            draft.arTestedAt = "";
            draft.verifiedFingerprint = "";
            configurations.Add(draft);
            Persist();
            return draft;
        }

        public bool MarkArTestPassed(string configId, string fingerprint)
        {
            AdminTrainingConfiguration config = Find(configId);
            if (config == null || config.state != AdminConfigState.Draft || string.IsNullOrWhiteSpace(fingerprint)) return false;
            config.state = AdminConfigState.ArTestPassed;
            config.arTestedAt = DateTimeOffset.UtcNow.ToString("O");
            config.verifiedFingerprint = fingerprint;
            config.updatedAt = config.arTestedAt;
            Persist();
            return true;
        }

        public bool Publish(string configId)
        {
            AdminTrainingConfiguration target = Find(configId);
            if (target == null || target.state != AdminConfigState.ArTestPassed || string.IsNullOrWhiteSpace(target.verifiedFingerprint)) return false;
            foreach (AdminTrainingConfiguration config in configurations)
                if (config.state == AdminConfigState.Published) config.state = AdminConfigState.Archived;
            target.state = AdminConfigState.Published;
            target.updatedAt = DateTimeOffset.UtcNow.ToString("O");
            Persist();
            return true;
        }

        public bool RollbackTo(string configId)
        {
            AdminTrainingConfiguration target = Find(configId);
            if (target == null || target.state != AdminConfigState.Archived) return false;
            foreach (AdminTrainingConfiguration config in configurations)
                if (config.state == AdminConfigState.Published) config.state = AdminConfigState.Archived;
            target.state = AdminConfigState.Published;
            target.updatedAt = DateTimeOffset.UtcNow.ToString("O");
            Persist();
            return true;
        }

        public bool BindPublishedTrackers(IEnumerable<SlimeVrHapticTrackerSnapshot> trackers, out string message)
        {
            EnsureLoaded();
            AdminTrainingConfiguration published = CurrentPublished();
            var next = (trackers ?? Array.Empty<SlimeVrHapticTrackerSnapshot>())
                .Where(t => t != null && t.connected && !string.IsNullOrWhiteSpace(t.hardwareId))
                .ToArray();
            var bindings = new List<AdminTrackerBinding>();
            foreach (SlimeVrHapticTrackerSnapshot tracker in next)
            {
                if (!FixedTrackerSlotMapping.TryParse(tracker.bodyPosition, out FixedTrackerSlot slot)) continue;
                if (bindings.Any(b => b.slot == slot)) continue;
                bindings.Add(new AdminTrackerBinding
                {
                    slot = slot,
                    hardwareId = tracker.hardwareId.Trim(),
                    ip = tracker.ip == null ? string.Empty : tracker.ip.Trim(),
                    online = tracker.connected,
                    batteryPercent = tracker.batteryPercent,
                    lastSeenAt = DateTimeOffset.UtcNow.ToString("O"),
                });
            }
            if (bindings.Count != Enum.GetValues(typeof(FixedTrackerSlot)).Length)
            {
                message = "需要同时识别左脚腕、右脚腕、左手腕、右手腕和腰这 5 个追踪器。当前识别到 " + bindings.Count + " 个。";
                return false;
            }
            published.trackerBindings = bindings.OrderBy(b => b.slot).ToArray();
            published.updatedAt = DateTimeOffset.UtcNow.ToString("O");
            Persist();
            message = "已保存 5 个固定追踪器的硬件 ID/MAC。普通用户将只读取这些设备。";
            return true;
        }

        private void Persist()
        {
            JsonFileStore.Save(FileName, new ConfigurationDocument { configurations = configurations.ToArray() });
        }

        private void EnsureLoaded()
        {
            if (configurations != null) return;
            if (!JsonFileStore.TryLoad(FileName, out ConfigurationDocument document) || document?.configurations == null || document.configurations.Length == 0)
            {
                configurations = new List<AdminTrainingConfiguration> { DefaultConfiguration() };
                JsonFileStore.Save(FileName, new ConfigurationDocument { configurations = configurations.ToArray() });
            }
            else
            {
                configurations = document.configurations.Where(c => c != null).ToList();
                foreach (AdminTrainingConfiguration config in configurations) NormalizeTrackerBindings(config);
            }
        }

        private static void NormalizeTrackerBindings(AdminTrainingConfiguration config)
        {
            if (config == null) return;
            var existing = (config.trackerBindings ?? Array.Empty<AdminTrackerBinding>())
                .Where(binding => binding != null)
                .GroupBy(binding => binding.slot)
                .ToDictionary(group => group.Key, group => group.First());
            config.trackerBindings = Enum.GetValues(typeof(FixedTrackerSlot))
                .Cast<FixedTrackerSlot>()
                .Select(slot => existing.TryGetValue(slot, out var binding)
                    ? binding
                    : new AdminTrackerBinding { slot = slot })
                .ToArray();
        }

        private static AdminTrainingConfiguration DefaultConfiguration()
        {
            string now = DateTimeOffset.UtcNow.ToString("O");
            return new AdminTrainingConfiguration
            {
                configId = "cfg_v1_0_0",
                versionName = "v1.0.0",
                state = AdminConfigState.Published,
                createdAt = now,
                updatedAt = now,
                actions = new[]
                {
                    new AdminActionDefinition { actionId = "dead_bug", displayName = "死虫式", trajectoryId = "dead_bug_v1", actionTags = new[] { "flexion_preference", "high_pain" }, coachSets = 1, coachReps = 3, coachHoldSeconds = 10, transitionSeconds = 2, keyFrameHoldSeconds = 10, keyFrameIds = new[] { "start", "hold", "end" }, gameDifficultySeconds = new[] { 5, 10, 20 } },
                    new AdminActionDefinition { actionId = "bird_dog", displayName = "鸟狗式", trajectoryId = "bird_dog_v1", actionTags = new[] { "extension_preference", "low_pain" }, coachSets = 1, coachReps = 3, coachHoldSeconds = 10, transitionSeconds = 2, keyFrameHoldSeconds = 10, keyFrameIds = new[] { "start", "hold", "end" }, gameDifficultySeconds = new[] { 5, 10, 20 } },
                },
                recommendationRules = new[]
                {
                    new AdminRecommendationRule { ruleId = "rule_high_pain", userTag = "准入疼痛较高", actionTag = "high_pain", priority = 120, actionIds = new[] { "dead_bug" } },
                    new AdminRecommendationRule { ruleId = "rule_low_pain", userTag = "腰部疼痛指数低", actionTag = "low_pain", priority = 90, actionIds = new[] { "bird_dog" } },
                },
                userTags = new[] { "准入疼痛较高", "腰部疼痛指数低", "伸展方向偏好", "屈曲方向偏好" },
                actionTags = new[] { "high_pain", "low_pain", "extension_preference", "flexion_preference" },
                trackerBindings = new[]
                {
                    new AdminTrackerBinding { slot = FixedTrackerSlot.LeftAnkle },
                    new AdminTrackerBinding { slot = FixedTrackerSlot.RightAnkle },
                    new AdminTrackerBinding { slot = FixedTrackerSlot.LeftWrist },
                    new AdminTrackerBinding { slot = FixedTrackerSlot.RightWrist },
                    new AdminTrackerBinding { slot = FixedTrackerSlot.Waist },
                },
            };
        }
    }
}
