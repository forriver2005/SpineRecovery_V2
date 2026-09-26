# 手机端到原 AR 的实际场景通路（2026-09-26）

本清单依据当前 SpineFlowMobile 的推荐动作和启动 Intent、原 Spine-Recovery 的目标场景校验/按钮事件、V2 的路由与 Build Settings。只统计现行普通用户推荐训练；联调入口单列。此轮为代码静态核对，未运行真机训练。

## 普通推荐训练

手机 `UserTagActionMapper.recommend` 只推荐死虫式或鸟狗式；`TrainingStartPayload.trainingActionsFor` 为同一动作生成 coach/game 两项；`ArSessionTokenContract.applyMobileSceneRouting` 从 `recommendedActions` 写 `targetScene`，`LegacyArAppLauncher` 通过 Intent 直接交给训练端。原 AR `SpineFlowTrainingSession.IsSupportedPracticeScene` 仅允许四个男女 Practice 场景及 `testPractice`，不允许 `CoachingChoose`。男性鸟狗式由 AR 的 `ResolveSceneForGender` 选择男性场景。

| 手机推荐与性别 | 原 AR 首场景 | 原 AR 后续可用入口 | V2 对应场景 |
| --- | --- | --- | --- |
| 女性死虫式 | `DeadBugPractice` | `DeadBugGaming`、`PlayBack`、返回手机 | `DeadBugPractice`、`SpineV2Minimal`、`PlayBack` |
| 男性死虫式 | `maleDeadBugPractice` | 共用 `DeadBugGaming`、`PlayBack`、返回手机 | `maleDeadBugPractice`、`SpineV2Minimal`、`PlayBack` |
| 女性鸟狗式 | `BirdDogPractice` | `BirdDogGaming`、`PlayBack`、返回手机 | `BirdDogPractice`、`BirdDogMinimal`、`PlayBack` |
| 男性鸟狗式 | `maleBirdDogPractice` | 共用 `BirdDogGaming`、`PlayBack`、返回手机 | `maleBirdDogPractice`、`BirdDogMinimal`、`PlayBack` |

教练完成菜单由 `DeadBugStartMenuController.ConfigurePracticeActionMenu` 限定为 Start、Game、PlayBack、Home；Game 进入同动作游戏，PlayBack 进入回放，Home 经 `SpineFlowTrainingSession.TryReturnToMobile` 向手机回传结果。回放页还可返回训练/游戏或手机。场景名换成 V2 对应实现不等于训练/评分功能已逐项验收。

**因此现行普通用户通路所需的场景集合是四个 Practice、两个对应游戏和 PlayBack，共七个。它们现在都列在 V2 Build Settings；回放虽已有场景和资源，完整录制后的播放仍需实际数据验证。** 迁移时还要保留这些场景实际引用的训练控制、模型、评分、语音、录制和结果回传资源，不能仅复制七个 `.unity` 文件。

## 单独处理的联调入口

手机只有 `MotionPackageProbe` 哨兵标签会把 `targetScene` 设为 `testPractice`；原 AR 该场景的 Game 按钮进入 `testGaming`。这是可触发的动作包联调通路，但不是当前普通用户推荐训练。若保留这项联调能力，需要连 `testPractice`/游戏去向及数据包行为一起设计和迁移；不能把它列为上述七场景普通训练闭环的缺口。当前 V2 仍接受 `testPractice` 名称，但 Build Settings 没有该场景，因此此哨兵通路尚未在 V2 闭合。

## 不按旧工程或 stash 全量迁移

- `CoachingChoose`、`CoachModeEntry`、旧动作选择控制器/图片：手机直接指定目标场景，普通启动不经过选择页。原 Practice 场景中某些 `LoadCoachingChooseScene` 序列化引用不代表有效完成菜单入口。
- `DeadBug`、`maleDeadBug 1`：旧演示/旧导航场景；不是普通手机目标，也不在四个教练场景的有效完成菜单中。
- 原 `BirdDogGaming` 里有一个默认隐藏的 Practice 按钮仍绑定 `LoadBirdDogScene`，而 `BirdDog` 未在原 Build Settings；这是遗留绑定，不应为此迁入一个新场景。V2 对应默认隐藏按钮已改为 `LoadBirdDogPracticeScene`。
- `HipTrustGaming`、`gamingguide`、`testGaming`、V2 的 HipThrust/节奏测试场景：不属于当前死虫式/鸟狗式普通推荐路径；`testGaming` 仅随上述联调通路评估。
- EVMC4U/uOSC 教程、编辑器工具、旧批量迁移脚本和整包 ProjectSettings：不能仅因它们存在于 stash 就恢复；按七个场景和运行时依赖逐项取用。

## 依据文件

- 手机：`D:/Code/SpineFlowMobile/app/src/main/java/com/metaspine/mobile/data/UserTagActionMapper.kt`、`TrainingStartPayload.kt`、`ar/session/ArSessionTokenContract.kt`、`ar/legacy/LegacyArAppLauncher.kt`。
- 原 AR：`D:/Code/Spine-Recovery/Assets/Scripts/SpineFlowTrainingSession.cs`、`DeadBugStartMenuController.cs`、`SceneSwitcher.cs`、四个 Practice 场景、`testPractice.unity`、`ProjectSettings/EditorBuildSettings.asset`。
- V2：`Assets/Scripts/SpineFlowTrainingSession.cs`、`SceneSwitcher.cs`、`ProjectSettings/EditorBuildSettings.asset`。
