# 医生处方到游戏入口的接入核对

核对日期：2026-11-27；项目：SpineRecovery_V2 / Gaming 工作区。

## 已确认支持的边界

`HipThrustPrescriptionDose.SetPrescriptionJson(json, itemId, out error)` 接受 D01 V2 `items[]` 中的 `holdSeconds`。它将时长传给保持时间轴、圆环显示、教练和结算目标。已在三个保持场景测试。

另一入口 `GameWorkoutSession.SetPlanJson(json, out error)` 接受动作串 `actions[]` 中的 `holdSeconds`；这是单独的动作串结构，不会自动把任意D01 JSON转成动作串。

## 当前尚未接通/不能确认的部分

- 当前项目未包含医生端处方编辑页面和医生端后端，不能据此证明医生端UI已有可编辑保持时长字段、保存接口或实际下发行为。
- 现有 `SpineFlowTrainingSession` 的 Android Intent 启动模型 `PlannedAction` 只有 actionId、actionName、arTaskType、level、sets、reps、motionPackageUri，没有 holdSeconds。
- 现有 Android 启动接收器通过 `AcceptMobileLaunch` 解析 `trainingPlan.recommendedActions` 并路由到练习场景，没有调用 `HipThrustPrescriptionDose.SetPrescriptionJson` 或 `GameWorkoutSession.SetPlanJson`。
- 当前非Editor源代码里，`SetPrescriptionJson` 的内部调用来自演示处方Awake；未发现把医生/手机网络载荷接到此处的调用。公开方法可供外部集成，但它本身不是自动接通的网络接口。

因此，已经验证的是 **模拟医生D01处方JSON → Unity公开消费接口 → 保持游戏倒计时**。不能将这次结果表述为真实医生端、后台、手机到Unity的端到端联调已经通过。

若要让医生端操作实际生效，需在真实医生端/手机端的数据保存及传输链路携带 holdSeconds，并在Unity集成入口调用保持处方消费接口或明确映射到保持动作串。旧的通用 hipthrust 动作键仍对应动态版，需要选用保持场景/保持动作键。
