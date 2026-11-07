# Verifies production item list storage against isolated temporary files.
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
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ZirconItemListTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$executable = Join-Path $testDirectory 'ItemListStorageTests.exe'
& $CompilerPath /nologo /target:exe /platform:x86 ('/out:' + $executable) (Join-Path $repositoryRoot 'Client\Envir\ItemListStorage.cs') (Join-Path $PSScriptRoot 'ItemListStorageTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Item list regression compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Item list regressions failed.' }
