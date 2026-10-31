$ErrorActionPreference = 'Stop'
$providerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $providerDir '..\..')
$venvPython = Join-Path $repoRoot '.venv\Scripts\python.exe'

if (-not (Test-Path $venvPython)) {
    python -m venv (Join-Path $repoRoot '.venv')
}

# The Better Lyrics worker uses Python's standard library only. No pip package
# or unofficial Musixmatch wrapper is required.
& $venvPython --version
Write-Host "Python ready. Set Lyrical_PYTHON to: $venvPython"
Write-Host 'For the current PowerShell session, run:'
Write-Host "`$env:Lyrical_PYTHON = `"$venvPython`""
