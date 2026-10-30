$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Development dependency: python -m pip install -r "$root\requirements.txt"
python (Join-Path $root 'generate_coach_lessons.py')
if ($LASTEXITCODE -ne 0) { throw 'Coach lesson generation failed; see Python output.' }
