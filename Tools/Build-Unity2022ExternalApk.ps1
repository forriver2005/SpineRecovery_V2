param(
    [Parameter(Mandatory = $true)] [string]$GradleProject,
    [Parameter(Mandatory = $true)] [string]$OutputApk,
    [string]$JavaHome = $env:JAVA_HOME,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
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
$text = $text.Replace('proguard-android.txt', 'proguard-android-optimize.txt')
Set-Content -LiteralPath $launcherGradle -Value $text -NoNewline -Encoding utf8
# Adapt Unity 2022's IL2CPP task to the Gradle 9 / AGP 9 public APIs.
$unityGradle = Join-Path $projectPath 'unityLibrary\build.gradle'
$text = Get-Content -LiteralPath $unityGradle -Raw
$text = $text.Replace('android.ndkDirectory', 'android.ndkPath')
$text = [regex]::Replace($text, '(?ms)^    exec \{(.*?)^    \}', '    providers.exec {$1    }.result.get()')
$text = [regex]::Replace($text, '(?m)^\s*debugSymbolLevel\s*\r?\n', '')
Set-Content -LiteralPath $unityGradle -Value $text -NoNewline -Encoding utf8

$taskNdkVersion = [regex]::Match($text, 'ndkVersion "([^"]+)"')
if ($taskNdkVersion.Success) {
    $launcherText = Get-Content -LiteralPath $launcherGradle -Raw
    if (!$launcherText.Contains('ndkVersion "')) {
        $launcherText = $launcherText.Replace('android {', 'android {' + "`n    ndkVersion `"" + $taskNdkVersion.Groups[1].Value + "`"")
        Set-Content -LiteralPath $launcherGradle -Value $launcherText -NoNewline -Encoding utf8
    }
}


$unityProguard = Join-Path $projectPath 'unityLibrary\proguard-unity.txt'
if (Test-Path -LiteralPath $unityProguard) {
    $text = Get-Content -LiteralPath $unityProguard -Raw
    $text = [regex]::Replace($text, '(?m)^\s*-ignorewarnings\s*\r?\n', '')
    Set-Content -LiteralPath $unityProguard -Value $text -NoNewline -Encoding utf8
}

$taskJavaHome = $env:JAVA_HOME
$taskPath = $env:Path
$env:JAVA_HOME = $JavaHome
$env:Path = "$JavaHome\bin;$env:Path"
Push-Location $projectPath
try {
    & .\gradlew.bat "assemble$Configuration" --no-daemon --console=plain
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
    $env:JAVA_HOME = $taskJavaHome
    $env:Path = $taskPath
}
$variant = $Configuration.ToLowerInvariant()
$builtApk = Join-Path $projectPath "launcher\build\outputs\apk\$variant\launcher-$variant.apk"
if (!(Test-Path -LiteralPath $builtApk)) { throw "APK not found: $builtApk. For unsigned Release output, configure signing in Unity before export." }
$apkDestination = [IO.Path]::GetFullPath($OutputApk)
New-Item -ItemType Directory -Path (Split-Path $apkDestination -Parent) -Force | Out-Null
Copy-Item -LiteralPath $builtApk -Destination $apkDestination -Force
Write-Host "APK created: $apkDestination"
