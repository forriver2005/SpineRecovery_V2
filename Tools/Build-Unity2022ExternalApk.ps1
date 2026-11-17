param(
    [Parameter(Mandatory = $true)] [string]$GradleProject,
    [Parameter(Mandatory = $true)] [string]$OutputApk,
    [string]$JavaHome = $env:JAVA_HOME,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [string]$CertificateSha256,
    [string]$ServiceId = 'hospital-a',
    [string]$HospitalKey = 'hospital-a',
    [switch]$AllowTestSigning,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$CertificateSha256 = "$CertificateSha256".Trim().ToLowerInvariant()
if ($CertificateSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Every LAN build requires -CertificateSha256 with the trusted server certificate SHA-256 fingerprint.'
}
if ([string]::IsNullOrWhiteSpace($ServiceId) -or [string]::IsNullOrWhiteSpace($HospitalKey)) {
    throw 'ServiceId and HospitalKey must identify the local hospital server.'
}
$projectPath = (Resolve-Path -LiteralPath $GradleProject).Path
$wrapperPath = Join-Path $PSScriptRoot 'GradleWrapper'
$nativeSource = Join-Path (Split-Path $PSScriptRoot -Parent) 'NativeAndroid\mobileData'
if (!(Test-Path -LiteralPath (Join-Path $JavaHome 'bin\java.exe'))) {
    throw 'Specify -JavaHome with a JDK 17 or newer for the external Gradle build.'
}
foreach ($relative in @('gradlew', 'gradlew.bat', 'gradle\wrapper\gradle-wrapper.jar', 'gradle\wrapper\gradle-wrapper.properties')) {
    $destination = Join-Path $projectPath $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $wrapperPath $relative) -Destination $destination -Force
}
$nativeDestination = Join-Path $projectPath 'native\mobileData'
New-Item -ItemType Directory -Path $nativeDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nativeSource 'build.gradle') -Destination $nativeDestination -Force
Copy-Item -LiteralPath (Join-Path $nativeSource 'src') -Destination $nativeDestination -Recurse -Force

$launcherGradle = Join-Path $projectPath 'launcher\build.gradle'
$text = Get-Content -LiteralPath $launcherGradle -Raw
$text = [regex]::Replace($text, 'compileSdkVersion\s+\d+', 'compileSdkVersion 36')
$text = [regex]::Replace($text, 'targetSdkVersion\s+\d+', 'targetSdkVersion 36')
$text = [regex]::Replace($text, 'buildToolsVersion\s+["''][^"'']+["'']', "buildToolsVersion '36.0.0'")
$text = $text.Replace('proguard-android.txt', 'proguard-android-optimize.txt')
$releaseBlock = [regex]::Match($text, '(?s)\brelease\s*\{(.*?)\}').Groups[1].Value
$releaseUsesTestSigning = $releaseBlock -match 'signingConfig\s+signingConfigs\.debug\b'
if ($Configuration -eq 'Release' -and $releaseUsesTestSigning) {
    if (!$AllowTestSigning) {
        throw 'The exported Release variant uses the Android debug certificate. Configure production signing, or pass -AllowTestSigning for an explicitly labelled test APK.'
    }
    Write-Warning 'Release TEST APK: using the Android debug certificate. This is not a production-signed release.'
}
[IO.File]::WriteAllText($launcherGradle, $text, (New-Object Text.UTF8Encoding($false)))
# Adapt Unity 2022's IL2CPP task to the Gradle 9 / AGP 9 public APIs.
$unityGradle = Join-Path $projectPath 'unityLibrary\build.gradle'
$text = Get-Content -LiteralPath $unityGradle -Raw
$text = [regex]::Replace($text, 'compileSdkVersion\s+\d+', 'compileSdkVersion 36')
$text = [regex]::Replace($text, 'targetSdkVersion\s+\d+', 'targetSdkVersion 36')
$text = [regex]::Replace($text, 'buildToolsVersion\s+["''][^"'']+["'']', "buildToolsVersion '36.0.0'")
$text = $text.Replace('android.ndkDirectory', 'android.ndkPath')
$text = [regex]::Replace($text, '(?ms)^    exec \{(.*?)^    \}', '    providers.exec {$1    }.result.get()')
$text = [regex]::Replace($text, '(?m)^\s*debugSymbolLevel\s*\r?\n', '')
[IO.File]::WriteAllText($unityGradle, $text, (New-Object Text.UTF8Encoding($false)))

$taskNdkVersion = [regex]::Match($text, 'ndkVersion "([^"]+)"')
if ($taskNdkVersion.Success) {
    $launcherText = Get-Content -LiteralPath $launcherGradle -Raw
    if (!$launcherText.Contains('ndkVersion "')) {
        $launcherText = $launcherText.Replace('android {', 'android {' + "`n    ndkVersion `"" + $taskNdkVersion.Groups[1].Value + "`"")
        [IO.File]::WriteAllText($launcherGradle, $launcherText, (New-Object Text.UTF8Encoding($false)))
    }
}


$unityProguard = Join-Path $projectPath 'unityLibrary\proguard-unity.txt'
if (Test-Path -LiteralPath $unityProguard) {
    $text = Get-Content -LiteralPath $unityProguard -Raw
    $text = [regex]::Replace($text, '(?m)^\s*-ignorewarnings\s*\r?\n', '')
    [IO.File]::WriteAllText($unityProguard, $text, (New-Object Text.UTF8Encoding($false)))
}

$taskJavaHome = $env:JAVA_HOME
$taskPath = $env:Path
$env:JAVA_HOME = $JavaHome
$env:Path = "$JavaHome\bin;$env:Path"
Push-Location $projectPath
try {
    $gradleArguments = @("assemble$Configuration", '--no-daemon', '--console=plain', '--max-workers=2',
        "-PspineflowLocalCertificateSha256=$CertificateSha256",
        "-PspineflowLocalServiceId=$ServiceId", "-PspineflowLocalHospitalKey=$HospitalKey")
    if ($Offline) { $gradleArguments += '--offline' }
    & .\gradlew.bat @gradleArguments
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
    $env:JAVA_HOME = $taskJavaHome
    $env:Path = $taskPath
}
$variant = $Configuration.ToLowerInvariant()
$builtApk = Join-Path $projectPath "launcher\build\outputs\apk\$variant\launcher-$variant.apk"
if (!(Test-Path -LiteralPath $builtApk) -and $Configuration -eq 'Release') {
    $builtApk = Join-Path $projectPath 'launcher\build\outputs\apk\release\launcher-release-unsigned.apk'
    if (Test-Path -LiteralPath $builtApk) { Write-Warning 'Unsigned Release APK: signing is required before installation.' }
}
if (!(Test-Path -LiteralPath $builtApk)) { throw "APK not found: $builtApk" }
$apkDestination = [IO.Path]::GetFullPath($OutputApk)
New-Item -ItemType Directory -Path (Split-Path $apkDestination -Parent) -Force | Out-Null
Copy-Item -LiteralPath $builtApk -Destination $apkDestination -Force
Write-Host "APK created: $apkDestination"
