# CODEX 项目记忆

- 2026-09-25：初始化项目记忆文件，待补充 V2 与 AR 教练模式震动实现对比结果。
- V2 结论：游戏模式通过 RhythmModeController.NoteResolved 事件接 RhythmHitHapticAdapter；仅 result.IsHit（Perfect/Great/Good）时按目标映射到单个远端 Tracker 并调用 CoachHapticFeedbackController.PulseOnce。
- V2 传输：CoachHapticFeedbackController 轮询 Android provider 或桌面 SlimeVR/API 发现部位到 IP，随后 GET http://<tracker>/motor?duration_ms=...；VMC GameModeBootstrap 在包含 RhythmModeController 和用户 Animator 的场景运行时补挂控制器/适配器。
- 与 AR 教练模式差异：共用 TrackerWearLocation、SlimeVR 自动发现、HTTP motor endpoint 和控制器类，但 AR PoseScorer 通过 Tick 按确认错误部位持续/每 2 秒震动，命中不触发；V2 是离散命中一次震动，且只打目标对应的一个远端部位。
- Android 真机路径：TrackerHaptics.cs 在非 Editor Android 下每 2 秒调用 content://com.metaspine.mobile.trackerbindings 的 getTrackers，不依赖是否从宿主跳转；跳转本身不是 Unity 轮询启动条件。
- Provider 位于 SpineFlowMobile/app/.../TrackerBindingProvider.kt，从 EmbeddedSlimeVrRuntime.snapshot() 取带 IP 的 IMU 和 bodyPosition 后返回 JSON。独立 dev.slimevr.server.android 的数据不会自动进入该 Provider。
- 已知联调风险：Provider 源码当前白名单是 com.anny.vrmdemo，而 V2 APK applicationId 是 com.DefaultCompany.SpineV2；若宿主 APK 未更新授权，会被 SecurityException 拒绝。即便授权成功，Provider 也可能返回 trackers:[]。
- 直接启动 V2 不会阻止 Unity 自己启动轮询，但宿主端 EmbeddedSlimeVrRuntime.start(context) 当前由 SpineFlowMobile 的 TrackingPreparation 页面按钮触发；若未走该流程，Provider 可能读到空快照。
- 2026-09-25: Female DeadBugPractice migration is UI-only; keep original training scoring, feedback, voice prompts, coach model, and button hierarchy. Only use the Gaming-style 1920x1080 screen canvases and split ScorePopup/BodyPartFeedback into left/right panels.
- 2026-09-25: Restored Assets/Scenes/DeadBugPractice.unity and its meta byte-for-byte from stash@{0}; scene-by-scene restore avoids conflicting with other untracked migration files.
- 2026-09-25: Corrected the baseline: the prior UI-only DeadBugPractice edit was discarded; the scene now matches stash@{0} byte-for-byte, with its directly referenced migration scripts restored from the saved stashes.
- 2026-09-25：DeadBugPractice 初始 StartMenu 仅调整为正向显示和 Gaming 式左右分栏坐标；未新增/删除控件，训练脚本、反馈、提示和事件逻辑未改。
- 2026-09-25：进一步按 DeadBugRhythmHapticsTest 的 StartMenu 复制可视缩放与字号：信息卡 1.3、Logo/次数卡 1.5、开始 2、难度 2.5、返回 3；难度文字 18 号加粗；保留教练场景原文字串和控件层级。
- 2026-09-25：组数文字方框问题来自 CoachSetProgressUI 只查激活的 MobileCoachToolbar，导致动态 TMP 文本拿不到 SourceHanSans 中文字体；改为包含隐藏对象查找，字体资源本身和 GUID 均完整。

2026-09-25：女性死虫式教练训练画面首轮按 1.25x 等比例放大：FollowViewRoot 同步放大模型、模型间距和附着标签；组数、评分和身体反馈面板同步放大并为右侧面板保留屏幕边距。

2026-09-25：组数面板去除黑色背景，保留白色加粗文字；脚本默认值和场景序列化值均设为透明。

2026-09-25：静态对照原始 DeadBugPractice：核心 coach/user/分段引用仍在，但 V2 已做移动适配，XR gaze→Standalone/touch、AvatarMotionSource、相机/画布、暂停/触觉、导航及 completion upright gate 均有行为差异，不能称全功能一模一样。

2026-09-25：按训练链路复核原版与 V2 DeadBugPractice：分段标签/顺序/组数、PoseScorer 核心评分与语音资源/调用、教练动作引用一致；V2 每段新增 applyFormalActionScoreMapping=0 不改变当前配置。已发现唯一训练参数差异是 requireUprightPoseForCompletionControls：原版 1、V2 场景 0，完成训练控制的直立姿态门槛被关闭。

2026-09-25：按原版训练行为恢复 DeadBugPractice 场景 requireUprightPoseForCompletionControls=1，完成阶段仍需先直立才显示四个控制按键。

2026-09-25：将 Assets/Scenes/DeadBugPractice.unity（女性死虫式教练场景）加入 EditorBuildSettings 首位，V2 APK 无外部指定场景启动时默认直接进入该场景；保留原有游戏场景条目。

2026-09-25：按原始 AR 躺倒锁定方向的横向构图继续优化 DeadBugPractice：FollowViewRoot 由 1.25 调到 1.5，用户/教练模型横向中心由 -0.23/0.31 调到 -0.38/0.46，Main Camera FOV 调到 51.38676 形成约 1.2x 画面放大；评分面板左下、身体部位面板右上、组数文字顶部居中并同步放大。
