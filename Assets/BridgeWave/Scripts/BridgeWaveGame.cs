using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeWave
{
    /// <summary>
    /// 臀桥 · 曲线跟随 — follow-the-curve glute bridge game.
    ///
    /// A target curve scrolls from right to left at <see cref="speedPx"/>; the patient's orb stays at <see cref="orbX"/>
    /// and its height comes from <see cref="height"/> (0 = hips flat, 1 = calibrated top), written every frame by the
    /// sensor (or by <see cref="SimulatedPatient"/> in the demo).
    /// One repetition of the curve: rest -> 抬起 (short steep rise, pink) -> 保持 (flat top, gold, stars)
    /// -> 慢慢放下 (long gentle slope, lilac, notes) -> rest. The long shallow slope is what asks for a slow return.
    /// Items are collected when they reach the orb while the orb is close to the curve; otherwise they are missed.
    /// The track is drawn procedurally with LineRenderers, so durations can be changed in the Inspector.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public class BridgeWaveGame : MonoBehaviour
    {
        [Header("Input (0 = flat, 1 = top)")]
        [Range(0f, 1f)] public float height;

        [Header("Standard-motion track (optional for original art demo)")]
        public BridgeWaveTrack standardTrack;
        public bool controlledClock;
        public float presentationTime;
        public bool inTrack;
        public bool judging;
        [Header("Curve (seconds)")]
        public float restBefore = 1.0f;
        public float liftTime = 1.2f;
        public float holdTime = 2.8f;
        public float lowerTime = 5.0f;
        public float restAfter = 0.5f;

        [Header("Layout (design px)")]
        public float speedPx = 62f;
        public float orbX = 250f;
        public float topY = 112f;
        public float bottomY = 296f;
        public float trailPx = 70f;
        public float stepPx = 3f;

        [Header("Scoring")]
        [Tooltip("Standard track half-width as a fraction of height span. Judgement uses shortest screen-plane distance to this constant-width track.")]
        public float tolerance = 0.18f;
        public int startCount = 2;
        public int totalCount = 8;
        [Tooltip("Item times inside the hold / lower phases, as fractions of that phase")]
        public float[] starFractions = { 0.16f, 0.5f, 0.84f };
        public float[] noteFractions = { 0.18f, 0.4f, 0.62f, 0.84f };

        [Header("Look")]
        public Material lineMaterial;
        public Color bandOuter = new Color(0.059f, 0.063f, 0.204f, 0.55f);
        public Color bandInner = new Color(0.169f, 0.180f, 0.471f, 0.9f);
        public Color liftColor = new Color(1f, 0.616f, 0.69f);
        public Color holdColor = new Color(1f, 0.839f, 0.42f);
        public Color lowerColor = new Color(0.722f, 0.69f, 1f);
        public Color liftGlow = new Color(0.941f, 0.416f, 0.557f);
        public Color holdGlow = new Color(0.91f, 0.694f, 0.227f);
        public Color lowerGlow = new Color(0.486f, 0.435f, 0.91f);
        public Sprite starSprite;
        public Sprite noteSprite;
        public int baseSortingOrder = 20;

        [Header("Orb")]
        public Transform orb;
        public SpriteRenderer haloLift;
        public SpriteRenderer haloHold;
        public SpriteRenderer haloLower;
        public SpriteRenderer burstGold;
        public SpriteRenderer burstLilac;

        [Header("HUD")]
        public SpriteRenderer captionLift;
        public SpriteRenderer captionHold;
        public SpriteRenderer captionLower;
        public LineRenderer counterRing;
        public Transform counterRoot;
        public Text counterText;

        public enum Phase { Rest, Lift, Hold, Lower }

        LineRenderer bandA, bandB;
        readonly List<LineRenderer> cores = new List<LineRenderer>();
        readonly List<LineRenderer> glows = new List<LineRenderer>();
        readonly List<SpriteRenderer> itemPool = new List<SpriteRenderer>();
        readonly Dictionary<long, float> resolved = new Dictionary<long, float>();   // item key -> time it reached the orb (sign = collected / missed)
        float burstGoldAt = -10f, burstLilacAt = -10f, countPopAt = -10f;
        int shownCount = -1;
        readonly List<Vector3> pts = new List<Vector3>(400);

        public float Rep { get { return restBefore + liftTime + holdTime + lowerTime + restAfter; } }
        public float GameTime { get { return controlledClock ? presentationTime : DesignSpace.Now; } }

        // ------------------------------------------------------------------ curve

        /// <summary>Target hip level 0..1 at game time s.</summary>
        public float Level(float s)
        {
            if (standardTrack != null) return standardTrack.Evaluate(s, totalCount).level;
            s = Mathf.Repeat(s, Rep);
            float a = restBefore, b = a + liftTime, c = b + holdTime, d = c + lowerTime;
            if (s < a) return 0f;
            if (s < b) return Smooth((s - a) / liftTime);
            if (s < c) return 1f;
            if (s < d) return 1f - Smooth((s - c) / lowerTime);
            return 0f;
        }

        public Phase PhaseAt(float s)
        {
            if (standardTrack != null) return standardTrack.Evaluate(s, totalCount).phase;
            s = Mathf.Repeat(s, Rep);
            float a = restBefore, b = a + liftTime, c = b + holdTime, d = c + lowerTime;
            if (s < a) return Phase.Rest;
            if (s < b) return Phase.Lift;
            if (s < c) return Phase.Hold;
            if (s < d) return Phase.Lower;
            return Phase.Rest;
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        float YAt(float level) { return bottomY - (bottomY - topY) * level; }
        float SAt(float x, float t) { return t + (x - orbX) / speedPx; }
        Vector3 W(float x, float y) { return DesignSpace.ToWorld(x, y); }

        // ------------------------------------------------------------------ setup

        void Start()
        {
            bandA = MakeLine("TrackBandOuter", 0.34f, bandOuter, baseSortingOrder);
            bandB = MakeLine("TrackBandInner", 0.28f, bandInner, baseSortingOrder + 1);
            if (standardTrack != null)
            {
                // LineRenderer expands perpendicular to the local tangent, not vertically.
                // The same half-width is used by the closest-point judgement below.
                bandB.widthMultiplier = 2f * tolerance * (bottomY - topY) * DesignSpace.PxToUnit;
                bandA.widthMultiplier = bandB.widthMultiplier + .06f;
            }
        }

        LineRenderer MakeLine(string name, float width, Color c, int order)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(transform, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 8;
            lr.numCornerVertices = 8;
            lr.alignment = LineAlignment.View;
            lr.widthCurve = AnimationCurve.Constant(0, 1, 1);
            lr.sharedMaterial = lineMaterial;
            lr.startColor = c;
            lr.endColor = c;
            lr.sortingOrder = order;
            return lr;
        }

        SpriteRenderer Item(int i)
        {
            while (itemPool.Count <= i)
            {
                GameObject g = new GameObject("Item" + itemPool.Count);
                g.transform.SetParent(transform, false);
                SpriteRenderer r = g.AddComponent<SpriteRenderer>();
                r.sortingOrder = baseSortingOrder + 10;
                itemPool.Add(r);
            }
            return itemPool[i];
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            float t = GameTime;
            float rep = Rep;

            // Track band across the whole screen
            pts.Clear();
            for (float x = -10f; x <= 854f; x += stepPx) pts.Add(W(x, YAt(Level(SAt(x, t)))));
            SetPoints(bandA, pts); SetPoints(bandB, pts);

            // Coloured phase lines: bright ahead of the orb, fading trail behind it, dim further back
            float sMin = SAt(-10f, t), sMax = SAt(854f, t);
            int used = 0;
            if (standardTrack != null)
            {
                for (int i = 0; i < totalCount; i++)
                {
                    var c = standardTrack.cycles[i % standardTrack.cycles.Length];
                    float a = standardTrack.CycleStart(i), b = a + c.liftSeconds, d = b + c.holdSeconds;
                    used = Segment(used, a, b, t, liftColor, liftGlow);
                    used = Segment(used, b, d, t, holdColor, holdGlow);
                    used = Segment(used, d, d + c.lowerSeconds, t, lowerColor, lowerGlow);
                }
            }
            else
            {
                for (int k = Mathf.FloorToInt(sMin / rep) - 1; k <= Mathf.CeilToInt(sMax / rep); k++)
                {
                    float r0 = k * rep;
                    used = Segment(used, r0 + restBefore, r0 + restBefore + liftTime, t, liftColor, liftGlow);
                    used = Segment(used, r0 + restBefore + liftTime, r0 + restBefore + liftTime + holdTime, t, holdColor, holdGlow);
                    used = Segment(used, r0 + restBefore + liftTime + holdTime, r0 + restBefore + liftTime + holdTime + lowerTime, t, lowerColor, lowerGlow);
                }
            }
            for (int i = used; i < cores.Count; i++) { cores[i].enabled = false; glows[i].enabled = false; }

            float level = Mathf.Clamp(height, -.25f, standardTrack != null ? BridgeWaveHeightInput.QualifiedHeight : 1.25f);
            if (orb != null) orb.position = W(orbX, YAt(level));

            if (standardTrack == null) UpdateItems(t, level);
            else UpdateStandardItems(t);
            UpdatePhaseVisuals(t);
            UpdateCounter(t);
        }

        int Segment(int used, float s0, float s1, float t, Color core, Color glow)
        {
            float xa = orbX + (s0 - t) * speedPx, xb = orbX + (s1 - t) * speedPx;
            if (xb < -10f || xa > 854f) return used;
            xa = Mathf.Max(xa, -10f);
            xb = Mathf.Min(xb, 854f);
            if (xb - xa < 1f) return used;
            if (cores.Count <= used)
            {
                glows.Add(MakeLine("Glow" + used, 0.10f, glow, baseSortingOrder + 2));
                cores.Add(MakeLine("Line" + used, 0.04f, core, baseSortingOrder + 3));
            }
            LineRenderer c = cores[used], g = glows[used];
            c.enabled = g.enabled = true;
            pts.Clear();
            for (float x = xa; x < xb; x += 2f) pts.Add(W(x, YAt(Level(SAt(x, t)))));
            pts.Add(W(xb, YAt(Level(SAt(xb, t)))));
            SetPoints(c, pts);
            SetPoints(g, pts);
            c.colorGradient = AlphaGradient(core, xa, xb, 1f);
            g.colorGradient = AlphaGradient(glow, xa, xb, 0.45f);
            return used + 1;
        }

        /// <summary>Alpha along x: 0.28 far behind the orb, ramps to 1 over the trail, 1 ahead.</summary>
        Gradient AlphaGradient(Color c, float xa, float xb, float peak)
        {
            float len = Mathf.Max(1f, xb - xa);
            float trailStart = orbX - trailPx;
            List<GradientAlphaKey> keys = new List<GradientAlphaKey>();
            keys.Add(new GradientAlphaKey(peak * AlphaAt(xa, trailStart), 0f));
            float u1 = (trailStart - xa) / len, u2 = (orbX - xa) / len;
            if (u1 > 0f && u1 < 1f) keys.Add(new GradientAlphaKey(peak * 0.28f, u1));
            if (u2 > 0f && u2 < 1f) keys.Add(new GradientAlphaKey(peak, u2));
            keys.Add(new GradientAlphaKey(peak * AlphaAt(xb, trailStart), 1f));
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, keys.ToArray());
            return gr;
        }

        float AlphaAt(float x, float trailStart)
        {
            if (x >= orbX) return 1f;
            if (x >= trailStart) return Mathf.Lerp(0.28f, 1f, (x - trailStart) / trailPx);
            return 0.28f;
        }

        static void SetPoints(LineRenderer lr, List<Vector3> p)
        {
            lr.positionCount = p.Count;
            for (int i = 0; i < p.Count; i++) lr.SetPosition(i, p[i]);
        }

        public void PulseHit()
        {
            if (PhaseAt(GameTime) == Phase.Hold) burstGoldAt = GameTime;
            else burstLilacAt = GameTime;
        }
        public void ResetFeedback()
        {
            resolved.Clear(); burstGoldAt = burstLilacAt = countPopAt = -10;
            shownCount = -1; inTrack = judging = false;
        }
        void UpdateStandardItems(float t)
        {
            int n=0;
            for(int k=0;k<totalCount;k++)
            {
                var cycle=standardTrack.cycles[k%standardTrack.cycles.Length];
                for(int i=0;i<starFractions.Length+noteFractions.Length;i++)
                {
                    bool star=i<starFractions.Length;
                    float s=standardTrack.CycleStart(k)+cycle.liftSeconds+(star?starFractions[i]*cycle.holdSeconds:cycle.holdSeconds+noteFractions[i-starFractions.Length]*cycle.lowerSeconds);
                    float x=orbX+(s-t)*speedPx;
                    if(x<orbX-50 || x>870)continue;
                    long key=k*32+i;
                    if(t>=s && !resolved.ContainsKey(key)) resolved[key]=(judging&&inTrack)?s:-s;
                    var r=Item(n++); r.enabled=true; r.sprite=star?starSprite:noteSprite;
                    r.transform.position=W(x,YAt(Level(s)));
                    r.transform.localScale=Vector3.one * .7f;
                    r.color=Color.white;
                    if(resolved.TryGetValue(key,out float hit))
                    {
                        float age=t-Mathf.Abs(hit);
                        r.color=hit>0?new Color(1,1,1,Mathf.Clamp01(1-age/.35f)):new Color(.5f,.5f,.6f,.45f);
                        if(hit>0)r.transform.localScale=Vector3.one*(.7f+age*2);
                    }
                }
            }
            for(int i=n;i<itemPool.Count;i++)itemPool[i].enabled=false;
            Burst(burstGold, judging && inTrack ? t-burstGoldAt : -1);
            Burst(burstLilac, judging && inTrack ? t-burstLilacAt : -1);
        }

        // ------------------------------------------------------------------ items

        void UpdateItems(float t, float level)
        {
            float rep = Rep;
            float holdStart = restBefore + liftTime, lowerStart = holdStart + holdTime;
            int n = 0;
            int kMin = Mathf.FloorToInt((t - 1f) / rep) - 1, kMax = Mathf.CeilToInt((t + 854f / speedPx) / rep);
            for (int k = kMin; k <= kMax; k++)
            {
                for (int i = 0; i < starFractions.Length + noteFractions.Length; i++)
                {
                    bool star = i < starFractions.Length;
                    float s = k * rep + (star ? holdStart + starFractions[i] * holdTime
                                              : lowerStart + noteFractions[i - starFractions.Length] * lowerTime);
                    float x = orbX + (s - t) * speedPx;
                    if (x < orbX - 25f || x > 870f) continue;
                    long key = (long)k * 32 + i;
                    float itemLevel = Level(s);

                    // resolve the moment it reaches the orb
                    float hit;
                    if (!resolved.TryGetValue(key, out hit) && t >= s)
                    {
                        bool ok = Mathf.Abs(level - itemLevel) <= tolerance;
                        hit = ok ? s : -s;
                        resolved[key] = hit;
                        if (ok) { if (star) burstGoldAt = t; else burstLilacAt = t; }
                    }

                    SpriteRenderer r = Item(n++);
                    r.enabled = true;
                    r.sprite = star ? starSprite : noteSprite;
                    r.transform.position = W(x, YAt(itemLevel) - (star ? 18f : 0f));
                    float a = 1f, sc = 1f;
                    if (resolved.TryGetValue(key, out hit))
                    {
                        float d = t - Mathf.Abs(hit);
                        if (hit > 0f) { sc = d < 0.12f ? Mathf.Lerp(1f, 1.7f, d / 0.12f) : Mathf.Lerp(1.7f, 2.2f, (d - 0.12f) / 0.33f); a = d < 0.12f ? 1f : Mathf.Clamp01(1f - (d - 0.12f) / 0.33f); }
                        else { a = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(d / 0.3f)); }
                    }
                    r.transform.localScale = new Vector3(sc, sc, 1f);
                    Color c = hit < 0f && resolved.ContainsKey(key) ? new Color(0.6f, 0.6f, 0.7f, a) : new Color(1f, 1f, 1f, a);
                    r.color = c;
                }
            }
            for (int i = n; i < itemPool.Count; i++) itemPool[i].enabled = false;

            // forget items that scrolled away
            if (resolved.Count > 64)
            {
                List<long> old = new List<long>();
                foreach (var kv in resolved) if (Mathf.Abs(kv.Value) < t - rep) old.Add(kv.Key);
                foreach (long k in old) resolved.Remove(k);
            }
            Burst(burstGold, t - burstGoldAt);
            Burst(burstLilac, t - burstLilacAt);
        }

        static void Burst(SpriteRenderer r, float d)
        {
            if (r == null) return;
            if (d < 0f || d > 0.5f) { SpriteAlpha.Set(r, 0f); return; }
            float k = Ease.OutQuad(d / 0.5f);
            float s = Mathf.Lerp(0.6f, 1.8f, k);
            r.transform.localScale = new Vector3(s, s, 1f);
            SpriteAlpha.Set(r, 1f - k);
        }

        // ------------------------------------------------------------------ phase colour, captions, counter

        void UpdatePhaseVisuals(float t)
        {
            Phase p = PhaseAt(t);
            bool lit = standardTrack == null || (judging && inTrack);
            Fade(haloLift, lit && p == Phase.Lift, 0.1f);
            Fade(haloHold, lit && p == Phase.Hold, 0.1f);
            Fade(haloLower, lit && p == Phase.Lower, 0.1f);
            if (!lit) { SpriteAlpha.Set(haloLift, 0); SpriteAlpha.Set(haloHold, 0); SpriteAlpha.Set(haloLower, 0); }
            Fade(captionLift, p == Phase.Lift, 0.15f);
            Fade(captionHold, p == Phase.Hold, 0.15f);
            Fade(captionLower, p == Phase.Lower, 0.15f);
        }

        static void Fade(SpriteRenderer r, bool on, float time)
        {
            if (r == null) return;
            float a = r.color.a;
            a = Mathf.MoveTowards(a, on ? 1f : 0f, Time.deltaTime / time);
            SpriteAlpha.Set(r, a);
        }

        void UpdateCounter(float t)
        {
            float rep = Rep;
            float lowerEnd = restBefore + liftTime + holdTime + lowerTime;
            int done = t < lowerEnd ? 0 : Mathf.FloorToInt((t - lowerEnd) / rep) + 1;
            int count = standardTrack != null ? standardTrack.Completed(t, totalCount) : Mathf.Min(totalCount, startCount + done);
            if (count != shownCount)
            {
                if (shownCount >= 0) countPopAt = t;
                shownCount = count;
                if (counterText != null) counterText.text = count.ToString();
            }
            if (counterRing != null)
            {
                float fill = standardTrack != null ? Mathf.Clamp01(t / standardTrack.Duration(totalCount)) : (float)count / totalCount;
                int segs = Mathf.Max(2, Mathf.CeilToInt(64 * fill) + 1);
                counterRing.positionCount = segs;
                Vector3 c = W(422f, 34f);
                float rad = 19f * DesignSpace.PxToUnit;
                for (int i = 0; i < segs; i++)
                {
                    float ang = (90f - 360f * fill * i / (segs - 1)) * Mathf.Deg2Rad;
                    counterRing.SetPosition(i, c + new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f));
                }
            }
            if (counterRoot != null)
            {
                float d = t - countPopAt;
                float s = d < 0f || d > 0.4f ? 1f : (d < 0.1f ? Mathf.Lerp(1f, 1.15f, d / 0.1f) : Mathf.Lerp(1.15f, 1f, (d - 0.1f) / 0.3f));
                counterRoot.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
