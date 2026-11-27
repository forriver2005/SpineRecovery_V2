# 模拟医生处方保持时长验证

日期：2026-11-27。Unity 2022.3.62f3c1，独立项目副本。游戏运行时代码本轮未修改。

**结论：模拟D01 V2 JSON送到游戏公开入口后，保持时长、圆环进度、倒计时、教练冻结和最终目标同步生效。真实医生UI/后端/手机的端到端下发尚未接通或验证，见 INTEGRATION_AUDIT.md。**

使用 fixtures/ 中的12份模拟医生处方，直接调用 `HipThrustPrescriptionDose.SetPrescriptionJson`，未直接调用 SetDose 或改时长字段。臀桥、左侧桥、右侧桥均覆盖20秒、45秒、省略字段、显式0。

| JSON holdSeconds | 实际每次目标 | 保持经过10秒后的界面 | 圆环保持段进度 |
| --- | --- | --- | --- |
| 20 | 20秒 | 剩余10.0秒 | 50% |
| 45 | 45秒 | 剩余35.0秒 | 22.22% |
| 省略 | 30秒 | 剩余20.0秒 | 33.33% |
| 0 | 30秒 | 剩余20.0秒 | 33.33% |

三个场景均检查0/5/10/一半/末尾时间点，并验证实际美术的shader进度、界面文本、教练与预览骨骼静止、到时进入放下以及最终分母。默认测试在45秒处方之后执行，证明新处方缺省不会沿用上一份的45秒。

另外，臀桥用真实开始按钮、5秒准备倒计时和未加速的Unity Update运行20秒、45秒、默认30秒，整个保持过程未定位或缩放时间。观测结果：

- PASS LIVE 20s: automatic Start/countdown, hold observed 19.998s (target 20.0s), no seek or time scaling, normal completion.
- PASS LIVE 45s: automatic Start/countdown, hold observed 44.999s (target 45.0s), no seek or time scaling, normal completion.
- PASS LIVE default: automatic Start/countdown, hold observed 30.000s (target 30.0s), no seek or time scaling, normal completion.

实际20秒保持中暂停/恢复通过：倒计时和圆环冻结；训练中和准备倒计时中再次提交JSON被拒绝，当前时长保留。

边界：校准和动捕输入使用编辑器合成夹具，不是真人设备实测。实时测试覆盖臀桥三个时长；左右侧桥执行的是JSON输入后确定时间点的场景验证。本轮未访问或修改真实医生端数据库，也未模拟网络和Android Intent投递。现有手机启动模型没有holdSeconds，亦未调用本次测试的处方入口。

- `verification.txt`：完整执行日志。
- `countdown.csv`：每个采样点的实际界面文字和圆环进度。
- `countdown-comparison.png`：相同经过10秒时的20秒/45秒处方画面对比。
- `fixtures/`：本次输入JSON。
- 复验菜单：`Tools > Ring Bridge > Simulate Doctor Hold Prescriptions`（确保项目根目录有上述fixtures目录）。
