# 女性死虫式训练功能对照（2026-09-26）

范围：从 SpineFlowMobile 推荐女性死虫式进入 V2 `DeadBugPractice`，到教练训练、评分、录制、完成按钮与返回手机。按用户要求用代码核对；核对脚本只经 Python stdin 执行，没有放进仓库，也没有操作 Unity UI。

| 环节 | 原 AR / V2 对照结果 | 证据与限制 |
| --- | --- | --- |
| 手机启动 | 推荐动作产生 `DeadBugPractice`，V2 接收后加载同名场景 | 手机 `TrainingStartPayload`/`ArSessionTokenContract`，V2 `SpineFlowTrainingSession.AcceptMobileLaunch`；未接入真实 Intent 重放。 |
| 开始和介绍 | V2 场景 StartPractice 事件仍在；教练/用户介绍和倒计时序列与原 AR 同一实现 | `VoicePromptManager.PracticeIntroductionSequence` 代码一致；两场景 VoicePromptManager 序列化块一致。 |
| 分段与训练量 | 分段推进与原 AR 一致；场景的 2 组 × 4 次只是独立启动回退配置。手机推荐女性死虫式默认 1 组 × 4 次，启动时应覆盖场景值 | `GetPlannedTrainingVolume`、`StartPracticeDirectly`、`SetTrainingVolume` 链路已对照；手机旧会话复用可造成预览与实际 Intent 的推荐量不一致，已修复复用条件。V2 接收和开始训练日志现打印最终训练量。 |
| 躺下与锁方向 | V2 使用 `AvatarMotionSource.LastBonePacketCounterInFrame` 判断用户追踪包 | `PracticeSessionController.UpdateUserTrackerFreshness`；Android 模型能锁方向说明此链路有骨骼数据，但不证明后续评分门槛通过。 |
| 评分推进 | V2 `PoseScorer` 原先只看 `/VMC/Ext/T` 时间帧包，新鲜度可被误判为过期，影响正式动作的 coarse readiness；已让骨骼包也作为新鲜度证据。此问题不能解释准备段锁方向后卡住，因为准备段使用独立的放松姿势门槛。 | Unity PlayMode 的骨骼包新鲜度测试通过；自然教练介绍后送入符合原 AR 门槛的仿真准备姿势，成功进入首段评分。真实手机追踪姿势仍未采集。 |
| 录制与结束 | `MotionRecorder` 源码与原 AR 一致；训练结束仍调用 `CompleteCoachTraining`，完成菜单仍有游戏/回放/返回手机事件 | 静态验证了方法和按钮引用；录制文件提交、回放和手机收到结果需要完整真机流程才能证明。 |

代码核对结果：上述 PoseScorer 九个核心状态方法、三个分段推进方法、三个语音序列方法及 MotionRecorder 全文件与原 AR 一致；场景主要训练参数一致；`git diff --check` 通过，手机端 `:app:compileDebugKotlin --offline` 通过。开始页标着“组数”的卡片原来在独立启动时错显每组次数 4，现统一显示组数（独立启动 2、手机推荐 1）；手机会话延迟到达时会刷新卡片。**不能据此宣称实机功能已完成**：当前 ADB 没有设备，Unity 正在打开项目，且 `Library/ScriptAssemblies/Assembly-CSharp.dll` 早于本次源码修复，修复后的 APK 尚未生成/安装。下一次真机需要确认接收日志中的 1 组 × 4 次、训练组数显示 1/1、准备段评分、正式动作评分、训练完成、录制提交、回放和结果返回手机。
