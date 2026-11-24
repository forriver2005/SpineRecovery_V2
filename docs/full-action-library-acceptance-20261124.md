# 完整动作库移动端集成与验收

工作分支：`integration/main-into-metaspineapp-20261124`，PR 目标：`MetaspineApp`。保留此前 main 合并提交 `3ab6033d` 的双亲历史。

## 支持范围

支持已有训练场景的 7 类教练动作和 11 个游戏入口。动作示范库里的其他坐姿、站姿和拉伸录制仍属于预览资源，不冒充已具备游戏或评分的训练动作。

| 处方 action | 教练 | 游戏 |
| --- | --- | --- |
| dead_bug | 死虫 | 录制节奏游戏 |
| bird_dog | 鸟狗 | 录制节奏游戏 |
| hip_thrust | 臀桥 | 新评分臀桥 |
| side_bridge | 左侧桥 | 动态左侧桥 |
| side_bridge_right | 右侧桥 | 动态右侧桥 |
| cat_camel | 卧位猫 | 星轨敲鼓 |
| curl_up | 卷腹 | 星轨敲鼓 |
| hip_thrust_taiko | 臀桥 | 星轨敲鼓 |
| hip_thrust_hold30 | 臀桥 | 保持模式 |
| side_bridge_hold30 | 左侧桥 | 保持模式 |
| side_bridge_right_hold30 | 右侧桥 | 保持模式 |

`hold30` 是保留的场景/协议标识。处方模式执行医生指定的 `holdSeconds`，不强制 30 秒。动作目录、Android 处方解析、教练/游戏导航和本地服务端映射已对齐；服务端已有的数字动作 ID 与冻结资源清单仍兼容。

## 变更

- 训练准备页可选择完整自由训练动作；绑定患者继续使用医生处方。
- 处方中的教练与游戏使用同一动作身份、难度和剂量。新敲鼓游戏执行 `sets × reps` 总次数与指定保持时长；处方中禁止更改难度和次数。
- 卷腹、卧位猫接入统一训练报告、本地检查点和后续处方动作入口；暂停时间不计入新游戏有效运行时长，中途返回保存已有测量。
- 新游戏教程可返回正确教练；教练、回放和动作库导航不再沿用“这些动作没有游戏”的旧限制。处方动作库不能切到其他训练动作。
- 无传感器证据不产生有效分数；保持时长为 0 时，完成有效动作不因不存在的保持目标受罚。
- 真机暴露的场景退出异常：释放 `HumanPoseHandler` 后清空引用，场景销毁时取消介绍但不再操作教练姿态，避免 Unity 已销毁对象的空引用。

## 自动检查

- Unity `MainIntegrationVerification.Run`：169 项检查，另有 16 项导航回归。涵盖打包场景/GUID、11 条路线、处方身份、0/7/30/600 秒时间线、保持时长优先级、无输入不计分和 0 秒目标。
- Android `:mobileData:testDebugUnitTest` 与 `:mobileData:testReleaseUnitTest`：各 13 项测试。涵盖全动作库剂量/路由/存储往返、混合处方结果身份与空分数、局域网同步。
- 本地服务端 `tests/mobile-prescription-projection.test.js`：9 项测试通过，使用临时数据库及随机端口，验证新动作组合处方、激活、下发、回执和结果上传。
- 服务端修改位于 WSL 仓库 `/home/wangruopu/SpineFlow/SpineFlowServer` 的现有本地集成分支；未重启或发布服务。可审阅 `Tools/Acceptance/server-library-compatibility.patch`。服务端正式动作选择仍取决于对应动作资源已在目标环境发布。

## 真机验收方法与边界

设备：BAL-AL80，1188 × 2790。独立应用 `com.metaspine.acceptance`（MetaSpine Acceptance），与正式 `com.metaspine.mobile` 并存。使用独立本地测试账号，不绑定正式患者。

`Tools/Acceptance/PhoneFlowAcceptance.cs.txt` 仅临时复制到 Assets 后构建，导出结束即移除。驱动仅在 Development Android 且包名为 `com.metaspine.acceptance` 时运行。构建设置 `SPINE_ACCEPTANCE_BUILD=1` 选择独立包名；普通构建仍使用正式包名。

手机测试使用明确标记的本地处方夹具，逐个验证：教练加载、教练/游戏门禁、游戏页面与统一报告组件、处方总次数与难度、不能提前进入下一项、教程返回、退出问卷、原生数据持久化、反馈保存。为检查未获准开始的游戏页面，驱动直接加载场景；不伪造教练完成事件、动作输入或训练成绩。

手机真机流程与服务端协议测试分别验证。此次未验证真实患者激活后的手机联网回传，也未进行佩戴追踪器后的动作识别、计分、震动、实际完成后跨项衔接及投屏眼镜验收，不能将无追踪器流程测试替代这些项目。

## 真机结果与交付包

- 最后一次全动作流程复测：`PHONE_FLOW_COMPLETE passed=142 failed=0 runtimeErrors=0`。覆盖 11 个动作入口，保存 24 张流程截图。原始记录位于 `D:/SpineFlow/tmp/main-integration-20261124/phone-final-evidence/`。
- 通过正常登录入口补验冷启动、基础资料、例行问卷、首页和历史页；历史页仍显示此前各轮累计 44 条中断训练及已保存反馈。未佩戴追踪器的记录没有标为完成。
- 全流程复测后调整了两处界面：将敲鼓页面次数数字上移 5 个布局单位；退出反馈页根据实际完成状态显示“完成”或“已结束”，统一使用“训练反馈 / 保存反馈”，避免将未开始即退出误称为完成，也避免显示误导性的最后动作编号。正常自由训练列表 → 卷腹页面手动复查：目标次数 8 与标题完整显示，开始按钮正确显示“等待动作传感器连接”。这两处界面微调后的包未重复运行整套 142 项检查。
- 最终界面截图：`D:/SpineFlow/tmp/main-integration-20261124/final-dose-ui.png`；冷启动历史截图：同目录 `cold-history.png`。
- 最终包重新登录后正确恢复待填反馈，标题为“卷腹 已结束”，按钮为“保存反馈”；截图为同目录 `final-feedback.png`。
- APK：`D:/SpineFlow/tmp/main-integration-20261124/MetaSpine-full-library-acceptance.apk`，312,939,726 字节；APK v2 签名验证通过，已安装在 BAL-AL80。
- SHA-256：`55201EFB00B879B3792D0F824F630ACC4324007711EC643E20FFF934AC9BDEB7`。

移动端代码、验收驱动源文件与本报告随 PR 提交；服务端兼容补丁仅作为配套文件提供，WSL 服务端仓库的修改尚未发布。正式手机应用保留，验收时临时启用的 USB 常亮已恢复关闭。APK、真机截图与日志保存在上述本地路径，不包含在 Git 提交中。
