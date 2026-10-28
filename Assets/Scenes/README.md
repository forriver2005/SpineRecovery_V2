# 正式游戏场景

| 游戏 | 场景文件 | Build Index |
| --- | --- | --- |
| 鸟狗式 | `BirdDog_RingSkinC_Recorded.unity` | 0 |
| 死虫式 | `DeadBug_RingSkinC_Recorded.unity` | 1 |
| 臀桥 | `HipThrust_RingBridge_Recorded.unity` | 2 |

当前只保留以上三个场景，全部使用新录制动画及右侧教练窗口。鸟狗和死虫使用 RingSkinC 视觉，全部难度仅有伸展、收回；臀桥使用敲鼓素材，保留抬起、保持、放下。教练与视觉跟随同一播放进度，暂停时同步停止。

直接打开场景并 Play，按校准、选择难度、开始训练流程操作。动作串统一路由到这三个场景。鸟狗左腿配右手、右腿配左手；臀桥 HIT 仅腰部震动。

原始录制来源、骨骼处理及验证入口见项目根目录 `README.md`。素材文件夹中的集成说明记录历史接入过程，当前场景列表以此文件和 Build Settings 为准。
