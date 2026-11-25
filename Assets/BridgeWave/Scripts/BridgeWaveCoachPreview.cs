using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeWave
{
    /// <summary>Samples the existing recorded coach by track phase; never writes patient input.</summary>
    [DefaultExecutionOrder(450)]
    public sealed class BridgeWaveCoachPreview : MonoBehaviour
    {
        public BridgeWaveFollowController controller;
        public Animator animator;
        public Camera previewCamera;
        public RawImage menuImage, gameImage;
        public TMP_Text menuPhase;
        public Text gamePhase;
        public float SourceFrame { get; private set; }
        RenderTexture texture, previousTexture;
        float previewTime;
        public void RestartPreview() => previewTime = 0;
        void Awake()
        {
            previousTexture = previewCamera.targetTexture;
            texture = new RenderTexture(840, 300, 24, RenderTextureFormat.ARGB32)
                { name = "BridgeWave Recorded Coach", antiAliasing = 2 };
            texture.Create();
            previewCamera.targetTexture = texture;
            menuImage.texture = gameImage.texture = texture;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0);
            animator.speed = 0;
        }
        void LateUpdate()
        {
            var track = controller.game.standardTrack;
            var state = controller.State;
            bool menu = state == BridgeWaveFollowController.RunState.Ready;
            if (menu) previewTime = Mathf.Repeat(previewTime + Time.unscaledDeltaTime, track.Duration(controller.game.totalCount) + track.restSeconds);
            float time = menu ? previewTime : controller.Session.Elapsed;
            var sample = track.Evaluate(time, controller.game.totalCount);
            int index = Mathf.Clamp(sample.cycle, 0, controller.game.totalCount - 1);
            var cycle = track.cycles[index % track.cycles.Length];
            var source = track.source.cycles[index % track.source.cycles.Length];
            float local = time - track.CycleStart(index);
            SourceFrame = local < 0 ? source.liftStartFrame :
                local < cycle.liftSeconds ? Mathf.Lerp(source.liftStartFrame, source.holdStartFrame, local / cycle.liftSeconds) :
                local < cycle.liftSeconds + cycle.holdSeconds ? Mathf.Lerp(source.holdStartFrame, source.lowerStartFrame, (local - cycle.liftSeconds) / cycle.holdSeconds) :
                Mathf.Lerp(source.lowerStartFrame, source.lowerEndFrame, Mathf.Clamp01((local - cycle.liftSeconds - cycle.holdSeconds) / cycle.lowerSeconds));
            animator.speed = 0;
            animator.Play("hipthrust_routine", 0, SourceFrame / track.source.sourceClip.frameRate / track.source.sourceClip.length);
            animator.Update(0);
            string phase = sample.phase == BridgeWaveGame.Phase.Lift ? "抬起" : sample.phase == BridgeWaveGame.Phase.Hold ? "保持" : sample.phase == BridgeWaveGame.Phase.Lower ? "慢慢放下" : "放平准备";
            if (state == BridgeWaveFollowController.RunState.Countdown) phase = "准备开始";
            if (state == BridgeWaveFollowController.RunState.Paused) phase += " · 已暂停";
            if (state == BridgeWaveFollowController.RunState.Finished) phase = "本组完成";
            menuPhase.text = "教练演示 · " + phase;
            gamePhase.text = "教练 · " + phase;
        }
        void OnDestroy()
        {
            if (previewCamera != null) previewCamera.targetTexture = previousTexture;
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
