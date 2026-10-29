左侧桥游戏（2026-10-29）

场景：
Assets/Scenes/SideBridge_RingBridge_Dynamic.unity
Assets/Scenes/SideBridge_RingBridge_Hold30.unity

两者沿用臀桥新评分场景的星空鼓面、金色保持进度光条、教练窗口、开始/倒计时/暂停/结束流程。
按用户确认，仅使用原始左侧支撑，不镜像手脚，也不自动切到右侧。

录制来源：桌面 腰椎第一次记录/腰椎第一次记录/5_侧桥/1/5_v1.motion.json。
1590 个原始 HumanPose 帧，30 fps，源文件 SHA-256：
ddc8a7bef2c386674c3b4876277e739c77a4e330d448fa56a5068ea59dc6f73a
SideBridge.coachrecording 为原始 motion.json 的逐字节副本。
直接导入 HumanPose，避免 FBX 中间腕/踝骨旋转丢失；保留全部肌肉通道和原始身体旋转。
原始文件仅有 start/end，现使用原始帧 330–1470；排除开头不完整的一次和末尾退出侧卧的动作。

动态阶段（原始帧，抬起起点 / 保持起点 / 放下起点 / 放下终点）：
第 1 次：435 / 480 / 695 / 734
第 2 次：880 / 950 / 1085 / 1190
第 3 次：1215 / 1259 / 1410 / 1459
动态版按上述实际录制段落的速度运行。处方次数不足或超过三次时，精确截取或循环这三个录制循环。
保留同一录制内部的低位调整间隔；第三次接回第一次时，用 0.5 秒 HumanPose 插值衔接，避免手脚跳位。
保持版先到达该循环的实际最高帧，然后完整冻结 30 秒。放下时以 0.15 秒姿态过渡接回原始下降段，
不把录制中最高点之后的原始保持平台再播放一遍，因此不会在 30 秒后额外等待数秒才下降。
三阶段、教练主模型、右侧预览、进度光条共享同一时钟；暂停不消耗保持时间。

臀桥评分审查与侧桥调整：
1. 臀桥的胸部/双足支撑平面在侧桥双脚重叠时可能退化，且双侧对称项不适合侧桥的天然不对称。
2. 侧桥改测骨盆相对左肘—左踝支撑线的有符号高度，再除以肩部至脚部的身体跨度。
   低位/高位标定值取三个录制循环的均值；数值不依赖平移、等比缩放或水平朝向。
3. 质量权重：躯干对齐 45%，左侧腿形与膝角 25%，左支撑臂及肩肘关系 20%，肩髋叠放 10%。
   右侧自由手臂不计入，不沿用臀桥的左右对称惩罚。
4. 独立检查左侧支撑和近水平躯干。相反支撑侧、直立/仰卧、缺失骨骼不计分。
5. 进入保持位的高度进度门槛：简单 65%、标准 75%、进阶 85%；
   退出门槛：48% / 58% / 68%；返回低位：22% / 23% / 25%。
   进入质量：60 / 70 / 80；有效保持质量：55 / 65 / 75。
6. 延用 0–100 连续保持评分：入位分 85 / 80 / 75，有效保持 3 / 5 / 7 秒达到 100；
   30 秒只规定教练保持时长，不把满分计时改成 30 秒。低质量冻结累计，断流后实时分清零。
7. HIT 也改用侧桥高度与当时教练姿态质量，且必须已校准、连接正常、最近 0.5 秒收到新帧。
   沿用仅 HIT 腰部震动。单纯教练播放/暂停/保持奖金不触发震动。
8. 原有臀桥的评分阈值、四循环动画和默认 30 秒时间轴路径保持不变；侧桥是显式配置。

输入与处方：
沿用现有 VMC/OSC 接入。不新增网络接口或原始 IMU 协议。
默认演示处方为 1 组 × 3 次，菜单标明“演示处方”；难度只改变评分门槛，不改变次数。
场景沿用当前臀桥的流程预览开关。真实计分时，在 SupineGameCalibration 关闭
skipCalibrationForPreview，按左侧支撑提示完成校准并连接动作数据；预览模式不会伪造患者分数。

已解析剂量入口（主线程）：
FindObjectOfType<HipThrustPrescriptionDose>().SetDose(sets, reps, out error)
sets 1–20，reps 1–100，单独场景执行 sets × reps；倒计时或训练中禁止换剂量。

动作串入口（组数由 GameWorkoutSession 执行，不重复乘入单次场景）：
{"actions":[{"action":"sidebridge","difficulty":"standard","sets":2,"reps":3},
            {"action":"sidebridge_hold30","difficulty":"simple","sets":1,"reps":2}]}
调用 GameWorkoutSession.Instance.SetPlanJson(json, out error)，然后 StartPlan(out error)。

直接 JSON 剂量适配：SetPrescriptionJson(json, itemId, out error) 使用 V2 items 结构，
侧桥场景明确配置 prescriptionActionId="side_bridge"。这只是本地侧桥适配键，
尚未确认医生端侧桥的正式编号；调用方应将医生端侧桥条目映射为该键，或直接调用 SetDose。
录制包中的 actionId="5" 是这次录制目录编号，不能当医生端编号：医生端已有编号 5 表示臀桥。
未修改另一个 MobileBridge 项目的既有三动作映射，也不将侧桥误识别为臀桥。

配乐：Enchanted Tiki 86 — cynicmusic / The Cynic Project，CC0。
来源及处理见 Assets/Audio/GameMusic/CREDITS.txt、output/sidebridge/music.json。

验证：Tools > Ring Bridge > Verify Left Side Bridge Games。
覆盖原始 1141 帧全身/手脚朝向、三阶段、30 秒实时时钟、精确次数、评分、失联/陈旧输入、
教练与预览同步、开始/暂停/返回、两组各三次处方执行。原臀桥新评分两场景另行回归。
评分验证使用合成输入和实际 Unity 骨骼；未使用真实穿戴设备或临床数据。
这里的支撑检查是骨骼几何近似，不是压力传感器检测，也不能确定真实肘/脚是否接触地面。
