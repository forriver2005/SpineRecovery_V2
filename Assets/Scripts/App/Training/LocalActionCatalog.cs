using System;
using System.Collections.Generic;

namespace SpineRecovery.App
{
    /// <summary>
    /// Server action id to the local Unity execution route.
    /// Keep this table as the single extension point for future doctor actions.
    /// </summary>
    public sealed class LocalActionDefinition
    {
        public string ServerActionId;
        public string LocalKey;
        public string FemaleCoachScene;
        public string MaleCoachScene;
        public string GameScene;
        public string EmbeddedMotionResource;

        public string CoachScene(bool male)
        {
            return male ? MaleCoachScene : FemaleCoachScene;
        }
    }

    public static class LocalActionCatalog
    {
        private static readonly Dictionary<string, LocalActionDefinition> Definitions =
            new Dictionary<string, LocalActionDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["dead_bug"] = new LocalActionDefinition
                {
                    ServerActionId = "dead_bug", LocalKey = "dead_bug",
                    FemaleCoachScene = "DeadBugPractice", MaleCoachScene = "maleDeadBugPractice",
                    GameScene = "SpineV2Minimal"
                },
                ["bird_dog"] = new LocalActionDefinition
                {
                    ServerActionId = "bird_dog", LocalKey = "bird_dog",
                    FemaleCoachScene = "BirdDogPractice", MaleCoachScene = "maleBirdDogPractice",
                    GameScene = "BirdDogMinimal"
                },
                // Future package-only mappings. They become executable after a local scene
                // and action contract are added for the corresponding server action id.
                ["1"] = new LocalActionDefinition { ServerActionId = "1", LocalKey = "clip_01", EmbeddedMotionResource = "ActionLibrary/Motions/clip_01" },
                ["2"] = new LocalActionDefinition { ServerActionId = "2", LocalKey = "clip_03", EmbeddedMotionResource = "ActionLibrary/Motions/clip_03" },
                ["3"] = new LocalActionDefinition { ServerActionId = "3", LocalKey = "clip_05", EmbeddedMotionResource = "ActionLibrary/Motions/clip_05" },
                ["4"] = new LocalActionDefinition { ServerActionId = "4", LocalKey = "clip_07", EmbeddedMotionResource = "ActionLibrary/Motions/clip_07" },
                ["5"] = new LocalActionDefinition { ServerActionId = "5", LocalKey = "clip_09", EmbeddedMotionResource = "ActionLibrary/Motions/clip_09" },
                ["6"] = new LocalActionDefinition { ServerActionId = "6", LocalKey = "clip_10", EmbeddedMotionResource = "ActionLibrary/Motions/clip_10" },
                ["7"] = new LocalActionDefinition { ServerActionId = "7", LocalKey = "clip_15", EmbeddedMotionResource = "ActionLibrary/Motions/clip_15" },
                ["8"] = new LocalActionDefinition { ServerActionId = "8", LocalKey = "clip_17", EmbeddedMotionResource = "ActionLibrary/Motions/clip_17" },
                ["9"] = new LocalActionDefinition { ServerActionId = "9", LocalKey = "clip_19", EmbeddedMotionResource = "ActionLibrary/Motions/clip_19" },
                ["10"] = new LocalActionDefinition { ServerActionId = "10", LocalKey = "clip_21", EmbeddedMotionResource = "ActionLibrary/Motions/clip_21" },
                ["11"] = new LocalActionDefinition { ServerActionId = "11", LocalKey = "clip_22", EmbeddedMotionResource = "ActionLibrary/Motions/clip_22" }
            };

        public static bool IsSupported(string serverActionId)
        {
            var definition = Find(serverActionId);
            return definition != null && !string.IsNullOrWhiteSpace(definition.FemaleCoachScene) &&
                   !string.IsNullOrWhiteSpace(definition.GameScene);
        }

        public static LocalActionDefinition Find(string serverActionId)
        {
            if (string.IsNullOrWhiteSpace(serverActionId)) return null;
            Definitions.TryGetValue(serverActionId.Trim(), out var definition);
            return definition;
        }
    }
}
