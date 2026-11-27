using System;
using UnityEngine;

namespace BridgeWave
{
    /// <summary>A fixed standard-motion chart. It has no dependency on patient input or a scorer.</summary>
    [CreateAssetMenu(menuName = "Training/Bridge Wave Standard Track")]
    public sealed class BridgeWaveTrack : ScriptableObject
    {
        [Serializable]
        public sealed class Cycle
        {
            public float liftSeconds, holdSeconds, lowerSeconds;
            public AnimationCurve lift = AnimationCurve.EaseInOut(0, 0, 1, 1);
            public AnimationCurve lower = AnimationCurve.EaseInOut(0, 1, 1, 0);
            public float Duration => liftSeconds + holdSeconds + lowerSeconds;
        }
        public HipThrustPhaseTimeline source;
        public float leadInSeconds = 1.5f;
        public float restSeconds = .6f;
        public Cycle[] cycles;
        public float CycleStart(int index)
        {
            float t = leadInSeconds;
            for (int i = 0; i < index; i++) t += cycles[i % cycles.Length].Duration + restSeconds;
            return t;
        }
        public float Duration(int repetitions) => CycleStart(repetitions - 1) + cycles[(repetitions - 1) % cycles.Length].Duration;
        public Sample Evaluate(float time, int repetitions)
        {
            for (int i = 0; i < repetitions; i++)
            {
                float s = time - CycleStart(i);
                Cycle c = cycles[i % cycles.Length];
                if (s < 0) return new Sample { cycle = i, phase = BridgeWaveGame.Phase.Rest };
                if (s < c.liftSeconds) return new Sample { cycle = i, phase = BridgeWaveGame.Phase.Lift, level = c.lift.Evaluate(s / c.liftSeconds) };
                s -= c.liftSeconds;
                if (s < c.holdSeconds) return new Sample { cycle = i, phase = BridgeWaveGame.Phase.Hold, level = 1 };
                s -= c.holdSeconds;
                if (s < c.lowerSeconds) return new Sample { cycle = i, phase = BridgeWaveGame.Phase.Lower, level = c.lower.Evaluate(s / c.lowerSeconds) };
            }
            return new Sample { cycle = repetitions, phase = BridgeWaveGame.Phase.Rest };
        }
        public int Completed(float time, int repetitions)
        {
            int count = 0;
            for (int i = 0; i < repetitions; i++)
                if (time >= CycleStart(i) + cycles[i % cycles.Length].Duration) count++;
            return count;
        }
        public struct Sample { public int cycle; public float level; public BridgeWaveGame.Phase phase; }
    }

    /// <summary>Continuous geometric judgement; advancing the track never changes patient height.</summary>
    public sealed class BridgeWaveSession
    {
        public readonly BridgeWaveTrack Track;
        public readonly int Repetitions;
        public readonly float Tolerance;
        public readonly float SpeedPixels, HeightPixels, SamplePixels;
        public float RadiusPixels => Tolerance * HeightPixels;
        public float Elapsed { get; private set; }
        public float InTrackSeconds { get; private set; }
        public float SampledSeconds { get; private set; }
        public int Hits { get; private set; }
        public int Combo { get; private set; }
        public bool Inside { get; private set; }
        public bool Paused { get; private set; }
        public bool Finished => Elapsed >= Track.Duration(Repetitions);
        public float Progress => Mathf.Clamp01(Elapsed / Track.Duration(Repetitions));
        public float NextPulseAt { get; private set; }
        public BridgeWaveSession(BridgeWaveTrack track, int repetitions, float tolerance,
            float speedPixels = 76f, float heightPixels = 184f, float samplePixels = 1f)
        {
            Track = track; Repetitions = Mathf.Max(1, repetitions); Tolerance = tolerance;
            SpeedPixels = Mathf.Max(1, speedPixels); HeightPixels = Mathf.Max(1, heightPixels); SamplePixels = Mathf.Max(.25f,samplePixels);
        }
        /// <summary>Distance to the centre polyline in design pixels. A point inside a constant-width stroke
        /// must have a closest point within one radius horizontally, so only this small neighbourhood is needed.
        /// Keeping vertical tolerance constant on slopes would disagree with the visible equal-width ribbon.</summary>
        public float DistanceToTrackPixels(float time, float height)
        {
            if(float.IsNaN(height)||float.IsInfinity(height))return float.PositiveInfinity;
            Vector2 point = new Vector2(0,height*HeightPixels);
            float radius=RadiusPixels,minimum=float.PositiveInfinity;
            int halfSamples=Mathf.CeilToInt(radius/SamplePixels)+1;
            float firstX=-halfSamples*SamplePixels;
            Vector2 previous=new Vector2(firstX,Track.Evaluate(time+firstX/SpeedPixels,Repetitions).level*HeightPixels);
            for(int i=-halfSamples+1;i<=halfSamples;i++)
            {
                float x=i*SamplePixels;
                Vector2 next=new Vector2(x,Track.Evaluate(time+x/SpeedPixels,Repetitions).level*HeightPixels);
                Vector2 segment=next-previous;
                float u=Mathf.Clamp01(Vector2.Dot(point-previous,segment)/Mathf.Max(.000001f,segment.sqrMagnitude));
                minimum=Mathf.Min(minimum,(point-(previous+u*segment)).sqrMagnitude);previous=next;
            }
            return Mathf.Sqrt(minimum);
        }
        public bool IsInsideCurve(float time,float height,bool fresh)
            => fresh && DistanceToTrackPixels(time,height) <= RadiusPixels;
        public void Pause(bool paused) { Paused = paused; Inside = false; }
        public bool Advance(float dt, float height, bool fresh)
        {
            if (Paused || Finished || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) { Inside = false; return false; }
            float step = Mathf.Min(dt, Track.Duration(Repetitions) - Elapsed);
            Elapsed += step;
            var target = Track.Evaluate(Elapsed, Repetitions);
            bool active = target.phase != BridgeWaveGame.Phase.Rest && !Finished;
            // A stalled frame must not backfill rewards for an unobserved interval.
            Inside = active && dt <= .1f && IsInsideCurve(Elapsed,BridgeWaveHeightInput.LimitToQualifiedHeight(height),fresh);
            if (active) SampledSeconds += step;
            if (Inside) InTrackSeconds += step; else Combo = 0;
            if (!Inside || Elapsed < NextPulseAt) return false;
            NextPulseAt = Elapsed + .3f;
            Hits++; Combo++;
            return true;
        }
    }
}
