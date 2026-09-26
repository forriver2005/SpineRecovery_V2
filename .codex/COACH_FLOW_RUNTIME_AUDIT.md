# 女性死虫式：手机到教练训练运行核对（2026-09-26）

验证代码只放在 `%TEMP%\SpineV2FlowAudit-6ee52872` 的 Unity 临时副本，未加入仓库。手机未连接 ADB；Android 真机的 IMU 姿势质量与 Tracker 电机响应不能由模拟器证明。

| 环节 | 运行证据 | 结论 |
| --- | --- | --- |
| 手机训练量 | `SpineFlowMobile` 默认教练推荐 1 组 × 4 次；原手机会话仓库仅凭 sessionId 复用旧载荷，启动管理器又直接取 `sessions.current()`，可造成预览与启动载荷不同。现比较推荐动作并在启动时以当前页面载荷取会话。 | 手机端已修复并通过 `:app:assembleDebug --offline`。 |
| Android Intent → V2 | 用与手机相同的两个 Intent extra 启动临时 x86_64 V2 APK，Unity 日志打印 `coach volume is 1 sets x 4 reps`。 | 有效载荷能进入 V2；若现场仍是 2 组，应核对实际安装版本/本次启动载荷，不能改场景回退值来掩盖丢包。 |
| 开始页/播放 | Unity PlayMode 注入 1×4 载荷；开始页数字为 1，点开始/播放后 `SegmentedCoachController` 为 1 组、每组 4 次，正式动作启动。 | 通过。场景序列化的 2×4 是独立启动回退。 |
| 准备姿势/稳定/评分 | 自然播放教练介绍与首个准备片段；持续送入满足原 AR 门槛的仿真人体姿势，状态经历 `WaitingStable → PreparingScore → Scoring → Idle` 并写入第一段分数。 | 通过。方向锁只要求躺下与双手伸展；准备段另要求双腿伸直和 0.35 秒持续确认，两者原 AR 就不同。此前将骨骼包新鲜度称作准备段卡住的确定原因是错误判断。 |
| 追踪新鲜度 | Unity PlayMode 给评分器仅送骨骼包时返回新鲜，停包 0.3 秒后返回过期。 | 通过；这项修复保护正式动作评分，不替代准备段姿势判断。 |
| 错误部位/震动 | 在正式训练状态显示左臂错误部位，产生 2 个可见高亮渲染器；配置测试用 Tracker 地址后，本地 HTTP 服务收到 `/motor?duration_ms=100`。 | 高亮和震动请求链通过；真实设备绑定/电机须在真机与 Tracker 上确认。 |

`PoseScorer.ShowMisalignedBodyParts` 还恢复了原 AR 在离开目标动作提示时清除旧肢体几何标记的操作，防止后续错误部位沿用上一段数据。核心分段、稳定、评分及语音门槛未调整。

完整九场景 V2 Android 开发包曾从临时副本构建并复制到 `D:\Code\SpineV2Builds\2026-09-26\SpineRecovery-V2-coach-debug.apk`（ARMv7）；对应手机端更新包是同目录的 `SpineFlowMobile-updated-debug.apk`。此阶段手机尚未连接；后续实机修复与安装见下节。

## 2026-09-26 实机卡在方向锁后：已找到启动异常

X4000 手机后来连接 ADB。旧 V2 安装包的运行日志显示 `ArgumentNullException: shader`，调用链是 `PoseScorer.Start → AvatarBodyPartHighlighter.Configure → Rebuild → EnsureMaterial → new Material(shader)`。原 AR 仓库的 `Assets/Shaders/BodyPartHighlightOverlay.shader` 未迁入 V2，Android 构建也裁剪了备用的 `Unlit/Color`。异常发生在 `PoseScorer.Start` 订阅 `coachController.OnSegmentHold` 之前，故方向锁仍可发生，但准备段的跟随提示、稳定倒计时和评分状态机不会开始。这是实机故障的直接原因；前面的编辑器流程测试没有发现 Android Shader 裁剪问题。

已从原 AR 迁入 Shader 和 `.meta`，把它加入 `ProjectSettings/GraphicsSettings.asset` 的 Always Included Shaders，并使 `AvatarBodyPartHighlighter` 在两个 Shader 都不可用时跳过高亮构建，不再阻断评分器初始化。临时项目 Android ARMv7 开发包构建日志确认编译了 `SpineRecovery/BodyPartHighlightOverlay`。更新 APK：`D:\Code\SpineV2Builds\2026-09-26\SpineRecovery-V2-coach-shaderfix-debug.apk`。该包与更新的手机端 APK 均以 `adb install -r` 覆盖安装到 X4000，保留应用数据。V2 在手机上重新启动后，Unity 错误日志为空，旧 `PoseScorer.Start` 异常未再出现。没有真人姿态输入的情况下，此次只能确认启动阻断已解除，不能把之后的真实姿态评分和电机响应宣称为实机通过。
