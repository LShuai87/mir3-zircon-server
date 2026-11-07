# Runs isolated loopback regressions using the production helper, without starting the game.
[CmdletBinding()]
param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'VS compiler discovery requires vswhere.exe; pass -CompilerPath explicitly.' }
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'VS Roslyn csc.exe was not found; pass -CompilerPath explicitly.' }
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ZirconConnectionTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$executable = Join-Path $testDirectory 'ClientConnectionAttemptTests.exe'
& $CompilerPath /nologo /target:exe /platform:x86 ('/out:' + $executable) (Join-Path $repositoryRoot 'Client\Envir\ClientConnectionAttempt.cs') (Join-Path $PSScriptRoot 'ClientConnectionAttemptTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Connection regression compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Connection regressions failed.' }
