using System;
using UnityEngine;

[Serializable]
public sealed class GameWorkoutAction
{
    public string action;
    public string difficulty = "standard";
    public int sets = 1;
    // Exact-dose variants use prescription reps; other games use their standard scene dose.
    public int reps;
    // Seconds per repetition for timed-hold actions; omitted/zero means 30 seconds.
    public float holdSeconds;
}

[Serializable]
public sealed class GameWorkoutPlan
{
    public GameWorkoutAction[] actions;

    public static string SceneFor(string action, bool recordedCoach = false)
    {
        if (action == "birddog_hold30") return "BirdDog_RingBridge_Hold30";
        if (action == "deadbug_hold30") return "DeadBug_RingBridge_Hold30";
        if (action == "crunch_hold30") return "Crunch_RingBridge_Hold30";
        if (action == "lyingcat_hold30" || action == "lying_cat_hold30") return "LyingCat_RingBridge_Hold30";
        if (action == "hipthrust_taiko" || action == "hipthrust_taiko_night") return "HipThrust_TaikoNight_Dynamic";
        if (action == "crunch" || action == "crunch_taiko") return "CrunchPractice_TaikoNight";
        if (action == "lyingcat" || action == "lying_cat" || action == "lying_cat_taiko") return "LyingCatPractice_TaikoNight";
        if (action == "hipthrust_curve" || action == "bridgewave") return "BridgeWave_StandardFollow";
        if (action == "hipthrust_newscore") return "HipThrust_RingBridge_NewScoring";
        if (action == "hipthrust_hold30") return "HipThrust_RingBridge_Hold30";
        if (action == "sidebridge") return "SideBridge_RingBridge_Dynamic";
        if (action == "sidebridge_hold30") return "SideBridge_RingBridge_Hold30";
        if (action == "sidebridge_right") return "SideBridgeOpposite_RingBridge_Dynamic";
        if (action == "sidebridge_right_hold30") return "SideBridgeOpposite_RingBridge_Hold30";
        if (action == "rightsidebridge") return "SideBridgeRight_RingBridge_Dynamic";
        if (action == "rightsidebridge_hold30") return "SideBridgeRight_RingBridge_Hold30";
        if (recordedCoach)
        {
            switch (action)
            {
                case "deadbug": return "DeadBug_RingSkinC_Recorded";
                case "birddog": return "BirdDog_RingSkinC_Recorded";
                case "hipthrust": return "HipThrust_RingBridge_NewScoring";
                default: return null;
            }
        }
        switch (action)
        {
            case "deadbug": return "DeadBug_RingSkinC_Recorded";
            case "birddog": return "BirdDog_RingSkinC_Recorded";
            case "hipthrust": return "HipThrust_RingBridge_NewScoring";
            default: return null;
        }
    }

    public static string ActionName(string action)
    {
        switch (action)
        {
            case "birddog_hold30": return "鸟狗式 · 保持";
            case "deadbug_hold30": return "死虫式 · 保持";
            case "crunch_hold30": return "卷腹 · 保持";
            case "lyingcat_hold30":
            case "lying_cat_hold30": return "卧位猫式 · 保持";
            case "hipthrust_taiko":
            case "hipthrust_taiko_night": return "臀桥 · 星轨敲鼓";
            case "crunch":
            case "crunch_taiko": return "卷腹 · 星轨敲鼓";
            case "lyingcat":
            case "lying_cat":
            case "lying_cat_taiko": return "卧位猫式 · 星轨敲鼓";
            case "deadbug": return "死虫式";
            case "birddog": return "鸟狗式";
            case "hipthrust": return "臀桥";
            case "hipthrust_curve":
            case "bridgewave": return "臀桥";
            case "hipthrust_newscore": return "臀桥 · 新评分";
            case "hipthrust_hold30": return "臀桥 · 保持";
            case "sidebridge": return "左侧桥 · 动态";
            case "sidebridge_hold30": return "左侧桥 · 保持";
            case "sidebridge_right":
            case "rightsidebridge": return "右侧桥 · 动态";
            case "sidebridge_right_hold30":
            case "rightsidebridge_hold30": return "右侧桥 · 保持";
            default: return action;
        }
    }

    public static bool IsTimedHold(string action) => action == "birddog_hold30" || action == "deadbug_hold30" || action == "crunch_hold30" || action == "lyingcat_hold30" || action == "lying_cat_hold30" || action == "hipthrust_hold30" || action == "sidebridge_hold30" || action == "sidebridge_right_hold30" || action == "rightsidebridge_hold30";

    public static string ActionName(GameWorkoutAction item) => ActionName(item.action) +
        (IsTimedHold(item.action) ? $" {TrainingHoldDuration.Resolve(item.holdSeconds):0.##}秒" : "");

    public static string DifficultyName(string difficulty) => difficulty == "simple" ? "简单" : difficulty == "hard" ? "困难" : "标准";

    public static bool IsTaikoNightAction(string action) =>
        action == "hipthrust_taiko" || action == "hipthrust_taiko_night" ||
        action == "crunch" || action == "crunch_taiko" ||
        action == "lyingcat" || action == "lying_cat" || action == "lying_cat_taiko";

    public GameWorkoutPlan ValidatedCopy()
    {
        if (actions == null || actions.Length == 0 || actions.Length > 64)
            throw new ArgumentException("动作串必须包含 1–64 个动作。");
        var copy = new GameWorkoutPlan { actions = new GameWorkoutAction[actions.Length] };
        int total = 0;
        for (int i = 0; i < actions.Length; i++)
        {
            var item = actions[i];
            if (item == null || SceneFor(item.action) == null) throw new ArgumentException($"第 {i + 1} 个动作无效。");
            if (item.difficulty != "simple" && item.difficulty != "standard" && item.difficulty != "hard")
                throw new ArgumentException($"第 {i + 1} 个动作难度无效。");
            if (item.sets < 1 || item.sets > 100) throw new ArgumentException("每个动作的组数必须为 1–100。");
            if (RequiresExactReps(item.action) && (item.reps < 1 || item.reps > 100))
                throw new ArgumentException("该动作必须提供处方 reps（1–100）。");
            if (SupportsOptionalReps(item.action) && (item.reps < 0 || item.reps > 100))
                throw new ArgumentException("该动作的 reps 必须为 0–100；0 使用难度默认次数。");
            float holdSeconds = item.holdSeconds;
            if (IsTimedHold(item.action)) holdSeconds = TrainingHoldDuration.Resolve(holdSeconds);
            else if (holdSeconds != 0f) throw new ArgumentException("holdSeconds 仅适用于保持类动作。");
            total += item.sets;
            copy.actions[i] = new GameWorkoutAction { action = item.action, difficulty = item.difficulty, sets = item.sets, reps = item.reps, holdSeconds = holdSeconds };
        }
        if (total > 1000) throw new ArgumentException("动作串总组数不能超过 1000。");
        return copy;
    }
    public static bool SupportsOptionalReps(string action) => action == "birddog" || action == "deadbug" || IsTaikoNightAction(action) || action == "hipthrust_curve" || action == "bridgewave" || action == "hipthrust" || action == "hipthrust_newscore";
    public static bool RequiresExactReps(string action) => IsTimedHold(action) ||
        action == "sidebridge" || action == "sidebridge_right" || action == "rightsidebridge";
}

