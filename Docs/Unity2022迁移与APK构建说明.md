# SpineRecovery Unity 2022 迁移与 APK 构建说明

## 固定环境

- Unity：`2022.3.62f3c1`
- 分支：`Unity_project`
- 项目：`C:\Users\16156\Documents\GitHub\SpineRecovery_V2`
- Java：Unity 2022 自带 OpenJDK 17

不要使用 Unity 6 打开此分支。

## 本次迁移内容

1. 将项目回迁到 Unity 2022.3.62f1 系列，并保留现有 Unity 场景、训练流程和 SlimeVR 集成。
2. 增加 Android 自定义 Gradle 基础模板，使用 AGP 9.0.0 与新版 D8，绕过 Unity 2022 内置 D8 处理 `slimevr-core.jar` 时的 `NullPointerException`。
3. 开启 Unity 的 Google Android Project 导出选项，使 Unity 只负责导出 Gradle 工程。
4. 增加 `Tools/Build-Unity2022ExternalApk.ps1`，自动复制 Gradle Wrapper、修正 Unity 2022/AGP 9 的 ProGuard 模板，并执行外部 Gradle 构建。

## APK 构建步骤

### 1. 用 Unity 导出 Gradle 工程

在 Unity 2022 中打开项目，执行 Android Build。也可以使用批处理参数：

```powershell
$env:SPINE_APK_OUTPUT = 'C:\Users\16156\Desktop\SpineRecovery_Unity2022_GradleExport'
$env:SPINE_GRADLE_EXPORT = '1'
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe' `
  -batchmode -quit -nographics `
  -projectPath 'C:\Users\16156\Documents\GitHub\SpineRecovery_V2' `
  -executeMethod SpineRecovery.Editor.BuildAndroidApk.PerformBuild `
  -logFile 'C:\Users\16156\Desktop\SpineRecovery_Unity2022_gradle_export.log'
```

### 2. 用外部新版 D8 构建 APK

```powershell
Set-Location 'C:\Users\16156\Documents\GitHub\SpineRecovery_V2'
& '.\Tools\Build-Unity2022ExternalApk.ps1' `
  -GradleProject 'C:\Users\16156\Desktop\SpineRecovery_Unity2022_GradleExport' `
  -GradleWrapperSource 'C:\Users\16156\Documents\GitHub\SpineFlowMobile' `
  -OutputApk 'C:\Users\16156\Desktop\SpineRecovery_Unity2022_external_final.apk'
```

脚本默认使用 Unity 2022 自带 JDK，也可以通过 `-JavaHome` 指定其他 JDK 17 路径。

## 已验证结果

外部 Gradle 构建已成功，生成 `launcher-debug.apk`。已验证的构建链为：Unity 2022 导出、Gradle 9.4.1、AGP 9.0.0、新版 D8。

直接在 Unity 2022 内执行 APK 构建仍可能触发旧版 D8 错误，因此发布 APK 时必须使用上述外部 Gradle 流程。
