# 模块 B：正向反馈与触觉交付

日期：2026-10-29。状态：代码已接入并离线编译，Unity 实际运行和设备同步验收未完成；不能称作物理同步已实现。

## 入口与规则映射

正式死虫、鸟狗分别使用 DeadBugPractice、BirdDogPractice 的 10 段 Animator，规则由模块 A 的 `CurrentPoseGuidanceRule` 在 segment 事件时取得，核心动作的 activeRegions 依次为 6、9、6、9；播放和次数仍由 SegmentedCoachController 驱动。绿显仅在正式观察、核心动作、新鲜 VMC 且无硬无效姿态时，显示 active 目标的有效正向判据；无错误候选不作为正确判据。死虫和鸟狗还要求目标近端骨骼 VMC 包新鲜；鸟狗额外要求弯曲几何可用和远端骨骼 VMC 包新鲜。没有修改原诊断或阈值。

| 动作 | 目标及正向依据 | tracker |
|---|---|---|
| 死虫 | 当前目标臂和对侧腿，TargetedExercisePoseGuidance 有效且各自 `shouldClear`；未达到清除阈值不绿 | 右臂/左腿：RightUpperArm、LeftThigh；左臂/右腿：LeftUpperArm、RightThigh |
| 鸟狗 | 同侧别；现有鸟狗躯干许可、肢体伸展及弯曲 `shouldClear`，并要求对应弯曲追踪有效 | 同死虫 |
| 臀桥 | torso；HipThrustPoseGuidance 的 `shouldActivate` 为目标高度进入带（其 `shouldClear` 是离开目标，不能当正确） | Abdomen |
| 卧位猫式、侧桥、卷腹 | Profile 当前活动 torso；有效 torso `shouldClear` 加原 progress 0.88–1.12 进入带、原 0.2 秒确认；离开进入带即不绿，退出滞回带不能维持绿色 | Abdomen |

腰背原独立逐 tracker 脉冲与臀桥独立脉冲不再从观察更新调用；统一由所有目标同时绿触发组事件。未知、新鲜度丢失及暂停不清除同一次已发送标记；新 session 或实际 set/repetition 切换时重置。组内任何 tracker 地址缺失时不发部分组；成功启动一组请求后本次不再重复，即使 HTTP 随后失败。触觉与可视和播放分离。没有变更游戏逻辑或数值阈值。禁用新鲜度开关与 Editor 静态骨架不能冒充有效追踪；正式反馈始终要求 VMC 数据新鲜。

## 可执行的代表路径及验证状态

在 Unity 打开 DeadBugPractice，连接真实 VMC 与对应 tracker，启动标准课程，完成讲解、示范和倒数，运行默认 1×4：1、3 次为右臂+左腿，2、4 次为左臂+右腿。让一个目标未到现有 clear 阈值：仅已到位的目标可绿且无组触觉；两目标均达到且有效：两处绿色，尝试同一逻辑事件发送对应两个请求一次；保持、短暂离开再回来、暂停恢复或追踪短暂失效后恢复均不应在该次重复发送。断开/过期 VMC 时全部不绿不震；四次播放继续。该步骤是运行路径与预期，**不是实测记录**。

`TrackerHaptics.SendPulse` 是每台 tracker 独立 `http://<address>/motor?duration_ms=...`，没有共同预约时间或组命令；代码在同一 Unity 帧发起独立请求，不能保证物理同步。没有可用设备起振传感与实际双设备测试，因此起振差未知，不能以 HTTP 响应差代替，也不能声称同步达标。设备绑定、每部位实际追踪质量、两台物理起振、四次现场运行、触觉失败和暂停恢复仍待实测。cua 本轮复查 Unity 正在运行但窗口列表为空；未取得运行画面。模块 A 所述音频及其他未完成事项仍以其交付为准。

## 检查与改动

- 用 Unity 2022.3.62f3c1 内置 Roslyn 与现有 Assembly-CSharp.rsp 离线编译全部运行时代码，退出码 0；这不证明运行效果。
- 本模块修改 `Assets/Scripts/PoseScorer.cs`、`PoseScorer.LumbarTeaching.cs`、`TrackerHaptics.cs`、`Assets/EVMC4U/ExternalReceiver.cs`。保留工作区已有改动及场景；未编辑场景、音频或游戏实现。
- 尚需实际 Unity PlayMode / VMC 和物理设备验收；由于没有可测物理同步能力，产品所要求的严格同步仍未完成。

## 总控复核后的针对性修补

- 臀桥的 `shouldActivate` 表示目标高度到位，原先被通用候选逻辑误作纠错。现在仅把该有效诊断的 `shouldClear`（高度差超过原退出阈值）作为臀桥纠错候选；未知和进入/退出之间不产生纠错。其他动作候选不变。
- 骨架 Transform 可计算不等于传感器有效。对死虫、鸟狗增加每个目标近端骨骼 VMC 包新鲜检查；鸟狗的弯曲还要求远端骨骼新鲜。VMC 骨骼包可能由 SlimeVR 推算生成，输入流未提供逐 tracker 观测有效性，因此新鲜包不能证明对应实体传感器实际有效，不能声称完全满足逐 tracker 失效时不误绿。绿显不依赖触觉发现服务、地址或马达请求。真实设备与单个 tracker 断连仍待验收。
- 更新后再次用 Unity 自带 Roslyn 和 Assembly-CSharp.rsp 离线编译，退出码 0；没有 Unity PlayMode、语音或设备实测。
