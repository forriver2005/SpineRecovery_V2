# 两个臀桥新评分场景

两个臀桥变体使用现有敲鼓视觉、录制动画和时间轴，分别提供原节奏新评分与最高点按处方保持（默认30秒）。原旧版场景已从构建列表移除；通用动作键 `hipthrust` 与 `hipthrust_newscore` 现在都路由至 `HipThrust_RingBridge_NewScoring`。

| 场景 | 行为 |
| --- | --- |
| `Assets/Scenes/HipThrust_RingBridge_NewScoring.unity` | 只替换计分。保持原始录制节奏及简单/标准/进阶的 4/8/12 次。 |
| `Assets/Scenes/HipThrust_RingBridge_Hold30.unity` | 录制动作抬到实际最高帧，教练和预览骨骼静止处方指定的时长（默认30秒），再继续放下。按处方精确执行次数，不向 4 的倍数取整。 |

两个场景已追加至 Build Settings。Play 后仍需在起始界面校准、点击开始。真实评分需要已校准的人体骨骼和实时 VMC 数据；跳过校准只用于流程预览，不产生模拟患者得分。

## 新评分来源与规则

来源：桌面 `臀桥训练场景交付包/臀桥训练场景交付包` 的说明文档及 `HipThrustPractice_Minimal.unitypackage` 内 `HipThrustHoldScorer.cs`。包 SHA-256：`0da1b2928d6e3554a04109268c3cd8e9cf736abae3c3182a627ec15fb2ff4d94`。

移植的是文档指定的 **连续 A/B 保持计分**；没有引入包里的自主练习场景、启动器、旧的分组综合分、骨骼约束器或 VMC 滤波设置。实际实现为 `HipThrustHoldRule`、`HipThrustHoldGeometry` 和 `HipThrustRuleScoring`。

骨盆到胸部/双足支撑面的垂直距离除以身体跨度，映射教练 A/B 端点。质量采用髋角 45%、大腿/小腿方向 30%、膝角 15%、左右对称 10%。端点来自当前实际录制动作，不依赖交付包中另一个 Animator 的状态名。评分不会改写手脚或其他骨骼的旋转。

| 难度 | 开始抬起 | 进入 B | 退出 B | 严格返回 A | 进入质量 | 有效质量 | 起评分 | 有效保持至满分 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 简单 | .08 | .20 | .14 | .10 | 35 | 30 | 85 | 3 秒 |
| 标准 | .15 | .40 | .28 | .15 | 50 | 45 | 80 | 5 秒 |
| 进阶 | .20 | .55 | .40 | .18 | 65 | 60 | 75 | 7 秒 |

进入 B 连续确认 .30 秒，退出 B .12 秒，回到 A .08 秒；放下 .20 秒后支持文档中的宽松返回条件。B 内质量不足时停止累加，B 外实时分为 0，保留上轮最高分（HUD 的 LAST）。数据缺失或超过 .5 秒无新帧时不累加；暂停冻结训练和计分。

保持版的 `holdSeconds` 表示**每次动作的保持秒数**，默认30秒。它同时定义教练在最高点冻结的时间、圆环保持进度、剩余秒数、既有评分器的时间目标，以及最终保持达标率的分母。姿势几何、合格阈值、HIT 条件不变。示范次数与患者实际合格次数仍分别统计。动态版继续使用原录制节奏。

## 处方接入

第二个场景附带明确标注的演示处方 `Assets/RingBridge/HipThrustHold30_PreviewPrescription.json`（1 组 × 4 次）。难度按钮只影响评分阈值，不改变处方次数。真实接入通过主线程调用，失败返回错误且保留原有效剂量；训练中/倒计时中不允许换处方。

### 已获取的医生 D01 V2 处方

```csharp
var dose = FindObjectOfType<HipThrustPrescriptionDose>();
bool ok = dose.SetPrescriptionJson(prescriptionJson, itemId: 123, out string error);
```

读取 `prescriptionContractVersion=2` 和 `items[]` 中指定 `itemId` 的 `actionId="5"`、`sets`、`reps`、`holdSeconds`。多个臀桥项目必须明确指定 itemId；其他动作不能误当作臀桥。组数限制 1–20，次数限制 1–100。单独场景按 `sets × reps` 完整播放，菜单明确显示组数和次数。动作串中的每组（包括不同动作之间和同一动作的下一组）固定休息30秒；单独游玩不追加组间休息。`holdSeconds` 省略或为0时恢复默认30秒；有效值为1–600秒（支持小数）。非法值拒绝整次更新，保留原有效处方。该范围为软件参数限制。此入口消费已获取的处方，不新增网络请求或资源下载。

### 现有动作串执行器

支持臀桥和其他已接入游戏的动作键；`hipthrust` 与 `hipthrust_newscore` 都指向新评分场景：

```json
{
  "actions": [
    { "action": "hipthrust_newscore", "difficulty": "simple", "sets": 1 },
    { "action": "hipthrust_hold30", "difficulty": "standard", "sets": 2, "reps": 3, "holdSeconds": 20 }
  ]
}
```

调用 `GameWorkoutSession.Instance.SetPlanJson(json, out error)`，然后 `StartPlan(out error)`。执行器每组传入精确 reps 和 holdSeconds，组数由执行器控制，不重复相乘；缺少合法 reps 会拒绝运行。已有手机分支的通用 `hipthrust` 导出不会自动选成 30 秒变体，调用方选择新变体时应显式设置上述动作键。

## 验证

`Tools → Ring Bridge → Verify New Scoring and 30 Second Scenes`：覆盖三个难度的数值阈值与确认时间、低质量/断流、几何平移旋转和等比缩放、精确次数、所有峰值帧、30 秒计时、手脚等骨骼静止、教练/预览/鼓面同步、开始/暂停/恢复/结束，以及实际执行器 2 组 × 3 次。

报告和截图位于 `output/hipthrust-new-rules/`。验证使用合成输入和实际 Unity 场景动画，没有使用真人或实体 IMU 来验证动作识别准确率。


## 可配置保持时长（2026-11-27）

适用保持类动作：`hipthrust_hold30`、`sidebridge_hold30`、`rightsidebridge_hold30`。旧动作键和场景名中的 `Hold30` 为兼容入口，不再代表固定时长。动态/往复动作不能使用该字段改变录制节奏；传入非0值会返回错误。

D01 V2 的每个 `items[]` 项目和动作串的每个 `actions[]` 项目均可设置 `holdSeconds`：

```json
{
  "prescriptionContractVersion": 2,
  "items": [
    { "itemId": 1, "actionId": "5", "sets": 2, "reps": 3, "holdSeconds": 20 }
  ]
}
```

D01 左侧桥标识为 `side_bridge`，右侧桥为 `right_side_bridge`，由场景的 `prescriptionActionId` 匹配。每次20秒、每组3次对应每组目标60秒。新处方未填写时长时恢复30秒，不继承上一份处方的20秒或45秒。

直接调用可用 `dose.SetDose(sets, reps, holdSeconds, out error)`；原三参数接口仍可用并恢复默认30秒。训练/倒计时中禁止更换。保持时间改变后，抬起和放下仍按原速播放，保持段的圆环扫过速度与教练冻结时间同步改变；动作串组间休息仍为30秒。

本仓库提供处方消费接口，不包含医生端时长编辑界面或新的网络传输。医生端需在上述入口已有的 JSON 中传入字段。

复验入口：`Tools > Ring Bridge > Verify Prescribed Hold Duration`；报告与截图见 `output/prescribed-hold/`。
