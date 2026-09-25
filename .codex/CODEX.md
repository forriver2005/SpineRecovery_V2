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

2026-09-25：只读盘点 5 份 stash（0/1/2 为拆分内容，3/4 为较早重叠备份），index 为空；四个 Practice 已在工作区。优先缺口是 PlayBack 场景和 Resources/ReplayAvatars 男女 prefab（现有回放按钮仍跳 PlayBack）；其次 CoachingChoose，testPractice 按动态动作包需求迁移。缺失路径去重 227（99 个非 meta）；旧迁移工具会重置 UI/直立门槛，不整包重跑。详细清单见 .codex/STASH_MIGRATION_AUDIT.md。
