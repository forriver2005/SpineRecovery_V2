# 正式游戏场景

直接打开对应场景，按原来的校准、选择难度、开始训练流程运行。

| 游戏 | 视觉 | 场景文件 | Build Index |
| --- | --- | --- | --- |
| 死虫式 | 旧版 | `DeadBug_Classic.unity` | 0 |
| 鸟狗式 | 旧版 | `BirdDog_Classic.unity` | 1 |
| 臀桥 | 原版 | `HipThrust.unity` | 2 |
| 鸟狗式 | RingSkinC 新版 | `BirdDog_RingSkinC.unity` | 3 |
| 死虫式 | RingSkinC 新版 | `DeadBug_RingSkinC.unity` | 4 |

Build Settings 启用上述五个游戏。测试场景保留在 `Tests/` 中，内容和 GUID 不变，默认不参与构建。

旧名称对应关系：`SpineV2Minimal` → `DeadBug_Classic`，`BirdDogMinimal` → `BirdDog_Classic`，`HipThrustMinimal` → `HipThrust`，`DeadBugRingSkinC` → `DeadBug_RingSkinC`。
重命名保留场景原始字节、meta 文件和 GUID；旧版场景内部对象、动画、资源引用和逻辑没有修改。外部工具若按场景名称打开，使用上表中的新名称；原三个游戏的构建索引保持不变。

新鸟狗式复制自鸟狗式旧版，保留 `birddog_start`、四组 `birddog_*_up/down`、`birddog_end`、原动作判定与训练流程，只接入环、音符、拖尾及命中效果的新视觉。
两款新版均使用 `View Scale = 0.8`，背景单独铺满视野。设置与验证说明见 `Assets/RingSkinC/DEADBUG_INTEGRATION.md`。
