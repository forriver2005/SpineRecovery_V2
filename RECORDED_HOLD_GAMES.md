# 圆环保持版与处方（2026-11-27）

新增四个独立场景，统一复用臀桥保持版的圆环、准备菜单、5秒倒计时、教练窗口、暂停/返回、结果报告和动作串执行器：

| 动作 | 场景 | 动作串键 | D01 actionId |
| --- | --- | --- | --- |
| 鸟狗保持 | Assets/Scenes/BirdDog_RingBridge_Hold30.unity | birddog_hold30 | 8 |
| 死虫保持 | Assets/Scenes/DeadBug_RingBridge_Hold30.unity | deadbug_hold30 | 7 |
| 卷腹保持 | Assets/Scenes/Crunch_RingBridge_Hold30.unity | crunch_hold30 | 6 |
| 卧位猫式保持 | Assets/Scenes/LyingCat_RingBridge_Hold30.unity | lyingcat_hold30（别名lying_cat_hold30） | 1 |

每次在该动作的录制目标姿势冻结指定时间，再按原节奏返回。鸟狗/死虫保留原来的对角肢体及左右交替顺序。卷腹/猫式使用原有录制的同一动作区间；保持时固定目标帧。

## 处方

`holdSeconds`单位是每次秒数，省略或0默认30秒，支持1–600秒。`reps`为每组次数，保持类必须给出1–100；`sets`由动作串执行器推进。组间休息固定30秒。

```json
{
  "actions": [
    {"action":"birddog_hold30","difficulty":"standard","sets":2,"reps":4,"holdSeconds":20},
    {"action":"deadbug","difficulty":"simple","sets":1,"reps":5},
    {"action":"crunch_hold30","difficulty":"standard","sets":1,"reps":3,"holdSeconds":45},
    {"action":"lyingcat_hold30","difficulty":"simple","sets":1,"reps":3}
  ]
}
```

主线程调用 `GameWorkoutSession.Instance.SetPlanJson(json,out error)` 和 `StartPlan(out error)`。原鸟狗和死虫往复版现在也支持精确reps，不再向整轮4次取整；省略/0保持原难度默认次数。

保持场景的单项D01入口与臀桥相同：`HipThrustPrescriptionDose.SetPrescriptionJson(json,itemId,out error)`。选择对应场景和表中actionId。单项按sets×reps播放；动作串每次只传当前组reps。非法更新保留已有处方；训练/倒计时期间不能更换。

## 总分与准入

最终使用 `FinalTrainingScore.Mode.TimedHold`：只在处方保持窗口中累计已确认合格秒数，除以全部计划保持秒数。累计、最长连续段和连续完成次数分别记录；缺测/暂停不补时；数据覆盖不足显示“数据不足”。

鸟狗和死虫读取现有RhythmMovementRange端点几何、方向、正确肢体与难度阈值。卷腹和猫式读取原动作评分器已认可的计时增量，并在静态保持窗口要求持续满足该评分器原有的进入目标幅度，防止把回落容差误当作到位保持。原往复评分器状态机、阈值和80/5/15总分不变。

正式启动要求对应准备姿势校准和新鲜的患者骨架数据。新四场景使用RecordedHoldPreparation核对自己的录制起始姿势，再走原稳定校准协程。单项显式流程预览仍按原规则运行；动作串不接受预览旁路。新增动作的身体部位提示对应伸展/卷起/拱背及回位。

## 配乐与导航

动作选择页提供16个正式训练场景。新增鸟狗/死虫保持沿用各自动作音乐；卷腹/猫式增加Synthwave 4k和LifeWave 2k，两个动作的往复与保持版分别共用。曲线臀桥也接入音乐。许可与来源见 `Assets/Audio/GameMusic/CREDITS.txt`。

## 验证入口

- `Tools > Ring Bridge > Verify Four Recorded Hold Games`：D01、20/45/默认30秒、正确/静止/半程/错误动作/缺测、开始倒计时/暂停/结算/返回/取消。
- `Tools > Training > Verify All Prescriptions and Workouts`：全部16游戏的JSON剂量和实际训练流程、17组混合动作串、真实30秒休息、重复完成去重与音乐生命周期。
- `Tools > Training > Install Complete Game Music`：幂等登记场景音乐。
- `Tools > Ring Bridge > Create Four Recorded Hold Games`：仅创建不存在的新场景，已有场景不会覆盖。

这些验证使用录制和合成动捕，不能代替真人穿戴与医生端真实下发联调。现有手机启动协议仍未接入保持时长字段，具体边界见 `output/doctor-prescription-hold/INTEGRATION_AUDIT.md`。

全流程与本机UDP验收结果见 `output/all-game-prescriptions/VALIDATION.md`。
