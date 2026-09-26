# CODEX 项目记忆

- 2026-09-26 实机根因：X4000 旧包中 `PoseScorer.Start()` 因原 AR 的 `BodyPartHighlightOverlay.shader` 漏迁、Android 后备 Shader 被裁剪而抛异常，导致 `OnSegmentHold` 从未订阅。已迁入 Shader/meta、纳入 Always Included Shaders，并使高亮缺 Shader 不阻断评分器。新 V2 和手机 APK 已保留数据覆盖安装，V2 冷启动 Unity 错误日志为空。详情见 `.codex/COACH_FLOW_RUNTIME_AUDIT.md`。

- 2026-09-26：本次把训练验证推进到 Unity PlayMode 和 Android 模拟器：1×4 手机载荷进入 V2、开始页/播放采用 1×4、自然介绍后仿真准备姿势通过稳定与首段评分、骨骼包新鲜度、左臂高亮和测试 Tracker HTTP `/motor` 请求均通过。方向锁成功并不表示准备段已达标，原 AR 准备段还要求伸直双腿并持续 0.35 秒；此前把骨骼包新鲜度当成准备段卡住的确定原因不成立。`PoseScorer` 已恢复原 AR 在非目标提示时清旧几何标记。临时副本已构建九场景 ARMv7 手机 APK；原用户手机未连接，尚未安装。完整记录见 `.codex/COACH_FLOW_RUNTIME_AUDIT.md`。

- 2026-09-26：手机推荐女性死虫式默认 1 组 × 4 次，V2 场景 2 组 × 4 次仅是无手机会话时回退。V2 原有 `GetPlannedTrainingVolume`→`SetTrainingVolume` 会使用手机值；新增接收/开练日志打印实际训练量，并在会话延迟接收时刷新开始页。开始页“组数”卡片之前无会话时误显每组次数 4，现显示组数 2。手机端修复了 `ArSessionRepository.begin` 仅凭 sessionId 复用旧推荐动作的缺陷；详见 `.codex/FEMALE_DEADBUG_FUNCTION_AUDIT.md`。真机 APK 尚未重建验证。

- 2026-09-26：针对用户指出的女性死虫式实机卡住，重新逐段比对原 AR/V2：PoseScorer 九个状态推进方法、SegmentedCoachController 三个推进方法、VoicePromptManager 三个序列方法和 MotionRecorder 全文件保持一致；场景训练量/姿势参数一致。实际迁移缺口是评分器的追踪新鲜度只看 `/VMC/Ext/T`，与方向锁的骨骼包判断不一致，已修复为两类包计数均可证明新鲜。静态核对不等于实机验收；当前 ADB 无设备、Unity Assembly-CSharp.dll 尚未从新源码重编，须重新打包安装验证。审计见 `.codex/FEMALE_DEADBUG_FUNCTION_AUDIT.md`。

- 2026-09-26：手机实测反馈女性死虫式在躺下并锁定方向后训练不推进。代码发现 V2 PracticeSessionController 的追踪新鲜度使用 AvatarMotionSource.LastBonePacketCounterInFrame，但 PoseScorer.UpdateGuidanceVmcFreshness 只看 LastPacketframeCounterInFrame（VMC `/VMC/Ext/T`），Android 骨骼数据可更新而时间帧包为 0，使正式动作 coarse readiness 一直为 false、超时后也不进入评分。PoseScorer 现把两种包计数的较大值作为新鲜度证据，保持原有姿势/评分门槛；仍需更新 APK 后实测确认。

- 2026-09-26：五份重叠 stash 已合并清理为一份 `stash@{0}`（8a1943b6）：只留工作区缺失的 209 路径/92 个非 meta，主要为旧选择/演示、`testPractice` 联调、旧 UI 图片/迁移工具及 EVMC4U/uOSC 示例。已迁入的七个普通训练场景及其工作区现有脚本/资源不再留旧版本于 stash。旧快照 SHA 备份在 `refs/codex/stash-archive/2026-09-26/{0..4}`；训练文件和 index 未改变，仅更新盘点文档。分类见 `.codex/STASH_MIGRATION_AUDIT.md` 顶部。

- 2026-09-26：先按 SpineFlowMobile 的 recommendedActions/targetScene 和原 AR 的允许场景、有效完成菜单追踪通路，再定迁移范围。普通训练只需四个男女 Practice、死虫/鸟狗两个游戏、PlayBack，共七个场景；V2 均已列入 Build Settings。`CoachingChoose` 和旧演示页不在手机启动通路，`testPractice`→`testGaming` 仅动作包哨兵联调使用。详见 `.codex/PHONE_AR_ROUTE_AUDIT.md`；旧 stash 清单是历史快照，不可直接当待办。

- 2026-09-25：初始化项目记忆文件，待补充 V2 与 AR 教练模式震动实现对比结果。
- V2 结论：游戏模式通过 RhythmModeController.NoteResolved 事件接 RhythmHitHapticAdapter；仅 result.IsHit（Perfect/Great/Good）时按目标映射到单个远端 Tracker 并调用 CoachHapticFeedbackController.PulseOnce。
- V2 传输：CoachHapticFeedbackController 轮询 Android provider 或桌面 SlimeVR/API 发现部位到 IP，随后 GET http://<tracker>/motor?duration_ms=...；VMC GameModeBootstrap 在包含 RhythmModeController 和用户 Animator 的场景运行时补挂控制器/适配器。
- 与 AR 教练模式差异：共用 TrackerWearLocation、SlimeVR 自动发现、HTTP motor endpoint 和控制器类，但 AR PoseScorer 通过 Tick 按确认错误部位持续/每 2 秒震动，命中不触发；V2 是离散命中一次震动，且只打目标对应的一个远端部位。
- Android 真机路径：TrackerHaptics.cs 在非 Editor Android 下每 2 秒调用 content://com.metaspine.mobile.trackerbindings 的 getTrackers，不依赖是否从宿主跳转；跳转本身不是 Unity 轮询启动条件。
- Provider 位于 SpineFlowMobile/app/.../TrackerBindingProvider.kt，从 EmbeddedSlimeVrRuntime.snapshot() 取带 IP 的 IMU 和 bodyPosition 后返回 JSON。独立 dev.slimevr.server.android 的数据不会自动进入该 Provider。
- 联调更新：SpineFlowMobile 的 TrackerBindingProvider 当前白名单已包含 com.DefaultCompany.SpineV2 和 com.anny.vrmdemo；旧白名单阻断已消除，但仍需真机确认宿主 APK 已更新且 Provider 返回非空 trackers。
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

2026-09-25：从 stash@{0} 按顺序恢复 BirdDogPractice、maleDeadBugPractice、maleBirdDogPractice 及 meta 到工作区；三场景均保留各自训练配置，统一套用女性死虫式横屏标准：FollowViewRoot 1.5、模型横向中心 -0.38/0.46、FOV 51.38676、左下评分/右上身体反馈/顶部组数面板，并将 completion upright gate 统一恢复为 1。三场景已加入 EditorBuildSettings。
- 2026-09-25：修正三场景迁移遗漏：以当前 DeadBugPractice 为唯一 UI 基准，将 BirdDogPractice、maleDeadBugPractice、maleBirdDogPractice 的 89 个 RectTransform（含开始页左右分栏、旋转、缩放、间距、训练面板位置）全部对齐，并同步 27 个 TMP 文字组件的字体/字号/颜色/对齐；保留各场景的动作状态、模型 prefab、评分与训练参数、文字内容和按钮事件。男性两场景的 BodyPartFeedback PreserveAspect 也同步，组数背景透明。未复制死虫专属图标或训练逻辑。
- 2026-09-25：三迁移场景站立准备姿态中 coach 与用户/原站立构图过近；仅将三个场景 coach prefab 实例根节点 x 从 0.46 微调到 0.56，用户模型、模型缩放、相机、动画、训练逻辑和评分配置不变。
- 2026-09-25：纠正上一轮对 coach 的误解：三迁移场景的教练人物根节点 x 已恢复为 0.46；实际需要上移的是挂在教练根节点上的世界空间 “Coach” 标识，m_AnchoredPosition.y 从 -1.301 调到 -0.6。四个教练场景的组数提示统一移到 anchoredPosition y=420、靠近上边界；ScorePopup 统一改为左上锚点 (0,1) / position (260,-180)。分数字体由无中文 glyph 的 LiberationSans SDF 改为项目已有 SourceHanSansSC-Heavy SDF，保留 ScorePopup 运行时逻辑。
- 2026-09-25：实机/编辑器截图表明 CoachLabel 的 y=-0.6 仍贴近站立教练头部；继续只上移标识到 y=1.2，四个教练场景统一，coach 人物根节点仍保持 x=0.46。
- 2026-09-25：根据四张运行画面修正实际构图：coach 根节点从 x=0.46 收回到 x=-0.05，避免站立/躺倒动作的右侧肢体出屏；CoachLabel 从 y=1.2 回到 y=0，保留在教练头部上方且不被移出画面。组数 y=420、ScorePopup 左上锚点和 SourceHan 中文分数字体保持不变。
2026-09-25：修正 ScoreDisplayUI 的真实运行时分数链：四个场景的 customFont 绑定 SourceHanSansSC-Heavy.otf，避免 UnityEngine.UI.Text 的中文方框；运行时分数面板锚到左上 (40,-40)，背景和面板描边透明，训练评分逻辑不变。
2026-09-25：按用户纠正回退构图：四个场景 coach 人物根节点恢复原始 x=0.46，只将世界空间 CoachLabel 单独上移到 y=-0.6；不再用 x=-0.05 把人物推向画面中央。
2026-09-25：根据横屏躺倒画面继续微调构图：四个场景 coach 根节点从 x=0.46 小幅左移到 x=0.30，避免右侧肢体出屏但不把人物推到画面中央；CoachLabel 从 y=-0.6 调到 y=0.4，恢复到教练头部上方可见区域。
2026-09-25：排查 CoachLabel 消失原因：场景文本和对象仍存在，PracticeSessionController 在介绍阶段有显隐切换；在正式训练开始后明确恢复 CoachLabel active，保证教练模式运行画面持续显示 Coach。
2026-09-25：修复两项运行时显示问题：ScorePopup 字号从 72 降到 36，ScoreText 宽度增至 360 并关闭自动换行，实时分数保持为单行；FaceCameraBillboard 对 RectTransform 改用 anchoredPosition 作为脱离父节点后的跟随偏移，避免 Coach 标签运行时回到模型原点/不可见位置。
2026-09-25：根据新运行截图继续修复：CoachLabel 脱离旋转模型后改用世界竖直方向偏移，避免躺倒动作把标签转到侧面/屏外；四场景标签偏移 y=3（父级 0.3 缩放后约 0.9 世界单位），正式训练和教练介绍均保持 active。ScorePopup 与旧 ScoreDisplayUI 统一压到 28 号，ScoreText 宽度 420、关闭换行，平均分/实时分数使用中文冒号并保持标签与数值同一行。
2026-09-25：再次按运行截图修正：CoachLabel 改为 PracticeSessionController 创建的屏幕空间 TMP 标签，按教练 Animator 所有 Renderer 的世界顶部投影定位；介绍教练/正式训练按原显隐阶段同步显示。原世界空间 CoachLabel 仅保留兼容并隐藏渲染。ScorePopup 回到较大但可控的 48 号，根节点左上固定 (40,-40)、scale=1、宽 520，关闭换行，平均分和数值同一行。
2026-09-25：本轮按投屏眼镜可读性调整：ScorePopup 回到原先的大字号 72，继续固定左上并保持“平均分/实时分数 + 数值”同一行；四个教练完成控制按键统一扩大到 280x100，保留原四个按钮、顺序、位置和功能；Coach 屏幕空间标签字号调到 42，并在投影超出边缘时钳制到屏内，避免标签消失。
2026-09-25：根据运行截图修正完成控制布局：四个按键扩大到 360x120，中心间距改为 150，四个中心点为 175/25/-125/-275，按钮文字调到 36，避免放大后上下贴住。
2026-09-25：按等比例放大要求，以 360x120 为基准统一乘 1.25：完成控制按键改为 450x150，中心间距 187.5，中心点 218.75/31.25/-156.25/-343.75，按钮文字改为 45，保持按钮、间距和文字同步缩放。
2026-09-25：纠正放大基准：不再沿用已变形的尺寸，恢复以原始 190x70 为基准统一放大 2 倍；完成控制按键改为 380x140，中心间距 200，中心点 250/50/-150/-350，按钮文字按原始 24 同步放大为 48。

2026-09-25：恢复回放迁移主体：从 stash@{0} 恢复 Assets/Scenes/PlayBack.unity、男女 Resources/ReplayAvatars prefab 及 PlayBack 直接依赖的 Assets/skel/Scripts 运行时脚本；PlayBack 加回 EditorBuildSettings。Unity 刷新后可正常导入并进入 PlayBack，运行时 ReplayRuntimeInstaller 创建 ReplayRoot，播放/暂停/PlaybackProgressSlider 已接入。当前本机 Replays 目录只有 completed=false 的临时录制，没有 latest_completed.json/commit.ok，因此运行画面会提示暂无已完成回放；未伪造或改写用户录制数据。

2026-09-25：回放排查：V2 回放仓库只接受已提交的 Replay V3 完成文件；本机 Replays 目录仍只有 completed=false 临时录制，所以“暂无已完成回放”是数据状态导致，不是按钮失效。原 AR 的 MotionPlaybackController 读取 legacy recorded_motion.json，数据格式和场景资源不同。PlayBack 的 ReplayRuntimeInstaller 现在会主动取得相机，设置横屏移动相机 (0,1.6,4)、FOV 51.38676、深蓝背景，并让相机朝两个人物中点；PlayBack 场景 coach/user 根节点为 x=0.30/-0.38。Unity 外部脚本修改后需 Ctrl+R 刷新；本轮已在编辑器保存 PlayBack 场景。

2026-09-25：对照原 AR PlayBack 后恢复人物构图：coach/user 根节点改回原 AR 的 x=-1.436/0.12，coach 根旋转改回 y=-90（四元数 w=0.7071068、y=-0.7071068）；V2 仍保留横屏相机和运行时相机对中逻辑。刷新并运行后两个人物均在画面内且左右分开。

2026-09-25：按手机横屏回放参考图重排 PlayBack：ActionMenu 改为 ScreenSpaceOverlay/1920x1080，播放和暂停固定左上同一行，进度条紧随其右；Practice(返回)、Home(主页)、Game(游戏)固定右侧竖排；用户/教练模型保持画面中下部左右分布。运行时 ConfigureMobileCanvas 与场景序列化值同步，按钮事件和回放逻辑不变。

2026-09-25：代码验收（验证脚本仅经 Python stdin 运行，未放仓库）：九个 Build Settings 场景路径/GUID 有效；四个教练场景与 PlayBack 的非内置资源 GUID 均可解析；四教练场景各有 PoseScorer、分段教练、录制、语音、评分、控制等核心组件，训练关键标量参数对照原 AR 均无差异，完成后站立门槛均为 1。四场景 EndMenuCanvas（教练流程不启用）各继承一个 PracAgain 按钮空 UnityEvent，原 AR 也如此。PlayBack 按钮目标均解析，但本机回放目录仅有 32 个 .tmp 录制，没有已完成回放，播放/进度实际动作仍待完整录制验证。尚缺 CoachingChoose/控制器/两张直接依赖图片、testPractice 场景；CoachModeEntry 为可选入口，DeadBug 与 maleDeadBug 1 为旧演示。SpineFlowTrainingSession 仍接受 CoachingChoose/testPractice 目标，当前 Build Settings 未包含它们。

2026-09-25：纠正语音资源判断：旧 testPractice 的 VoicePromptManager.readyCountdown 绑定 ReadyCountdown.mp3，属于教练/用户模型介绍后的训练前倒数；当前四个教练场景都绑定已存在的 ReadyCountdownShort.mp3。迁移 testPractice 时可考虑沿用现有短版，不应把旧长版音频表述为当前教练功能的必需缺口。

2026-09-26：核对手机端原训练流程：SpineFlowMobile 的 TrainingStartPayload 和 ArSessionTokenContract 按 recommendedActions 生成/覆盖 targetScene，直接跳 V2 对应死虫式/鸟狗式男女场景；没有动作选择页。CoachingChoose/控制器/旧图片不是当前待迁移目标，原盘点的建议已在 STASH_MIGRATION_AUDIT.md 顶部纠正。testPractice 仅当前动作包联调哨兵使用；后续医生处方多动作支持需单独设计，目前 V2 HasValidStartPayload 仅接受 2 条（一 coach、一 game）。

2026-09-25：四个教练场景初始 StartMenu 的 Times 卡片数字 TMP 由 y=-2.6 调到 y=21.4；用户认为首轮 y=9.4 仍偏低。Unity 重新加载 DeadBugPractice 后查看 Game 窗口截图，数字与卡片下方小字已分开。卡片、其他文字和训练逻辑不变。

2026-09-25：只读盘点 5 份 stash（0/1/2 为拆分内容，3/4 为较早重叠备份），index 为空；四个 Practice 已在工作区。优先缺口是 PlayBack 场景和 Resources/ReplayAvatars 男女 prefab（现有回放按钮仍跳 PlayBack）；其次 CoachingChoose，testPractice 按动态动作包需求迁移。缺失路径去重 227（99 个非 meta）；旧迁移工具会重置 UI/直立门槛，不整包重跑。详细清单见 .codex/STASH_MIGRATION_AUDIT.md。
2026-09-25：按“原始迁移成果/非迁移内容”重新分类：拆分的 stash@{0} 是主体迁移快照但混有一次性 Editor 工具、skel 依赖和旧工程设置；stash@{1} 只有两个 .meta；stash@{2} 主要是 EVMC4U/uOSC 外部包与 GamingTrackerHapticsAdapter。stash@{3} 是拆分前的重叠备份，stash@{4} 是 Gaming 合并前的 Coaching 全量备份；不能把整个 stash 当作可直接恢复的迁移成果。
2026-09-26：女性死虫式实机问题复核：PoseGuidanceRuntime.cs、PoseHighlightDiagnostics.cs 与原 AR 版逻辑一致；PoseScorer 的目标几何提取及 0.5s 确认/0.25s 释放、场景阈值也一致。红色部位快速切换时先查 SlimeVR 姿态漂移和 VMC 输入，不要随意改原阈值。震动真正断点是 V2 Android 仅读取手机 App 内嵌 SlimeVR Provider，而当时 10 个 IMU 在独立 dev.slimevr.server.android 进程，Provider 为空。SpineFlowMobile 已通过本机 SlimeVR 21110 FlatBuffers WebSocket 回退读取 10 个部位/IP；V2 实机读取绑定成功。当前 10 个设备状态均 DISCONNECTED，需在线后才可确认电机脉冲。四个教练场景相机深蓝底改纯黑，Android 目标 60 fps；已构建/安装 ARM64 IL2CPP 非 Development APK（D:\Code\SpineV2Builds\2026-09-26\SpineRecovery-V2-coach-arm64.apk），SurfaceFlinger 空闲画面间隔中位数 16.7ms；训练中帧率尚需在线姿态流确认。投屏眼镜普通屏幕镜像不能恢复 AR 空间锚定，黑底在光学透视设备上的透明效果取决于硬件。
2026-09-26 更正：上一条把独立 SlimeVR 作为震动数据回退源是错误方向，该回退已从 SpineFlowMobile 撤销并重新安装 App。内嵌 SlimeVR 本来就是完整服务端+GUI，Provider 只读本进程 SlimeVrRuntimeManager；独立进程也可单独看到同一批 IMU。两者都监听本机 UDP 6969 / WebSocket 21110，同时运行会端口冲突；原内嵌 GUI 未等自身服务就绪就连 21110，可能误连独立进程。已加自身服务就绪门槛，未就绪时不加载 GUI。前述“10 个部位已接上”证据来自错误回退，不能当作内嵌震动已修复。
2026-09-26：按游戏模式统一训练画面背景：四个教练场景和 PlayBack 相机均设 Skybox 清屏、黑色不透明背景（m_ClearFlags=1，RGBA=0/0/0/1）；回放 ReplayRuntimeInstaller 的运行时相机覆盖也同步改为该设置。游戏模式四场景本来已一致；回放进度条名为 Background 的 UI 子物体只是滑条轨道，不是全屏背景。
2026-09-27：实机回归后补齐手机端效果：上轮黑底仅改源码、未更新手机 APK；本轮重构建并 adb install -r 覆盖安装 2026-09-27 ARM64 包。PlayBack 用户/教练分别调到左/右 x=-1/+1，教练从侧向改正面，固定相机退至 z=5 以完整容纳双模型；普通投屏无 AR 空间锚定，不能通过走动观察 360°。教练开始面板确认按 mobile sessionId 写入 PlayerPrefs，跨场景/进程只出现一次；四个游戏场景 StartMenu 的 Return 按键移至左上并接回当前训练对应 coach 场景；手机返回 Intent 显式指向 com.metaspine.mobile.MainActivity。红色高亮本轮未改：V2 与原 AR 的 PoseGuidanceRuntime 只有注释差异，PoseHighlightDiagnostics 逻辑相同；准备阶段对未期望抬起的手臂仍用 body-plane 35°/25° 门槛，语义准备位只检查手部朝头方向/腿伸直，因此同一规则也可能对正确手臂姿势给红色提示，需要结合实机姿态输入进一步定位。
