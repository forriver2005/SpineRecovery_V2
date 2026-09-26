# V2 stash 迁移盘点（2026-09-25）

## 2026-09-26 整理后的实际 stash

现在只剩 `stash@{0}`（`8a1943b6fd3bc8f8a55ac11c1dd02a8db9b6be31`，`deferred legacy and optional integration assets only`）。从旧五份快照去重的 378 个变更路径中，移除了工作区已经有的 169 个路径及其旧版本，留下目前不在工作区的 209 个路径（92 个非 `.meta`）。这是保存而非迁移待办；整理 stash 未改动训练文件或 index，本次只更新盘点文档。

| 类别 | 路径数（非 meta） | 保留的内容和定位 |
| --- | ---: | --- |
| 旧场景 | 10（5） | `CoachModeEntry`、`CoachingChoose`、`DeadBug`、`maleDeadBug 1` 是旧入口/演示；`testPractice` 仅动作包联调按需评估。 |
| 场景控制脚本 | 6（3） | `CoachModeEntryController`、`CoachingActionSelectionController`、旧 `GamingTrackerHapticsAdapter`；均非当前普通训练闭环所需。 |
| 旧 UI 图片 | 44（22） | ImageSource 中的选择页、开始/结束页、评分等旧图片；当前七场景没有缺失这些图片的静态 GUID 依赖。 |
| 旧 Knife UI 图片 | 7（3） | 三张旧按钮贴图及目录/meta；随旧选择页保存。 |
| 语音 | 2（1） | `ReadyCountdown.mp3` 仅旧 `testPractice` 引用，四个现行教练场景使用已在工作区的 `ReadyCountdownShort.mp3`。 |
| 一次性编辑器工具 | 6（3） | `CoachModeMobileMigration`、`CoachStage3Wiring`、`Stage5AndroidBuild`，不能直接重跑覆盖现有适配。 |
| skel 编辑器脚本 | 9（4） | 四个 Editor 辅助脚本及目录/meta；回放所需运行时 skel 脚本已经在工作区。 |
| EVMC4U 附件 | 86（34） | 教程、示例、测试、额外接收器/Editor、许可文本等；不整包恢复。 |
| uOSC 附件 | 39（17） | Editor 和 Samples、package 描述；当前运行时代码已在工作区。 |

原五份重叠快照已从 stash 列表移除，原 SHA 分别存为 `refs/codex/stash-archive/2026-09-26/{0..4}` 供误删恢复，不在日常 stash 列表中。当前普通手机推荐训练所需的七个场景不在新 stash；未来若需要 `testPractice` 联调，应连其游戏去向重新评估，旧 `testGaming` 场景既不在当前工作区，也不在这份 stash。

> 本文下方的“迁移建议”和“当前缺失”是 2026-09-25 的历史快照，不是当前待办。现行手机→原 AR 可达场景及 V2 对照，以 [PHONE_AR_ROUTE_AUDIT.md](PHONE_AR_ROUTE_AUDIT.md) 为准；不可再按旧建议恢复 CoachingChoose、旧演示场景或整份 stash。

## 2026-09-26 流程校正

- 下文是恢复回放前的 stash 快照；PlayBack 场景、男女回放 prefab 和运行时 skel 依赖现已恢复，不能再按“当前缺失”清单直接操作。
- 正常训练由 SpineFlowMobile 的 `trainingPlan.recommendedActions` 计算 `targetScene`，Intent 直接进入 V2 的死虫式/鸟狗式男女教练场景；训练端不经过 `CoachingChoose`。因此 `CoachingChoose`、选择控制器和旧选择页图片不是当前流程的待迁移功能，旧代码中的导航引用仅作为遗留入口盘点。
- 当前手机端仅在动作包联调哨兵流程将 `targetScene` 设为 `testPractice`。该场景是按需的联调/未来扩展入口，不应当作现有普通推荐动作训练的阻塞项。其旧版 `ReadyCountdown.mp3` 引用可在未来迁移时评估是否改用现有 `ReadyCountdownShort.mp3`。
- 后续医生处方若要包含多种动作，应先定义处方动作顺序、场景路由和完成结果契约；当前 V2 启动 payload 校验固定要求一条 coach 加一条 game 动作，不能直接声称已经支持多动作处方。

## 当前状态

- 分支：Coaching；HEAD：b14bdc4（女性死虫式）。
- Git index 当前为空；共有 5 份 stash。stash 编号会随以后新增/删除而改变，下表记录 SHA 便于定位。
- 四个固定教练训练场景已经在工作区，当前的 UI 和模型布局比 stash 更新。BirdDogPractice、maleDeadBugPractice、maleBirdDogPractice 及其 meta 目前仍是未跟踪文件。
- 本次未 apply/pop/drop 任何 stash，未恢复场景、脚本、资源或工程设置。

## 各份保存内容

文件数指各 stash 相对自己的基线的变更路径，包含 meta 以及 stash 的未跟踪文件父提交；不是待迁移功能数。

| stash | SHA | 保存内容 | 变更路径数 | 当前同内容 | 当前内容不同 | 当前路径缺失 |
|---|---|---|---:|---:|---:|---:|
| `stash@{0}` | `ab2bc217ac` | 剩余教练迁移：10 个场景、语音、UI 图片、回放模型、控制器和工程设置 | 180 | 69 | 11 | 100 |
| `stash@{1}` | `3993bf0f3b` | 两个脚本的配套 meta | 2 | 1 | 0 | 1 |
| `stash@{2}` | `8fdc670d06` | EVMC4U/uOSC 配套文件和旧游戏震动适配器 | 141 | 15 | 0 | 126 |
| `stash@{3}` | `aed5821dd3` | 拆分前的较早迁移备份 | 311 | 77 | 7 | 227 |
| `stash@{4}` | `de244e9c89` | Gaming 合并前的 Coaching 完整变更备份 | 378 | 135 | 16 | 227 |

- stash@{3} 的变更路径全部包含在前 3 份中；除旧项目记忆外，保存的内容也相同。
- stash@{4} 还有旧 Android 设置及部分插件运行时文件；这些额外路径当前均已存在，不是新增待恢复文件。
- 五份去重后，当前缺失 227 个路径，其中 99 个为非 meta 文件。

## 迁移建议

### 1. 优先：回放闭环

- `Assets/Scenes/PlayBack.unity` 及 meta。
- `Assets/Resources/ReplayAvatars/female.prefab`、`male.prefab` 及目录/资源 meta。
- PlayBack 场景直接引用的 4 个 skel 脚本：LumbarSeamAligner、SkelFemaleWalkSimulator、SkinnedMeshLoosePartSeparator、SpineRotationColorVisualizer，恢复时要逐项核对用途及相关引用。
- 已有 `ReplaySceneBootstrap` 通过 `Resources.Load` 加载 ReplayAvatars；这种动态依赖不会出现在场景 GUID 引用中。
- 四个训练场景的 PlayBack 按钮均绑定 `LoadPlayBackScene`；当前目标场景不存在，Build Settings 也未收录，所以回放导航尚不完整。
- 按当前四场景的横屏字号、模型大小、相机及左右布局标准适配回放，之后再加入构建列表。

### 2. 下一步：动作选择 / 场景导航

- `CoachingChoose.unity` 和 `CoachingActionSelectionController.cs` 及 meta。
- 直接缺失依赖：ConfirmButton.png、Knife UI 的 button 1 normal.png、上述 4 个 skel 脚本。
- 选择控制器仍保留 AR 凝视停留选择逻辑，恢复后需要按手机触控流程检查，不能仅复制就认为完成。
- `CoachModeEntry.unity` + `CoachModeEntryController.cs` 是此前迁移新增的入口，可按是否需要独立选择入口决定；默认启动女性死虫式仍应保留。
- 四个训练场景的 Home 实际绑定 ReturnToMobileApp，不是 CoachModeEntry。不要误认为新增入口是当前主页按钮的必要依赖。

### 3. 按需：数据驱动训练和旧演示页

- `testPractice.unity` 是统一数据驱动训练场景，不是单纯测试资源；加载服务器动作包/按 actionId 训练时需要它。它引用的 `ReadyCountdown.mp3` 也未恢复。
- `DeadBug.unity`、`maleDeadBug 1.unity` 是额外旧场景，不是当前四个 Practice 的缺失版本。恢复前按是否保留其导航和演示用途决定。
- 四个当前训练场景使用的语音、中文字体，以及在 stash 缺失资源集合中对应的静态 GUID 依赖，没有发现待补缺口。

### 4. 暂不整包恢复

- `CoachModeMobileMigration.cs`、`CoachStage3Wiring.cs` 是一次性旧迁移工具。Apply 会批量重写场景；CoachStage3Wiring 还会设置 requireUprightPoseForCompletionControls=false 并创建工具栏，和当前要求冲突。
- `Stage5AndroidBuild.cs` 保存了旧 ARM64/IL2CPP/OpenGLES3 打包做法及固定输出目录，可参考其设置；不要直接运行旧脚本覆盖当前配置。
- 当前 ProjectSettings 相比 stash@{0}：AndroidTargetArchitectures 为 1（旧为 2），Android scriptingBackend 没有显式 IL2CPP 条目，Android 图形 API 列表也不同。打包前应单独确认 ARM64/IL2CPP/图形 API，不整文件回滚。
- `GamingTrackerHapticsAdapter.cs` 和配套 meta 是旧游戏动作误差持续震动方案；当前 `RhythmHitHapticAdapter` 是命中震动。不能因为文件缺失就直接加回。
- EVMC4U/uOSC 的大量示例、教程、编辑器面板不是训练运行时缺口。运行时接收器/OSC 代码已有；LICENSE/第三方说明可单独补齐。
- 22 张旧 ImageSource 图片及 3 张 Knife UI 图片大多不被当前场景使用，应随目标场景依赖恢复，避免把旧 UI 一起覆盖回来。

## 依据

- 对 5 份 stash 的基线、工作树和未跟踪父提交做 Git blob 比对。
- 检查当前构建场景清单、四场景 ActionMenu 按钮持久化事件、SceneSwitcher 和 Replay 动态资源加载路径。
- 将缺失资源 meta 的 GUID 与当前 Assets 下场景/prefab/asset/material/controller/meta 的引用对比；未发现当前项目静态引用这些缺失 GUID。动态按名字加载的回放资源另行检查，确实缺失。
- 本次为静态盘点，没有运行或恢复这些尚缺失的场景。

## 当前缺失的非 meta 文件清单

### Assets/Audio（1）

- `Assets/Audio/VoicePrompts/ReadyCountdown.mp3`

### Assets/EVMC4U（34）

- `Assets/EVMC4U/3rdpartylicenses(ExternalReceiverPack).txt`
- `Assets/EVMC4U/Editor/ExternalReceiverEditor.cs`
- `Assets/EVMC4U/Editor/Resources/tutorial/caution_bg.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/define.txt`
- `Assets/EVMC4U/Editor/Resources/tutorial/ignore.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/ok_button.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start/start_bg.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start/start_english.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start/start_japanese.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_en/discord.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_en/howtouse.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_en/vmcprotocol.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_first/en.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_first/ja.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_ja/discord.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_ja/howtouse.png`
- `Assets/EVMC4U/Editor/Resources/tutorial/start_ja/vmcprotocol.png`
- `Assets/EVMC4U/Editor/Tutorial.cs`
- `Assets/EVMC4U/ExternalController.cs`
- `Assets/EVMC4U/LICENSE`
- `Assets/EVMC4U/SampleScripts/CCCameraControl/CCCameraControl.cs`
- `Assets/EVMC4U/SampleScripts/CalibrationByController/CalibrationByController.cs`
- `Assets/EVMC4U/SampleScripts/CapsuleRigidbodyMover/CapsuleRigidbodyMover.cs`
- `Assets/EVMC4U/SampleScripts/DeviceAttacher/DeviceAttacher.cs`
- `Assets/EVMC4U/SampleScripts/FreezeSwitch/FreezeSwitch.cs`
- `Assets/EVMC4U/SampleScripts/HandCatch/HandCatch.cs`
- `Assets/EVMC4U/SampleScripts/HandCatch/HandCatch_Helper.cs`
- `Assets/EVMC4U/SampleScripts/HandCatch/HandCatch_WeaponHelper.cs`
- `Assets/EVMC4U/SampleScripts/ObjectSwitch/ObjectSwitch.cs`
- `Assets/EVMC4U/SampleScripts/TeleportKit/TeleportManager.cs`
- `Assets/EVMC4U/SampleScripts/misc/HiResolutionPhotoCamera.cs`
- `Assets/EVMC4U/Test/AddComponentTest.cs`
- `Assets/EVMC4U/Test/DaisyChainTesting.cs`
- `Assets/EVMC4U/Test/InputTesting.cs`

### Assets/Editor（3）

- `Assets/Editor/CoachModeMobileMigration.cs`
- `Assets/Editor/CoachStage3Wiring.cs`
- `Assets/Editor/Stage5AndroidBuild.cs`

### Assets/ImageSource（22）

- `Assets/ImageSource/CoachingMode.png`
- `Assets/ImageSource/ConfirmButton.png`
- `Assets/ImageSource/DbUI.png`
- `Assets/ImageSource/DeadBugName.png`
- `Assets/ImageSource/DedbugInfo.png`
- `Assets/ImageSource/EndBG.png`
- `Assets/ImageSource/ExerciseInfo.png`
- `Assets/ImageSource/HardButton.png`
- `Assets/ImageSource/HpBG.png`
- `Assets/ImageSource/PracAgainButton.png`
- `Assets/ImageSource/ReturnButton2.png`
- `Assets/ImageSource/ReturnHomeButton2.png`
- `Assets/ImageSource/Score.png`
- `Assets/ImageSource/SimpleButton.png`
- `Assets/ImageSource/StandardButtonWhight.png`
- `Assets/ImageSource/StartTrainingButton.png`
- `Assets/ImageSource/StarterBG.png`
- `Assets/ImageSource/Times.png`
- `Assets/ImageSource/TimesWhite.png`
- `Assets/ImageSource/Vector (3).png`
- `Assets/ImageSource/gamingMode.png`
- `Assets/ImageSource/panelUIHome.png`

### Assets/Knife（3）

- `Assets/Knife/Customizable Hologramm Shader/Textures/UI/button 1 click.png`
- `Assets/Knife/Customizable Hologramm Shader/Textures/UI/button 1 hover.png`
- `Assets/Knife/Customizable Hologramm Shader/Textures/UI/button 1 normal.png`

### Assets/Resources（2）

- `Assets/Resources/ReplayAvatars/female.prefab`
- `Assets/Resources/ReplayAvatars/male.prefab`

### Assets/Scenes（6）

- `Assets/Scenes/CoachModeEntry.unity`
- `Assets/Scenes/CoachingChoose.unity`
- `Assets/Scenes/DeadBug.unity`
- `Assets/Scenes/PlayBack.unity`
- `Assets/Scenes/maleDeadBug 1.unity`
- `Assets/Scenes/testPractice.unity`

### Assets/Scripts（3）

- `Assets/Scripts/CoachModeEntryController.cs`
- `Assets/Scripts/CoachingActionSelectionController.cs`
- `Assets/Scripts/GamingTrackerHapticsAdapter.cs`

### Assets/skel（8）

- `Assets/skel/Scripts/Editor/LumbarSeamAlignerEditor.cs`
- `Assets/skel/Scripts/Editor/SkelFemaleWalkSimulatorEditor.cs`
- `Assets/skel/Scripts/Editor/SkinnedMeshLoosePartSeparatorEditor.cs`
- `Assets/skel/Scripts/Editor/SpineRotationColorVisualizerEditor.cs`
- `Assets/skel/Scripts/LumbarSeamAligner.cs`
- `Assets/skel/Scripts/SkelFemaleWalkSimulator.cs`
- `Assets/skel/Scripts/SkinnedMeshLoosePartSeparator.cs`
- `Assets/skel/Scripts/SpineRotationColorVisualizer.cs`

### Assets/uOSC（17）

- `Assets/uOSC/Editor/EditorUtil.cs`
- `Assets/uOSC/Editor/uOSC.Editor.asmdef`
- `Assets/uOSC/Editor/uOscClientEditor.cs`
- `Assets/uOSC/Editor/uOscServerEditor.cs`
- `Assets/uOSC/Samples/Blob/Blob.unity`
- `Assets/uOSC/Samples/Blob/ClientBlobTest.cs`
- `Assets/uOSC/Samples/Blob/ServerBlobTest.cs`
- `Assets/uOSC/Samples/Blob/uOSC Blob.mat`
- `Assets/uOSC/Samples/Blob/uOSC Sample Texture.png`
- `Assets/uOSC/Samples/Bundle/Bundle.unity`
- `Assets/uOSC/Samples/Bundle/ClientBundleTest.cs`
- `Assets/uOSC/Samples/Client to Server/Client to Server.unity`
- `Assets/uOSC/Samples/Client to Server/Client.unity`
- `Assets/uOSC/Samples/Client to Server/ClientTest.cs`
- `Assets/uOSC/Samples/Client to Server/Server.unity`
- `Assets/uOSC/Samples/Client to Server/ServerTest.cs`
- `Assets/uOSC/package.json`

