using UnityEngine;

namespace BridgeWave
{
    /// <summary>
    /// Demo input for <see cref="BridgeWaveGame"/>: a patient who follows the curve with a small lag and a slight wobble
    /// while holding (same as the design board). For real use disable this component and write
    /// BridgeWaveGame.height from the IMU / pose tracking every frame (0 = hips flat, 1 = calibrated top).
    /// </summary>
    [RequireComponent(typeof(BridgeWaveGame))]
    public class SimulatedPatient : MonoBehaviour
    {
        public float lagSeconds = 0.12f;
        public float holdWobble = 0.009f;

        BridgeWaveGame game;
        float level;

        void Awake()
        {
            game = GetComponent<BridgeWaveGame>();
        }

        void Update()
        {
            float t = game.GameTime;
            float target = game.Level(t);
            level += (target - level) * (1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, lagSeconds)));
            float wob = game.PhaseAt(t) == BridgeWaveGame.Phase.Hold ? holdWobble * Mathf.Sin(t * 7.3f) : 0f;
            game.height = Mathf.Clamp01(level + wob);
        }
    }
}
