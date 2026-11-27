// Auto-generated from 臀桥 · 曲线跟随. Design px: 844x390, origin top-left, y down.
using System.Collections.Generic;

namespace BridgeWave
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

    public static class WaveLayout
    {
        /// <summary>Sprite name -> design-space centre and sorting order. Items / orb / bursts are centred on the origin.</summary>
        public static readonly Dictionary<string, LayerInfo> Layers = new Dictionary<string, LayerInfo>
        {
            { "bw_bg", new LayerInfo(422.0f, 195.0f, 0) },
            { "bw_sparkle_0", new LayerInfo(96.0f, 100.0f, 1) },
            { "bw_sparkle_1", new LayerInfo(232.0f, 150.0f, 1) },
            { "bw_sparkle_2", new LayerInfo(612.0f, 40.0f, 1) },
            { "bw_sparkle_3", new LayerInfo(790.0f, 80.0f, 1) },
            { "bw_sparkle_4", new LayerInfo(812.0f, 250.0f, 1) },
            { "bw_star_glow", new LayerInfo(676.0f, 162.0f, 1) },
            { "bw_meteor", new LayerInfo(267.0f, 46.0f, 2) },
            { "bw_lanterns", new LayerInfo(449.5f, 231.5f, 3) },
            { "bw_guides", new LayerInfo(420.0f, 205.0f, 5) },
            { "bw_item_star", new LayerInfo(0.0f, 0.0f, 30) },
            { "bw_item_note", new LayerInfo(0.0f, 0.0f, 30) },
            { "bw_burst_gold", new LayerInfo(0.0f, 0.0f, 44) },
            { "bw_burst_lilac", new LayerInfo(0.0f, 0.0f, 44) },
            { "bw_orb_glow", new LayerInfo(0.0f, 0.0f, 40) },
            { "bw_halo_lift", new LayerInfo(0.0f, 0.0f, 41) },
            { "bw_halo_hold", new LayerInfo(0.0f, 0.0f, 41) },
            { "bw_halo_lower", new LayerInfo(0.0f, 0.0f, 41) },
            { "bw_orb", new LayerInfo(0.0f, 0.0f, 42) },
            { "bw_cap_lift", new LayerInfo(422.0f, 74.5f, 50) },
            { "bw_cap_hold", new LayerInfo(422.0f, 74.5f, 50) },
            { "bw_cap_lower", new LayerInfo(422.0f, 74.5f, 50) },
            { "bw_counter_bg", new LayerInfo(422.0f, 34.0f, 50) },
            { "bw_hud_pause", new LayerInfo(40.0f, 30.0f, 60) },
        };

        public static readonly SparkleInfo[] Sparkles =
        {
            new SparkleInfo(-0.00f, 2.40f),
            new SparkleInfo(0.60f, 2.40f),
            new SparkleInfo(1.20f, 2.40f),
            new SparkleInfo(1.80f, 2.40f),
            new SparkleInfo(0.90f, 2.40f)
        };

        public const float MeteorCenterOffsetX = 27.0f;
        public const float MeteorCenterOffsetY = -10.0f;
    }
}
