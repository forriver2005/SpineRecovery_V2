# 模块 A：动作课程与静态语音交付

日期：2026-10-29。需求来源仅为 12 号产品规格及本轮模块 A 指令。

**状态：实现与离线编译已交付，患者体验验收未完成。** 六动作均有专属静态音频及正式播放节点，但本轮没有取得 Unity 实际画面、扬声器声音或逐次运行记录，不能称为六项患者验收通过。侧桥真实支撑侧及卧位猫式具体躯干方向仍需实际画面确认后补精确措辞。

## 1. 体验基线与本轮修改

开工前检查 git status，保留全部原有场景、字体、评分、语音、入口等未提交修改；没有 reset、stash、checkout、清空用户目录或覆盖场景。现有工程已部分实现课程，问题集中在以下几项：

- 正式动作库把 clip_29/30 路由到 DeadBugPractice/BirdDogPractice。两场景未挂 PracticeSceneInitializer，实际是各 10 段 Animator，不能误称目前全都运行单段 JSON。去掉通用初始化器中额外覆盖整包的方法，避免它启用时破坏原分段路径；外部显式 JSON 加载能力保留。
- 臀桥与腰背场景通过 RuntimeInitializeOnLoadMethod 的 sceneLoaded 接线。此前 BuildPracticePackage/BuildPackage 的提前 return 返回单段整包，使后面的真实单次切片不可达。本轮恢复项目已有切片、规则与端点对齐代码，不再按整包重播计次。
- 原卧位猫式 setup 错写“仰卧”；按 Profile 改为四点支撑。原 hold 对死虫/鸟狗重复播放 rep 口令；改为独立正常呼吸句。
- 新课程按：现有开始/朝向选择 → 专属完整讲解 + 原教练/用户模型介绍 → 完整示范 → 原 ReadyCountdownShort → 原 keepStill/2/1/start 全序列 → 逐次跟练。没有第二个“开始讲解”确认。
- 正式每次先在前次回位/起始姿势播放次数与侧别准备句，随后启动原动画和短方向句；原固定保持窗播呼吸，回位开始播回位句。准备阶段不会停在承重姿势上。
- 新增讲解、示范、准备跟练标题。正式进度显示真实次数、准备/抬起或伸展/保持/回位、当前侧别。侧别来自实际 segment，避免奇数组数处方发生显示/语音侧别错配。
- 超过四次没有录制相应数字时，播无数字的正确侧别准备句，屏幕继续显示真实次数；不把第 5 次报成第 1 次。
- 纠正取当前明确有效的 active region 偏差，连续 1 秒稳定后存一条；只在本次回位后休息播，优先完整播旧 nextAction。未知清空，跳过/停止清空，每次至多一条。无追踪不补猜测。不再让通用低分 PlayAdjustPose 抢占六动作课程。
- 修复事件中的 currentSegmentInfo 一直使用 Auto 而未带入真实 segment rule 的问题；当前目标诊断现在取 CurrentPoseGuidanceRule。此处只给观察/纠正真实上下文，不改变阈值。
- 移除“2 秒姿态不新鲜便 HeadsetDisconnected”与“关节限位过滤的 HardInvalidPose 便 SafetyStop”两条误停课路径。后者源于 AvatarJointLimitFilter 的 InvalidRotation/SuddenJump/HardOutOfRange/HingeOutOfRange，并非明确断连/患者安全事件。仍保留显式 CoachHardStopRequested、StopTimeline、技术失败及用户退出链；讲解/示范期间收到明确停止也会取消后续启动。

## 2. 六动作内容、来源与播放资产

完整逐句台词、实际 WAV 时长、路径、节点和依据见 [逐句音频表](14-module-a-audio-table.md)。机器可读清单为 `Assets/Audio/CoachLessons/audio-manifest.json`，含 76 个交付 WAV 的源文本、时长、格式和 SHA-256。兼容 setup/泛化 adjust 素材明确标为兼容项，不冒充正式课逐句均已播出。

| 动作 | 正式后端与单次 | 专属讲解时长 | 实际使用的关键音频 | 规则依据/未决点 |
|---|---|---:|---|---|
| 死虫 | 场景 10 段 Animator；每个 up/down 一次 | 30.160 s | deadbug_lesson、rep1–4、move、hold、return、adjust_Left/RightArm/Leg | activeRegions 6/9、目标肢体几何诊断；仅中性部位调整 |
| 鸟狗 | 场景 10 段 Animator；每个 up/down 一次 | 29.085 s | birddog_lesson、rep1–4、move、hold、return、adjust_Left/RightArm/Leg | 同上，独立四点支撑/前后伸展讲解 |
| 臀桥 | clip_26，22–27 s 上行；36.5–40 s 回落 | 22.598 s | hipthrust_lesson、count1–4、move、hold、return、adjust | HipThrustCoachMode/HipThrustPoseGuidance；不猜臀部过高/过低 |
| 卧位猫式 | clip_23，10–14.5 s 动作；16–20 s 回位 | 20.972 s | catcamel_lesson、count1–4、move、hold、return、adjust | Profiles[0] 四点支撑、小幅背部活动；具体屈伸方向未视觉确认，当前仍用 Profile 的“小幅活动背部” |
| 侧桥 | clip_27，14–17 s 撑起；23–26 s 回落 | 24.532 s | sidebridge_lesson、count1–4、move、hold、return、adjust | Profiles[1]；支撑规则 LeftArm，但未据此猜录制侧别；当前“看清示范侧”尚不满足最终明确左右要求 |
| 卷腹 | clip_28，7–10.5 s 卷起；12.5–15.5 s 回位 | 21.833 s | curlup_lesson、count1–4、move、hold、return、adjust | Profiles[2]：上背小幅卷起、颈部舒适、不拉扯；纠正只说上背位置，不宣称检测到颈部代偿 |

文件前缀均为 `Assets/Resources/CoachLessons/Coach_`，后缀 `.wav`。男女同名 Practice 场景均走相同 sceneLoaded/场景名映射。新资产使用 Windows System.Speech/SAPI、Microsoft Huihui Desktop zh-CN、Rate=0、Volume=100、默认 pitch；22050 Hz、16-bit、mono PCM WAV。生成脚本 `Assets/Audio/CoachLessons/Generate-CoachLessons.ps1`，源台词 `lesson-lines.tsv`（保留既有文件名，实际逗号 CSV）。不新增运行时 TTS/API。

## 3. 相位与时长

- 死虫/鸟狗：原 Animator 的实际采样时间不改。场景 scoringDuration 是到位后的独立固定窗：准备 2 s、core 5 s、return 2 s；不能把这些字段直接当作动画 clip 长度。原 FBX 段边界保留，实际 AnimationClip.length 等待 Unity 可运行核对。
- 死虫 return 后原 restAfterReturnSeconds=4 s；鸟狗无显式字段时沿用控制器默认 4 s。新休息窗取 `max(原休息, nextAction.length + 本次待播纠正.length + 0.1)`，仅在回位后的休息姿势容纳完整语音，不增加 core 承重保持。
- 其余四项切片后读取真实 frame.time：臀桥上行 4.9642 s / 回落 3.4679 s；猫式 4.4666 / 3.9667 s；侧桥 2.9661 / 2.9673 s；卷腹 3.4660 / 2.9973 s。均保留原 2 s 固定保持；不加速源帧。音频短方向约 1.864–2.224 s、呼吸约 1.754–1.834 s、回位约 1.759–2.024 s。
- rep 准备句按完整 AudioClip.length + 0.15 s 安排，动画冻结在准备/回位姿势。它是课程时间轴的预定时长，不等待动作做对，也不由观察回调推进。
- 示范：死虫/鸟狗播放 0→1→2→3→4→9，即左右各一次完整往返与收势，避免把四次练习再示范一遍；其余四项完整 0→1→2→3。保留 core 原固定保持，既不报训练计次，也不启动测量/正确绿显/触觉。关键帧示范不启用 Animator 覆盖骨架。示范后摆回准备段末帧并冻结，完整倒数期间保留该姿势。
- 暂停使用既有 MobileCoachControls 的 Time.timeScale/AudioListener.pause，加语音源 Pause/UnPause 与暂停感知时钟；示范和准备 coroutine 同样暂停。跳过调用 StopAll 丢弃旧语音；已结束重复仍由一次性 currentPlanEntryCounted 防重。

## 4. 死虫/鸟狗逐次计划（不是实际运行日志）

两动作正式默认 1×4 均为以下计划，rep 数字分别有独立完整静态音频：

| 次数 | segment | 实际目标 mask | 准备音频 | 动作/保持/回位 |
|---|---|---:|---|---|
| 1/4 | 1 up → 2 down | 6：右臂+左腿 | Coach_<action>_rep1 | move → hold → return → 旧 nextAction → 若有稳定纠正则播一条 |
| 2/4 | 3 up → 4 down | 9：左臂+右腿 | Coach_<action>_rep2 | 同上，每次重说侧别 |
| 3/4 | 5 up → 6 down | 6：右臂+左腿 | Coach_<action>_rep3 | 同上 |
| 4/4 | 7 up → 8 down | 9：左臂+右腿 | Coach_<action>_rep4 | 完整回位后 count，一组完成，进入 segment 9 与 finish cue |

**本轮没有取得上述四次实际播放记录。** 不用静态计划伪装运行证据。实现已添加 `[CoachLesson]`（scene/rep/segment/phase/target）、`[CoachDemo]` 和 `[CoachAudio]`（clip/秒数/pitch）日志供恢复后的代表路径核对。

## 5. 原语音保留映射与 Resources 冲突

- 原 coachIntroduction → userModelIntroduction：保留原音频、0.3 s 视觉 lead 和 0.35 s 阶段 gap。
- 原 readyCountdown：移到完整示范后，仍播放完整文件，内部数字、停顿、尾音未处理。
- 原 keepStill → 2 → 1 → start：完整调用原 StabilityCountdownSequence，保留 0.15 s 间隔；没有把两段倒数剪在一起，没有改 pitch。
- 原 nextAction：每次回位后完整播放，先于纠正；原 beatDing 为 FinishCue；其它旧语音/提示音资产与序列化引用原样保留。
- 无任何旧音频二进制修改。发现两份 `Resources/Audio/VoicePrompts/Coach_*`：将 `Assets/Assets/Resources/Audio` 连同 meta 精确移动到非 Resources 的 `Assets/Assets/LegacyCoachAudio`，没有删除文件。旧 `Assets/Resources/Audio/VoicePrompts` 留存。新课程唯一加载 `Resources/CoachLessons`，不再依赖重复旧路径。

## 6. 本轮验证、阻塞与后续核对

已做：使用当前 Unity Bee 产生的 Assembly-CSharp.rsp、Unity 自带 Roslyn/.NET 编译所有当前运行时代码，通过；证据 [compile.log](module-a-evidence/compile.log)。git diff --check 通过（已有换行提示）。76 个 WAV 均可解析，实测格式与时长已进入 manifest。四项裁切 frame 数及时间已读取核对，未扩展测试框架。

未做/不能宣称通过：六项逐条实际听感；Unity 实际播放；死虫/鸟狗四次左右与屏幕/声音对齐；暂停恢复/跳过的实际听觉；猫式方向、侧桥支撑侧的画面确认；失去曾经新鲜的追踪后的真实连续播放；设备 haptics。编译和音频时长不替代这些。

具体阻塞：PID 27736 是本项目主进程，26460/45404 是 AssetImportWorker，未关闭任何进程。最初 computer-use 能列出主窗口 12262086，但提示 minimized；activate_window 超时，此后 sky 与 cua_repl 都返回 Unity `windows=[]`。用户反馈已打开后重新获取列表仍为 `windows=[]`，`getApp('Unity')` 返回需要来自 inventory 的 windowId，不能猜窗口。最后 Editor.log 仍停留在 16:55:16、Assembly-CSharp.dll 16:55:07，最新源代码尚未由该编辑器刷新。没有通过 PowerShell UIAutomation 绕过。

临时本地运行观察脚本已从 Assets 移到 `.codex/ModuleALocalRun.cs`，待执行命令已清理，不会在用户恢复编辑器后自动切场景或启动 PlayMode。它不是交付运行时代码/新测试框架。窗口可用后只需代表死虫、鸟狗和一个腰背动作；同时补侧桥侧别/猫式方向截图，再收听其余动作专属音频即可，不建冗长矩阵。

## 7. 本轮改动文件

- `Assets/Scripts/SegmentedCoachController.cs`：准备句/短相位句、示范、暂停/跳过/取消、目标上下文与诊断日志。
- `Assets/Scripts/VoicePromptManager.cs`：完整讲解、示范后的原倒数、自然呼吸、稳定部位纠正与休息窗、唯一资源路径、音频日志。
- `Assets/Scripts/PracticeSessionController.cs`：讲解→示范→倒数顺序/标题，移除姿态数据误停课，讲解阶段明确停止处理。
- `Assets/Scripts/CoachSetProgressUI.cs`：课程阶段、真实次数/动作相位/目标侧别。
- `Assets/Scripts/PracticeSceneInitializer.cs`：移除自动整包覆盖，显式 JSON 路由保留。
- `Assets/Scripts/HipThrustCoachMode.cs`、`LumbarCoachMode.cs`：恢复已有单次录制切片，移除覆盖为整包的提前 return。
- `Assets/Scripts/PoseScorer.cs`：诊断获得真实 segment rule，稳定纠正输入与无数据清理；未改诊断阈值。
- `Assets/Audio/CoachLessons/*`、`Assets/Resources/CoachLessons/*`：源文本、生成脚本、README、manifest 与静态 WAV。
- `Assets/Assets/LegacyCoachAudio`：原重复语音完整保留移位。
- 本文、`14-module-a-audio-table.md`、`module-a-evidence/compile.log`、`.codex/CODEX.md`。

本轮未编辑任何场景 YAML、构建列表、字体、游戏路由、完成选择或触觉传输实现；原有这些修改保留。B/C 仍由总控后续串行处理。
