2026-10-30 六游戏安卓测试版交付与验证

范围
暂停菜单与开始菜单加入六个动作选择：鸟狗、死虫、臀桥动态、臀桥30秒、侧桥动态、侧桥30秒。
切换会取消当前处方执行和训练、清理计时与震动状态，进入新场景的开始界面。
暂停同步冻结音乐、教练与节奏计时；应用进入后台也暂停。
测试版强制恢复真实动作校准，避免场景中原有的预览绕过开关造成假成绩。
本次不改变动作质量评分、结算评分或录制动作。六个场景与模型资产未修改。

验证结果
navigation-vmc.txt：六个实际场景在 Play Mode 中完成选择、开始倒计时、暂停/恢复、返回和切换；UDP 实际接收、骨长保持、断流过期通过。
slime-unity.txt：合成姿态 -> 桌面实际 SlimeVR 服务 39540 -> VMC UDP 39539 -> 六个场景的实际用户 Animator；躯干、左右手脚运动与服务输出一致；没有伪造闲置成绩。
user-rig-inspection.txt：六场景均为20个 Humanoid 映射、9个蒙皮网格。初始局部和根相对旋转为零，缩放正确，绑定矩阵闭合误差为0；左右骨骼名称与位置匹配，脚尖朝前。
20个关节逐个测试：可见网格确实运动，其他关节不串位；超大的发送端位置不改变用户骨长；复位不残留旋转或网格变形。
192个动作采样：经实际 EVMC4U 消息处理器驱动的网格与 Unity Humanoid 动作重定向的网格最大误差小于0.1毫米（固定根位置和用户比例）。
额外故障注入：给手腕加入未冻结的30度初始旋转，归一化检查正确拒绝。Android 打包现在包含该模型预检。
PNG 为实际模型的离线姿态渲染，用于核对形态；不代表实机追踪器输入。

APK
构建结果 Succeeded，0 errors。详见 build.txt。
安装包：桌面 SpineRecovery_六项游戏测试_20261030.apk
包名 com.metaspine.recovery.gamingtest，版本0.2.0-test.20261030 / 2026103001。
Android 8.0+，target API35，ARM64+ARMv7，IL2CPP，OpenGLES3，固定横屏，Development / Debug签名。
实际APK 108869858字节；build.txt 的 Size 是 Unity BuildReport 的总输出大小，不是APK文件大小。
SHA256 38B09FE4688D9DE4A13AEE22A1F10A5197957B2230C2F7DFB716C0B307CFB398
apksigner 验证v2签名通过，zipalign -c 4通过；Manifest包含网络权限、普通UnityPlayerActivity和横屏设置；未要求VR设备。
检查IL2CPP生成代码确认 SPINE_TEST_BUILD 生效，动作选择不会在Android中被编译关闭。
本机未连接安卓设备，未执行安装与设备运行测试。

SlimeVR修复
原桌面安装缺少JRE；保留原安装，在桌面 SlimeVR_修复版 建立可用副本并补充官方Temurin17 JRE，原桌面快捷方式指向修复版。
已验证服务启动、VMC输出与Unity接收。完成后清理合成追踪器，配置恢复 trackers: {}，mirrorTracking: false。
当前VMC目标127.0.0.1用于电脑本地Unity；手机测试需设置为手机IP:39539。详见桌面 六项游戏测试说明_20261030.txt。
用户按Esc后停止界面自动化，未继续操作应用窗口。最终启动由用户双击快捷方式。

边界
追踪器未连接：未验证真实佩戴部位分配、安装方向校准、IMU漂移、无线网络延迟和物理震动。
软件模型已满足Freeze T-Pose归一化条件，不需要重复冻结；这不替代SlimeVR佩戴校准。
本次APK使用当前工作区字体资产；其已有未提交修改及场景删除保持原样，未混入本次代码提交。
构建在 tmp/two-phase-haptics-validation 的验证副本进行，原Unity编辑器与主项目构建设置未更改。
Android模块/JDK/SDK/NDK配置在 tmp/android-test 的用户目录工具链中，未覆盖Program Files中的Unity。

可重复入口
Unity批处理 -executeMethod GameTestBuildVerification.Run （Play Mode，自动退出）
Unity批处理 -executeMethod SlimeUnityVerification.Run （需SlimeVR服务运行，仅隔离测试环境使用；生成合成追踪器）
Unity批处理 -quit -executeMethod UserRigVerification.Inspect
Unity批处理 -buildTarget Android -quit -executeMethod AndroidTestBuild.Build
使用同版本Unity2022.3.62f3c1及Android模块。当前代理环境Gradle使用TLS1.2，正常网络环境不需要本机代理设置。
