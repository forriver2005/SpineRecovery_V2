# 教练模式本地模拟驱动

这是验收工具，不是实物设备，不参与正式打包。仅向本机 `127.0.0.1:39539` 发送 VMC，并在 `127.0.0.1:18761` 提供模拟 tracker 状态和 motor HTTP 接收器。

## 使用

1. Unity 打开正式 Practice 场景。在菜单 `Spine Recovery > Export Coach Simulator Motions` 从当前用户 Humanoid 导出六动作数据（已导出在 data 下）。
2. 运行 `Build.ps1` 可从 Simulator.cs 重建 CoachSimulator.exe，使用 Windows 自带 .NET Framework 编译器，无在线依赖。
3. 启动 CoachSimulator.exe。可传第一个参数更改 UDP 端口。默认控制文件在 EXE 同目录 control.json。
4. Unity Editor 验收前创建项目内 `.codex/enable-coach-simulator` 空文件。进入 Play 后 Editor 工具只为本次运行连接模拟状态地址；不保存场景。没有该文件时不接模拟状态。
5. 从正式开始按钮运行课程。编辑 control.json 可切换输入，文件变化自动读取。
6. 完成后把 `stop` 改为 true，删除 `.codex/enable-coach-simulator`，退出 Play。再次启动前恢复 stop=false。

## 控制项

- action: deadbug / birddog / hipthrust / catcamel / sidebridge / curlup。
- time: -1 为循环播放；非负数为录制秒数定格。不同录制与正式课程不是自动同步，不能把错相位误判为判定缺陷。
- yaw: 绕竖直轴转向的度数，作用于发送的 Hips 输入，供 A/B 显示对齐验证。
- stream: false 停止 VMC 发包，用于丢帧/未知状态。
- connected/status: 模拟设备状态；status=OK 才有效，可改 ERROR。此状态不会修改动作判定阈值。
- motorFail: true 时模拟 motor 返回 HTTP 503，播放应仍继续。
- stop: true 正常退出程序。

`events.log` 明确标注 SIMULATED MOTOR，并记录请求抵达时间。该时间只能证明本地 HTTP 接收情况，不能证明实体起振同步。当前死虫使用 clip_29，鸟狗使用独立腿侧映射派生素材 BirdDogRecordedSides（原文件不变），其余四项使用正式动作库 JSON 录制；经当前用户 Humanoid 重定向后导出30Hz骨骼局部变换。发送端对相位切换的四元数做连续过渡（每包最多约6度），避免瞬时跳变造成模拟假异常。运行时经过现有 EVMC4U 接收器、关节过滤和正式正向判定，不直接设置绿显、评分或计次，也不修改正式动画/语音时长。

## 六场景顺序运行

Editor-only 的 CoachSixSimulationRun 在显式存在 `.codex/run-six-simulation` 时运行；可用 `.codex/resume-six-index` 写入0至5选择起始动作。当前按 DeadBugRecordedPractice、BirdDogRecordedPractice、臀桥、卧位猫式、侧桥、卷腹顺序打开正式场景（旧死虫/鸟狗课程保留，不受新录制接管）并调用同一 StartPractice 入口，正常播放讲解、示范、倒数和默认4次练习。

输入按当前相位切换到源录制对应准备/目标/回位姿态；第2次加70度转向，第3次置设备无效，第4次恢复设备并令motor返回503。正式播放节奏完全不受该工具控制。日志在 six-run.log；INTERRUPTED/DISCARD 不能计为完成。切换前脏场景只另存副本到 `.codex/scene-backups`，不会覆盖原场景。

这是输入与HTTP的模拟验收，不能证明真实IMU精度、蓝牙/网络延迟、实体起振或患者听感。具体通过/未通过结果以验收报告为准。

开发复核附加标记：`.codex/sim-only-current` 在当前动作完成后退出顺序运行；`.codex/sim-visual-review` 只复核臀桥、侧桥、卷腹，其中侧桥调用原选择处理器选择B，其余默认A。结束后都应删除，避免影响下次标准六动作顺序运行。

新录制复核：`.codex/sim-recorded-only` 在新鸟狗完成后停止（从index0可顺序验证两个新场景）。`.codex/pause-on-diagonal-green` 仅在真实患者高亮组件同时显示右臂/左腿时暂停Editor，便于保存截图；不是强制绿显。完成后删除标记。

`birddog_positive.vmc` 是额外的独立合成输入：把活动手臂沿躯干方向伸展，用于验证旧正向判定及同组HTTP。只有 `.codex/sim-positive-input` 标记才会使用；它不代表录制素材验收，普通 `birddog.vmc` 未做该动作修改。

旧课程独立复核：菜单 Export Original Coach Simulator Targets 将旧FBX每个正式分段的末端姿势导出为 original_*.vmc（单帧目标，发送端仍平滑过渡）。`.codex/sim-original-courses` 选择旧DeadBugPractice/BirdDogPractice，并按实际clipStateName发送对应目标。默认六动作输入及新场景不变。使用后删除标记。此输入验证末端姿势与反馈，不代表患者完整运动轨迹。
