# SpineRecovery_V2

## 手机网络模式

Debug 和 Release 均只支持医院局域网。手机必须连接服务器所在 Wi-Fi；医疗 API、处方资源和版本下载统一通过同网段自动发现及固定服务器证书的 TLS 连接。医院 Wi-Fi 无互联网也可使用；仅蜂窝网络、错误医院、错误证书或找不到服务器时连接失败，不会转向公网。

构建时必须提供 `spineflowLocalCertificateSha256`，并确认 `spineflowLocalServiceId` 与 `spineflowLocalHospitalKey`。证书指纹是公开配置，可从受信服务器的 `public-config.json` 获取；不要复制私钥到手机工程。缺少或错误格式的指纹会拒绝构建，旧 `spineflowServerUrl` 参数不再支持。

`https://spineflow.local:3001` 是稳定的逻辑入口，不依赖 DNS 解析；真实连接只使用已发现、经过证书及医院身份校验的同网段私有 IPv4 地址。旧私网绑定继续保留原患者、队列键和回执语义。旧公网绑定无法连接，数据保留，需工作人员另行处理，不能直接把其他医院的数据改绑到本机。

Tracker 震动 HTTP 也仅允许字面私有 IPv4（可含端口），拒绝公网/域名并禁止重定向。手机其他系统应用的联网不受本工程控制。

## 构建和验证

原生模块测试示例（在项目根目录；先设置本机 JDK 和 Android SDK 环境）：

```powershell
$hospitalConfig = Get-Content '<受信服务器公开配置路径>/public-config.json' -Raw | ConvertFrom-Json
& ./Tools/GradleWrapper/gradlew.bat -p NativeAndroid `
  :mobileData:testDebugUnitTest :mobileData:testReleaseUnitTest `
  -Pandroid.onlyEnableUnitTestForTheTestedBuildType=false `
  "-PspineflowLocalCertificateSha256=$($hospitalConfig.certificateSha256)" `
  "-PspineflowLocalServiceId=$($hospitalConfig.serviceId)" `
  "-PspineflowLocalHospitalKey=$($hospitalConfig.hospitalKey)"
& ./Tools/Test-TrackerLanPolicy.ps1
```

AGP 9 默认只启用被测 build type 的单元测试，上述参数用于同时验证两个变体。[Android 官方版本说明](https://developer.android.com/build/releases/agp-9-0-0-release-notes)

可选 `Minimal*HttpTest` 通过 `SPINEFLOW_BRIDGE_FIXTURE` 指定独立测试医院夹具，使用仅在 `src/test` 编译的 loopback 适配器，复用业务解析与回执校验；它不绕过正式应用网络限制，也不代表 Android Wi-Fi 发现已实测。两个变体须分别使用新生成的夹具，避免上一轮停训/问卷等状态影响下一轮。未提供夹具或新鲜 Unity 投影时，相应可选测试会跳过。

完整 APK 需先用 Unity 的 `SpineRecovery.Editor.BuildAndroidApk.PerformBuild` 导出，设置 `SPINE_GRADLE_EXPORT=1`、`SPINE_APK_OUTPUT=<导出目录>`。正式构建还应设 `SPINE_BUILD_CONFIGURATION=Release`，避免使用 Unity Development player。C# 有改动时必须重新导出，不能只更新原生模块。

```powershell
& ./Tools/Build-Unity2022ExternalApk.ps1 `
  -GradleProject '<Unity导出目录>' -OutputApk '<输出APK路径>' `
  -JavaHome '<JDK路径>' -Configuration Release `
  -CertificateSha256 $hospitalConfig.certificateSha256 `
  -ServiceId $hospitalConfig.serviceId -HospitalKey $hospitalConfig.hospitalKey
```

正式发布需要配置正式签名。导出工程若使用 Android debug 证书，Release 构建需显式传 `-AllowTestSigning`，产物只能标为 Release 测试包；未签名产物不能直接安装。不要用测试证书冒充正式签名。`-Offline` 可使用本机已缓存的构建依赖。
