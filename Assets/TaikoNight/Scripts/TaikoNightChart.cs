using UnityEngine;

namespace TaikoNight
{
    /// <summary>Piecewise keyframe evaluation; ease 0 linear, 1 OutQuad, 2 InOutSine. Clamps outside the key range.</summary>
    public static class Keys
    {
        public static float Eval(float t, float[] times, float[] values, int ease)
        {
            if (t <= times[0]) return values[0];
            for (int i = 0; i < times.Length - 1; i++)
            {
                if (t <= times[i + 1])
                {
                    float span = times[i + 1] - times[i];
                    float u = span > 0f ? (t - times[i]) / span : 1f;
                    if (ease == 1) u = Ease.OutQuad(u);
                    else if (ease == 2) u = Ease.InOutSine(u);
                    return Mathf.Lerp(values[i], values[i + 1], u);
                }
            }
            return values[values.Length - 1];
        }
    }

    /// <summary>
    /// Demo chart for 敲鼓 · 星轨统一版 (6 s loop). Notes roll in from the right across the drum head at 110 design px/s
    /// and are judged when their centre reaches the judge circle (drum-head centre, x = 422).
    ///   1.0 s  抬起 note: left stick strikes, pink ripple + head wave, PERFECT, hold ring starts
    ///   1.0 - 4.0 s  gold hold ring fills around the judge circle, gold glow builds
    ///   4.0 s  放下 note: right stick strikes, blue ripple + head wave, PERFECT
    /// In production drive DesignSpace.Now from music time and spawn notes from the chart.
    /// </summary>
    public class TaikoNightChart : MonoBehaviour
    {
        [Header("Timing")]
        public float loopLength = 6f;
        public float speedPx = 110f;
        public float liftHit = 1f;
        public float lowerHit = 4f;

        [Header("Lane")]
        public Transform strip;

        [Header("Judge circle")]
        public Transform judge;
        public SpriteRenderer holdGlow;
        public SpriteRenderer rippleDon;
        public SpriteRenderer rippleKa;
        public LineRenderer holdRingGlow;
        public LineRenderer holdRing;
        public Vector2 judgeCenterPx = new Vector2(422f, 232f);
        public float holdRadiusPx = 40f;
        public int holdSegments = 96;
        public SpriteRenderer perfect;
        [Tooltip("Keep the chart's visual demo feedback enabled for the standalone demo. " +
                 "Gameplay scenes should drive judgement from the tracker instead.")]
        public bool automaticPerfectFeedback = true;

        [Header("Drum")]
        public SpriteRenderer headFlash;
        public SpriteRenderer waveDon;
        public SpriteRenderer waveKa;
        public Transform stickLeft;
        public Transform stickRight;
        public SpriteRenderer sparkDon;
        public SpriteRenderer sparkKa;

        static readonly float[] StickT = { -0.36f, -0.18f, 0f, 0.12f, 0.2f, 0.5f };
        static readonly float[] StickV = { 12f, 19f, 0f, 5f, 2f, 12f };   // degrees raised from the strike pose
        Vector3 perfectBase;

        // Distances in the gameplay lane are measured from real recording timestamps.
        // Fifty design pixels per second leaves even the short cat hold readable.
        const float PlayerPixelsPerSecond = 50f;
        public const float AddedHoldSeconds = .5f;
        static readonly Vector2[] NoNotes = new Vector2[0];
        Vector2[] playerNotes = NoNotes; // x: top of lift, y: start of return
        readonly Transform[] lifts = new Transform[3], lowers = new Transform[3], bars = new Transform[3];
        float barWidth, playerTime, measuredHoldProgress;
        bool measuredLift;
        float measuredHitTime = float.NegativeInfinity;

        public void UsePlayerJudgements()
        {
            automaticPerfectFeedback = false;
            measuredHitTime = float.NegativeInfinity;
            playerNotes = NoNotes;
            measuredHoldProgress = 0f;
            for (int i = 0; i < lifts.Length; i++)
            {
                lifts[i] = strip.Find("Lift" + (i - 1));
                lowers[i] = strip.Find("Lower" + (i - 1));
                bars[i] = strip.Find("HoldBar" + (i - 1));
            }
            barWidth = bars[0].GetComponent<SpriteRenderer>().sprite.bounds.size.x;
            strip.localPosition = Vector3.zero;
            SetPlayerTime(0);
            RenderChart();
        }

        public void SetPlayerNotes(Vector2[] intervals)
        {
            playerNotes = intervals;
            SetPlayerTime(0);
        }

        // Live hold feedback is shown only for a measured, qualifying player pose.
        public void SetMeasuredHold(float fraction) => measuredHoldProgress = Mathf.Clamp01(fraction);

        public void SetPlayerTime(float seconds)
        {
            playerTime = seconds;
            int slot = 0;
            foreach (Vector2 interval in playerNotes)
            {
                float liftOffset = (interval.x - seconds) * PlayerPixelsPerSecond;
                float lowerOffset = (interval.y - seconds) * PlayerPixelsPerSecond;
                if (lowerOffset < -40) continue;
                if (liftOffset > 460 || slot == lifts.Length) break;
                float scale = DesignSpace.PxToUnit;
                PlaceNote(lifts[slot], liftOffset * scale);
                PlaceNote(lowers[slot], lowerOffset * scale);
                PlaceNote(bars[slot], (liftOffset + lowerOffset) * .5f * scale);
                bars[slot].localScale = new Vector3((lowerOffset - liftOffset) * scale / barWidth, 1, 1);
                slot++;
            }
            for (; slot < lifts.Length; slot++)
            {
                lifts[slot].gameObject.SetActive(false);
                lowers[slot].gameObject.SetActive(false);
                bars[slot].gameObject.SetActive(false);
            }
        }

        static void PlaceNote(Transform part, float x)
        {
            part.gameObject.SetActive(true);
            Vector3 position = part.localPosition;
            position.x = x;
            part.localPosition = position;
        }

        public void ShowPlayerHit(bool hit)
        {
            if (!hit) measuredHoldProgress = 0f;
            measuredHitTime = hit ? Time.unscaledTime : float.NegativeInfinity;
            if (hit)
            {
                measuredLift = false;
                foreach (Vector2 interval in playerNotes)
                    if (Mathf.Abs(playerTime - interval.x) < .5f) { measuredLift = true; break; }
            }
            RenderChart();
        }

        void Start()
        {
            if (perfect != null) perfectBase = perfect.transform.localPosition;
        }

        void Update() => RenderChart();

        void RenderChart()
        {
            float now = DesignSpace.Now;
            float t = Mathf.Repeat(now, loopLength);

            if (automaticPerfectFeedback && strip != null)
            {
                float shift = Mathf.Repeat(now * speedPx, loopLength * speedPx);
                strip.localPosition = new Vector3(-shift * DesignSpace.PxToUnit, 0f, 0f);
            }

            float hitAge = Time.unscaledTime - measuredHitTime;
            float dLift = automaticPerfectFeedback ? Since(t, liftHit) : measuredLift ? hitAge : float.PositiveInfinity;
            float dLower = automaticPerfectFeedback ? Since(t, lowerHit) : measuredLift ? float.PositiveInfinity : hitAge;
            float dAny = Mathf.Min(dLift, dLower);

            if (judge != null)
            {
                float s = Keys.Eval(dAny, new[] { 0f, 0.06f, 0.3f }, new[] { 1f, 1.16f, 1f }, 1);
                judge.localScale = new Vector3(s, s, 1f);
            }
            Ring(rippleDon, dLift, 0.6f, 0.6f, 2f);
            Ring(rippleKa, dLower, 0.6f, 0.6f, 2f);
            Ring(waveDon, dLift, 0.6f, 0.3f, 1f, 0.9f);
            Ring(waveKa, dLower, 0.6f, 0.3f, 1f, 0.9f);
            if (headFlash != null) SpriteAlpha.Set(headFlash, Keys.Eval(dAny, new[] { 0f, 0.32f }, new[] { 0.45f, 0f }, 1));

            // PERFECT on both hits: pop in, float up, fade
            if (perfect != null)
            {
                float age = automaticPerfectFeedback ? dAny : Time.unscaledTime - measuredHitTime;
                float[] ts = { 0f, 0.08f, 0.22f, 0.75f, 0.95f };
                SpriteAlpha.Set(perfect, Keys.Eval(age, ts, new[] { 0f, 1f, 1f, 1f, 0f }, 1));
                float sc = Keys.Eval(age, ts, new[] { 0.8f, 1.15f, 1f, 1f, 1f }, 1);
                float y = Keys.Eval(age, ts, new[] { 6f, 0f, 0f, -6f, -8f }, 1);
                perfect.transform.localScale = new Vector3(sc, sc, 1f);
                perfect.transform.localPosition = perfectBase + new Vector3(0f, -y * DesignSpace.PxToUnit, 0f);
            }


            // Hold: gold ring fills clockwise from 12 o'clock between the two hits, glow builds, then fades
            float holdLen = lowerHit - liftHit;
            float dh = Mathf.Repeat(t - liftHit, loopLength);
            float fill, alpha;
            if (!automaticPerfectFeedback) { fill = measuredHoldProgress; alpha = fill > 0f ? 1f : 0f; }
            else if (dh < holdLen) { fill = dh / holdLen; alpha = 1f; }
            else if (dh < holdLen + 0.3f) { fill = 1f; alpha = 1f - (dh - holdLen) / 0.3f; }
            else { fill = 0f; alpha = 0f; }
            DrawRing(holdRingGlow, fill, alpha * 0.7f);
            DrawRing(holdRing, fill, alpha);
            if (holdGlow != null)
            {
                float g = 0f;
                if (dh < holdLen) g = dh < 0.3f ? Mathf.Lerp(0f, 0.55f, dh / 0.3f) : Mathf.Lerp(0.55f, 0.9f, (dh - 0.3f) / (holdLen - 0.35f));
                else if (dh < holdLen + 0.35f) g = Mathf.Lerp(0.9f, 0f, (dh - holdLen) / 0.35f);
                SpriteAlpha.Set(holdGlow, automaticPerfectFeedback ? g : .9f * measuredHoldProgress);
            }

            // Sticks: left (pink) on the lift note, right (blue) on the lower note; sprites are drawn in the strike pose
            if (stickLeft != null) stickLeft.localRotation = Quaternion.Euler(0f, 0f, Keys.Eval(automaticPerfectFeedback ? Signed(t, liftHit) : dLift, StickT, StickV, 2));
            if (stickRight != null) stickRight.localRotation = Quaternion.Euler(0f, 0f, -Keys.Eval(automaticPerfectFeedback ? Signed(t, lowerHit) : dLower, StickT, StickV, 2));
            Spark(sparkDon, dLift);
            Spark(sparkKa, dLower);
        }

        float Since(float t, float hit)
        {
            return Mathf.Repeat(t - hit, loopLength);
        }

        float Signed(float t, float hit)
        {
            return Mathf.Repeat(t - hit + loopLength * 0.5f, loopLength) - loopLength * 0.5f;
        }

        static void Ring(SpriteRenderer r, float d, float dur, float s0, float s1, float a0 = 1f)
        {
            if (r == null) return;
            if (d > dur) { SpriteAlpha.Set(r, 0f); return; }
            float k = Ease.OutQuad(d / dur);
            float s = Mathf.Lerp(s0, s1, k);
            r.transform.localScale = new Vector3(s, s, 1f);
            SpriteAlpha.Set(r, a0 * (1f - k));
        }

        static void Spark(SpriteRenderer r, float d)
        {
            if (r == null) return;
            float[] ts = { 0f, 0.03f, 0.45f };
            SpriteAlpha.Set(r, Keys.Eval(d, ts, new[] { 0f, 1f, 0f }, 1));
            float s = Keys.Eval(d, ts, new[] { 0.3f, 1.15f, 0.8f }, 1);
            r.transform.localScale = new Vector3(s, s, 1f);
        }

        void DrawRing(LineRenderer lr, float fill, float alpha)
        {
            if (lr == null) return;
            lr.enabled = fill > 0.001f && alpha > 0.001f;
            if (!lr.enabled) return;
            int count = Mathf.Max(2, Mathf.CeilToInt(holdSegments * fill) + 1);
            lr.positionCount = count;
            Vector3 c = DesignSpace.ToWorld(judgeCenterPx.x, judgeCenterPx.y);
            float rad = holdRadiusPx * DesignSpace.PxToUnit;
            for (int i = 0; i < count; i++)
            {
                float u = fill * i / (count - 1);
                float ang = (90f - 360f * u) * Mathf.Deg2Rad;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f));
            }
            Color a = lr.startColor;
            Color b = lr.endColor;
            a.a = alpha;
            b.a = alpha;
            lr.startColor = a;
            lr.endColor = b;
        }
    }
}
