$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot 'Simulator.cs'
$outputPath = Join-Path $PSScriptRoot 'CoachSimulator.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /r:System.Web.Extensions.dll "/out:$outputPath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Simulator build failed' }
