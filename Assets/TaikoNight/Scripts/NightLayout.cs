// Auto-generated from 敲鼓 · 星轨统一版. Design px: 844x390, origin top-left, y down.
using System.Collections.Generic;

namespace TaikoNight
{
    public struct LayerInfo
    {
        public float x;
        public float y;
        public int order;
        public LayerInfo(float x, float y, int order) { this.x = x; this.y = y; this.order = order; }
    }

    public struct SparkleInfo
    {
        public float phase;
        public float period;
        public SparkleInfo(float phase, float period) { this.phase = phase; this.period = period; }
    }

    public static class NightLayout
    {
        /// <summary>Sprite name -> design-space center of its default placement and sorting order.</summary>
        public static readonly Dictionary<string, LayerInfo> Layers = new Dictionary<string, LayerInfo>
        {
            { "tn_bg", new LayerInfo(422.0f, 195.0f, 0) },
            { "tn_sparkle_0", new LayerInfo(96.0f, 100.0f, 1) },
            { "tn_sparkle_1", new LayerInfo(232.0f, 150.0f, 1) },
            { "tn_sparkle_2", new LayerInfo(612.0f, 40.0f, 1) },
            { "tn_sparkle_3", new LayerInfo(790.0f, 80.0f, 1) },
            { "tn_sparkle_4", new LayerInfo(812.0f, 250.0f, 1) },
            { "tn_star_glow", new LayerInfo(676.0f, 162.0f, 1) },
            { "tn_meteor", new LayerInfo(267.0f, 46.0f, 2) },
            { "tn_lanterns", new LayerInfo(449.5f, 231.5f, 3) },
            { "tn_coach_card", new LayerInfo(110.0f, 200.0f, 10) },
            { "tn_coach_placeholder", new LayerInfo(110.0f, 200.0f, 11) },
            { "tn_drum", new LayerInfo(422.0f, 275.0f, 20) },
            { "tn_head_flash", new LayerInfo(422.0f, 232.0f, 22) },
            { "tn_wave_don", new LayerInfo(422.0f, 232.0f, 23) },
            { "tn_wave_ka", new LayerInfo(422.0f, 232.0f, 23) },
            { "tn_lane", new LayerInfo(621.0f, 232.0f, 30) },
            { "tn_lane_mask", new LayerInfo(622.0f, 232.0f, 0) },
            { "tn_hold_bar", new LayerInfo(697.0f, 232.0f, 31) },
            { "tn_note_up", new LayerInfo(422.0f, 240.0f, 32) },
            { "tn_note_down", new LayerInfo(422.0f, 240.0f, 32) },
            { "tn_hold_glow", new LayerInfo(422.0f, 232.0f, 40) },
            { "tn_judge_ring", new LayerInfo(422.0f, 232.0f, 41) },
            { "tn_judge", new LayerInfo(422.0f, 232.0f, 42) },
            { "tn_ripple_don", new LayerInfo(422.0f, 232.0f, 44) },
            { "tn_ripple_ka", new LayerInfo(422.0f, 232.0f, 44) },
            { "tn_stick_left", new LayerInfo(294.0f, 140.0f, 50) },
            { "tn_stick_right", new LayerInfo(550.0f, 140.0f, 50) },
            { "tn_spark_don", new LayerInfo(392.0f, 222.0f, 51) },
            { "tn_spark_ka", new LayerInfo(452.0f, 222.0f, 51) },
            { "tn_perfect", new LayerInfo(422.0f, 116.0f, 52) },
            { "tn_front_cloud", new LayerInfo(430.0f, 383.0f, 60) },
            { "tn_hud_pause", new LayerInfo(40.0f, 30.0f, 70) },
        };

        /// <summary>Background sparkle twinkle timing, same order as tn_sparkle_N.</summary>
        public static readonly SparkleInfo[] Sparkles =
        {
            new SparkleInfo(-0.00f, 2.40f),
            new SparkleInfo(0.60f, 2.40f),
            new SparkleInfo(1.20f, 2.40f),
            new SparkleInfo(1.80f, 2.40f),
            new SparkleInfo(0.90f, 2.40f)
        };

        /// <summary>Drum band studs (design px), twinkle 2.4 s with 0.3 s steps.</summary>
        public static readonly UnityEngine.Vector2[] Studs =
        {
            new UnityEngine.Vector2(279.2f, 297.0f),
            new UnityEngine.Vector2(305.6f, 310.3f),
            new UnityEngine.Vector2(346.0f, 320.1f),
            new UnityEngine.Vector2(395.6f, 325.3f),
            new UnityEngine.Vector2(448.4f, 325.3f),
            new UnityEngine.Vector2(498.0f, 320.1f),
            new UnityEngine.Vector2(538.4f, 310.3f),
            new UnityEngine.Vector2(564.8f, 297.0f)
        };

        public const float MeteorCenterOffsetX = 27.0f;
        public const float MeteorCenterOffsetY = -10.0f;
    }
}
