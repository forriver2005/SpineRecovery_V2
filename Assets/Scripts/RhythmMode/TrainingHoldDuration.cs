using System;

/// <summary>Shared prescription duration contract, in seconds per repetition.</summary>
public static class TrainingHoldDuration
{
    public const float DefaultSeconds = 30f;
    public const float MinimumSeconds = 1f;
    public const float MaximumSeconds = 600f;

    // Missing JSON fields deserialize as zero. Zero explicitly restores the default.
    public static float Resolve(float seconds)
    {
        if (seconds == 0f) return DefaultSeconds;
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < MinimumSeconds || seconds > MaximumSeconds)
            throw new ArgumentException("保持时长 holdSeconds 须为 1–600 秒；省略或 0 使用默认 30 秒。");
        return seconds;
    }
}
