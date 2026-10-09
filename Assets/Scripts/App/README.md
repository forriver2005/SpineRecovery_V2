# AppShell 第一阶段

这一层只负责应用壳，不替换现有姿态评分和训练核心：

- `AppBootstrap`：创建全局状态、用户仓库、路由和训练场景服务。
- `AppRouter` / `AppRoute`：统一管理登录页、首页和后续页面的路由。
- `JsonUserRepository`：使用 `Application.persistentDataPath` 保存本地用户会话；当前支持 Android 端同款演示账号 `new1–new10`、`old1–old10`，演示密码为 `123`。
- `TrainingPlan` / `TrainingSessionService`：把首页选择转换成训练计划，并校验目标场景是否在 Build Settings。
- `AppShellUi`：运行时生成最小登录页和训练入口，不依赖手工制作 Prefab。
- `AppShellSceneMenu`：在 Unity 菜单中执行 `Spine Recovery/Create AppShell Scene`，生成 `Assets/Scenes/AppShell.unity` 并加入 Build Settings。

当前入口映射：

- Dead Bug 测试 -> `SpineV2Minimal`
- Bird Dog 测试 -> `BirdDogMinimal`

进入 Unity 后先执行一次菜单项生成入口场景，再把 `AppShell` 设为启动场景即可。正式接入时，将 `JsonUserRepository` 替换为 API 仓库，并将 `TrainingPlan` 的场景映射改成真实 AR 场景即可。完整 Android 患者流程仍需继续移植，当前 AppShell 属于运行框架和界面验证阶段。
