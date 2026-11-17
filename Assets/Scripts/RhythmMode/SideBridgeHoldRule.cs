using UnityEngine;

/// <summary>One measured attempt per demonstrated side bridge. A true low position arms each attempt.</summary>
public sealed class SideBridgeHoldRule
{
    public enum Stage { Rest, Raising, Holding, Lowering }
    public Stage Phase { get; private set; }
    public bool InputValid { get; private set; }
    public bool HoldingCorrectly { get; private set; }
    public bool LowConfirmed { get; private set; }
    public bool RoundFinished { get; private set; }
    public float ContinuousHoldSeconds => (float)continuousHold;
    public float BestContinuousSeconds => (float)bestContinuous;
    public float TotalValidHoldSeconds => (float)totalValidHold;
    public float RoundValidHoldSeconds => (float)roundValidHold;
    public bool RequiresContinuousHold { get; private set; }
    public float TargetHoldSeconds { get; private set; }
    public float RoundBest { get; private set; }
    public float LastRoundBest { get; private set; }
    public float LastRoundHoldSeconds { get; private set; }
    public float SessionBest { get; private set; }
    public int CompletedRounds { get; private set; }
    public int QualifiedRounds { get; private set; }
    public float DisplayHoldSeconds => RoundFinished ? LastRoundHoldSeconds : RequiresContinuousHold?ContinuousHoldSeconds:RoundValidHoldSeconds;
    public float LiveScore => InputValid && HoldingCorrectly && Phase == Stage.Holding && !RoundFinished
        ? Mathf.Lerp(entryScore,100,Mathf.Clamp01((RequiresContinuousHold?ContinuousHoldSeconds:RoundValidHoldSeconds)/scoreSeconds)) : 0;
    public float EnterProgress { get; private set; }
    public float SustainProgress => EnterProgress-.05f;
    public float LowProgress => rest;
    public float QualityTolerance { get; private set; }
    public float StableSpeed { get; private set; }
    public float EnterQuality { get; private set; }
    public float ValidQuality { get; private set; }
    public float DefaultScoreSeconds { get; private set; }
    public void BreakContinuity(){if(RequiresContinuousHold)continuousHold=pendingHold=0;HoldingCorrectly=false;confirmation=badSeconds=0;}
    float exit, rest, entryScore, scoreSeconds, confirmation, badSeconds, missingSeconds;
    float start, recognizeTop, loweringDrop, loweringSpeed, peak, previousProgress, speed, phaseSeconds, supportLoss;
    bool hasProgress;
    double continuousHold,bestContinuous,totalValidHold,roundValidHold,pendingHold;

    public void Reset(string difficulty)
    {
        // Recognition/quality defaults come from the delivered SideBridgeHoldScorer.
        // The game additionally requires a meaningful high excursion for dose credit;
        // detecting B at 15/35% is feedback, not proof of a full repetition.
        if(difficulty=="simple") {start=.11f;recognizeTop=.15f;exit=.10f;EnterProgress=.60f;rest=.24f;ValidQuality=35;entryScore=85;DefaultScoreSeconds=3;QualityTolerance=1.6f;StableSpeed=.060f;loweringDrop=.10f;loweringSpeed=.035f;}
        else if(difficulty=="hard") {start=.25f;recognizeTop=.45f;exit=.30f;EnterProgress=.70f;rest=.20f;ValidQuality=60;entryScore=75;DefaultScoreSeconds=7;QualityTolerance=1.05f;StableSpeed=.030f;loweringDrop=.07f;loweringSpeed=.025f;}
        else {start=.20f;recognizeTop=.35f;exit=.24f;EnterProgress=.65f;rest=.22f;ValidQuality=50;entryScore=80;DefaultScoreSeconds=5;QualityTolerance=1.25f;StableSpeed=.035f;loweringDrop=.08f;loweringSpeed=.025f;}
        EnterQuality=ValidQuality;
        CompletedRounds=QualifiedRounds=0;LastRoundBest=LastRoundHoldSeconds=SessionBest=0;totalValidHold=0;
        BeginRepetition(DefaultScoreSeconds,DefaultScoreSeconds);
    }

    public void BeginRepetition(float targetSeconds,float fullScoreSeconds,bool continuous=true)
    {
        TargetHoldSeconds=Mathf.Max(.3f,targetSeconds);scoreSeconds=Mathf.Max(.3f,fullScoreSeconds);
        RequiresContinuousHold=continuous;
        RoundFinished=false;ResetAttempt();
    }

    void ResetAttempt()
    {
        Phase=Stage.Rest;LowConfirmed=InputValid=HoldingCorrectly=false;
        continuousHold=bestContinuous=roundValidHold=pendingHold=0;RoundBest=confirmation=badSeconds=missingSeconds=0;
        hasProgress=false;peak=speed=phaseSeconds=supportLoss=0;
    }

    public void Step(float progress,float quality,float deltaTime,bool validInput,float eligibleHoldDelta=-1,bool sideSupport=true,bool stable=true,bool lowPosture=true)
    {
        float dt=float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)?0:Mathf.Clamp(deltaTime,0,.1f);
        float holdDt=float.IsNaN(eligibleHoldDelta)||float.IsInfinity(eligibleHoldDelta)?0:eligibleHoldDelta<0?dt:Mathf.Clamp(eligibleHoldDelta,0,dt);
        InputValid=validInput&&deltaTime<=.1001f&&!float.IsNaN(progress)&&!float.IsInfinity(progress)&&!float.IsNaN(quality)&&!float.IsInfinity(quality);
        HoldingCorrectly=false;
        if(!InputValid)
        {
            float missingDt=float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)?0:Mathf.Max(0,deltaTime);
            confirmation=0;pendingHold=0;missingSeconds+=missingDt;badSeconds+=missingDt;
            if(TrainingScoringTolerance.GapExpired(badSeconds))continuousHold=0;
            // A long tracking loss cannot bridge two unrelated poses into a repetition.
            if(missingSeconds>=.35f&&!RoundFinished)ResetAttempt();
            return;
        }
        missingSeconds=0;
        if(RoundFinished)return;
        float velocity=hasProgress?(progress-previousProgress)/Mathf.Max(.001f,dt):0;
        speed=Mathf.Lerp(speed,velocity,1-Mathf.Exp(-dt*10));previousProgress=progress;hasProgress=true;phaseSeconds+=dt;
        switch(Phase)
        {
            case Stage.Rest:
                if(progress<=rest&&lowPosture)
                {confirmation+=dt;if(confirmation+.00001f>=.08f)LowConfirmed=true;}
                else
                {
                    confirmation=0;
                    if(LowConfirmed&&progress>=start){ChangePhase(Stage.Raising);StepRaising(progress,quality,dt,holdDt,sideSupport,stable);}
                }
                break;
            case Stage.Raising: StepRaising(progress,quality,dt,holdDt,sideSupport,stable);break;
            case Stage.Holding:
                peak=Mathf.Max(peak,progress);
                HoldingCorrectly=sideSupport&&stable&&progress>=SustainProgress&&quality>=ValidQuality;
                if(HoldingCorrectly){badSeconds=0;AddHold(holdDt);}
                else
                {
                    badSeconds+=dt;
                    // Reject isolated noisy samples without counting them as valid time.
                    if(TrainingScoringTolerance.GapExpired(badSeconds))continuousHold=0;
                }
                supportLoss=sideSupport?0:supportLoss+dt;
                // The delivery's relative-peak descent detection avoids waiting
                // until the pelvis is almost back on the floor. Confirmation
                // also prevents one bad packet from ending a real hold.
                // The game recording itself contains >8% high-plateau ripples.
                // Require leaving the forgiving high zone as well as the
                // delivery's trend/drop; otherwise a correct replay ends early.
                bool descending=speed<=-loweringSpeed&&progress<=peak-loweringDrop&&progress<EnterProgress;
                if(descending||progress<=exit||supportLoss>=.30f)
                {
                    confirmation+=dt;
                    if(confirmation+.00001f>=.15f){ChangePhase(Stage.Lowering);HoldingCorrectly=false;}
                }
                else confirmation=0;
                break;
            case Stage.Lowering:
                if(progress<=rest&&lowPosture)
                {
                    confirmation+=dt;
                    if(confirmation+.00001f>=.08f&&phaseSeconds+.00001f>=.20f)
                    {
                        float achieved=RequiresContinuousHold?BestContinuousSeconds:RoundValidHoldSeconds;
                        CompletedRounds++;if(TrainingScoringTolerance.HoldQualified(achieved,TargetHoldSeconds,RequiresContinuousHold))QualifiedRounds++;
                        LastRoundBest=RoundBest;LastRoundHoldSeconds=achieved;
                        RoundFinished=true;Phase=Stage.Rest;continuousHold=0;
                    }
                }
                else confirmation=0;
                break;
        }
        RoundBest=Mathf.Max(RoundBest,LiveScore);SessionBest=Mathf.Max(SessionBest,RoundBest);
    }

    void ChangePhase(Stage value)
    {
        Phase=value;confirmation=phaseSeconds=supportLoss=0;
        if(value==Stage.Lowering)continuousHold=0;
    }

    void StepRaising(float progress,float quality,float dt,float holdDt,bool sideSupport,bool stable)
    {
        peak=Mathf.Max(peak,progress);
        if(progress>=recognizeTop&&sideSupport)
        {
            badSeconds=0;confirmation+=dt;
            if(progress>=EnterProgress&&quality>=ValidQuality&&stable)pendingHold+=holdDt;
            if(confirmation+.00001f>=.15f)
            {
                ChangePhase(Stage.Holding);HoldingCorrectly=progress>=EnterProgress&&quality>=ValidQuality&&stable;
                // These already observed good samples are real hold time, not a timer bonus.
                AddHold((float)pendingHold);pendingHold=0;
            }
        }
        else
        {
            // A brief dip near the entry boundary does not erase all preceding
            // stable samples. Only actually good entry samples add hold time.
            badSeconds+=dt;
            if(badSeconds>.1f||progress<SustainProgress||quality<ValidQuality){confirmation=0;pendingHold=0;}
            if(progress<=rest){Phase=Stage.Rest;LowConfirmed=true;badSeconds=0;}
        }
    }

    void AddHold(float dt)
    {
        continuousHold+=dt;totalValidHold+=dt;roundValidHold+=dt;
        bestContinuous=System.Math.Max(bestContinuous,continuousHold);
    }
}
