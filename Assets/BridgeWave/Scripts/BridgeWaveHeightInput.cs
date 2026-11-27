using UnityEngine;

namespace BridgeWave
{
    /// <summary>Live hip height, normalized using one stable supine rest pose and the recorded standard lift.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class BridgeWaveHeightInput : MonoBehaviour
    {
        public const float QualifiedHeight = 1f;
        public const int Port = 39539;
        public const float CalibrationSeconds = 1.5f;
        public Animator patient;
        public AvatarMotionSource motion;
        [Tooltip("Standard low-to-high excursion sampled on this patient avatar, in leg-length units.")]
        public float referenceHeightSpan;
        public float lowHeight, highHeight;
        public bool Calibrated { get; private set; }
        public bool Calibrating { get; private set; }
        public bool Fresh { get; private set; }
        public bool PoseReady => Fresh && SupineGameCalibration.IsLying(patient);
        public float CalibrationProgress => Mathf.Clamp01(stableSeconds / CalibrationSeconds);
        public float Height { get; private set; }
        public float RawHeight { get; private set; }
        public bool AboveQualifiedHeight => Fresh && Calibrated && RawHeight > highHeight;
        public string Message { get; private set; } = "请连接动作传感器，再进行躺姿校准";
        public bool Valid => Fresh && Calibrated;
        public bool CanStart => Fresh && Calibrated && !Calibrating;
        public bool EditingAllowed { get; set; } = true;
        float pendingSeconds, stableSeconds, minimumHeight, maximumHeight, sumHeight;
        int samples, lastSampleFrame = -1;
        string calibrationHint;
        readonly HumanBodyBones[] stableBones = { HumanBodyBones.Hips, HumanBodyBones.Chest,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
        readonly Quaternion[] firstRotations = new Quaternion[6];

        void Awake()
        {
            if (patient == null) return;
            patient.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var host = new GameObject("Bridge Wave VMC Input · UDP 39539");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var receiver = host.AddComponent<EVMC4U.ExternalReceiver>();
            var server = host.AddComponent<uOSC.uOscServer>();
            motion = host.AddComponent<AvatarMotionSource>();
            var bridge = host.AddComponent<VmcGameModeSourceBridge>();
            server.port = Port; server.autoStart = true;
            receiver.Model = patient.gameObject;
            receiver.RootPositionSynchronize = receiver.RootRotationSynchronize = receiver.RootScaleOffsetSynchronize = false;
            receiver.BonePositionSynchronize = false;
            receiver.BonePositionFilterEnable = receiver.BoneRotationFilterEnable = false;
            motion.Configure(patient); bridge.Configure(receiver, motion);
            host.SetActive(true);
        }
        public static bool TryHeight(Animator animator, out float value)
        {
            value = 0;
            if (animator == null || !animator.isHuman) return false;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (hips == null || left == null || right == null || thigh == null || knee == null) return false;
            float length = Vector3.Distance(thigh.position, knee.position) + Vector3.Distance(knee.position, left.position);
            if (length < .01f) return false;
            value = (hips.position.y - (left.position.y + right.position.y) * .5f) / length;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
        public static float LimitToQualifiedHeight(float normalizedHeight) => Mathf.Min(normalizedHeight, QualifiedHeight);
        public static float NormalizeHeight(float measured, float low, float high)
        {
            float span = high - low;
            if (float.IsNaN(measured) || float.IsInfinity(measured) || span < .05f) return 0;
            return LimitToQualifiedHeight((measured - low) / span);
        }
        public void Read()
        {
            float measured = RawHeight;
            Fresh = motion != null && motion.HasFreshFrame && TryHeight(patient, out measured);
            RawHeight = measured;
            if (Fresh && Calibrated) Height = NormalizeHeight(RawHeight, lowHeight, highHeight);
            Message = !Fresh ? "未收到动作传感器信号，请连接设备" :
                referenceHeightSpan < .05f ? "标准动作参考缺失，请重新生成场景" :
                Calibrating ? !PoseReady ? "请仰卧屈膝、臀部放平，保持不动" :
                    stableSeconds > 0 ? $"躺姿校准中 · 保持不动 {CalibrationSeconds - stableSeconds:0.0} 秒" : calibrationHint :
                !Calibrated ? "传感器已连接 · 请仰卧屈膝，点击「躺姿校准」" :
                AboveQualifiedHeight ? "已达到标准高度 · 小球保持在上限" :
                "躺姿校准完成 · 臀部高度控制小球";
        }
        void LateUpdate()
        {
            Read();
            StepCalibration(Time.unscaledDeltaTime);
        }
        public void BeginCalibration()
        {
            if (!EditingAllowed) return;
            Read();
            if (!Fresh || referenceHeightSpan < .05f) return;
            Calibrated = false; Calibrating = true; Height = 0;
            ResetSamples();
            calibrationHint = "请保持臀部放平，等待校准完成";
            Read();
        }
        public void CancelCalibration()
        {
            Calibrating = false;
            ResetSamples();
        }
        void ResetSamples()
        {
            pendingSeconds = stableSeconds = sumHeight = 0;
            samples = 0;
            lastSampleFrame = -1;
        }
        void StepCalibration(float deltaTime)
        {
            if (!Calibrating || !EditingAllowed) return;
            if (!PoseReady || deltaTime > .1f)
            {
                ResetSamples();
                return;
            }
            // Re-reading the last network frame cannot complete a calibration.
            pendingSeconds += Mathf.Max(0, deltaTime);
            if (lastSampleFrame == motion.LastPacketframeCounterInFrame) return;
            if (pendingSeconds > .1f) { ResetSamples(); return; }
            lastSampleFrame = motion.LastPacketframeCounterInFrame;
            bool moved = false;
            if (samples > 0)
            {
                moved = Mathf.Max(maximumHeight, RawHeight) - Mathf.Min(minimumHeight, RawHeight) > .015f;
                for (int i = 0; i < stableBones.Length; i++)
                {
                    var bone = patient.GetBoneTransform(stableBones[i]);
                    if (bone != null && Quaternion.Angle(bone.rotation, firstRotations[i]) > 8f) moved = true;
                }
            }
            if (moved)
            {
                ResetSamples();
                calibrationHint = "检测到移动 · 请保持臀部放平，重新计时";
                Read();
                return;
            }
            if (samples == 0)
            {
                minimumHeight = maximumHeight = RawHeight;
                for (int i = 0; i < stableBones.Length; i++)
                {
                    var bone = patient.GetBoneTransform(stableBones[i]);
                    if (bone != null) firstRotations[i] = bone.rotation;
                }
            }
            minimumHeight = Mathf.Min(minimumHeight, RawHeight);
            maximumHeight = Mathf.Max(maximumHeight, RawHeight);
            sumHeight += RawHeight;
            samples++;
            stableSeconds += pendingSeconds;
            pendingSeconds = 0;
            if (stableSeconds >= CalibrationSeconds && samples >= 15)
            {
                lowHeight = sumHeight / samples;
                highHeight = lowHeight + referenceHeightSpan;
                Calibrated = true;
                Calibrating = false;
            }
            Read();
        }
    }
}
