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
        bool BindPublishedTrackers(IEnumerable<AdminTrackerBinding> bindings, out string message);
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
            public int trackerBindingVersion;
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
            JsonFileStore.Save(FileName, Document());
            EmbeddedSlimeVrBridge.ReloadFixedBindings();
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

        public bool BindPublishedTrackers(IEnumerable<AdminTrackerBinding> bindings, out string message)
        {
            EnsureLoaded();
            AdminTrainingConfiguration published = CurrentPublished();
            var next = (bindings ?? Array.Empty<AdminTrackerBinding>())
                .Where(binding => binding != null && !string.IsNullOrWhiteSpace(binding.hardwareId))
                .ToArray();
            if (!AdminTrackerBinding.CanBind(next, out message)) return false;
            string now = DateTimeOffset.UtcNow.ToString("O");
            published.trackerBindings = next.OrderBy(binding => binding.bodyPosition).Select(binding => new AdminTrackerBinding
            {
                bodyPosition = binding.bodyPosition,
                hardwareId = binding.hardwareId.Trim(),
                ip = binding.ip?.Trim() ?? string.Empty,
                online = binding.online,
                batteryPercent = binding.batteryPercent,
                lastSeenAt = now,
            }).ToArray();
            published.trackerBindingsLocked = true;
            published.trackerBindingsDisabled = false;
            published.updatedAt = DateTimeOffset.UtcNow.ToString("O");
            Persist();
            message = "已保存这 5 个 IMU，可随时重新匹配。";
            return true;
        }

        public void ClearTrackerBindings()
        {
            EnsureLoaded();
            // Also clear archived/draft snapshots so publishing or rollback cannot
            // silently reinstate a previous device's MAC allowlist.
            foreach (var config in configurations) ClearBindings(config);
            Persist();
        }

        private static void ClearBindings(AdminTrainingConfiguration config)
        {
            config.trackerBindings = Array.Empty<AdminTrackerBinding>();
            config.trackerBindingsLocked = false;
            config.trackerBindingsDisabled = false;
        }

        private void Persist()
        {
            JsonFileStore.Save(FileName, Document());
            EmbeddedSlimeVrBridge.ReloadFixedBindings();
        }

        private ConfigurationDocument Document() => new ConfigurationDocument
        {
            trackerBindingVersion = 1,
            configurations = configurations.ToArray()
        };

        private void EnsureLoaded()
        {
            if (configurations != null) return;
            if (!JsonFileStore.TryLoad(FileName, out ConfigurationDocument document) || document?.configurations == null || document.configurations.Length == 0)
            {
                configurations = new List<AdminTrainingConfiguration> { DefaultConfiguration() };
                Persist();
            }
            else
            {
                configurations = document.configurations.Where(c => c != null).ToList();
                if (document.trackerBindingVersion < 1)
                {
                    foreach (var config in configurations) ClearBindings(config);
                    Persist();
                }
                foreach (AdminTrainingConfiguration config in configurations) NormalizeTrackerBindings(config);
            }
        }

        private static void NormalizeTrackerBindings(AdminTrainingConfiguration config)
        {
            if (config == null || (config.trackerBindings?.Any(row => !string.IsNullOrEmpty(row?.bodyPosition)) ?? false)) return;
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
