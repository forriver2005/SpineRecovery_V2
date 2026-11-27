// Shared phase labels used by the imported Crunch and Lying Cat rule files.
public enum HipThrustPhase
{
    WaitingForRaise,
    Raising,
    Holding,
    Lowering,
    Completed
}

public enum HipThrustDifficultyMode
{
    Hard,
    Medium,
    Easy
}

public interface ITaikoHoldScorer
{
    bool TrackingAvailable { get; }
    float ValidHoldSeconds { get; }
    float TargetHoldSeconds { get; }
    float CurrentLiveComparisonScore { get; }
    float LastRoundScore { get; }
    event System.Action<float> RoundCompleted;
    bool BeginContinuousSession();
    void CancelSession();
    void SetDifficultyMode(HipThrustDifficultyMode mode);
}
