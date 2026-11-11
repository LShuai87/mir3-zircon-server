# Independent geometry checks; requires neither the client process nor game resources.
[CmdletBinding()]
param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Pass -CompilerPath for the Visual Studio C# compiler.' }
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconGameUiFillTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$executable = Join-Path $testDirectory 'OfficialUiFillTests.exe'
& $CompilerPath /nologo /target:exe /r:System.Drawing.dll ('/out:' + $executable) (Join-Path $repositoryRoot 'Client\Models\GameUi\GameUiFillGeometry.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiFillTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Official UI fill test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Official UI fill geometry tests failed.' }
