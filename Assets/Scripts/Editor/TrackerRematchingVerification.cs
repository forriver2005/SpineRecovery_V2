using System;
using System.IO;
using System.Linq;
using SpineRecovery.App;
using UnityEngine;

public static class TrackerRematchingVerification
{
    private static int checks;

    public static void Run()
    {
        ClinicalPrescriptionVerification.Run();
        string path = JsonFileStore.PathFor("spine-admin-configurations.json");
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            var old = new AdminTrainingConfiguration
            {
                configId = "published", state = AdminConfigState.Published,
                trackerBindingsLocked = true, trackerBindings = Trackers(1),
                userTags = new[] { "preserve-user-tags" }
            };
            var archived = JsonUtility.FromJson<AdminTrainingConfiguration>(JsonUtility.ToJson(old));
            archived.configId = "archived";
            archived.state = AdminConfigState.Archived;
            File.WriteAllText(path, "{\"configurations\":[" + JsonUtility.ToJson(old) + "," +
                JsonUtility.ToJson(archived) + "]}");
            var repository = new JsonAdminConfigRepository();
            Check(repository.ListConfigurations().All(Unbound), "Upgrade removes every legacy MAC allowlist");
            Check(repository.CurrentPublished().userTags.Single() == "preserve-user-tags", "Upgrade preserves other configuration");
            Check(File.ReadAllText(path).Contains("\"trackerBindingVersion\": 1"), "Migration is persisted");
            Check(repository.BindPublishedTrackers(Trackers(2), out _), "New sensors can be selected");
            Check(repository.BindPublishedTrackers(Trackers(3), out _), "A saved selection can be replaced");
            var reloaded = new JsonAdminConfigRepository();
            Check(reloaded.CurrentPublished().trackerBindings.All(t => t.hardwareId.StartsWith("03:")),
                "Restart preserves newly selected devices rather than migrating again");
            Check(!reloaded.BindPublishedTrackers(Trackers(4).Take(4), out _), "Incomplete selection rejected");
            var duplicate = Trackers(4);
            duplicate[4].hardwareId = duplicate[0].hardwareId;
            Check(!reloaded.BindPublishedTrackers(duplicate, out _), "Duplicate devices rejected");
            Check(reloaded.CurrentPublished().trackerBindings.All(t => t.hardwareId.StartsWith("03:")),
                "Invalid replacement leaves current selection intact");
            reloaded.ClearTrackerBindings();
            Check(reloaded.ListConfigurations().All(Unbound), "Rematching clears current and historical bindings");
            Check(new JsonAdminConfigRepository().ListConfigurations().All(Unbound), "Clearing survives restart");
            Check(reloaded.RollbackTo("archived") && Unbound(reloaded.CurrentPublished()), "Rollback cannot restore obsolete MACs");
            Check(reloaded.BindPublishedTrackers(Trackers(5), out _), "Matching works again after clearing");
            Debug.Log("TrackerRematchingVerification PASS: " + checks + " checks");
        }
        finally
        {
            if (original == null) File.Delete(path);
            else File.WriteAllBytes(path, original);
        }
    }

    private static bool Unbound(AdminTrainingConfiguration config) =>
        !config.trackerBindingsLocked && !config.trackerBindingsDisabled &&
        config.trackerBindings.All(t => string.IsNullOrEmpty(t.hardwareId));

    private static AdminTrackerBinding[] Trackers(int group) =>
        new[] { "left_lower_leg", "right_lower_leg", "left_lower_arm", "right_lower_arm", "waist" }
            .Select((position, i) => new AdminTrackerBinding
            {
                bodyPosition = position, hardwareId = $"{group:X2}:00:00:00:00:{i:X2}", online = true
            }).ToArray();

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
