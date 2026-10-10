param(
    [Parameter(Mandatory = $true)] [string]$GradleProject,
    [Parameter(Mandatory = $true)] [string]$GradleWrapperSource,
    [Parameter(Mandatory = $true)] [string]$OutputApk,
    [string]$JavaHome = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK'
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path $GradleProject).Path
$wrapperPath = (Resolve-Path $GradleWrapperSource).Path

Copy-Item (Join-Path $wrapperPath 'gradlew') $projectPath -Force
Copy-Item (Join-Path $wrapperPath 'gradlew.bat') $projectPath -Force
Copy-Item (Join-Path $wrapperPath 'gradle') (Join-Path $projectPath 'gradle') -Recurse -Force

# Unity 2022's generated templates use names removed by AGP 9 and fail before D8 runs.
$launcherGradle = Join-Path $projectPath 'launcher\build.gradle'
if (Test-Path $launcherGradle) {
    $text = Get-Content $launcherGradle -Raw
    $text = $text.Replace('proguard-android.txt', 'proguard-android-optimize.txt')
    Set-Content $launcherGradle $text -NoNewline
}
$unityProguard = Join-Path $projectPath 'unityLibrary\proguard-unity.txt'
if (Test-Path $unityProguard) {
    $text = Get-Content $unityProguard -Raw
    $text = [regex]::Replace($text, '(?m)^\s*-ignorewarnings\s*\r?\n', '')
    Set-Content $unityProguard $text -NoNewline
}

$env:JAVA_HOME = $JavaHome
$env:Path = "$JavaHome\bin;$env:Path"
Push-Location $projectPath
try {
    & .\gradlew.bat assembleDebug --no-daemon
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

$builtApk = Join-Path $projectPath 'launcher\build\outputs\apk\debug\launcher-debug.apk'
if (!(Test-Path $builtApk)) { throw "APK not found: $builtApk" }
Copy-Item $builtApk $OutputApk -Force
Write-Host "APK created: $OutputApk"
