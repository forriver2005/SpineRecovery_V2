using System;

/// <summary>Packaged coach/game pairs. Preview-only recordings are deliberately excluded.</summary>
public static class TrainingActionCatalog
{
    public sealed class Entry
    {
        public readonly string Key, Name, Coach, Game;
        public readonly string[] Aliases;
        public Entry(string key, string name, string coach, string game, params string[] aliases)
        { Key = key; Name = name; Coach = coach; Game = game; Aliases = aliases; }
    }

    public static readonly Entry[] Entries = {
        new Entry("dead_bug", "死虫", "DeadBugPractice", "DeadBug_RingSkinC_Recorded", "deadbug", "dead_bug_recorded"),
        new Entry("bird_dog", "鸟狗", "BirdDogPractice", "BirdDog_RingSkinC_Recorded", "birddog", "bird_dog_recorded"),
        new Entry("hip_thrust", "臀桥", "HipThrustPractice", "HipThrust_RingBridge_NewScoring", "hipthrust", "hipthrust_newscore"),
        new Entry("side_bridge", "左侧桥", "SideBridgePractice", "SideBridge_RingBridge_Dynamic", "sidebridge", "side_bridge_recorded"),
        new Entry("side_bridge_right", "右侧桥", "SideBridgeOppositePractice", "SideBridgeOpposite_RingBridge_Dynamic", "sidebridge_right"),
        new Entry("cat_camel", "卧位猫", "CatCamelPractice", "LyingCatPractice_TaikoNight", "lying_cat", "lyingcat", "lying_cat_taiko", "lying_cat_recorded"),
        new Entry("curl_up", "卷腹", "CurlUpPractice", "CrunchPractice_TaikoNight", "crunch", "crunch_taiko", "crunch_recorded"),
        new Entry("hip_thrust_taiko", "臀桥 · 星轨敲鼓", "HipThrustPractice", "HipThrust_TaikoNight_Dynamic", "hipthrust_taiko", "hipthrust_taiko_night"),
        new Entry("hip_thrust_hold30", "臀桥 · 保持", "HipThrustPractice", "HipThrust_RingBridge_Hold30", "hipthrust_hold30"),
        new Entry("side_bridge_hold30", "左侧桥 · 保持", "SideBridgePractice", "SideBridge_RingBridge_Hold30", "sidebridge_hold30"),
        new Entry("side_bridge_right_hold30", "右侧桥 · 保持", "SideBridgeOppositePractice", "SideBridgeOpposite_RingBridge_Hold30", "sidebridge_right_hold30")
    };

    public static Entry Find(string action)
    {
        if (string.IsNullOrEmpty(action)) return null;
        if (action.EndsWith("_coach", StringComparison.Ordinal)) action = action.Substring(0, action.Length - 6);
        else if (action.EndsWith("_game", StringComparison.Ordinal)) action = action.Substring(0, action.Length - 5);
        return Array.Find(Entries, entry => entry.Key == action || Array.IndexOf(entry.Aliases, action) >= 0);
    }

    public static Entry ForGame(string scene) => Array.Find(Entries, entry => entry.Game == scene);
    public static Entry ForCoach(string scene)
    {
        if (scene != null && scene.StartsWith("male", StringComparison.Ordinal)) scene = scene.Substring(4);
        return Array.Find(Entries, entry => entry.Coach == scene);
    }
}
