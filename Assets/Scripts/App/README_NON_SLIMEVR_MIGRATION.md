# 非 SlimeVR 迁移边界

这个 Unity 分支只承接 SpineFlowMobile 的患者流程、数据持久化和管理员配置契约；SlimeVR Android runtime、GeckoView、USB 设备和校准 Activity 由另一个分支维护，合并时只保留一个 Android 依赖入口。

## 已落地的数据基础

- `UserSession` 增加 `UserRole`、疼痛记录和训练历史记录数组。
- `JsonUserRepository` 保留现有 `new1–new10` / `old1–old10` 演示账号，并提供管理员凭据入口 `admin / 123`。管理员会话不写入患者账号文件。
- `JsonAdminConfigRepository` 提供已发布配置、草稿/测试/归档状态模型、按用户标签解析推荐动作，以及单一已发布版本约束。
- `JsonFileStore` 继续作为 Unity 编辑器和 Android 的共同本地持久化适配层；文件实际写入 `Application.persistentDataPath`。

## 对照 SpineFlowMobile 的下一步

1. 当前问卷和训练前检查已经写入 `PainHistoryRecord`，History 页面会读取疼痛与训练记录；训练场景完成回调仍需与 SlimeVR/AR 结果合并时接入 `TrainingHistoryRecord`。
2. 管理员登录后的只读配置页已经可以查看版本、动作、推荐规则和发布状态；凭据与原 SpineFlowMobile 历史管理员入口统一为 `admin / admin`。
3. 已增加草稿复制、AR 测试通过、发布和回滚状态机；发布前必须有测试指纹，患者流程只读取 `Published` 配置。详细动作参数编辑器仍可继续细化。
4. 若需要与服务器同步，再新增 API repository；不要让 UI 直接读写 JSON 或 SQLite。

## 本轮对照结果

- 患者问卷和首次动作评估会生成与已发布配置匹配的用户标签。
- 训练首页会依据 `Published` 配置解析推荐动作，不再只依赖固定按钮文案。
- 管理员摘要会列出启用的推荐规则；患者侧仍只能读取 `Published` 快照。
- 已加入本地注册、激活码绑定演示、例行问卷、问卷答案保存和 24 小时提醒；激活码的服务器预览/核销和例行问卷远端同步仍需接 API repository。

## 合并约束

- 非 SlimeVR 分支不新增或修改 `Assets/Plugins/Android` 中的 SlimeVR/GeckoView 依赖。
- 患者侧不能因为残留 AR 会话或未验证草稿进入管理员模式。
- 同一时刻只能存在一个 `Published` 配置。
